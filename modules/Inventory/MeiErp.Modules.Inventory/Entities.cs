using MeiErp.Platform.Kernel;

namespace MeiErp.Modules.Inventory;

/// <summary>Something bought, held and sold.</summary>
public class Item : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>
    /// Which set of stock books this item belongs to - the main store or the
    /// workshop's spares. An item lives in exactly one, which is what keeps the
    /// two valuations and the two reorder reports apart.
    /// </summary>
    public int DomainId { get; set; }
    public StockDomain? Domain { get; set; }

    public int? CategoryId { get; set; }
    public ItemCategory? Category { get; set; }
    public int? ProductFamilyId { get; set; }
    public ProductFamily? ProductFamily { get; set; }
    public InventoryItemKind Kind { get; set; }
    public int? ParentItemId { get; set; }
    public Item? ParentItem { get; set; }

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
    public decimal ReorderQuantity { get; set; }
    public string? Barcode { get; set; }
    public bool IsSerialized { get; set; }
    public bool IsBatchTracked { get; set; }

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

    /// <summary>
    /// The book this movement belongs to. Derivable from the item, but carried
    /// here as well so the stock ledger can be read one book at a time without
    /// joining every row back to the item - the same reason the code and name
    /// are snapshotted alongside.
    /// </summary>
    public int DomainId { get; set; }

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
    public int? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

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
