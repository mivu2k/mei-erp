using MeiErp.Platform.Kernel;

namespace MeiErp.Modules.Inventory;

/// <summary>Something bought, held and sold.</summary>
public class Item : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    public int? CategoryId { get; set; }
    public ItemCategory? Category { get; set; }

    /// <summary>Each, box, metre. Free text because every trade names them differently.</summary>
    public string Unit { get; set; } = "each";

    /// <summary>
    /// Quantity on hand, kept as a running figure rather than summed from the
    /// ledger on every read. <see cref="StockMovement"/> is the truth; this is
    /// the cache, and <c>StockService</c> is the only thing allowed to move it.
    /// </summary>
    public decimal QuantityOnHand { get; set; }

    /// <summary>
    /// Weighted average cost. Moves on every purchase, never on a sale - which
    /// is why a delivery has to snapshot the cost it went out at.
    /// </summary>
    public decimal AverageCost { get; set; }

    /// <summary>What the last purchase actually cost, for spotting price drift.</summary>
    public decimal? LastCost { get; set; }

    public decimal SellingPrice { get; set; }

    /// <summary>Below this, the item shows on the reorder report.</summary>
    public decimal ReorderLevel { get; set; }

    public bool IsActive { get; set; } = true;

    public decimal StockValue => QuantityOnHand * AverageCost;
}

public class ItemCategory : AuditableEntity
{
    public string Name { get; set; } = "";
    public string? Code { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Every change in stock, ever. Append-only: a movement is never edited or
/// removed, because the quantity on hand is only trustworthy if the history
/// that produced it is complete.
/// </summary>
public class StockMovement : AuditableEntity
{
    public int ItemId { get; set; }
    public Item? Item { get; set; }

    /// <summary>Snapshotted so a movement report reads correctly after a rename.</summary>
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";

    public DateOnly Date { get; set; }

    public StockMovementType Type { get; set; }

    /// <summary>Signed: positive brings stock in, negative takes it out.</summary>
    public decimal Quantity { get; set; }

    /// <summary>What each unit cost on the way in, or was carried at on the way out.</summary>
    public decimal UnitCost { get; set; }

    /// <summary>Quantity on hand immediately after this movement, for auditing the running figure.</summary>
    public decimal BalanceAfter { get; set; }

    public string? Reference { get; set; }
    public string? Narration { get; set; }

    public string? SourceDocumentType { get; set; }
    public int? SourceDocumentId { get; set; }

    public decimal Value => Math.Abs(Quantity) * UnitCost;
}

public enum StockMovementType
{
    /// <summary>Bought in, against a goods receipt.</summary>
    Receipt = 0,

    /// <summary>Sold and delivered out.</summary>
    Delivery = 1,

    /// <summary>Counted and corrected.</summary>
    Adjustment = 2,

    /// <summary>Opening figure when the item was first set up.</summary>
    Opening = 3,

    /// <summary>Returned by a customer, back into stock.</summary>
    SalesReturn = 4,

    /// <summary>Sent back to the supplier.</summary>
    PurchaseReturn = 5
}

/// <summary>A supplier or a customer. One record, because the same company is often both.</summary>
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

    public bool IsActive { get; set; } = true;
}

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

    public int ItemId { get; set; }
    public Item? Item { get; set; }
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";

    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }

    /// <summary>How much has arrived so far. Never exceeds Quantity.</summary>
    public decimal Received { get; set; }

    public decimal LineTotal => Quantity * UnitCost;
    public decimal Outstanding => Quantity - Received;
}

/// <summary>
/// Goods physically arriving. This is what moves stock, not the order.
/// </summary>
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
    public Item? Item { get; set; }
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
