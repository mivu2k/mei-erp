using MeiErp.Platform.Kernel;

namespace MeiErp.Modules.Trade.Tests;

/// <summary>
/// A stand-in for whoever is holding the stock.
///
/// Trade's rules - an order is a commitment, nothing is reserved, more cannot
/// be received than was ordered, the cost is snapshotted at posting - are its
/// own, and testing them through a real Inventory database would be testing
/// somebody else's arithmetic at the same time. The weighted average and the
/// stock ledger have their own tests in the Inventory suite.
///
/// It does keep enough behaviour to be honest: quantities move, issues refuse
/// to go negative, and staged movements only land on <see cref="SaveAsync"/> -
/// so a test can still prove that a refused delivery moved nothing.
/// </summary>
public sealed class FakeStockPort : ITradeStockPort
{
    private readonly Dictionary<int, TradeItem> _items = [];
    private readonly List<Action> _staged = [];
    private readonly List<TradeReturn> _returns = [];

    public const int MainBookId = 1;
    public const int SpareBookId = 2;

    /// <summary>Every movement that has actually been committed.</summary>
    public List<(int ItemId, decimal Quantity)> Committed { get; } = [];

    public void AddItem(int id, string code, string name,
        decimal onHand = 0, decimal averageCost = 0, decimal sellingPrice = 0, int bookId = MainBookId) =>
        _items[id] = new TradeItem(id, bookId, code, name, "each",
            onHand, averageCost, sellingPrice, false, false);

    public decimal OnHand(int itemId) => _items[itemId].QuantityOnHand;

    public Task<IReadOnlyList<StockBook>> BooksAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<StockBook>>(
            [new(MainBookId, "MAIN", "Main Store", true), new(SpareBookId, "SPARE", "Spare Parts", false)]);

    public Task<StockBook?> DefaultBookAsync(CancellationToken ct = default) =>
        Task.FromResult<StockBook?>(new(MainBookId, "MAIN", "Main Store", true));

    public Task<IReadOnlyList<TradeItem>> ItemsAsync(
        int bookId, string? search = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TradeItem>>(
            [.. _items.Values.Where(x => x.BookId == bookId)]);

    public Task<TradeItem?> ItemAsync(int itemId, CancellationToken ct = default) =>
        Task.FromResult(_items.TryGetValue(itemId, out var i) ? i : null);

    public Task<Result> StageReceiptAsync(
        int itemId, decimal quantity, decimal unitCost, DateOnly date,
        string? reference, string? documentType, int? documentId,
        IReadOnlyList<string>? serialNumbers = null,
        string? batchNumber = null, DateOnly? expiresOn = null,
        CancellationToken ct = default)
    {
        if (!_items.ContainsKey(itemId)) return Task.FromResult(Result.Fail("No such item.", "stock.no-item"));

        _staged.Add(() =>
        {
            _items[itemId] = _items[itemId] with
            {
                QuantityOnHand = _items[itemId].QuantityOnHand + quantity
            };
            Committed.Add((itemId, quantity));
        });

        return Task.FromResult(Result.Success());
    }

    public Task<Result> StageIssueAsync(
        int itemId, decimal quantity, DateOnly date,
        string? reference, string? documentType, int? documentId,
        IReadOnlyList<string>? serialNumbers = null, string? issuedTo = null,
        CancellationToken ct = default)
    {
        if (!_items.ContainsKey(itemId)) return Task.FromResult(Result.Fail("No such item.", "stock.no-item"));

        if (_items[itemId].QuantityOnHand < quantity)
            return Task.FromResult(Result.Fail("Not enough stock.", "stock.insufficient"));

        _staged.Add(() =>
        {
            _items[itemId] = _items[itemId] with
            {
                QuantityOnHand = _items[itemId].QuantityOnHand - quantity
            };
            Committed.Add((itemId, -quantity));
        });

        return Task.FromResult(Result.Success());
    }

    public Task SaveAsync(CancellationToken ct = default)
    {
        foreach (var apply in _staged) apply();
        _staged.Clear();
        return Task.CompletedTask;
    }

    public Task<Result<TradeReturn>> PostReturnAsync(
        TradeReturnInput input, CancellationToken ct = default)
    {
        var row = new TradeReturn(_returns.Count + 1, $"R-{_returns.Count + 1}", input.Date,
            input.SupplierReturn, input.PartyId, input.PartyName, input.SourceReference,
            input.Reason, input.Lines.Count, 0);

        _returns.Add(row);
        return Task.FromResult(Result.Success(row));
    }

    public Task<IReadOnlyList<TradeReturn>> ReturnsAsync(
        bool supplierReturns, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TradeReturn>>(
            [.. _returns.Where(x => x.SupplierReturn == supplierReturns)]);
}
