using MeiErp.Platform.Kernel;
using MeiErp.Platform.Workflow;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Finance;

/// <summary>
/// What happens to the gap between what was taken and what was spent.
///
/// Twenty thousand taken, seventeen spent: three thousand has to go somewhere,
/// and pretending otherwise is how advances quietly become salary.
/// </summary>
public enum DifferenceHandling
{
    /// <summary>The difference moves through cash immediately - handed back, or paid out.</summary>
    SettleNow = 0,

    /// <summary>
    /// Parked on the person's advance account, to be cleared later. The books
    /// keep showing that they are holding it.
    /// </summary>
    Outstanding = 1,

    /// <summary>Turned into a salary deduction for the next payroll run to recover.</summary>
    RecoverFromPayroll = 2
}

/// <summary>One receipt against an advance.</summary>
public class AdvanceExpense : Entity
{
    /// <summary>The advance-kind payment request this receipt belongs to.</summary>
    public int AdvanceId { get; set; }
    public PaymentRequest? Advance { get; set; }

    public DateOnly Date { get; set; }
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }

    /// <summary>Which head this spend is charged to when the advance settles.</summary>
    public int? ExpenseAccountId { get; set; }
    public Account? ExpenseAccount { get; set; }

    public string? ReceiptNumber { get; set; }
}

public interface IAdvanceService
{
    Task<IReadOnlyList<PaymentRequest>> ListAsync(PaymentRequestStatus? status, bool mineOnly, bool directorOnly = false, CancellationToken ct = default);
    Task<PaymentRequest?> GetAsync(int id, CancellationToken ct = default);
    Task<Result<PaymentRequest>> SaveDraftAsync(AdvanceInput input, CancellationToken ct = default);
    Task<Result<PaymentRequest>> SubmitAsync(int id, CancellationToken ct = default);

    /// <summary>Hands the money over: Dr the person's advance account, Cr cash.</summary>
    Task<Result<PaymentRequest>> DisburseAsync(
        int id, decimal amount, int fromAccountId, DateOnly date, CancellationToken ct = default);

    /// <summary>Records the receipts. Does not post anything yet.</summary>
    Task<Result<PaymentRequest>> JustifyAsync(
        int id, IReadOnlyList<AdvanceExpenseInput> expenses, CancellationToken ct = default);

    /// <summary>
    /// Closes the advance: charges the receipts to their heads, clears the
    /// person's advance account, and sends the difference where it was told to.
    /// </summary>
    Task<Result<PaymentRequest>> SettleAsync(
        int id, DifferenceHandling handling, int cashAccountId, DateOnly date,
        CancellationToken ct = default);

    /// <summary>Clears an outstanding difference later, when the person hands it back.</summary>
    Task<Result<PaymentRequest>> ClearDifferenceAsync(
        int id, decimal amount, int cashAccountId, DateOnly date, CancellationToken ct = default);
}

public sealed record AdvanceInput(
    int? Id, string Purpose, decimal Amount, DateOnly NeededBy,
    string? PersonId, string? PersonName, string? DepartmentId,
    bool IsDirectorRequest = false);

public sealed record AdvanceExpenseInput(
    DateOnly Date, string Description, decimal Amount, int? ExpenseAccountId, string? ReceiptNumber);

public sealed class AdvanceService(
    FinanceDbContext db,
    IVoucherService vouchers,
    IApprovalEngine approvals,
    ICurrentUser currentUser,
    IClock clock) : IAdvanceService
{
    public const string DocumentType = "finance.advance";

    /// <summary>Where money held by staff sits until it is accounted for.</summary>
    private const string AdvanceAccountCode = "1700";
    private const string DirectorCapitalCode = "3210";
    private const string PayableAccountCode = "2100";

    public async Task<IReadOnlyList<PaymentRequest>> ListAsync(
        PaymentRequestStatus? status, bool mineOnly, bool directorOnly = false, CancellationToken ct = default)
    {
        var query = db.PaymentRequests.AsNoTracking().Include(a => a.Expenses)
            .Where(a => a.Kind == PaymentRequestKind.Advance)
            .AsQueryable();

        if (status is not null) query = query.Where(a => a.Status == status);

        if (mineOnly)
        {
            var me = currentUser.UserId ?? "";
            query = query.Where(a => a.RequestedByUserId == me);
        }

        query = query.Where(a => a.IsDirectorRequest == directorOnly);

        return await query.OrderByDescending(a => a.Id).Take(500).ToListAsync(ct);
    }

    public Task<PaymentRequest?> GetAsync(int id, CancellationToken ct = default) =>
        db.PaymentRequests
          .Include(a => a.Expenses).ThenInclude(e => e.ExpenseAccount)
          .FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<Result<PaymentRequest>> SaveDraftAsync(
        AdvanceInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Purpose))
            return Result.Fail<PaymentRequest>("Say what the advance is for.", "advance.no-purpose");

        if (input.Amount <= 0)
            return Result.Fail<PaymentRequest>("The amount must be more than nothing.", "advance.bad-amount");

        PaymentRequest advance;

        if (input.Id is null or 0)
        {
            advance = new PaymentRequest
            {
                Kind = PaymentRequestKind.Advance,
                Reference = await NextReferenceAsync(input.IsDirectorRequest, ct),
                RequestedByUserId = input.PersonId ?? currentUser.UserId ?? "",
                RequestedByName = input.PersonName ?? currentUser.Name ?? "",
                Status = PaymentRequestStatus.Draft
            };
            db.PaymentRequests.Add(advance);
        }
        else
        {
            var existing = await db.PaymentRequests.FirstOrDefaultAsync(a => a.Id == input.Id, ct);
            if (existing is null) return Result.Fail<PaymentRequest>("That advance no longer exists.", "advance.not-found");

            var previousStatus = db.Entry(existing).OriginalValues
                .GetValue<PaymentRequestStatus>(nameof(PaymentRequest.Status));

            if (previousStatus is not (PaymentRequestStatus.Draft or PaymentRequestStatus.Returned))
            {
                return Result.Fail<PaymentRequest>(
                    "This has already been submitted and cannot be edited. Withdraw it first.",
                    "advance.not-editable");
            }

            advance = existing;
        }

        advance.Title = input.Purpose;
        advance.Amount = input.Amount;
        advance.NeededBy = input.NeededBy;
        advance.DepartmentId = input.DepartmentId;
        advance.IsDirectorRequest = input.IsDirectorRequest;

        await db.SaveChangesAsync(ct);
        return Result.Success(advance);
    }

    public async Task<Result<PaymentRequest>> SubmitAsync(int id, CancellationToken ct = default)
    {
        var advance = await db.PaymentRequests.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (advance is null) return Result.Fail<PaymentRequest>("That advance no longer exists.", "advance.not-found");

        if (advance.Status is not (PaymentRequestStatus.Draft or PaymentRequestStatus.Returned))
            return Result.Fail<PaymentRequest>("This has already been submitted.", "advance.already-submitted");

        var submitted = await approvals.SubmitAsync(new SubmitApproval(
            ModuleKey: FinanceModule.Key,
            DocumentType: DocumentType,
            DocumentId: advance.Id,
            DocumentReference: advance.Reference,
            Summary: (advance.IsDirectorRequest ? "Director fund of " : "Advance of ") + $"{advance.Amount:N2} to {advance.RequestedByName} — {advance.Title}",
            DocumentUrl: $"/finance/advances/{advance.Id}",
            Amount: advance.Amount,
            Currency: "PKR",
            DepartmentId: advance.DepartmentId), ct);

        if (submitted.Failed) return Result.Fail<PaymentRequest>(submitted.Error!, submitted.Code);

        advance.Status = PaymentRequestStatus.Pending;
        advance.ApprovalRequestId = submitted.Value.Id;
        advance.SubmittedUtc = clock.UtcNow;
        advance.DecisionComment = null;

        await db.SaveChangesAsync(ct);
        return Result.Success(advance);
    }

    public async Task<Result<PaymentRequest>> DisburseAsync(
        int id, decimal amount, int fromAccountId, DateOnly date, CancellationToken ct = default)
    {
        var advance = await db.PaymentRequests.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (advance is null) return Result.Fail<PaymentRequest>("That advance no longer exists.", "advance.not-found");

        if (advance.Status is not PaymentRequestStatus.Approved)
            return Result.Fail<PaymentRequest>("Only an approved advance can be paid out.", "advance.not-approved");

        if (amount <= 0)
            return Result.Fail<PaymentRequest>("The amount must be more than nothing.", "advance.bad-amount");

        if (amount > advance.Amount)
        {
            // Handing over more than was approved defeats the approval.
            return Result.Fail<PaymentRequest>(
                $"{amount:N2} is more than the {advance.Amount:N2} that was approved.",
                "advance.over-disbursement");
        }

        var advanceAccount = await AdvanceAccountAsync(advance, ct);
        if (advanceAccount.Failed) return Result.Fail<PaymentRequest>(advanceAccount.Error!, advanceAccount.Code);

        // Dr the person's advance account, Cr cash. The money has left the
        // business but is not an expense yet - it is still theirs to account for.
        var posted = await vouchers.PostSystemVoucherAsync(new SystemVoucher(
            Type: VoucherType.Payment,
            Date: date,
            Narration: $"{advance.Reference}: advance to {advance.RequestedByName} — {advance.Title}",
            Lines:
            [
                new VoucherLineInput(advanceAccount.Value.Id, amount, 0,
                    advance.Title, advance.RequestedByUserId, advance.RequestedByName),
                new VoucherLineInput(fromAccountId, 0, amount, advance.RequestedByName)
            ],
            Module: FinanceModule.Key,
            DocumentType: DocumentType,
            DocumentId: advance.Id,
            DocumentReference: advance.Reference), ct);

        if (posted.Failed) return Result.Fail<PaymentRequest>(posted.Error!, posted.Code);

        advance.Status = PaymentRequestStatus.Disbursed;
        advance.DisbursedAmount = amount;
        advance.AdvanceAccountId = advanceAccount.Value.Id;
        advance.DisbursementVoucherId = posted.Value.Id;
        advance.DisbursedUtc = clock.UtcNow;

        await db.SaveChangesAsync(ct);
        return Result.Success(advance);
    }

    public async Task<Result<PaymentRequest>> JustifyAsync(
        int id, IReadOnlyList<AdvanceExpenseInput> expenses, CancellationToken ct = default)
    {
        var advance = await db.PaymentRequests.Include(a => a.Expenses)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (advance is null) return Result.Fail<PaymentRequest>("That advance no longer exists.", "advance.not-found");

        if (advance.Status is not (PaymentRequestStatus.Disbursed or PaymentRequestStatus.Justified))
        {
            return Result.Fail<PaymentRequest>(
                "Receipts can only be entered against an advance that has been paid out.",
                "advance.not-disbursed");
        }

        if (expenses.Count == 0)
            return Result.Fail<PaymentRequest>("Enter at least one receipt.", "advance.no-expenses");

        foreach (var expense in expenses)
        {
            if (expense.Amount <= 0)
                return Result.Fail<PaymentRequest>("Every receipt needs an amount.", "advance.bad-expense");

            if (string.IsNullOrWhiteSpace(expense.Description))
                return Result.Fail<PaymentRequest>("Every receipt needs a description.", "advance.no-description");

            if (expense.ExpenseAccountId is null)
                return Result.Fail<PaymentRequest>($"'{expense.Description}' needs a head to charge it to.", "advance.no-head");
        }

        db.AdvanceExpenses.RemoveRange(advance.Expenses);
        advance.Expenses.Clear();

        foreach (var expense in expenses)
        {
            advance.Expenses.Add(new AdvanceExpense
            {
                Date = expense.Date,
                Description = expense.Description,
                Amount = expense.Amount,
                ExpenseAccountId = expense.ExpenseAccountId,
                ReceiptNumber = expense.ReceiptNumber
            });
        }

        advance.JustifiedAmount = expenses.Sum(e => e.Amount);
        advance.Status = PaymentRequestStatus.Justified;
        advance.JustifiedUtc = clock.UtcNow;

        await db.SaveChangesAsync(ct);
        return Result.Success(advance);
    }

    public async Task<Result<PaymentRequest>> SettleAsync(
        int id, DifferenceHandling handling, int cashAccountId, DateOnly date,
        CancellationToken ct = default)
    {
        var advance = await db.PaymentRequests.Include(a => a.Expenses)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (advance is null) return Result.Fail<PaymentRequest>("That advance no longer exists.", "advance.not-found");

        if (advance.Status is not PaymentRequestStatus.Justified)
            return Result.Fail<PaymentRequest>("Only a justified advance can be settled.", "advance.not-justified");

        var advanceAccount = await AdvanceAccountAsync(advance, ct);
        if (advanceAccount.Failed) return Result.Fail<PaymentRequest>(advanceAccount.Error!, advanceAccount.Code);

        var disbursed = advance.DisbursedAmount ?? 0;
        var justified = advance.JustifiedAmount ?? 0;
        var difference = disbursed - justified;

        var lines = new List<VoucherLineInput>();

        // Every receipt lands on its own head. This is the moment the money
        // actually becomes an expense.
        foreach (var expense in advance.Expenses)
        {
            lines.Add(new VoucherLineInput(
                expense.ExpenseAccountId!.Value, expense.Amount, 0,
                expense.Description, advance.RequestedByUserId, advance.RequestedByName));
        }

        // The person's advance account is cleared of what they have accounted
        // for, so their outstanding balance stops including it.
        lines.Add(new VoucherLineInput(
            advanceAccount.Value.Id, 0, justified,
            $"Settling {advance.Reference}", advance.RequestedByUserId, advance.RequestedByName));

        if (difference != 0)
        {
            switch (handling)
            {
                case DifferenceHandling.SettleNow:
                    // The gap moves through cash now: money handed back, or paid
                    // out if they spent more than they were given.
                    lines.Add(difference > 0
                        ? new VoucherLineInput(cashAccountId, difference, 0, "Advance returned")
                        : new VoucherLineInput(cashAccountId, 0, -difference, "Advance shortfall paid"));

                    lines.Add(difference > 0
                        ? new VoucherLineInput(advanceAccount.Value.Id, 0, difference,
                            "Advance returned", advance.RequestedByUserId, advance.RequestedByName)
                        : new VoucherLineInput(advanceAccount.Value.Id, -difference, 0,
                            "Advance shortfall", advance.RequestedByUserId, advance.RequestedByName));
                    break;

                case DifferenceHandling.Outstanding when difference < 0:
                    // They are owed money. Left as a negative on the advance
                    // asset head it reads as though they still hold company
                    // cash; it belongs on a liability, because the company owes
                    // it back to them.
                    var payable = await PersonPayableAccountAsync(advance, ct);
                    if (payable.Failed) return Result.Fail<PaymentRequest>(payable.Error!, payable.Code);

                    lines.Add(new VoucherLineInput(
                        advanceAccount.Value.Id, -difference, 0,
                        "Advance overspend", advance.RequestedByUserId, advance.RequestedByName));

                    lines.Add(new VoucherLineInput(
                        payable.Value.Id, 0, -difference,
                        $"Owed to {advance.RequestedByName}", advance.RequestedByUserId, advance.RequestedByName));
                    break;

                case DifferenceHandling.Outstanding:
                case DifferenceHandling.RecoverFromPayroll:
                    // Unspent money they are still holding. The gap stays on
                    // their advance account, so the books keep showing it.
                    // Nothing extra is posted - the balance already says so.
                    break;
            }
        }

        var posted = await vouchers.PostSystemVoucherAsync(new SystemVoucher(
            Type: VoucherType.Journal,
            Date: date,
            Narration: $"{advance.Reference}: settling advance to {advance.RequestedByName}",
            Lines: lines,
            Module: FinanceModule.Key,
            DocumentType: DocumentType,
            DocumentId: advance.Id,
            DocumentReference: advance.Reference), ct);

        if (posted.Failed) return Result.Fail<PaymentRequest>(posted.Error!, posted.Code);

        advance.Status = PaymentRequestStatus.Settled;
        advance.SettlementVoucherId = posted.Value.Id;
        advance.SettledUtc = clock.UtcNow;
        advance.DifferenceHandling = handling;

        // Settled now means nothing is left outstanding; the other two leave the
        // gap on the person's account for ClearDifferenceAsync or payroll.
        advance.ClearedDifference = handling is DifferenceHandling.SettleNow ? difference : 0;

        await db.SaveChangesAsync(ct);
        return Result.Success(advance);
    }

    public async Task<Result<PaymentRequest>> ClearDifferenceAsync(
        int id, decimal amount, int cashAccountId, DateOnly date, CancellationToken ct = default)
    {
        var advance = await db.PaymentRequests.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (advance is null) return Result.Fail<PaymentRequest>("That advance no longer exists.", "advance.not-found");

        if (advance.Status is not PaymentRequestStatus.Settled)
            return Result.Fail<PaymentRequest>("This advance has not been settled yet.", "advance.not-settled");

        if (advance.OutstandingDifference == 0)
            return Result.Fail<PaymentRequest>("Nothing is outstanding on this advance.", "advance.nothing-outstanding");

        if (amount <= 0)
            return Result.Fail<PaymentRequest>("The amount must be more than nothing.", "advance.bad-amount");

        if (amount > Math.Abs(advance.OutstandingDifference))
        {
            return Result.Fail<PaymentRequest>(
                $"Only {Math.Abs(advance.OutstandingDifference):N2} is outstanding.",
                "advance.over-clearing");
        }

        var advanceAccount = await AdvanceAccountAsync(advance, ct);
        if (advanceAccount.Failed) return Result.Fail<PaymentRequest>(advanceAccount.Error!, advanceAccount.Code);

        var returning = advance.OutstandingDifference > 0;

        // Settling an outstanding overspend parked it on the person's payable,
        // so paying them clears that head - not the advance account, which
        // settlement already emptied.
        var owedAccount = advanceAccount;
        if (!returning)
        {
            owedAccount = await PersonPayableAccountAsync(advance, ct);
            if (owedAccount.Failed) return Result.Fail<PaymentRequest>(owedAccount.Error!, owedAccount.Code);
        }

        var posted = await vouchers.PostSystemVoucherAsync(new SystemVoucher(
            Type: returning ? VoucherType.Receipt : VoucherType.Payment,
            Date: date,
            Narration: $"{advance.Reference}: {(returning ? "returned by" : "paid to")} {advance.RequestedByName}",
            Lines: returning
                ? [
                    new VoucherLineInput(cashAccountId, amount, 0, "Advance returned"),
                    new VoucherLineInput(advanceAccount.Value.Id, 0, amount,
                        "Advance returned", advance.RequestedByUserId, advance.RequestedByName)
                  ]
                : [
                    new VoucherLineInput(owedAccount.Value.Id, amount, 0,
                        "Advance shortfall", advance.RequestedByUserId, advance.RequestedByName),
                    new VoucherLineInput(cashAccountId, 0, amount, "Advance shortfall paid")
                  ],
            Module: FinanceModule.Key,
            DocumentType: DocumentType,
            DocumentId: advance.Id,
            DocumentReference: advance.Reference), ct);

        if (posted.Failed) return Result.Fail<PaymentRequest>(posted.Error!, posted.Code);

        advance.ClearedDifference += returning ? amount : -amount;

        await db.SaveChangesAsync(ct);
        return Result.Success(advance);
    }

    /// <summary>
    /// The account an advance clears through.
    ///
    /// Once disbursed the advance keeps the account it was put on, because
    /// settling has to clear the same head the money went to. Only a fresh
    /// disbursement resolves a new one.
    /// </summary>
    private async Task<Result<Account>> AdvanceAccountAsync(PaymentRequest advance, CancellationToken ct)
    {
        if (advance.AdvanceAccountId is { } existing)
        {
            var held = await db.Accounts.FirstOrDefaultAsync(a => a.Id == existing, ct);
            if (held is not null) return Result.Success(held);
        }

        // Advances raised before per-person accounts existed were posted to the
        // shared head. They have to settle back to it, not to a new sub-account
        // that never received the disbursement.
        return advance.Status is PaymentRequestStatus.Draft or PaymentRequestStatus.Pending or PaymentRequestStatus.Approved
            ? await PersonAdvanceAccountAsync(advance, ct)
            : await SharedAccountAsync(advance.IsDirectorRequest, ct);
    }

    private async Task<Result<Account>> SharedAccountAsync(bool director, CancellationToken ct)
    {
        var code = director ? DirectorCapitalCode : AdvanceAccountCode;
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Code == code, ct);

        return account is null
            ? Result.Fail<Account>(
                $"The {code} account needed for this advance is missing from the chart of accounts.",
                "advance.no-account")
            : Result.Success(account);
    }

    /// <summary>
    /// This person's own advance account, created on first use.
    ///
    /// One shared advance head tells you the company is owed something and
    /// nothing about by whom; recovering that costs an afternoon with the
    /// voucher lines. A head per person makes the trial balance answer it.
    /// </summary>
    private async Task<Result<Account>> PersonAdvanceAccountAsync(PaymentRequest advance, CancellationToken ct)
    {
        var parent = await SharedAccountAsync(advance.IsDirectorRequest, ct);
        if (parent.Failed) return parent;

        var name = advance.IsDirectorRequest
            ? $"Director — {advance.RequestedByName}"
            : advance.RequestedByName;

        return await EnsureChildAccountAsync(parent.Value, name, advance.RequestedByUserId, ct);
    }

    private async Task<Result<Account>> EnsureChildAccountAsync(
        Account parent, string name, string personId, CancellationToken ct)
    {
        var existing = await db.Accounts
            .FirstOrDefaultAsync(a => a.ParentId == parent.Id && a.PersonId == personId, ct);

        if (existing is not null) return Result.Success(existing);

        var siblings = await db.Accounts.CountAsync(a => a.ParentId == parent.Id, ct);

        var child = new Account
        {
            Code = $"{parent.Code}-{(siblings + 1):D3}",
            Name = name,
            Type = parent.Type,
            ParentId = parent.Id,
            PersonId = personId,
            IsPostable = true,
            IsSystem = true,
            Description = $"Advances held by {name}."
        };

        db.Accounts.Add(child);

        // A heading with a balance of its own double-counts itself against its
        // children in every report, so the parent stops being postable the
        // moment it gains one.
        if (parent.IsPostable)
        {
            var used = await db.VoucherLines.AnyAsync(l => l.AccountId == parent.Id, ct);
            if (!used) parent.IsPostable = false;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(child);
    }

    /// <summary>
    /// This person's payable, for an overspend left outstanding.
    ///
    /// The company owes them, so it belongs on a liability head. Left as a
    /// negative on the advance asset account it reads as though they still hold
    /// money that is in fact owed back to them.
    /// </summary>
    private async Task<Result<Account>> PersonPayableAccountAsync(PaymentRequest advance, CancellationToken ct)
    {
        var parent = await db.Accounts.FirstOrDefaultAsync(a => a.Code == PayableAccountCode, ct);

        if (parent is null)
        {
            return Result.Fail<Account>(
                $"The {PayableAccountCode} account needed to record what is owed back is missing " +
                "from the chart of accounts.",
                "advance.no-payable-account");
        }

        return await EnsureChildAccountAsync(
            parent, $"Payable — {advance.RequestedByName}", advance.RequestedByUserId, ct);
    }

    private async Task<string> NextReferenceAsync(bool director, CancellationToken ct)
    {
        var year = clock.Today.Year;
        var stem = $"{(director ? "DFR" : "ADV")}-{year % 100:D2}-";
        var count = await db.PaymentRequests.IgnoreQueryFilters()
            .CountAsync(a => a.Reference.StartsWith(stem), ct);
        return stem + (count + 1).ToString().PadLeft(4, '0');
    }
}

/// <summary>How Finance hears that an advance request was decided.</summary>
public sealed class AdvanceApprovalSink(FinanceDbContext db) : IApprovalSink
{
    public string DocumentType => AdvanceService.DocumentType;

    public async Task<Result> OnSettledAsync(
        int documentId, ApprovalStatus status, ApprovalRequest request, CancellationToken ct = default)
    {
        var advance = await db.PaymentRequests.FirstOrDefaultAsync(a => a.Id == documentId, ct);
        if (advance is null)
            return Result.Fail("The advance behind this approval has gone.", "advance.not-found");

        advance.DecisionComment = request.Actions
            .OrderByDescending(a => a.ActedUtc)
            .Select(a => a.Comment)
            .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

        advance.Status = status switch
        {
            ApprovalStatus.Approved => PaymentRequestStatus.Approved,
            ApprovalStatus.Rejected => PaymentRequestStatus.Rejected,
            ApprovalStatus.Returned => PaymentRequestStatus.Returned,
            ApprovalStatus.Cancelled => PaymentRequestStatus.Cancelled,
            _ => advance.Status
        };

        return Result.Success();
    }
}
