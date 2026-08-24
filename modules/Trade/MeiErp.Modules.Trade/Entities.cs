using MeiErp.Platform.Kernel;

namespace MeiErp.Modules.Trade;

/// <summary>
/// Somebody the business trades with. One record, because the same company is
/// very often both a customer and a supplier, and because a supplier who has to
/// be created twice to be paid once is how duplicate ledgers start.
///
/// This is the platform's single trading master. It replaced Inventory's own
/// party list and the workshop's separate customer and supplier lists; the
/// sides are flags on one row rather than three tables.
/// </summary>
public class Party : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    public string Code { get; set; } = "";
    public string Name { get; set; } = "";

    public bool IsCustomer { get; set; }
    public bool IsSupplier { get; set; }

    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? TaxNumber { get; set; }

    /// <summary>Net terms. Null means nothing agreed, which is not the same as zero.</summary>
    public int? PaymentTermDays { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Reads as "Customer", "Supplier" or "Customer &amp; supplier".</summary>
    public string Sides => (IsCustomer, IsSupplier) switch
    {
        (true, true) => "Customer & supplier",
        (true, false) => "Customer",
        (false, true) => "Supplier",
        _ => "Neither"
    };
}

// ---------------------------------------------------------------- buying

/// <summary>
/// An order placed on a supplier. A commitment, not a stock movement - nothing
/// moves until the goods actually arrive and a receipt is posted.
/// </summary>
public class PurchaseOrder : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    public string Number { get; set; } = "";
    public DateOnly Date { get; set; }

    public int PartyId { get; set; }
    public Party? Party { get; set; }
    public string PartyName { get; set; } = "";

    /// <summary>
    /// Which stock book the goods are being bought into - the main store or the
    /// workshop's spares. Fixed on the order because every line's item has to
    /// belong to it.
    /// </summary>
    public int DomainId { get; set; }

    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;

    public List<PurchaseOrderLine> Lines { get; set; } = [];

    public string? Notes { get; set; }

    public int? ApprovalRequestId { get; set; }
    public string? DecisionComment { get; set; }

    public decimal Total => Lines.Sum(l => l.LineTotal);

    /// <summary>True once every line has been received in full.</summary>
    public bool IsFullyReceived => Lines.Count > 0 && Lines.All(l => l.Received >= l.Quantity);
}

public enum PurchaseOrderStatus
{
    Draft = 0,
    Pending = 1,
    Approved = 2,

    /// <summary>Some but not all goods have arrived.</summary>
    PartiallyReceived = 3,

    Received = 4,
    Rejected = 5,
    Returned = 6,
    Cancelled = 7
}

public class PurchaseOrderLine : Entity
{
    public int PurchaseOrderId { get; set; }
    public PurchaseOrder? Order { get; set; }

    /// <summary>Points at an Inventory item. No foreign key - a different module owns it.</summary>
    public int ItemId { get; set; }
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";

    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }

    /// <summary>How much has arrived so far. Never exceeds Quantity.</summary>
    public decimal Received { get; set; }

    public decimal LineTotal => Quantity * UnitCost;
    public decimal Outstanding => Quantity - Received;
}

/// <summary>Goods physically arriving. This is what moves stock, not the order.</summary>
public class GoodsReceipt : AuditableEntity
{
    public string Number { get; set; } = "";
    public DateOnly Date { get; set; }

    public int PurchaseOrderId { get; set; }
    public PurchaseOrder? Order { get; set; }

    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";

    public List<GoodsReceiptLine> Lines { get; set; } = [];

    public string? Notes { get; set; }

    public decimal Total => Lines.Sum(l => l.Quantity * l.UnitCost);
}

public class GoodsReceiptLine : Entity
{
    public int GoodsReceiptId { get; set; }
    public GoodsReceipt? Receipt { get; set; }

    public int ItemId { get; set; }
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";

    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
}

// ---------------------------------------------------------------- selling

/// <summary>
/// An order taken from a customer.
///
/// Confirming one reserves nothing. A soft reservation the stock figure does
/// not honour is worse than none, because two orders can still be promised the
/// same unit while both look safe.
/// </summary>
public class SalesOrder : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    public string Number { get; set; } = "";
    public DateOnly Date { get; set; }

    public int PartyId { get; set; }
    public Party? Party { get; set; }
    public string PartyName { get; set; } = "";

    /// <summary>Which stock book the goods are being sold out of.</summary>
    public int DomainId { get; set; }

    public SalesOrderStatus Status { get; set; } = SalesOrderStatus.Draft;

    public List<SalesOrderLine> Lines { get; set; } = [];

    public string? Notes { get; set; }

    public decimal Total => Lines.Sum(l => l.LineTotal);

    public bool IsFullyDelivered => Lines.Count > 0 && Lines.All(l => l.Delivered >= l.Quantity);
}

public enum SalesOrderStatus
{
    Draft = 0,
    Confirmed = 1,
    PartiallyDelivered = 2,
    Delivered = 3,
    Cancelled = 4
}

public class SalesOrderLine : Entity
{
    public int SalesOrderId { get; set; }
    public SalesOrder? Order { get; set; }

    public int ItemId { get; set; }
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    public decimal Delivered { get; set; }

    /// <summary>
    /// What the goods cost, snapshotted when they went out.
    ///
    /// The weighted average moves with the next purchase, so a margin worked
    /// out live would silently rewrite itself. There is a test for exactly that.
    /// </summary>
    public decimal UnitCost { get; set; }

    public decimal LineTotal => Quantity * UnitPrice;
    public decimal Outstanding => Quantity - Delivered;
    public decimal Margin => (UnitPrice - UnitCost) * Delivered;
}

/// <summary>Goods physically leaving. This is what moves stock, not the order.</summary>
public class Delivery : AuditableEntity
{
    public string Number { get; set; } = "";
    public DateOnly Date { get; set; }

    public int SalesOrderId { get; set; }
    public SalesOrder? Order { get; set; }

    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";

    /// <summary>Who signed for it. A delivery note is signed against a name.</summary>
    public string? CollectedBy { get; set; }

    public List<DeliveryLine> Lines { get; set; } = [];

    public string? Notes { get; set; }
}

public class DeliveryLine : Entity
{
    public int DeliveryId { get; set; }
    public Delivery? Delivery { get; set; }

    public int ItemId { get; set; }
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    /// <summary>Weighted average at the moment of posting. Frozen deliberately.</summary>
    public decimal UnitCost { get; set; }

    public decimal Margin => (UnitPrice - UnitCost) * Quantity;
}
