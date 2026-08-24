using MeiErp.Platform.Kernel;
using MeiErp.Platform.Workflow;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MeiErp.Modules.Trade.Tests;

/// <summary>
/// The rules that make an order a commitment and a receipt or delivery the
/// thing that actually moves goods.
///
/// These moved here with buying and selling themselves. Every one of them is a
/// rule that fails quietly rather than loudly if it breaks - a silently
/// rewritten margin, stock promised twice, a half-posted delivery.
/// </summary>
[Collection("postgres")]
public sealed class BuyingAndSellingTests : IAsyncLifetime
{
    private static readonly string BaseConnection =
        Environment.GetEnvironmentVariable("MEIERP_TEST_DB")
        ?? "Host=127.0.0.1;Username=meierp;Password=DevPassword1!;";

    private readonly string _database = $"mei_trade_{Guid.NewGuid():N}";
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero));
    private readonly SystemUser _user = new("Trade Tester");

    private bool _available;
    private int _customerId, _supplierId;

    private const int ItemId = 42;

    private string Connection => BaseConnection + $"Database={_database};";

    private TradeDbContext NewDb() =>
        new(new DbContextOptionsBuilder<TradeDbContext>().UseNpgsql(Connection).Options, _user, _clock);

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
            await db.EnsureAuditTableForTestsAsync();

            var customer = new Party { Code = "CUST", Name = "A Customer", IsCustomer = true };
            var supplier = new Party { Code = "SUPP", Name = "A Supplier", IsSupplier = true };
            db.AddRange(customer, supplier);
            await db.SaveChangesAsync();

            _customerId = customer.Id;
            _supplierId = supplier.Id;
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

    private static FakeStockPort Stocked(decimal onHand, decimal averageCost = 100)
    {
        var port = new FakeStockPort();
        port.AddItem(ItemId, "W-1", "Widget", onHand, averageCost, sellingPrice: 150);
        return port;
    }

    // ---------- buying ----------

    [SkippableFact]
    public async Task Goods_cannot_be_received_against_an_unapproved_order()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        var purchasing = new PurchasingService(db, Stocked(0), new NoApprovals(), _clock);

        var order = await purchasing.SaveOrderAsync(new PurchaseOrderInput(
            null, _supplierId, FakeStockPort.MainBookId, _clock.Today, null,
            [new PurchaseOrderLineInput(ItemId, 10, 100)]));
        Assert.True(order.Ok, order.Error);

        var received = await purchasing.ReceiveAsync(new ReceiptInput(
            order.Value.Id, _clock.Today, null, [new ReceiptLineInput(ItemId, 10, 100)]));

        // Otherwise someone commits the company to a purchase by unloading a van.
        Assert.True(received.Failed);
        Assert.Equal("po.not-approved", received.Code);
    }

    [SkippableFact]
    public async Task More_cannot_be_received_than_was_ordered()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        var purchasing = new PurchasingService(db, Stocked(0), new NoApprovals(), _clock);

        var order = await purchasing.SaveOrderAsync(new PurchaseOrderInput(
            null, _supplierId, FakeStockPort.MainBookId, _clock.Today, null,
            [new PurchaseOrderLineInput(ItemId, 10, 100)]));

        await ApproveAsync(db, order.Value.Id);

        var received = await purchasing.ReceiveAsync(new ReceiptInput(
            order.Value.Id, _clock.Today, null, [new ReceiptLineInput(ItemId, 15, 100)]));

        Assert.True(received.Failed);
        Assert.Equal("receipt.over-receipt", received.Code);
    }

    [SkippableFact]
    public async Task A_partial_receipt_leaves_the_order_open()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        var port = Stocked(0);
        var purchasing = new PurchasingService(db, port, new NoApprovals(), _clock);

        var order = await purchasing.SaveOrderAsync(new PurchaseOrderInput(
            null, _supplierId, FakeStockPort.MainBookId, _clock.Today, null,
            [new PurchaseOrderLineInput(ItemId, 10, 100)]));

        await ApproveAsync(db, order.Value.Id);

        Assert.True((await purchasing.ReceiveAsync(new ReceiptInput(
            order.Value.Id, _clock.Today, null, [new ReceiptLineInput(ItemId, 4, 100)]))).Ok);

        db.ChangeTracker.Clear();
        var after = await db.PurchaseOrders.Include(o => o.Lines).FirstAsync(o => o.Id == order.Value.Id);

        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, after.Status);
        Assert.Equal(6, after.Lines.Single().Outstanding);
        Assert.Equal(4, port.OnHand(ItemId));

        // Other modules hear about the receipt through the outbox rather than a
        // direct call, so a failure over there cannot roll this back.
        var message = await db.Outbox.SingleAsync();
        Assert.Equal(PurchasingService.GoodsReceiptPostedEvent, message.EventType);
        Assert.Contains("\"Amount\":400", message.Payload, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task An_order_cannot_mix_stock_books()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();

        var port = new FakeStockPort();
        port.AddItem(ItemId, "SP-1", "Gasket", bookId: FakeStockPort.SpareBookId);

        var purchasing = new PurchasingService(db, port, new NoApprovals(), _clock);

        // Buying into the main store, but the line is a workshop spare. It could
        // never be received onto a shelf this order is entitled to touch.
        var order = await purchasing.SaveOrderAsync(new PurchaseOrderInput(
            null, _supplierId, FakeStockPort.MainBookId, _clock.Today, null,
            [new PurchaseOrderLineInput(ItemId, 1, 10)]));

        Assert.True(order.Failed);
        Assert.Equal("po.wrong-book", order.Code);
    }

    // ---------- selling ----------

    [SkippableFact]
    public async Task A_delivery_keeps_the_cost_it_went_out_at_when_prices_rise_later()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        var port = Stocked(10, averageCost: 100);
        var sales = new SalesService(db, port, _clock);

        var order = await sales.SaveOrderAsync(new SalesOrderInput(
            null, _customerId, FakeStockPort.MainBookId, _clock.Today, null,
            [new SalesOrderLineInput(ItemId, 5, 150)]));
        Assert.True(order.Ok, order.Error);
        await sales.ConfirmAsync(order.Value.Id);

        Assert.True((await sales.DeliverAsync(new DeliveryInput(
            order.Value.Id, _clock.Today, "Someone", null,
            [new DeliveryLineInput(ItemId, 5)]))).Ok);

        // Prices go up after the goods left, and the weighted average moves.
        port.AddItem(ItemId, "W-1", "Widget", onHand: 105, averageCost: 500, sellingPrice: 150);

        db.ChangeTracker.Clear();
        var line = await db.DeliveryLines.SingleAsync();

        // The delivery still says what it actually cost. Reading the average
        // live would silently rewrite last month's margin every time somebody
        // bought at a new price - the sale would show a loss it never made.
        Assert.Equal(100, line.UnitCost);
        Assert.Equal(250, line.Margin);       // (150 - 100) x 5
    }

    [SkippableFact]
    public async Task A_delivery_short_of_stock_moves_nothing_at_all()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        var port = Stocked(3);
        var sales = new SalesService(db, port, _clock);

        var order = await sales.SaveOrderAsync(new SalesOrderInput(
            null, _customerId, FakeStockPort.MainBookId, _clock.Today, null,
            [new SalesOrderLineInput(ItemId, 5, 150)]));
        await sales.ConfirmAsync(order.Value.Id);

        var delivered = await sales.DeliverAsync(new DeliveryInput(
            order.Value.Id, _clock.Today, null, null, [new DeliveryLineInput(ItemId, 5)]));

        Assert.True(delivered.Failed);
        Assert.Equal("delivery.insufficient-stock", delivered.Code);

        db.ChangeTracker.Clear();

        // Checked before anything moves, so the refusal leaves no half-posted
        // delivery to unpick by hand.
        Assert.Equal(3, port.OnHand(ItemId));
        Assert.Empty(port.Committed);
        Assert.Empty(await db.Deliveries.ToListAsync());
    }

    [SkippableFact]
    public async Task Confirming_a_sales_order_reserves_nothing()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        var port = Stocked(10);
        var sales = new SalesService(db, port, _clock);

        var first = await sales.SaveOrderAsync(new SalesOrderInput(
            null, _customerId, FakeStockPort.MainBookId, _clock.Today, null,
            [new SalesOrderLineInput(ItemId, 10, 150)]));
        await sales.ConfirmAsync(first.Value.Id);

        // A soft reservation the stock figure does not honour is worse than
        // none: two orders can still be promised the same unit while both look
        // safe. So confirming moves nothing, and the shortage surfaces at
        // delivery, where stock is really checked.
        Assert.Equal(10, port.OnHand(ItemId));

        var second = await sales.SaveOrderAsync(new SalesOrderInput(
            null, _customerId, FakeStockPort.MainBookId, _clock.Today, null,
            [new SalesOrderLineInput(ItemId, 10, 150)]));
        Assert.True(second.Ok, second.Error);
        Assert.True((await sales.ConfirmAsync(second.Value.Id)).Ok);
    }

    // ---------- the party master ----------

    [SkippableFact]
    public async Task A_party_that_is_neither_customer_nor_supplier_is_refused()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();

        var result = await new PartyService(db).SaveAsync(
            new Party { Code = "X", Name = "Nobody" });

        // Such a record could never appear on any document, so it is a
        // data-entry slip rather than a state anyone wants.
        Assert.True(result.Failed);
        Assert.Equal("party.no-side", result.Code);
    }

    [SkippableFact]
    public async Task A_party_can_be_both_sides_and_shows_in_both_lists()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        var parties = new PartyService(db);

        var saved = await parties.SaveAsync(new Party
        {
            Code = "BOTH",
            Name = "A Trading Company",
            IsCustomer = true,
            IsSupplier = true,
            PaymentTermDays = 30,
            Notes = "Buys and sells"
        });
        Assert.True(saved.Ok, saved.Error);

        // The same company is very often both, which is the whole reason there
        // is one master rather than a customer list and a supplier list.
        Assert.Contains(await parties.ListAsync(customers: true), p => p.Code == "BOTH");
        Assert.Contains(await parties.ListAsync(suppliers: true), p => p.Code == "BOTH");

        var reloaded = (await parties.ListAsync(customers: true)).Single(p => p.Code == "BOTH");
        Assert.Equal(30, reloaded.PaymentTermDays);
        Assert.Equal("Buys and sells", reloaded.Notes);
        Assert.Equal("Customer & supplier", reloaded.Sides);
    }

    [SkippableFact]
    public async Task A_duplicate_party_code_is_refused()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        var parties = new PartyService(db);

        var clash = await parties.SaveAsync(
            new Party { Code = "CUST", Name = "Someone else", IsCustomer = true });

        Assert.True(clash.Failed);
        Assert.Equal("party.duplicate-code", clash.Code);
    }

    private static async Task ApproveAsync(TradeDbContext db, int orderId)
    {
        var order = await db.PurchaseOrders.FirstAsync(o => o.Id == orderId);
        order.Status = PurchaseOrderStatus.Approved;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    /// <summary>Approval routing is tested against the engine itself; buying only needs a stand-in.</summary>
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
}
