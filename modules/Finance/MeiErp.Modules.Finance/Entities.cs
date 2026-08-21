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
    /// Which way a balance on this account normally sits. Assets and expenses
    /// are debit-natured; liabilities, equity and income are credit-natured.
    /// Used to present a balance as a positive number on the side it belongs.
    /// </summary>
    public bool IsDebitNatured =>
        Type is AccountType.Asset or AccountType.Expense;
}

public enum AccountType
{
    Asset = 0,
    Liability = 1,
    Equity = 2,
    Income = 3,
    Expense = 4
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

    public decimal Amount { get; set; }

    /// <summary>Which expense head this is charged to. Chosen by the raiser, confirmed by the accountant.</summary>
    public int? ExpenseAccountId { get; set; }
    public Account? ExpenseAccount { get; set; }

    /// <summary>Which cash or bank head it is paid out of. Chosen at payment time.</summary>
    public int? PaidFromAccountId { get; set; }
    public Account? PaidFromAccount { get; set; }

    public string RequestedByUserId { get; set; } = "";
    public string RequestedByName { get; set; } = "";
    public string? DepartmentId { get; set; }

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
        Status is PaymentRequestStatus.Draft
               or PaymentRequestStatus.Pending
               or PaymentRequestStatus.Returned
               or PaymentRequestStatus.Approved;
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
    Cancelled = 6
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
