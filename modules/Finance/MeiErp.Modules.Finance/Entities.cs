using MeiErp.Platform.Kernel;

namespace MeiErp.Modules.Finance;

/// <summary>
/// A head in the chart of accounts.
///
/// The tree is unlimited in depth, but only leaves can be posted to: a parent
/// with its own balance and children that also have balances double-counts
/// itself in every report.
/// </summary>
public class Account : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    /// <summary>Numeric code, e.g. "1100". Sorts the chart and is what people say aloud.</summary>
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public AccountType Type { get; set; }

    public int? ParentId { get; set; }
    public Account? Parent { get; set; }
    public List<Account> Children { get; set; } = [];

    /// <summary>
    /// False for a heading that only groups others. Posting to a parent is
    /// refused rather than allowed and reconciled later.
    /// </summary>
    public bool IsPostable { get; set; } = true;

    /// <summary>
    /// Created by the system and depended on by code - the cash head a payment
    /// clears through, retained earnings. Cannot be deleted.
    /// </summary>
    public bool IsSystem { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Description { get; set; }

    /// <summary>
    /// Set on a head that belongs to one person - their advances, what is owed
    /// back to them. Matched on rather than the name, so renaming somebody does
    /// not strand the account they already have entries on.
    /// </summary>
    public string? PersonId { get; set; }

    /// <summary>
    /// Who may charge spend to this head when raising a request.
    ///
    /// A person claiming a taxi fare should be offered the handful of heads
    /// that apply to them, not the whole chart - and a director's categories
    /// are not the same list. Untagged heads are offered to nobody, so the
    /// picker stays short until somebody decides a head belongs on it.
    /// </summary>
    public ExpenseAudience Audience { get; set; } = ExpenseAudience.None;

    /// <summary>
    /// Which way a balance on this account normally sits. Assets and expenses
    /// are debit-natured; liabilities, equity and income are credit-natured.
    /// Used to present a balance as a positive number on the side it belongs.
    /// </summary>
    public bool IsDebitNatured =>
        Type is AccountType.Asset or AccountType.Expense;
}

/// <summary>Who is offered a head as a spending category. Flags, so one head can serve both.</summary>
[Flags]
public enum ExpenseAudience
{
    /// <summary>Not offered as a category. The default, so the chart stays out of the picker until tagged.</summary>
    None = 0,

    Staff = 1,
    Director = 2,

    Everyone = Staff | Director
}

public enum AccountType
{
    Asset = 0,
    Liability = 1,
    Equity = 2,
    Income = 3,
    Expense = 4
}

/// <summary>Maps one integration event to the two Finance accounts it affects.</summary>
public class PostingRule : AuditableEntity
{
    public string EventType { get; set; } = "";
    public string Name { get; set; } = "";
    public int DebitAccountId { get; set; }
    public Account? DebitAccount { get; set; }
    public int CreditAccountId { get; set; }
    public Account? CreditAccount { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// One accounting entry: a balanced set of debits and credits.
///
/// <b>Everything financial in the platform ends up here.</b> No module writes
/// financial state directly; they all post a voucher. That is the guarantee
/// that the books balance, and it is the single most important rule in this
/// codebase.
/// </summary>
public class Voucher : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    public string Number { get; set; } = "";

    public VoucherType Type { get; set; }

    /// <summary>The business date it belongs to, which is not the date it was typed.</summary>
    public DateOnly Date { get; set; }

    public string Narration { get; set; } = "";

    public VoucherStatus Status { get; set; } = VoucherStatus.Draft;

    public List<VoucherLine> Lines { get; set; } = [];

    /// <summary>Which module raised this, when it came from one. Null for a hand-typed voucher.</summary>
    public string? SourceModule { get; set; }
    public string? SourceDocumentType { get; set; }
    public int? SourceDocumentId { get; set; }
    public string? SourceReference { get; set; }
    /// <summary>Stable machine key used only for replay protection; unlike the printed reference it must be unique.</summary>
    public string? SourceIdempotencyKey { get; set; }

    public DateTime? PostedUtc { get; set; }
    public string? PostedBy { get; set; }

    /// <summary>Set when this voucher was reversed. Points at the reversing voucher.</summary>
    public int? ReversedByVoucherId { get; set; }

    /// <summary>Set on a reversing voucher, pointing back at what it undid.</summary>
    public int? ReversalOfVoucherId { get; set; }

    public decimal TotalDebit => Lines.Sum(l => l.Debit);
    public decimal TotalCredit => Lines.Sum(l => l.Credit);

    /// <summary>
    /// Compared to the cent. Floating point would make this intermittently
    /// false, which is why money is decimal everywhere.
    /// </summary>
    public bool IsBalanced => TotalDebit == TotalCredit;

    public bool IsPosted => Status is VoucherStatus.Posted;
}

public enum VoucherType
{
    Journal = 0,
    Payment = 1,
    Receipt = 2,
    Contra = 3,

    /// <summary>Closes income and expense into retained earnings at year end.</summary>
    Closing = 4
}

public enum VoucherStatus
{
    Draft = 0,

    /// <summary>In the books. Immutable from here on.</summary>
    Posted = 1,

    /// <summary>Reversed by a contra entry. The original stays exactly as it was.</summary>
    Reversed = 2
}

/// <summary>
/// One side of one entry. Exactly one of Debit or Credit carries a value.
/// </summary>
public class VoucherLine : Entity
{
    public int VoucherId { get; set; }
    public Voucher? Voucher { get; set; }

    public int AccountId { get; set; }
    public Account? Account { get; set; }

    /// <summary>Snapshotted so a ledger printed today still reads correctly after a rename.</summary>
    public string AccountCode { get; set; } = "";
    public string AccountName { get; set; } = "";

    public decimal Debit { get; set; }
    public decimal Credit { get; set; }

    public string? Narration { get; set; }

    /// <summary>
    /// Who the money is traceable to, where it is traceable to one person.
    /// This is what the ledger's person filter reads. An aggregated payroll
    /// voucher deliberately carries none.
    /// </summary>
    public string? PersonId { get; set; }
    public string? PersonName { get; set; }

    /// <summary>
    /// Which project and department the spend belongs to, stamped on the line
    /// rather than only on the document. A report that has to join back to the
    /// originating request cannot cover a hand-written voucher, and a spend
    /// report that silently omits those is worse than none.
    /// </summary>
    public string? ProjectId { get; set; }
    public string? DepartmentId { get; set; }

    public decimal SignedAmount => Debit - Credit;
}

/// <summary>
/// A request to spend money, routed through the platform approval engine and
/// turned into a payment voucher once approved and paid.
/// </summary>
public class PaymentRequest : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    public string Reference { get; set; } = "";

    public string Title { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>
    /// Whether the money is being claimed back or asked for up front.
    ///
    /// One record rather than two, because they are the same request at
    /// different points: an itemized claim arrives with its receipts, an advance
    /// arrives without them and produces them later. Splitting them meant asking
    /// people to pick a screen before they knew which they wanted.
    /// </summary>
    public PaymentRequestKind Kind { get; set; } = PaymentRequestKind.Itemized;

    public decimal Amount { get; set; }

    // ---- advance-only, all null until Kind is Advance and it has been paid out

    /// <summary>What was actually handed over. Usually the amount asked for.</summary>
    public decimal? DisbursedAmount { get; set; }

    /// <summary>What the receipts came to once they were produced.</summary>
    public decimal? JustifiedAmount { get; set; }

    /// <summary>The person's advance head, fixed at disbursement.</summary>
    public int? AdvanceAccountId { get; set; }
    public Account? AdvanceAccount { get; set; }

    public int? DisbursementVoucherId { get; set; }
    public int? SettlementVoucherId { get; set; }

    public DateTime? DisbursedUtc { get; set; }
    public DateTime? JustifiedUtc { get; set; }
    public DateTime? SettledUtc { get; set; }

    /// <summary>Where the disbursed-versus-justified gap was sent.</summary>
    public DifferenceHandling? DifferenceHandling { get; set; }

    /// <summary>How much of an outstanding difference has since been cleared.</summary>
    public decimal ClearedDifference { get; set; }

    /// <summary>
    /// Taken minus spent. Positive means they are holding money that is not
    /// theirs; negative means they spent more than they were given.
    /// </summary>
    public decimal? Difference =>
        DisbursedAmount is null || JustifiedAmount is null
            ? null
            : DisbursedAmount.Value - JustifiedAmount.Value;

    /// <summary>What is still owed after any partial clearing.</summary>
    public decimal OutstandingDifference => (Difference ?? 0) - ClearedDifference;

    /// <summary>Receipts produced against an advance. Empty for an itemized claim.</summary>
    public List<AdvanceExpense> Expenses { get; set; } = [];

    /// <summary>Which expense head this is charged to. Chosen by the raiser, confirmed by the accountant.</summary>
    public int? ExpenseAccountId { get; set; }
    public Account? ExpenseAccount { get; set; }

    /// <summary>Which cash or bank head it is paid out of. Chosen at payment time.</summary>
    public int? PaidFromAccountId { get; set; }
    public Account? PaidFromAccount { get; set; }

    public string RequestedByUserId { get; set; } = "";
    public string RequestedByName { get; set; } = "";
    public string? DepartmentId { get; set; }

    /// <summary>Which project the spend belongs to, carried onto the voucher lines.</summary>
    public string? ProjectId { get; set; }
    public string? ProjectName { get; set; }

    /// <summary>True for a director fund request, kept separate from ordinary staff payment requests.</summary>
    public bool IsDirectorRequest { get; set; }

    /// <summary>Who is being paid - a supplier, a member of staff, a landlord.</summary>
    public string? PayeeName { get; set; }

    public DateOnly NeededBy { get; set; }

    public PaymentRequestStatus Status { get; set; } = PaymentRequestStatus.Draft;

    public int? ApprovalRequestId { get; set; }

    /// <summary>The voucher raised when it was paid. Null until then.</summary>
    public int? VoucherId { get; set; }
    public Voucher? Voucher { get; set; }

    public DateTime? SubmittedUtc { get; set; }
    public DateTime? PaidUtc { get; set; }
    public string? DecisionComment { get; set; }

    public bool IsOpen =>
        Status is not (PaymentRequestStatus.Paid
                    or PaymentRequestStatus.Settled
                    or PaymentRequestStatus.Rejected
                    or PaymentRequestStatus.Cancelled);

    public List<PaymentRequestLine> Lines { get; set; } = [];
}

public enum PaymentRequestKind
{
    /// <summary>Known items and amounts, claimed back. Ends at Paid.</summary>
    Itemized = 0,

    /// <summary>A lump sum taken up front and accounted for later. Ends at Settled.</summary>
    Advance = 1
}

/// <summary>An itemized reimbursement line; ordinary advances remain lump-sum Advance records.</summary>
public class PaymentRequestLine : AuditableEntity
{
    public int PaymentRequestId { get; set; }
    public PaymentRequest? PaymentRequest { get; set; }
    public string? Category { get; set; }
    public decimal Amount { get; set; }
    public string? Reason { get; set; }
    public string? Description { get; set; }
    public int? ExpenseAccountId { get; set; }
    public Account? ExpenseAccount { get; set; }

    /// <summary>
    /// The receipt behind this line. Held in the database rather than on disk so
    /// one backup covers the claim and its evidence together - a receipt that
    /// went missing between the two is a claim nobody can defend at audit.
    /// </summary>
    public byte[]? Attachment { get; set; }
    public string? AttachmentName { get; set; }
    public string? AttachmentContentType { get; set; }

    public bool HasAttachment => Attachment is not null && Attachment.Length > 0;
}

public enum PaymentRequestStatus
{
    Draft = 0,
    Pending = 1,

    /// <summary>Signed off, but the money has not moved yet.</summary>
    Approved = 2,

    /// <summary>Paid, and a voucher exists. Terminal.</summary>
    Paid = 3,

    Rejected = 4,
    Returned = 5,
    Cancelled = 6,

    // ---- advances only, after the money is handed over

    /// <summary>Money handed over; receipts not yet produced.</summary>
    Disbursed = 7,

    /// <summary>Receipts entered, waiting for someone to accept them.</summary>
    Justified = 8,

    /// <summary>Closed. The gap has been dealt with one way or another. Terminal.</summary>
    Settled = 9
}

/// <summary>
/// An accounting period. Once closed, nothing may be posted into it - that is
/// what makes a signed-off trial balance stay signed off.
/// </summary>
public class FiscalYear : AuditableEntity
{
    public string Name { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsClosed { get; set; }
    public DateTime? ClosedUtc { get; set; }
    public string? ClosedBy { get; set; }

    /// <summary>The closing voucher that moved income and expense into retained earnings.</summary>
    public int? ClosingVoucherId { get; set; }

    public bool Contains(DateOnly date) => date >= StartDate && date <= EndDate;
}
