using MeiErp.Platform.Kernel;

namespace MeiErp.Modules.Trade;

/// <summary>
/// Which way round a commercial document faces.
///
/// A quotation the business sends a customer and one a supplier sends the
/// business are the same document read from opposite ends: lines, totals, tax,
/// validity, an approval. Writing them twice would mean fixing every pricing
/// bug twice, which is the duplication this module exists to remove. So there
/// is one implementation and this tells it which side it is on - and which
/// module's permissions and screens it belongs to.
/// </summary>
public enum TradeDirection
{
    /// <summary>Facing a customer. Owned by the Sales module.</summary>
    Sales = 0,

    /// <summary>Facing a supplier. Owned by the Purchase module.</summary>
    Purchase = 1
}

/// <summary>
/// The lifecycle every commercial document shares.
///
/// The important line is between <see cref="Draft"/> and everything after it. A
/// draft is a working note: editable, deletable, committing nobody. Once it is
/// submitted it is a real document with a number people quote at each other, so
/// it stops being editable and corrections happen by cancelling and reissuing.
/// </summary>
public enum DocumentStatus
{
    /// <summary>Being written. Editable, and binds nobody.</summary>
    Draft = 0,

    /// <summary>Submitted and waiting on whoever has to sign it off.</summary>
    PendingApproval = 1,

    /// <summary>Signed off. Ready to be sent, or turned into the next document.</summary>
    Approved = 2,

    /// <summary>Sent out to the other party, awaiting their answer.</summary>
    Sent = 3,

    /// <summary>The other party said yes. A quotation can now become an order.</summary>
    Accepted = 4,

    /// <summary>Turned down - by the approver, or by the other party.</summary>
    Rejected = 5,

    /// <summary>Handed back for correction. Editable again.</summary>
    Returned = 6,

    /// <summary>Committed to the books. Terminal for an invoice.</summary>
    Posted = 7,

    Cancelled = 8
}

public static class DocumentStatusRules
{
    /// <summary>A document nobody outside has seen yet can still be changed.</summary>
    public static bool IsEditable(this DocumentStatus s) =>
        s is DocumentStatus.Draft or DocumentStatus.Returned;

    /// <summary>Nothing further can happen to these.</summary>
    public static bool IsClosed(this DocumentStatus s) =>
        s is DocumentStatus.Rejected or DocumentStatus.Cancelled or DocumentStatus.Posted;
}

/// <summary>
/// A price offered, in either direction: one the business quotes a customer, or
/// one a supplier quotes the business.
/// </summary>
public class Quotation : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    public string Number { get; set; } = "";
    public TradeDirection Direction { get; set; }

    public DateOnly Date { get; set; }

    /// <summary>After this the prices are no longer being stood behind.</summary>
    public DateOnly? ValidUntil { get; set; }

    public int PartyId { get; set; }
    public Party? Party { get; set; }
    public string PartyName { get; set; } = "";

    /// <summary>Which stock book the goods come from. Every line must belong to it.</summary>
    public int DomainId { get; set; }

    /// <summary>
    /// The workshop job this was quoted for, if any. No foreign key - Repair is
    /// another module and another schema - so this is an id plus a snapshot of
    /// what to call it on screen.
    /// </summary>
    public int? JobId { get; set; }
    public string? JobReference { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

    public decimal TaxPercent { get; set; }
    public decimal Discount { get; set; }

    public string? Notes { get; set; }
    public string? Terms { get; set; }

    public int? ApprovalRequestId { get; set; }
    public string? DecisionComment { get; set; }

    /// <summary>Set when this became an order, so the chain reads forwards.</summary>
    public int? ConvertedToOrderId { get; set; }

    public List<QuotationLine> Lines { get; set; } = [];

    public decimal Subtotal => Lines.Sum(l => l.LineTotal);
    public decimal Taxable => Math.Max(0, Subtotal - Discount);
    public decimal Tax => Math.Round(Taxable * TaxPercent / 100m, 2);
    public decimal Total => Taxable + Tax;

    public bool IsExpiredOn(DateOnly today) => ValidUntil is { } d && d < today;
}

public class QuotationLine : Entity
{
    public int QuotationId { get; set; }
    public Quotation? Quotation { get; set; }

    /// <summary>
    /// The stock item, when there is one. Null for labour or a one-off charge,
    /// which is why the description carries the meaning rather than the link.
    /// </summary>
    public int? ItemId { get; set; }
    public string? ItemCode { get; set; }

    public string Description { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }

    public decimal LineTotal => Math.Round(Quantity * UnitPrice, 2);
}

/// <summary>
/// The bill. A sales invoice is money owed to the business; a purchase invoice
/// is money the business owes.
/// </summary>
public class Invoice : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    public string Number { get; set; } = "";
    public TradeDirection Direction { get; set; }

    public DateOnly Date { get; set; }
    public DateOnly? DueDate { get; set; }

    public int PartyId { get; set; }
    public Party? Party { get; set; }
    public string PartyName { get; set; } = "";

    public int DomainId { get; set; }

    /// <summary>The order this was billed from, when it came from one.</summary>
    public int? SalesOrderId { get; set; }
    public int? PurchaseOrderId { get; set; }

    /// <summary>Their invoice number, on a purchase invoice.</summary>
    public string? TheirReference { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

    public decimal TaxPercent { get; set; }
    public decimal Discount { get; set; }

    /// <summary>
    /// Settled so far. Owned by whatever records receipts and payments; an edit
    /// screen must never set it, or the balance stops meaning anything.
    /// </summary>
    public decimal AmountSettled { get; set; }

    public string? Notes { get; set; }

    public int? ApprovalRequestId { get; set; }
    public string? DecisionComment { get; set; }

    public List<InvoiceLine> Lines { get; set; } = [];

    public decimal Subtotal => Lines.Sum(l => l.LineTotal);
    public decimal Taxable => Math.Max(0, Subtotal - Discount);
    public decimal Tax => Math.Round(Taxable * TaxPercent / 100m, 2);
    public decimal Total => Taxable + Tax;
    public decimal Balance => Total - AmountSettled;

    public bool IsSettled => Balance <= 0;

    /// <summary>Overdue only once posted: a draft owes nobody anything.</summary>
    public bool IsOverdueOn(DateOnly today) =>
        Status == DocumentStatus.Posted && !IsSettled && DueDate is { } d && d < today;
}

public class InvoiceLine : Entity
{
    public int InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    public int? ItemId { get; set; }
    public string? ItemCode { get; set; }

    public string Description { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }

    public decimal LineTotal => Math.Round(Quantity * UnitPrice, 2);
}
