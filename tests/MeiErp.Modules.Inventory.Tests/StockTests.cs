using MeiErp.Modules.Inventory;
using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MeiErp.Modules.Inventory.Tests;

/// <summary>
/// Stock arithmetic and the rules that keep the quantity on hand honest.
///
/// The weighted average and the delivery cost snapshot are the two things most
/// likely to be quietly wrong, and both distort margin rather than throwing.
/// </summary>
[Collection("postgres")]
public sealed class StockTests : IAsyncLifetime
{
    private static readonly string BaseConnection =
        Environment.GetEnvironmentVariable("MEIERP_TEST_DB")
        ?? "Host=127.0.0.1;Username=meierp;Password=DevPassword1!;";

    private readonly string _database = $"mei_inv_{Guid.NewGuid():N}";
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero));
    private readonly TestUser _user = new("user-1", "Storekeeper");

    private bool _available;
    private int _itemId, _customerId, _supplierId;

    private string Connection => BaseConnection + $"Database={_database};";

    public async Task InitializeAsync()
    {
        try
        {
            await using (var admin = new DbContext(new DbContextOptionsBuilder()
                .UseNpgsql(BaseConnection + "Database=postgres;").Options))
            {
                await admin.Database.ExecuteSqlRawAsync($"CREATE DATABASE \"{_database}\";");
            }

            await using var db = NewDb();
            await db.Database.EnsureCreatedAsync();

            var item = new Item { Code = "W-1", Name = "Widget", Unit = "each", IsActive = true };
            var customer = new Party { Code = "CUST", Name = "A Customer", IsCustomer = true, IsActive = true };
            var supplier = new Party { Code = "SUPP", Name = "A Supplier", IsSupplier = true, IsActive = true };

            db.Items.Add(item);
            db.Parties.AddRange(customer, supplier);
            await db.SaveChangesAsync();

            _itemId = item.Id; _customerId = customer.Id; _supplierId = supplier.Id;
            _available = true;
        }
        catch (NpgsqlException) { _available = false; }
    }

    public async Task DisposeAsync()
    {
        if (!_available) return;
        try
        {
            await using var admin = new DbContext(new DbContextOptionsBuilder()
                .UseNpgsql(BaseConnection + "Database=postgres;").Options);
            await admin.Database.ExecuteSqlRawAsync($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE);");
        }
        catch { /* a stray throwaway database is harmless */ }
    }

    private InventoryDbContext NewDb() =>
        new(new DbContextOptionsBuilder<InventoryDbContext>().UseNpgsql(Connection).Options, _user, _clock);

    private static StockService NewStock(InventoryDbContext db) => new(db);

    private Task<Result<StockMovement>> Receive(StockService stock, decimal qty, decimal cost) =>
        stock.ReceiveAsync(_itemId, qty, cost, _clock.Today,
            StockMovementType.Receipt, "TEST", null, null);

    // ---------- weighted average ----------

    [SkippableFact]
    public async Task The_first_receipt_sets_the_average_to_what_was_paid()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var stock = NewStock(db);

        await Receive(stock, 10, 100);

        var item = await db.Items.SingleAsync();
        Assert.Equal(10, item.QuantityOnHand);
        Assert.Equal(100, item.AverageCost);
    }

    [SkippableFact]
    public async Task A_second_receipt_at_a_different_price_weights_the_average()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var stock = NewStock(db);

        await Receive(stock, 10, 100);   // 1,000
        await Receive(stock, 10, 200);   // 2,000

        var item = await db.Items.SingleAsync();

        // 3,000 over 20 units. A plain average of the two prices would also give
        // 150 here, which is why the next test uses unequal quantities.
        Assert.Equal(20, item.QuantityOnHand);
        Assert.Equal(150, item.AverageCost);
    }

    [SkippableFact]
    public async Task The_average_is_weighted_by_quantity_not_by_price()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var stock = NewStock(db);

        await Receive(stock, 90, 100);   // 9,000
        await Receive(stock, 10, 200);   // 2,000

        var item = await db.Items.SingleAsync();

        // 11,000 over 100 = 110. A naive mean of the prices would say 150, and
        // would overstate the value of stock by a third.
        Assert.Equal(110, item.AverageCost);
    }

    [SkippableFact]
    public async Task Issuing_stock_does_not_move_the_average()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var stock = NewStock(db);

        await Receive(stock, 10, 100);
        await stock.IssueAsync(_itemId, 4, _clock.Today,
            StockMovementType.Delivery, "DN-1", null, null);

        var item = await db.Items.SingleAsync();

        // Only purchases change what stock is carried at.
        Assert.Equal(6, item.QuantityOnHand);
        Assert.Equal(100, item.AverageCost);
    }

    // ---------- guards ----------

    [SkippableFact]
    public async Task Stock_cannot_go_negative()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var stock = NewStock(db);

        await Receive(stock, 5, 100);

        var result = await stock.IssueAsync(_itemId, 10, _clock.Today,
            StockMovementType.Delivery, "DN-1", null, null);

        // Negative stock is a lie that surfaces during a count, at the worst
        // possible moment.
        Assert.True(result.Failed);
        Assert.Equal("stock.insufficient", result.Code);
    }

    [SkippableFact]
    public async Task An_adjustment_without_a_reason_is_refused()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var stock = NewStock(db);

        await Receive(stock, 10, 100);

        var result = await stock.AdjustToAsync(_itemId, 8, _clock.Today, "  ");

        // An unexplained adjustment is indistinguishable from theft.
        Assert.True(result.Failed);
        Assert.Equal("stock.no-reason", result.Code);
    }

    [SkippableFact]
    public async Task A_count_writes_the_difference_as_a_movement()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var stock = NewStock(db);

        await Receive(stock, 10, 100);
        await stock.AdjustToAsync(_itemId, 8, _clock.Today, "Two broken in the store");

        var item = await db.Items.SingleAsync();
        var adjustment = await db.StockMovements
            .SingleAsync(m => m.Type == StockMovementType.Adjustment);

        Assert.Equal(8, item.QuantityOnHand);
        Assert.Equal(-2, adjustment.Quantity);
        Assert.Equal("Two broken in the store", adjustment.Narration);
    }

    [SkippableFact]
    public async Task Every_movement_records_the_balance_it_left_behind()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var stock = NewStock(db);

        await Receive(stock, 10, 100);
        await stock.IssueAsync(_itemId, 3, _clock.Today, StockMovementType.Delivery, "DN-1", null, null);
        await Receive(stock, 5, 120);

        var movements = await db.StockMovements.OrderBy(m => m.Id).ToListAsync();

        // The running figure is auditable against its own history.
        Assert.Equal([10m, 7m, 12m], movements.Select(m => m.BalanceAfter));
    }

    [SkippableFact]
    public async Task The_rebuild_corrects_a_quantity_that_has_drifted()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var stock = NewStock(db);

        await Receive(stock, 10, 100);

        // Simulate drift - a crash mid-write, or a hand-edited row.
        var item = await db.Items.SingleAsync();
        item.QuantityOnHand = 999;
        await db.SaveChangesAsync();

        var corrected = await stock.RebuildQuantitiesAsync();

        db.ChangeTracker.Clear();
        var rebuilt = await db.Items.SingleAsync();

        // Movements are the truth; the running figure is only a cache.
        Assert.Equal(1, corrected);
        Assert.Equal(10, rebuilt.QuantityOnHand);
    }

    // ---------- the cost snapshot ----------

    [SkippableFact]
    public async Task A_delivery_keeps_the_cost_it_went_out_at_when_prices_rise_later()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var stock = NewStock(db);
        var sales = new SalesService(db, stock, _clock);

        await Receive(stock, 10, 100);

        var order = await sales.SaveOrderAsync(new SalesOrderInput(
            null, _customerId, _clock.Today, null,
            [new SalesOrderLineInput(_itemId, 5, 150)]));
        Assert.True(order.Ok, order.Error);
        await sales.ConfirmAsync(order.Value.Id);

        var delivered = await sales.DeliverAsync(new DeliveryInput(
            order.Value.Id, _clock.Today, "Someone", null,
            [new DeliveryLineInput(_itemId, 5)]));
        Assert.True(delivered.Ok, delivered.Error);

        // Prices go up after the goods left. The weighted average moves.
        await Receive(stock, 100, 500);

        db.ChangeTracker.Clear();
        var line = await db.DeliveryLines.SingleAsync();
        var item = await db.Items.SingleAsync();

        // The delivery still says what it actually cost. Reading the average
        // live would silently rewrite last month's margin every time somebody
        // bought at a new price - the sale would show a loss it never made.
        Assert.Equal(100, line.UnitCost);
        Assert.Equal(250, line.Margin);       // (150 - 100) x 5
        Assert.NotEqual(item.AverageCost, line.UnitCost);
    }

    // ---------- buying and selling ----------

    [SkippableFact]
    public async Task Goods_cannot_be_received_against_an_unapproved_order()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var stock = NewStock(db);
        var purchasing = new PurchasingService(db, stock, new NoApprovals(), _clock);

        var order = await purchasing.SaveOrderAsync(new PurchaseOrderInput(
            null, _supplierId, _clock.Today, null,
            [new PurchaseOrderLineInput(_itemId, 10, 100)]));

        var received = await purchasing.ReceiveAsync(new ReceiptInput(
            order.Value.Id, _clock.Today, null,
            [new ReceiptLineInput(_itemId, 10, 100)]));

        // Otherwise someone commits the company to a purchase by unloading a van.
        Assert.True(received.Failed);
        Assert.Equal("po.not-approved", received.Code);
    }

    [SkippableFact]
    public async Task More_cannot_be_received_than_was_ordered()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var stock = NewStock(db);
        var purchasing = new PurchasingService(db, stock, new NoApprovals(), _clock);

        var order = await purchasing.SaveOrderAsync(new PurchaseOrderInput(
            null, _supplierId, _clock.Today, null,
            [new PurchaseOrderLineInput(_itemId, 10, 100)]));

        var approved = await db.PurchaseOrders.FirstAsync(o => o.Id == order.Value.Id);
        approved.Status = PurchaseOrderStatus.Approved;
        await db.SaveChangesAsync();

        var received = await purchasing.ReceiveAsync(new ReceiptInput(
            order.Value.Id, _clock.Today, null,
            [new ReceiptLineInput(_itemId, 15, 100)]));

        Assert.True(received.Failed);
        Assert.Equal("receipt.over-receipt", received.Code);
    }

    [SkippableFact]
    public async Task A_partial_receipt_leaves_the_order_open()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var stock = NewStock(db);
        var purchasing = new PurchasingService(db, stock, new NoApprovals(), _clock);

        var order = await purchasing.SaveOrderAsync(new PurchaseOrderInput(
            null, _supplierId, _clock.Today, null,
            [new PurchaseOrderLineInput(_itemId, 10, 100)]));

        var live = await db.PurchaseOrders.FirstAsync(o => o.Id == order.Value.Id);
        live.Status = PurchaseOrderStatus.Approved;
        await db.SaveChangesAsync();

        await purchasing.ReceiveAsync(new ReceiptInput(
            order.Value.Id, _clock.Today, null, [new ReceiptLineInput(_itemId, 4, 100)]));

        db.ChangeTracker.Clear();
        var after = await db.PurchaseOrders.Include(o => o.Lines)
            .FirstAsync(o => o.Id == order.Value.Id);

        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, after.Status);
        Assert.Equal(6, after.Lines.Single().Outstanding);
        Assert.Equal(4, (await db.Items.SingleAsync()).QuantityOnHand);
    }

    [SkippableFact]
    public async Task A_delivery_short_of_stock_moves_nothing_at_all()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var stock = NewStock(db);
        var sales = new SalesService(db, stock, _clock);

        await Receive(stock, 3, 100);

        var order = await sales.SaveOrderAsync(new SalesOrderInput(
            null, _customerId, _clock.Today, null,
            [new SalesOrderLineInput(_itemId, 5, 150)]));
        await sales.ConfirmAsync(order.Value.Id);

        var delivered = await sales.DeliverAsync(new DeliveryInput(
            order.Value.Id, _clock.Today, null, null,
            [new DeliveryLineInput(_itemId, 5)]));

        Assert.True(delivered.Failed);
        Assert.Equal("delivery.insufficient-stock", delivered.Code);

        db.ChangeTracker.Clear();

        // Checked before anything moves, so the refusal leaves no half-posted
        // delivery to unpick by hand.
        Assert.Equal(3, (await db.Items.SingleAsync()).QuantityOnHand);
        Assert.Empty(await db.Deliveries.ToListAsync());
    }

    [SkippableFact]
    public async Task Confirming_a_sales_order_reserves_nothing()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var stock = NewStock(db);
        var sales = new SalesService(db, stock, _clock);

        await Receive(stock, 10, 100);

        var order = await sales.SaveOrderAsync(new SalesOrderInput(
            null, _customerId, _clock.Today, null,
            [new SalesOrderLineInput(_itemId, 10, 150)]));
        await sales.ConfirmAsync(order.Value.Id);

        db.ChangeTracker.Clear();

        // Deliberate: a soft reservation the stock figure does not honour is
        // worse than none, because two orders can still be promised the same
        // unit while both look safe.
        Assert.Equal(10, (await db.Items.SingleAsync()).QuantityOnHand);
    }

    // ---------- master data ----------

    [SkippableFact]
    public async Task An_item_holding_stock_cannot_be_deleted()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var stock = NewStock(db);
        var catalog = new CatalogService(db);

        await Receive(stock, 5, 100);

        var result = await catalog.DeleteItemAsync(_itemId);

        Assert.True(result.Failed);
        Assert.Equal("item.has-stock", result.Code);
    }

    [SkippableFact]
    public async Task An_item_with_history_cannot_be_deleted_even_at_nil_stock()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var stock = NewStock(db);
        var catalog = new CatalogService(db);

        await Receive(stock, 5, 100);
        await stock.IssueAsync(_itemId, 5, _clock.Today,
            StockMovementType.Delivery, "DN-1", null, null);

        var result = await catalog.DeleteItemAsync(_itemId);

        // This is what keeps Item's soft-delete filter from ever hiding stock
        // movements. See the note in InventoryDbContext.
        Assert.True(result.Failed);
        Assert.Equal("item.has-history", result.Code);
    }

    [SkippableFact]
    public async Task A_party_that_is_neither_customer_nor_supplier_is_refused()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var catalog = new CatalogService(db);

        var result = await catalog.SavePartyAsync(new Party { Name = "Nobody" });

        // Such a record could not be used anywhere, so saving it only creates
        // confusion later.
        Assert.True(result.Failed);
        Assert.Equal("party.no-side", result.Code);
    }

    [SkippableFact]
    public async Task Editing_an_item_cannot_change_its_stock_or_cost()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var stock = NewStock(db);
        var catalog = new CatalogService(db);

        await Receive(stock, 10, 100);
        db.ChangeTracker.Clear();

        var edited = await catalog.GetItemAsync(_itemId);
        edited!.Name = "Renamed";
        edited.QuantityOnHand = 5000;      // attempted, and must be ignored
        edited.AverageCost = 1;

        await catalog.SaveItemAsync(edited);
        db.ChangeTracker.Clear();

        var saved = await db.Items.SingleAsync();

        Assert.Equal("Renamed", saved.Name);
        Assert.Equal(10, saved.QuantityOnHand);
        Assert.Equal(100, saved.AverageCost);
    }

    /// <summary>Approval is tested against the engine itself; purchasing only needs a stand-in.</summary>
    private sealed class NoApprovals : MeiErp.Platform.Workflow.IApprovalEngine
    {
        public Task<Result<MeiErp.Platform.Workflow.ApprovalRequest>> SubmitAsync(
            MeiErp.Platform.Workflow.SubmitApproval request, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(new MeiErp.Platform.Workflow.ApprovalRequest { Id = 1 }));

        public Task<Result<MeiErp.Platform.Workflow.ApprovalRequest>> DecideAsync(
            int requestId, MeiErp.Platform.Workflow.ApprovalDecision decision,
            string? comment, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<Result> CancelAsync(int requestId, string? reason, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());

        public Task<Result<MeiErp.Platform.Workflow.ApprovalRequest>> ResubmitAsync(
            int requestId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<MeiErp.Platform.Workflow.ApprovalInboxItem>> InboxAsync(
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MeiErp.Platform.Workflow.ApprovalInboxItem>>([]);

        public Task<Result> CanDecideAsync(int requestId, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());

        public Task<MeiErp.Platform.Workflow.ApprovalHistory?> HistoryAsync(
            string documentType, int documentId, CancellationToken ct = default) =>
            Task.FromResult<MeiErp.Platform.Workflow.ApprovalHistory?>(null);
    }

    private sealed class TestUser(string id, string name) : ICurrentUser
    {
        public string? UserId { get; } = id;
        public string? Name { get; } = name;
        public string? Email => null;
        public bool IsAuthenticated => true;
        public bool Can(string permission) => true;
        public bool InModule(string moduleKey) => true;
        public IReadOnlyCollection<string> Roles { get; } = [];
    }
}
