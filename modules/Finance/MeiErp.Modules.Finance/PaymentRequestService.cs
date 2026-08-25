using MeiErp.Platform.Kernel;
using MeiErp.Platform.Workflow;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Finance;

public interface IPaymentRequestService
{
    Task<IReadOnlyList<PaymentRequest>> ListAsync(
        PaymentRequestStatus? status, bool mineOnly, bool directorOnly = false, CancellationToken ct = default);

    /// <summary>
    /// Every money request, both kinds together, for the one screen people
    /// actually work from. <paramref name="kind"/> null means both.
    /// </summary>
    Task<IReadOnlyList<PaymentRequest>> ListAllAsync(
        PaymentRequestKind? kind, PaymentRequestStatus? status, bool mineOnly,
        bool directorOnly = false, CancellationToken ct = default);

    Task<PaymentRequest?> GetAsync(int id, CancellationToken ct = default);
    Task<Result<PaymentRequest>> SaveDraftAsync(PaymentRequestInput input, CancellationToken ct = default);
    Task<Result<PaymentRequest>> SubmitAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Pays an approved request: posts the voucher and marks it paid. This is
    /// the moment money actually moves and the books change.
    /// </summary>
    /// <param name="classifications">
    /// Expense head per line id, assigned by the accountant at payment. Key 0
    /// classifies a request that has no lines. Null leaves whatever the raiser
    /// chose.
    /// </param>
    Task<Result<PaymentRequest>> PayAsync(
        int id, int paidFromAccountId, DateOnly date,
        IReadOnlyDictionary<int, int>? classifications = null, CancellationToken ct = default);

    /// <summary>The receipt attached to one request line, for download.</summary>
    Task<PaymentRequestLine?> AttachmentAsync(int lineId, CancellationToken ct = default);

    Task<Result> CancelAsync(int id, CancellationToken ct = default);
}

/// <summary>One project a spend can be charged against.</summary>
public sealed record ProjectOption(string Id, string Name);

/// <summary>One department a spend can be attributed to.</summary>
public sealed record DepartmentOption(string Id, string Name);

/// <summary>
/// The projects a payment request can be charged to.
///
/// Projects belong to Tender, and Finance must not reference a business module,
/// so it states what it needs and the host supplies it. A deployment without
/// Tender gets an empty list and the picker simply does not appear.
/// </summary>
public interface IFinanceProjectDirectory
{
    Task<IReadOnlyList<ProjectOption>> ActiveProjectsAsync(CancellationToken ct = default);

    /// <summary>
    /// Departments, which Identity owns. Same reasoning: Finance names what it
    /// needs and the host supplies it.
    /// </summary>
    Task<IReadOnlyList<DepartmentOption>> DepartmentsAsync(CancellationToken ct = default);
}

/// <summary>Used when no project source is registered.</summary>
public sealed class NoProjectDirectory : IFinanceProjectDirectory
{
    public Task<IReadOnlyList<ProjectOption>> ActiveProjectsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ProjectOption>>([]);

    public Task<IReadOnlyList<DepartmentOption>> DepartmentsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DepartmentOption>>([]);
}

public sealed record PaymentRequestInput(
    int? Id, string Title, string? Description, decimal Amount,
    int? ExpenseAccountId, string? PayeeName, DateOnly NeededBy, string? DepartmentId,
    bool IsDirectorRequest = false,
    IReadOnlyList<PaymentRequestLineInput>? Lines = null,
    string? ProjectId = null, string? ProjectName = null);

public sealed record PaymentRequestLineInput(
    string? Category, decimal Amount, string? Reason, string? Description, int? ExpenseAccountId,
    byte[]? Attachment = null, string? AttachmentName = null, string? AttachmentContentType = null);

public sealed class PaymentRequestService(
    FinanceDbContext db,
    IVoucherService vouchers,
    IApprovalEngine approvals,
    ICurrentUser currentUser,
    IClock clock) : IPaymentRequestService
{
    public const string DocumentType = "finance.payment-request";

    public async Task<IReadOnlyList<PaymentRequest>> ListAsync(
        PaymentRequestStatus? status, bool mineOnly, bool directorOnly = false, CancellationToken ct = default)
    {
        // Advances share this table but run a different lifecycle and have
        // their own screen. Without this filter each list shows the other's
        // documents alongside actions that do not apply to them.
        var query = db.PaymentRequests.AsNoTracking()
            .Include(r => r.ExpenseAccount)
            .Include(r => r.Lines).ThenInclude(l => l.ExpenseAccount)
            .Where(r => r.Kind == PaymentRequestKind.Itemized)
            .AsQueryable();

        if (status is not null) query = query.Where(r => r.Status == status);

        if (mineOnly)
        {
            var me = currentUser.UserId ?? "";
            query = query.Where(r => r.RequestedByUserId == me);
        }

        query = query.Where(r => r.IsDirectorRequest == directorOnly);

        return await query.OrderByDescending(r => r.Id).Take(500).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PaymentRequest>> ListAllAsync(
        PaymentRequestKind? kind, PaymentRequestStatus? status, bool mineOnly,
        bool directorOnly = false, CancellationToken ct = default)
    {
        var query = db.PaymentRequests.AsNoTracking()
            .Include(r => r.ExpenseAccount)
            .Include(r => r.Lines)
            .AsQueryable();

        if (kind is not null) query = query.Where(r => r.Kind == kind);
        if (status is not null) query = query.Where(r => r.Status == status);

        if (mineOnly)
        {
            var me = currentUser.UserId ?? "";
            query = query.Where(r => r.RequestedByUserId == me);
        }

        query = query.Where(r => r.IsDirectorRequest == directorOnly);

        return await query.OrderByDescending(r => r.Id).Take(500).ToListAsync(ct);
    }

    public Task<PaymentRequest?> GetAsync(int id, CancellationToken ct = default) =>
        db.PaymentRequests
          .Include(r => r.ExpenseAccount)
          .Include(r => r.PaidFromAccount)
          .Include(r => r.Voucher)
          .Include(r => r.Lines).ThenInclude(l => l.ExpenseAccount)
          .FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<Result<PaymentRequest>> SaveDraftAsync(
        PaymentRequestInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Title))
            return Result.Fail<PaymentRequest>("Say what the money is for.", "request.no-title");

        if (input.Amount <= 0 && (input.Lines is null || input.Lines.Count == 0))
            return Result.Fail<PaymentRequest>("The amount must be more than nothing.", "request.bad-amount");

        if (input.Lines is not null)
        {
            if (input.Lines.Count == 0)
                return Result.Fail<PaymentRequest>("Add at least one item to an itemized request.", "request.no-lines");
            if (input.Lines.Any(l => l.Amount <= 0))
                return Result.Fail<PaymentRequest>("Every request item needs a positive amount.", "request.bad-line-amount");
            if (input.Lines.Any(l => string.IsNullOrWhiteSpace(l.Reason)))
                return Result.Fail<PaymentRequest>("Every request item needs a reason.", "request.no-line-reason");
        }

        PaymentRequest request;

        if (input.Id is null or 0)
        {
            request = new PaymentRequest
            {
                Reference = await NextReferenceAsync(input.IsDirectorRequest, ct),
                RequestedByUserId = currentUser.UserId ?? "",
                RequestedByName = currentUser.Name ?? "",
                Status = PaymentRequestStatus.Draft
            };
            db.PaymentRequests.Add(request);
        }
        else
        {
            var existing = await db.PaymentRequests.Include(r => r.Lines)
                .FirstOrDefaultAsync(r => r.Id == input.Id, ct);
            if (existing is null)
                return Result.Fail<PaymentRequest>("That request no longer exists.", "request.not-found");

            if (existing.Status is not (PaymentRequestStatus.Draft or PaymentRequestStatus.Returned))
            {
                // Changing the amount under an approver is how someone signs off
                // one figure and authorises another.
                return Result.Fail<PaymentRequest>(
                    "This has already been submitted and cannot be edited. Withdraw it first.",
                    "request.not-editable");
            }

            request = existing;
        }

        request.Title = input.Title;
        request.Description = input.Description;
        request.Amount = input.Amount;
        request.ExpenseAccountId = input.ExpenseAccountId;
        request.PayeeName = input.PayeeName;
        request.NeededBy = input.NeededBy;
        request.DepartmentId = input.DepartmentId;
        request.ProjectId = input.ProjectId;
        request.ProjectName = input.ProjectName;
        request.IsDirectorRequest = input.IsDirectorRequest;

        if (input.Lines is not null)
        {
            db.PaymentRequestLines.RemoveRange(request.Lines);
            request.Lines = input.Lines.Select(line => new PaymentRequestLine
            {
                Category = line.Category,
                Amount = line.Amount,
                Reason = line.Reason,
                Description = line.Description,
                ExpenseAccountId = line.ExpenseAccountId,
                Attachment = line.Attachment,
                AttachmentName = line.AttachmentName,
                AttachmentContentType = line.AttachmentContentType
            }).ToList();
            request.Amount = request.Lines.Sum(line => line.Amount);
            request.ExpenseAccountId = request.Lines.Count == 1
                ? request.Lines[0].ExpenseAccountId
                : null;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(request);
    }

    public async Task<Result<PaymentRequest>> SubmitAsync(int id, CancellationToken ct = default)
    {
        var request = await db.PaymentRequests.Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null)
            return Result.Fail<PaymentRequest>("That request no longer exists.", "request.not-found");

        if (request.Status is not (PaymentRequestStatus.Draft or PaymentRequestStatus.Returned))
            return Result.Fail<PaymentRequest>("This has already been submitted.", "request.already-submitted");

        var submitted = await approvals.SubmitAsync(new SubmitApproval(
            ModuleKey: FinanceModule.Key,
            DocumentType: DocumentType,
            DocumentId: request.Id,
            DocumentReference: request.Reference,
            Summary: (request.IsDirectorRequest ? "Director fund: " : "") + $"{request.Title} — {request.Amount:N2}" +
                     (request.PayeeName is null ? "" : $" to {request.PayeeName}"),
            DocumentUrl: $"/finance/requests/{request.Id}",

            // The amount is what drives band routing: under 50,000 one
            // signature, above it more. That is the whole point of the engine.
            Amount: request.Amount,
            Currency: "PKR",
            DepartmentId: request.DepartmentId), ct);

        if (submitted.Failed)
            return Result.Fail<PaymentRequest>(submitted.Error!, submitted.Code);

        request.Status = PaymentRequestStatus.Pending;
        request.ApprovalRequestId = submitted.Value.Id;
        request.SubmittedUtc = clock.UtcNow;
        request.DecisionComment = null;

        await db.SaveChangesAsync(ct);
        return Result.Success(request);
    }

    public async Task<Result<PaymentRequest>> PayAsync(
        int id, int paidFromAccountId, DateOnly date,
        IReadOnlyDictionary<int, int>? classifications = null, CancellationToken ct = default)
    {
        var request = await db.PaymentRequests.Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null)
            return Result.Fail<PaymentRequest>("That request no longer exists.", "request.not-found");

        if (request.Status is not PaymentRequestStatus.Approved)
        {
            // Paying something unapproved is exactly what the approval engine
            // exists to prevent.
            return Result.Fail<PaymentRequest>(
                "Only an approved request can be paid.", "request.not-approved");
        }

        // The accountant classifies at payment, which is the point of this
        // parameter: the person claiming a taxi fare should not have to know
        // which head it belongs under, and guessing is worse than leaving it to
        // somebody who knows the chart.
        if (classifications is not null)
        {
            foreach (var line in request.Lines)
            {
                if (classifications.TryGetValue(line.Id, out var accountId))
                    line.ExpenseAccountId = accountId;
            }

            if (classifications.TryGetValue(0, out var headerAccountId))
                request.ExpenseAccountId = headerAccountId;
        }

        if (request.ExpenseAccountId is null && request.Lines.Count == 0)
            return Result.Fail<PaymentRequest>("Choose which expense head this is charged to.", "request.no-expense-head");

        if (request.Lines.Any(line => line.ExpenseAccountId is null))
            return Result.Fail<PaymentRequest>("Assign an expense head to every request item before paying.", "request.no-line-expense-head");

        if (request.VoucherId is not null)
            return Result.Fail<PaymentRequest>("This has already been paid.", "request.already-paid");

        // Dr expense, Cr cash. Everything financial goes through the ledger -
        // nothing here writes a balance directly.
        var posted = await vouchers.PostSystemVoucherAsync(new SystemVoucher(
            Type: VoucherType.Payment,
            Date: date,
            Narration: $"{request.Reference}: {request.Title}" +
                       (request.PayeeName is null ? "" : $" — {request.PayeeName}"),
            Lines:
            [
                // Project and department ride on every expense line, so spend
                // reports can group by them without joining back to the request
                // - which would miss every voucher raised by hand.
                ..(request.Lines.Count > 0
                    ? request.Lines.Select(line => new VoucherLineInput(
                        line.ExpenseAccountId!.Value, line.Amount, 0,
                        line.Reason ?? line.Description ?? line.Category ?? request.Title,
                        request.RequestedByUserId, request.RequestedByName,
                        request.ProjectId, request.DepartmentId))
                    : [new VoucherLineInput(request.ExpenseAccountId!.Value, request.Amount, 0,
                        request.Title, request.RequestedByUserId, request.RequestedByName,
                        request.ProjectId, request.DepartmentId)]),
                new VoucherLineInput(paidFromAccountId, 0, request.Amount, request.PayeeName)
            ],
            Module: FinanceModule.Key,
            DocumentType: DocumentType,
            DocumentId: request.Id,
            DocumentReference: request.Reference), ct);

        if (posted.Failed)
            return Result.Fail<PaymentRequest>(posted.Error!, posted.Code);

        request.Status = PaymentRequestStatus.Paid;
        request.VoucherId = posted.Value.Id;
        request.PaidFromAccountId = paidFromAccountId;
        request.PaidUtc = clock.UtcNow;

        await db.SaveChangesAsync(ct);
        return Result.Success(request);
    }

    public Task<PaymentRequestLine?> AttachmentAsync(int lineId, CancellationToken ct = default) =>
        db.PaymentRequestLines.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lineId, ct);

    public async Task<Result> CancelAsync(int id, CancellationToken ct = default)
    {
        var request = await db.PaymentRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null) return Result.Fail("That request no longer exists.", "request.not-found");

        if (request.Status is PaymentRequestStatus.Paid)
        {
            // The money has gone. Reverse the voucher instead - cancelling would
            // leave a payment in the books with nothing explaining it.
            return Result.Fail(
                "This has been paid. Reverse its voucher instead of cancelling the request.",
                "request.already-paid");
        }

        if (!request.IsOpen) return Result.Fail("This has already been decided.", "request.not-open");

        if (request.Status is PaymentRequestStatus.Pending && request.ApprovalRequestId is not null)
            return await approvals.CancelAsync(request.ApprovalRequestId.Value, "Withdrawn by the requester", ct);

        request.Status = PaymentRequestStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<string> NextReferenceAsync(bool director, CancellationToken ct)
    {
        var year = clock.Today.Year;
        var stem = $"{(director ? "DFR" : "PR")}-{year % 100:D2}-";
        var count = await db.PaymentRequests
            .IgnoreQueryFilters()
            .CountAsync(r => r.Reference.StartsWith(stem), ct);
        return stem + (count + 1).ToString().PadLeft(4, '0');
    }
}

/// <summary>
/// How Finance hears that a payment request was decided.
///
/// Approval does not move money - it only authorises it. The voucher is posted
/// later, when someone actually pays. Keeping those separate is what lets an
/// approved request wait for funds without the books claiming it was settled.
/// </summary>
public sealed class PaymentRequestApprovalSink(FinanceDbContext db) : IApprovalSink
{
    public string DocumentType => PaymentRequestService.DocumentType;

    public async Task<Result> OnSettledAsync(
        int documentId, ApprovalStatus status, ApprovalRequest request, CancellationToken ct = default)
    {
        var payment = await db.PaymentRequests.FirstOrDefaultAsync(r => r.Id == documentId, ct);
        if (payment is null)
            return Result.Fail("The payment request behind this approval has gone.", "request.not-found");

        payment.DecisionComment = request.Actions
            .OrderByDescending(a => a.ActedUtc)
            .Select(a => a.Comment)
            .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

        payment.Status = status switch
        {
            ApprovalStatus.Approved => PaymentRequestStatus.Approved,
            ApprovalStatus.Rejected => PaymentRequestStatus.Rejected,
            ApprovalStatus.Returned => PaymentRequestStatus.Returned,
            ApprovalStatus.Cancelled => PaymentRequestStatus.Cancelled,
            _ => payment.Status
        };

        return Result.Success();
    }
}
