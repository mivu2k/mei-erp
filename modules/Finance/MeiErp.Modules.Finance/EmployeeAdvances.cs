using MeiErp.Platform.Kernel;
using MeiErp.Platform.Workflow;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Finance;

/// <summary>
/// Money lent to a member of staff and taken back out of their salary.
///
/// Deliberately not the same thing as <see cref="Advance"/>. That one is money
/// handed over for a trip and accounted for with receipts; this one is a loan
/// with nothing to justify and a repayment schedule instead. Collapsing the two
/// is how "did they spend it or do they still owe it?" stops having an answer.
/// </summary>
public class EmployeeAdvance : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    public string Reference { get; set; } = "";

    public string PersonId { get; set; } = "";
    public string PersonName { get; set; } = "";

    public string? DepartmentId { get; set; }

    public decimal Amount { get; set; }
    public string? Reason { get; set; }

    public EmployeeAdvanceStatus Status { get; set; } = EmployeeAdvanceStatus.Draft;

    /// <summary>When the whole balance is expected back, if a date was agreed.</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>How many salary runs it is spread over. At least one.</summary>
    public int InstallmentCount { get; set; } = 1;

    /// <summary>
    /// What comes off each month. Stored rather than divided on the fly so a
    /// later change to the schedule cannot silently restate what was agreed.
    /// </summary>
    public decimal MonthlyDeduction { get; set; }

    public decimal RepaidAmount { get; set; }

    public int? ApprovalRequestId { get; set; }
    public string? DecisionComment { get; set; }

    /// <summary>The person's own advance head, fixed at disbursement.</summary>
    public int? AdvanceAccountId { get; set; }
    public Account? AdvanceAccount { get; set; }

    public int? DisbursementVoucherId { get; set; }

    public DateTime? SubmittedUtc { get; set; }
    public DateTime? DisbursedUtc { get; set; }
    public DateTime? SettledUtc { get; set; }

    public List<EmployeeAdvanceInstallment> Installments { get; set; } = [];

    public decimal OutstandingBalance => Amount - RepaidAmount;

    public bool IsOpen => Status is not (EmployeeAdvanceStatus.Settled
                                      or EmployeeAdvanceStatus.Rejected
                                      or EmployeeAdvanceStatus.Cancelled);
}

public enum EmployeeAdvanceStatus
{
    Draft = 0,
    Pending = 1,

    /// <summary>Signed off, but the money has not been handed over yet.</summary>
    Approved = 2,

    /// <summary>Paid out; the schedule exists and the first deduction is due.</summary>
    Disbursed = 3,

    /// <summary>Something has come back, but not all of it.</summary>
    Repaying = 4,

    /// <summary>Repaid in full.</summary>
    Settled = 5,

    Rejected = 6,
    Returned = 7,
    Cancelled = 8
}

/// <summary>One month's repayment.</summary>
public class EmployeeAdvanceInstallment : AuditableEntity
{
    public int EmployeeAdvanceId { get; set; }
    public EmployeeAdvance? EmployeeAdvance { get; set; }

    public int Number { get; set; }
    public DateOnly DueDate { get; set; }

    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public DateOnly? PaidDate { get; set; }

    public InstallmentStatus Status { get; set; } = InstallmentStatus.Pending;

    /// <summary>Set when the money came back outside payroll.</summary>
    public int? RepaymentVoucherId { get; set; }

    public decimal Outstanding => Amount - PaidAmount;
}

public enum InstallmentStatus
{
    Pending = 0,
    PartiallyPaid = 1,
    Paid = 2
}

public sealed record EmployeeAdvanceInput(
    int? Id, decimal Amount, string? Reason, int InstallmentCount,
    DateOnly? DueDate, string? PersonId = null, string? PersonName = null,
    string? DepartmentId = null);

public interface IEmployeeAdvanceService
{
    Task<IReadOnlyList<EmployeeAdvance>> ListAsync(
        EmployeeAdvanceStatus? status, bool mineOnly, CancellationToken ct = default);

    Task<EmployeeAdvance?> GetAsync(int id, CancellationToken ct = default);

    Task<Result<EmployeeAdvance>> SaveDraftAsync(EmployeeAdvanceInput input, CancellationToken ct = default);
    Task<Result<EmployeeAdvance>> SubmitAsync(int id, CancellationToken ct = default);

    /// <summary>Hands the money over and builds the repayment schedule.</summary>
    Task<Result<EmployeeAdvance>> DisburseAsync(
        int id, int fromAccountId, DateOnly date, CancellationToken ct = default);

    /// <summary>Records a repayment made outside payroll - cash handed back.</summary>
    Task<Result<EmployeeAdvance>> RepayAsync(
        int id, decimal amount, int cashAccountId, DateOnly date, CancellationToken ct = default);

    Task<Result> CancelAsync(int id, CancellationToken ct = default);

    /// <summary>What payroll should take off this person this month.</summary>
    Task<IReadOnlyList<EmployeeAdvance>> DueForRecoveryAsync(
        string personId, DateOnly asAt, CancellationToken ct = default);

    /// <summary>Everyone still owing something, for the outstanding view.</summary>
    Task<IReadOnlyList<EmployeeAdvance>> OutstandingAsync(CancellationToken ct = default);
}

/// <summary>
/// The monthly figure, rounded so the instalments add back to the whole.
///
/// Pure and static for the same reason the router is: the awkward cases are all
/// about rounding, and none are testable if the rule is buried in a service.
/// </summary>
public static class RepaymentSchedule
{
    public static decimal Monthly(decimal amount, int months) =>
        months <= 0 ? amount : Math.Round(amount / months, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The instalment amounts. The last one absorbs the rounding remainder, so
    /// twelve months of 1,000/3 comes back to 1,000 and not 999.96.
    /// </summary>
    public static IReadOnlyList<decimal> Split(decimal amount, int months)
    {
        if (months <= 1) return [amount];

        var monthly = Monthly(amount, months);
        var parts = new List<decimal>();

        for (var i = 0; i < months - 1; i++) parts.Add(monthly);
        parts.Add(amount - monthly * (months - 1));

        return parts;
    }
}

public sealed class EmployeeAdvanceService(
    FinanceDbContext db,
    IVoucherService vouchers,
    IApprovalEngine approvals,
    ICurrentUser currentUser,
    IClock clock) : IEmployeeAdvanceService
{
    public const string DocumentType = "finance.employee-advance";

    private const string AdvanceAccountCode = "1700";

    public async Task<IReadOnlyList<EmployeeAdvance>> ListAsync(
        EmployeeAdvanceStatus? status, bool mineOnly, CancellationToken ct = default)
    {
        var query = db.EmployeeAdvances.AsNoTracking().Include(a => a.Installments).AsQueryable();

        if (status is not null) query = query.Where(a => a.Status == status);

        if (mineOnly)
        {
            var me = currentUser.UserId ?? "";
            query = query.Where(a => a.PersonId == me);
        }

        return await query.OrderByDescending(a => a.Id).Take(500).ToListAsync(ct);
    }

    public Task<EmployeeAdvance?> GetAsync(int id, CancellationToken ct = default) =>
        db.EmployeeAdvances
          .Include(a => a.Installments.OrderBy(i => i.Number))
          .FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<Result<EmployeeAdvance>> SaveDraftAsync(
        EmployeeAdvanceInput input, CancellationToken ct = default)
    {
        if (input.Amount <= 0)
            return Result.Fail<EmployeeAdvance>("The amount must be more than nothing.", "salary-advance.bad-amount");

        if (input.InstallmentCount < 1)
        {
            return Result.Fail<EmployeeAdvance>(
                "It has to be repaid over at least one month.", "salary-advance.bad-term");
        }

        EmployeeAdvance advance;

        if (input.Id is null or 0)
        {
            advance = new EmployeeAdvance
            {
                Reference = await NextReferenceAsync(ct),
                PersonId = input.PersonId ?? currentUser.UserId ?? "",
                PersonName = input.PersonName ?? currentUser.Name ?? "",
                Status = EmployeeAdvanceStatus.Draft
            };
            db.EmployeeAdvances.Add(advance);
        }
        else
        {
            var existing = await db.EmployeeAdvances.FirstOrDefaultAsync(a => a.Id == input.Id, ct);
            if (existing is null)
                return Result.Fail<EmployeeAdvance>("That advance no longer exists.", "salary-advance.not-found");

            // Read the previous status from the change tracker: an edit screen
            // hands back the very instance it loaded, so comparing `existing` to
            // the incoming object is always false.
            var previousStatus = db.Entry(existing).OriginalValues
                .GetValue<EmployeeAdvanceStatus>(nameof(EmployeeAdvance.Status));

            if (previousStatus is not (EmployeeAdvanceStatus.Draft or EmployeeAdvanceStatus.Returned))
            {
                return Result.Fail<EmployeeAdvance>(
                    "This has already been submitted and cannot be edited. Withdraw it first.",
                    "salary-advance.not-editable");
            }

            advance = existing;
        }

        advance.Amount = input.Amount;
        advance.Reason = input.Reason;
        advance.InstallmentCount = input.InstallmentCount;
        advance.DueDate = input.DueDate;
        advance.DepartmentId = input.DepartmentId;
        advance.MonthlyDeduction = RepaymentSchedule.Monthly(input.Amount, input.InstallmentCount);

        await db.SaveChangesAsync(ct);
        return Result.Success(advance);
    }

    public async Task<Result<EmployeeAdvance>> SubmitAsync(int id, CancellationToken ct = default)
    {
        var advance = await db.EmployeeAdvances.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (advance is null)
            return Result.Fail<EmployeeAdvance>("That advance no longer exists.", "salary-advance.not-found");

        if (advance.Status is not (EmployeeAdvanceStatus.Draft or EmployeeAdvanceStatus.Returned))
            return Result.Fail<EmployeeAdvance>("This has already been submitted.", "salary-advance.already-submitted");

        var submitted = await approvals.SubmitAsync(new SubmitApproval(
            ModuleKey: FinanceModule.Key,
            DocumentType: DocumentType,
            DocumentId: advance.Id,
            DocumentReference: advance.Reference,
            Summary: $"Salary advance of {advance.Amount:N2} to {advance.PersonName}, " +
                     $"repaid over {advance.InstallmentCount} " +
                     $"{(advance.InstallmentCount == 1 ? "month" : "months")}",
            DocumentUrl: $"/finance/salary-advances/{advance.Id}",
            Amount: advance.Amount,
            Currency: "PKR",
            DepartmentId: advance.DepartmentId), ct);

        if (submitted.Failed) return Result.Fail<EmployeeAdvance>(submitted.Error!, submitted.Code);

        advance.Status = EmployeeAdvanceStatus.Pending;
        advance.ApprovalRequestId = submitted.Value.Id;
        advance.SubmittedUtc = clock.UtcNow;

        await db.SaveChangesAsync(ct);
        return Result.Success(advance);
    }

    public async Task<Result<EmployeeAdvance>> DisburseAsync(
        int id, int fromAccountId, DateOnly date, CancellationToken ct = default)
    {
        var advance = await db.EmployeeAdvances.Include(a => a.Installments)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (advance is null)
            return Result.Fail<EmployeeAdvance>("That advance no longer exists.", "salary-advance.not-found");

        if (advance.Status is not EmployeeAdvanceStatus.Approved)
        {
            return Result.Fail<EmployeeAdvance>(
                "Only an approved advance can be paid out.", "salary-advance.not-approved");
        }

        if (advance.DisbursementVoucherId is not null)
            return Result.Fail<EmployeeAdvance>("This has already been paid out.", "salary-advance.already-disbursed");

        var account = await PersonAccountAsync(advance, ct);
        if (account.Failed) return Result.Fail<EmployeeAdvance>(account.Error!, account.Code);

        // Dr their advance head, Cr cash. The money has left the business but is
        // not an expense - it is a debt they are going to repay.
        var posted = await vouchers.PostSystemVoucherAsync(new SystemVoucher(
            Type: VoucherType.Payment,
            Date: date,
            Narration: $"{advance.Reference}: salary advance to {advance.PersonName}",
            Lines:
            [
                new VoucherLineInput(account.Value.Id, advance.Amount, 0,
                    advance.Reason ?? "Salary advance", advance.PersonId, advance.PersonName),
                new VoucherLineInput(fromAccountId, 0, advance.Amount, advance.PersonName)
            ],
            Module: FinanceModule.Key,
            DocumentType: DocumentType,
            DocumentId: advance.Id,
            DocumentReference: advance.Reference), ct);

        if (posted.Failed) return Result.Fail<EmployeeAdvance>(posted.Error!, posted.Code);

        // The schedule starts next month: deducting from the salary that is
        // being paid in the same breath as the loan is not what was agreed.
        var parts = RepaymentSchedule.Split(advance.Amount, advance.InstallmentCount);
        var first = new DateOnly(date.Year, date.Month, 1).AddMonths(1);

        for (var i = 0; i < parts.Count; i++)
        {
            advance.Installments.Add(new EmployeeAdvanceInstallment
            {
                Number = i + 1,
                DueDate = first.AddMonths(i),
                Amount = parts[i],
                Status = InstallmentStatus.Pending
            });
        }

        advance.Status = EmployeeAdvanceStatus.Disbursed;
        advance.AdvanceAccountId = account.Value.Id;
        advance.DisbursementVoucherId = posted.Value.Id;
        advance.DisbursedUtc = clock.UtcNow;

        await db.SaveChangesAsync(ct);
        return Result.Success(advance);
    }

    public async Task<Result<EmployeeAdvance>> RepayAsync(
        int id, decimal amount, int cashAccountId, DateOnly date, CancellationToken ct = default)
    {
        var advance = await db.EmployeeAdvances.Include(a => a.Installments)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (advance is null)
            return Result.Fail<EmployeeAdvance>("That advance no longer exists.", "salary-advance.not-found");

        if (advance.Status is not (EmployeeAdvanceStatus.Disbursed or EmployeeAdvanceStatus.Repaying))
            return Result.Fail<EmployeeAdvance>("There is nothing to repay on this.", "salary-advance.not-repayable");

        if (amount <= 0)
            return Result.Fail<EmployeeAdvance>("The amount must be more than nothing.", "salary-advance.bad-amount");

        if (amount > advance.OutstandingBalance)
        {
            return Result.Fail<EmployeeAdvance>(
                $"Only {advance.OutstandingBalance:N2} is still owed on this advance.",
                "salary-advance.over-repayment");
        }

        if (advance.AdvanceAccountId is null)
            return Result.Fail<EmployeeAdvance>("This advance was never paid out.", "salary-advance.not-disbursed");

        // Dr cash, Cr their advance head: the debt comes down as the money
        // comes back.
        var posted = await vouchers.PostSystemVoucherAsync(new SystemVoucher(
            Type: VoucherType.Receipt,
            Date: date,
            Narration: $"{advance.Reference}: repayment from {advance.PersonName}",
            Lines:
            [
                new VoucherLineInput(cashAccountId, amount, 0, "Advance repayment"),
                new VoucherLineInput(advance.AdvanceAccountId.Value, 0, amount,
                    "Advance repayment", advance.PersonId, advance.PersonName)
            ],
            Module: FinanceModule.Key,
            DocumentType: DocumentType,
            DocumentId: advance.Id,
            DocumentReference: $"{advance.Reference}-R{advance.Installments.Count(i => i.PaidAmount > 0) + 1}",
            IdempotencyKey: $"{advance.Reference}-repay-{date:yyyyMMdd}-{amount}"), ct);

        if (posted.Failed) return Result.Fail<EmployeeAdvance>(posted.Error!, posted.Code);

        ApplyToSchedule(advance, amount, date, posted.Value.Id);

        await db.SaveChangesAsync(ct);
        return Result.Success(advance);
    }

    /// <summary>
    /// Spreads a repayment over the instalments still owing, oldest first, and
    /// rolls the advance's own status forward.
    ///
    /// Internal so payroll can apply a deduction the same way without posting
    /// anything: the salary voucher has already credited the advance head, and
    /// posting again here would take the money twice.
    /// </summary>
    internal static void ApplyToSchedule(
        EmployeeAdvance advance, decimal amount, DateOnly date, int? voucherId)
    {
        var left = amount;

        foreach (var installment in advance.Installments
                     .Where(i => i.Status is not InstallmentStatus.Paid)
                     .OrderBy(i => i.Number))
        {
            if (left <= 0) break;

            var take = Math.Min(installment.Outstanding, left);

            installment.PaidAmount += take;
            installment.PaidDate = date;
            installment.RepaymentVoucherId ??= voucherId;
            installment.Status = installment.Outstanding <= 0
                ? InstallmentStatus.Paid
                : InstallmentStatus.PartiallyPaid;

            left -= take;
        }

        advance.RepaidAmount += amount;

        advance.Status = advance.RepaidAmount >= advance.Amount
            ? EmployeeAdvanceStatus.Settled
            : EmployeeAdvanceStatus.Repaying;

        if (advance.Status is EmployeeAdvanceStatus.Settled) advance.SettledUtc = DateTime.UtcNow;
    }

    public async Task<Result> CancelAsync(int id, CancellationToken ct = default)
    {
        var advance = await db.EmployeeAdvances.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (advance is null) return Result.Fail("That advance no longer exists.", "salary-advance.not-found");

        if (advance.DisbursementVoucherId is not null)
        {
            // The money is out. Cancelling would leave a balance on their head
            // that nothing is scheduled to recover.
            return Result.Fail(
                "This has already been paid out. Record repayments instead, or reverse the voucher.",
                "salary-advance.already-disbursed");
        }

        if (advance.Status is EmployeeAdvanceStatus.Pending && advance.ApprovalRequestId is not null)
            await approvals.CancelAsync(advance.ApprovalRequestId.Value, "Withdrawn by the requester", ct);

        advance.Status = EmployeeAdvanceStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<IReadOnlyList<EmployeeAdvance>> DueForRecoveryAsync(
        string personId, DateOnly asAt, CancellationToken ct = default) =>
        await db.EmployeeAdvances
            .Include(a => a.Installments)
            .Where(a => a.PersonId == personId
                     && (a.Status == EmployeeAdvanceStatus.Disbursed
                      || a.Status == EmployeeAdvanceStatus.Repaying)
                     && a.Installments.Any(i => i.Status != InstallmentStatus.Paid && i.DueDate <= asAt))
            .OrderBy(a => a.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<EmployeeAdvance>> OutstandingAsync(CancellationToken ct = default) =>
        await db.EmployeeAdvances.AsNoTracking()
            .Include(a => a.Installments)
            .Where(a => a.Status == EmployeeAdvanceStatus.Disbursed
                     || a.Status == EmployeeAdvanceStatus.Repaying)
            .OrderBy(a => a.PersonName)
            .ToListAsync(ct);

    /// <summary>
    /// This person's own advance head, shared with trip advances: one place
    /// answers "what does this person owe the company".
    /// </summary>
    private async Task<Result<Account>> PersonAccountAsync(EmployeeAdvance advance, CancellationToken ct)
    {
        var parent = await db.Accounts.FirstOrDefaultAsync(a => a.Code == AdvanceAccountCode, ct);

        if (parent is null)
        {
            return Result.Fail<Account>(
                $"The {AdvanceAccountCode} account needed for this advance is missing from the chart of accounts.",
                "salary-advance.no-account");
        }

        var existing = await db.Accounts
            .FirstOrDefaultAsync(a => a.ParentId == parent.Id && a.PersonId == advance.PersonId, ct);

        if (existing is not null) return Result.Success(existing);

        var siblings = await db.Accounts.CountAsync(a => a.ParentId == parent.Id, ct);

        var child = new Account
        {
            Code = $"{parent.Code}-{(siblings + 1):D3}",
            Name = advance.PersonName,
            Type = parent.Type,
            ParentId = parent.Id,
            PersonId = advance.PersonId,
            IsPostable = true,
            IsSystem = true,
            Description = $"Advances held by {advance.PersonName}."
        };

        db.Accounts.Add(child);

        if (parent.IsPostable)
        {
            var used = await db.VoucherLines.AnyAsync(l => l.AccountId == parent.Id, ct);
            if (!used) parent.IsPostable = false;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(child);
    }

    private async Task<string> NextReferenceAsync(CancellationToken ct)
    {
        var stem = $"SAL-{clock.Today.Year % 100:D2}-";
        var count = await db.EmployeeAdvances.IgnoreQueryFilters()
            .CountAsync(a => a.Reference.StartsWith(stem), ct);
        return stem + (count + 1).ToString().PadLeft(4, '0');
    }
}

/// <summary>How Finance hears that a salary advance was decided.</summary>
public sealed class EmployeeAdvanceApprovalSink(FinanceDbContext db) : IApprovalSink
{
    public string DocumentType => EmployeeAdvanceService.DocumentType;

    public async Task<Result> OnSettledAsync(
        int documentId, ApprovalStatus status, ApprovalRequest request, CancellationToken ct = default)
    {
        var advance = await db.EmployeeAdvances.FirstOrDefaultAsync(a => a.Id == documentId, ct);
        if (advance is null) return Result.Success();

        advance.DecisionComment = request.Actions
            .OrderByDescending(a => a.ActedUtc)
            .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.Comment))?.Comment;

        advance.Status = status switch
        {
            ApprovalStatus.Approved => EmployeeAdvanceStatus.Approved,
            ApprovalStatus.Rejected => EmployeeAdvanceStatus.Rejected,
            ApprovalStatus.Returned => EmployeeAdvanceStatus.Returned,
            ApprovalStatus.Cancelled => EmployeeAdvanceStatus.Cancelled,
            _ => advance.Status
        };

        return Result.Success();
    }
}
