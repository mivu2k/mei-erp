using MeiErp.Modules.Inventory;
using MeiErp.Modules.Trade;
using MeiErp.Platform.Kernel;

namespace MeiErp.Host.Services;

/// <summary>
/// Lets Sales &amp; Purchase move stock that Inventory owns.
///
/// Trade states what it needs as <see cref="ITradeStockPort"/>; this is the only
/// code that knows both modules exist, and it lives in the host for that reason
/// - the same arrangement that lets Finance read attendance out of HR without
/// the two modules referencing each other.
///
/// Everything is staged rather than saved. A goods receipt and the stock
/// movement it causes have to commit together, so the caller owns the save.
/// </summary>
public sealed class InventoryStockAdapter(
    IStockService stock,
    ICatalogService catalog,
    IStockDomainService books,
    IStockTrackingService tracking,
    IInventoryReturnService returns,
    InventoryDbContext db) : ITradeStockPort
{
    public async Task<IReadOnlyList<StockBook>> BooksAsync(CancellationToken ct = default) =>
        (await books.ListAsync(ct))
            .Select(b => new StockBook(b.Id, b.Code, b.Name, b.IsDefault))
            .ToList();

    public async Task<StockBook?> DefaultBookAsync(CancellationToken ct = default)
    {
        var all = await BooksAsync(ct);
        return all.FirstOrDefault(b => b.IsDefault) ?? all.FirstOrDefault();
    }

    public async Task<IReadOnlyList<TradeItem>> ItemsAsync(
        int bookId, string? search = null, CancellationToken ct = default) =>
        (await catalog.ItemsAsync(search, false, bookId, ct)).Select(Flatten).ToList();

    public async Task<TradeItem?> ItemAsync(int itemId, CancellationToken ct = default) =>
        await catalog.GetItemAsync(itemId, ct) is { } item ? Flatten(item) : null;

    public async Task<Result> StageReceiptAsync(
        int itemId, decimal quantity, decimal unitCost, DateOnly date,
        string? reference, string? documentType, int? documentId,
        IReadOnlyList<string>? serialNumbers = null,
        string? batchNumber = null, DateOnly? expiresOn = null,
        CancellationToken ct = default)
    {
        // Serials and batches first: it refuses an incomplete serial list, and
        // failing before the quantity moves keeps the two consistent.
        var tracked = await tracking.StageReceiptAsync(
            new(itemId, quantity, unitCost, date, batchNumber, expiresOn, serialNumbers ?? [], reference), ct);
        if (tracked.Failed) return tracked;

        var moved = await stock.StageReceiptAsync(itemId, quantity, unitCost, date,
            StockMovementType.Receipt, reference, documentType, documentId, ct);

        // The movement itself is Inventory's business; Trade only needs to know
        // whether the stock moved.
        return moved.Ok ? Result.Success() : Result.Fail(moved.Error!, moved.Code);
    }

    public async Task<Result> StageIssueAsync(
        int itemId, decimal quantity, DateOnly date,
        string? reference, string? documentType, int? documentId,
        IReadOnlyList<string>? serialNumbers = null, string? issuedTo = null,
        CancellationToken ct = default)
    {
        var tracked = await tracking.StageIssueAsync(
            itemId, quantity, serialNumbers ?? [], date, issuedTo, reference, ct);
        if (tracked.Failed) return tracked;

        var moved = await stock.StageIssueAsync(itemId, quantity, date,
            StockMovementType.Delivery, reference, documentType, documentId, ct);

        return moved.Ok ? Result.Success() : Result.Fail(moved.Error!, moved.Code);
    }

    public Task SaveAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public async Task<Result<TradeReturn>> PostReturnAsync(
        TradeReturnInput input, CancellationToken ct = default)
    {
        var posted = await returns.PostAsync(new ReturnInput(
            input.SupplierReturn ? InventoryReturnKind.PurchaseReturn : InventoryReturnKind.SalesReturn,
            input.PartyId, input.PartyName, input.Date, input.SourceReference,
            input.Reason, input.Notes,
            [.. input.Lines.Select(l =>
                new ReturnLineInput(l.ItemId, l.Quantity, l.SerialNumbers, l.BatchNumber))]), ct);

        return posted.Ok
            ? Result.Success(Flatten(posted.Value))
            : Result.Fail<TradeReturn>(posted.Error!, posted.Code);
    }

    public async Task<IReadOnlyList<TradeReturn>> ReturnsAsync(
        bool supplierReturns, CancellationToken ct = default) =>
        (await returns.ListAsync(
            supplierReturns ? InventoryReturnKind.PurchaseReturn : InventoryReturnKind.SalesReturn, ct))
        .Select(Flatten).ToList();

    private static TradeReturn Flatten(InventoryReturn r) => new(
        r.Id, r.Number, r.Date, r.Kind == InventoryReturnKind.PurchaseReturn,
        r.PartyId, r.PartyName, r.SourceReference, r.Reason, r.Lines.Count, r.Total);

    /// <summary>
    /// Flattens the Inventory entity to what a commercial document needs. Trade
    /// never sees <c>Item</c> itself, so a change to how stock is modelled
    /// cannot ripple into the order screens.
    /// </summary>
    private static TradeItem Flatten(Item i) => new(
        i.Id, i.DomainId, i.Code, i.Name, i.Unit,
        i.QuantityOnHand, i.AverageCost, i.SellingPrice,
        i.IsSerialized, i.IsBatchTracked);
}
