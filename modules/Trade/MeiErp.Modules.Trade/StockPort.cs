using MeiErp.Platform.Kernel;

namespace MeiErp.Modules.Trade;

/// <summary>
/// What buying and selling need from whoever is holding the stock.
///
/// Trade owns the commercial documents; Inventory owns the goods and the stock
/// ledger. Rather than referencing that module directly, Trade states what it
/// needs and the host wires an adapter over Inventory - the same arrangement
/// Finance uses to read attendance out of HR. It keeps the two modules
/// independently buildable and testable, and it means a business that never
/// installs Inventory can still raise a purchase order against a stand-in.
///
/// Nothing here posts to Finance's ledger. The stock books remain standalone.
/// </summary>
public interface ITradeStockPort
{
    /// <summary>The books goods can be bought into or sold out of.</summary>
    Task<IReadOnlyList<StockBook>> BooksAsync(CancellationToken ct = default);

    /// <summary>The default book, for a screen that has not been told which one.</summary>
    Task<StockBook?> DefaultBookAsync(CancellationToken ct = default);

    /// <summary>Sellable goods in one book.</summary>
    Task<IReadOnlyList<TradeItem>> ItemsAsync(int bookId, string? search = null, CancellationToken ct = default);

    Task<TradeItem?> ItemAsync(int itemId, CancellationToken ct = default);

    /// <summary>
    /// Brings goods in and moves the weighted average. Staged, not saved: the
    /// caller owns the single atomic save covering the document and the stock.
    /// </summary>
    /// <param name="serialNumbers">
    /// Required, and required to be exactly <paramref name="quantity"/> of them,
    /// for a serialised item. Ignored otherwise.
    /// </param>
    /// <param name="batchNumber">Required for a batch-tracked item.</param>
    Task<Result> StageReceiptAsync(
        int itemId, decimal quantity, decimal unitCost, DateOnly date,
        string? reference, string? documentType, int? documentId,
        IReadOnlyList<string>? serialNumbers = null,
        string? batchNumber = null, DateOnly? expiresOn = null,
        CancellationToken ct = default);

    /// <summary>
    /// Takes goods out at the current weighted average, refusing to go negative.
    /// Staged, for the same reason as the receipt.
    /// </summary>
    /// <param name="serialNumbers">
    /// A serialised line must name exactly the units it ships. Issuing named
    /// units moves the quantity itself, so such a line must not also be
    /// quantity-adjusted - double-decrementing is the easy bug here.
    /// </param>
    Task<Result> StageIssueAsync(
        int itemId, decimal quantity, DateOnly date,
        string? reference, string? documentType, int? documentId,
        IReadOnlyList<string>? serialNumbers = null, string? issuedTo = null,
        CancellationToken ct = default);

    /// <summary>Commits whatever has been staged, together with the caller's own changes.</summary>
    Task SaveAsync(CancellationToken ct = default);

    /// <summary>
    /// Goods coming back. A customer return puts them back on the shelf; a
    /// supplier return sends them off it.
    ///
    /// Unlike a receipt or an issue, this posts on its own rather than staging:
    /// a return is a complete document in itself, with no separate header for
    /// Trade to save alongside it. The serial and batch rules it enforces live
    /// with the stock, which is why the whole operation stays on that side.
    /// </summary>
    Task<Result<TradeReturn>> PostReturnAsync(TradeReturnInput input, CancellationToken ct = default);

    /// <summary>Returns of one kind, newest first.</summary>
    Task<IReadOnlyList<TradeReturn>> ReturnsAsync(bool supplierReturns, CancellationToken ct = default);
}

/// <param name="SupplierReturn">
/// False sends goods back onto the shelf from a customer; true sends them off
/// it, back to a supplier.
/// </param>
public sealed record TradeReturnInput(
    bool SupplierReturn, int PartyId, string PartyName, DateOnly Date,
    string? SourceReference, string Reason, string? Notes,
    IReadOnlyList<TradeReturnLineInput> Lines);

public sealed record TradeReturnLineInput(
    int ItemId, decimal Quantity, string? SerialNumbers = null, string? BatchNumber = null);

/// <summary>A posted return, flattened for the screens that list it.</summary>
public sealed record TradeReturn(
    int Id, string Number, DateOnly Date, bool SupplierReturn,
    int PartyId, string PartyName, string? SourceReference,
    string Reason, int LineCount, decimal Total);

/// <summary>One set of stock books, as Trade needs to see it.</summary>
public sealed record StockBook(int Id, string Code, string Name, bool IsDefault);

/// <summary>
/// A sellable thing, flattened to what a commercial document needs. Trade never
/// sees the Inventory entity, so a change to how stock is modelled cannot ripple
/// into the order screens.
/// </summary>
public sealed record TradeItem(
    int Id,
    int BookId,
    string Code,
    string Name,
    string Unit,
    decimal QuantityOnHand,
    decimal AverageCost,
    decimal SellingPrice,
    bool IsSerialized,
    bool IsBatchTracked);
