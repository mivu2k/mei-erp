using MeiErp.Modules.Inventory;
using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MeiErp.Platform.Reporting;
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
    private int _itemId;
    private int _mainDomainId, _spareDomainId;

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
            await db.EnsureAuditTableForTestsAsync();

            // The two stock books, as the seeder creates them. Everything else
            // in this fixture belongs to the main one; the spare book exists so
            // the tests can prove the two never leak into each other.
            var main = new StockDomain { Code = StockDomainCodes.Main, Name = "Main Store", IsDefault = true };
            var spare = new StockDomain { Code = StockDomainCodes.Spare, Name = "Spare Parts" };
            db.StockDomains.AddRange(main, spare);
            await db.SaveChangesAsync();

            var item = new Item { Code = "W-1", Name = "Widget", Unit = "each", IsActive = true, DomainId = main.Id };
            db.Items.Add(item);
            await db.SaveChangesAsync();

            _itemId = item.Id;
            _mainDomainId = main.Id; _spareDomainId = spare.Id;
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

    [SkippableFact]
    public async Task A_receipt_is_attributed_to_the_default_warehouse()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db=NewDb();await Receive(NewStock(db),10,100);
        var warehouse=await db.Warehouses.SingleAsync();var balance=await db.WarehouseBalances.SingleAsync();
        Assert.True(warehouse.IsDefault);Assert.Equal(10,balance.Quantity);
        Assert.Equal(warehouse.Id,(await db.StockMovements.SingleAsync()).WarehouseId);
    }

    [SkippableFact]
    public async Task Transfer_dispatch_and_short_receipt_preserve_the_visible_gap()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db=NewDb();await Receive(NewStock(db),10,100);
        var destination=new Warehouse{Name="Branch",Code="BR",DomainId=_mainDomainId};db.Add(destination);await db.SaveChangesAsync();
        var source=await db.Warehouses.SingleAsync(x=>x.IsDefault);var service=new TransferService(db,_clock,_user);
        var saved=await service.SaveAsync(new(null,_clock.Today,source.Id,destination.Id,null,null,[new(_itemId,6)]));
        Assert.True(saved.Ok,saved.Error);Assert.True((await service.DispatchAsync(saved.Value.Id,"Storekeeper")).Ok);
        var line=(await service.GetAsync(saved.Value.Id))!.Lines.Single();Assert.True((await service.ReceiveAsync(saved.Value.Id,new Dictionary<int,decimal>{{line.Id,5}},"Receiver")).Ok);
        var balances=await db.WarehouseBalances.OrderBy(x=>x.WarehouseId).ToListAsync();
        Assert.Equal(9,balances.Sum(x=>x.Quantity));Assert.Equal(-1,(await service.GetAsync(saved.Value.Id))!.Lines.Single().Shortfall);
    }

    [SkippableFact]
    public async Task Posting_a_warehouse_count_updates_location_total_and_ledger_once()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db=NewDb();await Receive(NewStock(db),10,100);var warehouse=await db.Warehouses.SingleAsync();
        var service=new InventoryCountService(db,_clock,_user);var count=await service.CreateAsync(warehouse.Id,"Monthly count");
        Assert.True((await service.RecordAsync(count.Value.Id,[new(_itemId,8,"Two damaged")])).Ok);
        Assert.True((await service.PostAsync(count.Value.Id)).Ok);Assert.False((await service.PostAsync(count.Value.Id)).Ok);
        Assert.Equal(8,(await db.Items.SingleAsync()).QuantityOnHand);Assert.Equal(8,(await db.WarehouseBalances.SingleAsync()).Quantity);
        Assert.Equal(1,await db.StockMovements.CountAsync(x=>x.Reference==count.Value.Number));
    }

    [SkippableFact]
    public async Task Serialized_receipt_requires_exact_unique_serials_and_delivery_marks_them_sold()
    {
        Skip.IfNot(_available,"No PostgreSQL available.");await using var db=NewDb();var item=await db.Items.SingleAsync();item.IsSerialized=true;await db.SaveChangesAsync();
        var tracking=new StockTrackingService(db,_clock);
        Assert.False((await tracking.StageReceiptAsync(new(_itemId,2,100,_clock.Today,null,null,["SN-1"],"GR-1"))).Ok);
        Assert.True((await tracking.StageReceiptAsync(new(_itemId,2,100,_clock.Today,null,null,["SN-1","SN-2"],"GR-1"))).Ok);await Receive(NewStock(db),2,100);
        Assert.False((await tracking.StageIssueAsync(_itemId,1,["MISSING"],_clock.Today,"Customer","DN-1")).Ok);
        Assert.True((await tracking.StageIssueAsync(_itemId,1,["SN-1"],_clock.Today,"Customer","DN-1")).Ok);await db.SaveChangesAsync();
        var units=await tracking.UnitsAsync(_itemId);Assert.Equal(2,units.Count);Assert.Equal(StockUnitStatus.Sold,units.Single(x=>x.SerialNumber=="SN-1").Status);
    }

    [SkippableFact]
    public async Task Batch_receipt_requires_a_batch_and_issue_uses_earliest_expiry_first()
    {
        Skip.IfNot(_available,"No PostgreSQL available.");await using var db=NewDb();var item=await db.Items.SingleAsync();item.IsBatchTracked=true;await db.SaveChangesAsync();var tracking=new StockTrackingService(db,_clock);
        Assert.False((await tracking.StageReceiptAsync(new(_itemId,5,10,_clock.Today,null,null,[],"GR-1"))).Ok);
        Assert.True((await tracking.StageReceiptAsync(new(_itemId,3,10,_clock.Today,"LATE",_clock.Today.AddDays(60),[],"GR-1"))).Ok);Assert.True((await tracking.StageReceiptAsync(new(_itemId,2,10,_clock.Today,"EARLY",_clock.Today.AddDays(10),[],"GR-2"))).Ok);await Receive(NewStock(db),5,10);
        Assert.True((await tracking.StageIssueAsync(_itemId,3,[],_clock.Today,"Customer","DN-1")).Ok);await db.SaveChangesAsync();
        var batches=await tracking.BatchesAsync(_itemId);Assert.Equal(0,batches.Single(x=>x.BatchNumber=="EARLY").RemainingQuantity);Assert.Equal(2,batches.Single(x=>x.BatchNumber=="LATE").RemainingQuantity);
    }

    [SkippableFact]
    public async Task Product_family_organizes_models_and_accessories_without_duplicating_stock()
    {
        Skip.IfNot(_available,"No PostgreSQL available.");await using var db=NewDb();var hierarchy=new ProductHierarchyService(db);var catalog=new CatalogService(db);
        var family=await hierarchy.SaveAsync(new(){Name="Laptop",SkuPrefix="LAP"});var model=await db.Items.SingleAsync();model.ProductFamilyId=family.Id;model.Kind=InventoryItemKind.Model;await db.SaveChangesAsync();
        var accessory=await catalog.SaveItemAsync(new(){Code="CHG-1",Name="Charger",Unit="each",Kind=InventoryItemKind.Accessory,ParentItemId=model.Id,ProductFamilyId=family.Id,IsActive=true});
        Assert.True(accessory.Ok,accessory.Error);var loaded=(await hierarchy.ListAsync()).Single();Assert.Equal(2,loaded.Items.Count);Assert.Equal(model.Id,loaded.Items.Single(x=>x.Kind==InventoryItemKind.Accessory).ParentItemId);
    }

    [SkippableFact]
    public async Task Accessory_cannot_be_attached_to_a_different_product_family()
    {
        Skip.IfNot(_available,"No PostgreSQL available.");await using var db=NewDb();var hierarchy=new ProductHierarchyService(db);var catalog=new CatalogService(db);var first=await hierarchy.SaveAsync(new(){Name="Laptop"});var second=await hierarchy.SaveAsync(new(){Name="Printer"});var model=await db.Items.SingleAsync();model.ProductFamilyId=first.Id;await db.SaveChangesAsync();
        var result=await catalog.SaveItemAsync(new(){Code="INK",Name="Ink",Kind=InventoryItemKind.Accessory,ParentItemId=model.Id,ProductFamilyId=second.Id,IsActive=true});Assert.True(result.Failed);Assert.Equal("item.family-mismatch",result.Code);
    }

    [SkippableFact]
    public async Task Customer_and_supplier_returns_are_separate_auditable_ledger_movements()
    {
        Skip.IfNot(_available,"No PostgreSQL available.");await using var db=NewDb();await Receive(NewStock(db),10,100);var service=new InventoryReturnService(db,NewStock(db),_clock,_user);
        var customer=await service.PostAsync(new(InventoryReturnKind.SalesReturn,101,"A Customer",_clock.Today,"DN-1","Customer rejected",null,[new(_itemId,2)]));Assert.True(customer.Ok,customer.Error);
        var supplier=await service.PostAsync(new(InventoryReturnKind.PurchaseReturn,202,"A Supplier",_clock.Today,"GR-1","Faulty goods",null,[new(_itemId,3)]));Assert.True(supplier.Ok,supplier.Error);
        Assert.Equal(9,(await db.Items.SingleAsync()).QuantityOnHand);var moves=await db.StockMovements.Where(x=>x.SourceDocumentType=="inventory-return").OrderBy(x=>x.Id).ToListAsync();Assert.Equal([2m,-3m],moves.Select(x=>x.Quantity));Assert.Equal(2,await db.InventoryReturns.CountAsync());
    }

    [SkippableFact]
    public async Task Return_requires_a_reason_and_cannot_send_more_to_supplier_than_is_held()
    {
        Skip.IfNot(_available,"No PostgreSQL available.");await using var db=NewDb();await Receive(NewStock(db),2,100);var service=new InventoryReturnService(db,NewStock(db),_clock,_user);
        Assert.Equal("return.no-reason",(await service.PostAsync(new(InventoryReturnKind.PurchaseReturn,202,"A Supplier",_clock.Today,null," ",null,[new(_itemId,1)]))).Code);
        Assert.Equal("return.insufficient",(await service.PostAsync(new(InventoryReturnKind.PurchaseReturn,202,"A Supplier",_clock.Today,null,"Faulty",null,[new(_itemId,3)]))).Code);
        Assert.Equal(2,(await db.Items.SingleAsync()).QuantityOnHand);
    }

    [SkippableFact]
    public async Task Every_inventory_report_executes_against_the_real_schema()
    {
        Skip.IfNot(_available,"No PostgreSQL available.");await using var db=NewDb();await Receive(NewStock(db),2,100);
        var services=new ServiceCollection().AddSingleton(db).AddSingleton<IClock>(_clock).AddInventoryReports();using var provider=services.BuildServiceProvider();var reports=provider.GetServices<ReportDefinition>().ToList();
        foreach(var report in reports){var result=await report.Run(new ReportRequest{From=_clock.Today.AddDays(-7),To=_clock.Today,AsAt=_clock.Today},default);Assert.NotNull(result.Columns);Assert.NotNull(result.Rows);}
    }

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

    // ---- The two stock books -------------------------------------------------
    //
    // The workshop's spares and the main store's goods share one implementation
    // and must never share a figure. These pin the seams where they could leak.

    [SkippableFact]
    public async Task The_same_item_code_is_free_in_each_stock_book()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        var catalog = new CatalogService(db);

        // "W-1" is already taken in the main book by the fixture.
        var clash = await catalog.SaveItemAsync(
            new Item { Code = "W-1", Name = "Another widget", DomainId = _mainDomainId });
        Assert.False(clash.Ok);

        // The workshop numbers its parts independently, so the same code there
        // is not a clash at all.
        var spare = await catalog.SaveItemAsync(
            new Item { Code = "W-1", Name = "Widget spare", DomainId = _spareDomainId });
        Assert.True(spare.Ok, spare.Error);
    }

    [SkippableFact]
    public async Task Listing_a_book_never_shows_the_other_books_items()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        var catalog = new CatalogService(db);
        Assert.True((await catalog.SaveItemAsync(
            new Item { Code = "SP-1", Name = "Gasket", IsActive = true, DomainId = _spareDomainId })).Ok);

        var main = await catalog.ItemsAsync(domainId: _mainDomainId);
        var spare = await catalog.ItemsAsync(domainId: _spareDomainId);

        Assert.Equal(["W-1"], main.Select(x => x.Code));
        Assert.Equal(["SP-1"], spare.Select(x => x.Code));

        // Null spans both, which is what a group-wide valuation wants.
        Assert.Equal(2, (await catalog.ItemsAsync()).Count);
    }

    [SkippableFact]
    public async Task A_receipt_lands_on_its_own_books_default_warehouse()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        var catalog = new CatalogService(db);
        var part = await catalog.SaveItemAsync(
            new Item { Code = "SP-9", Name = "Belt", IsActive = true, DomainId = _spareDomainId });
        Assert.True(part.Ok, part.Error);

        var stock = NewStock(db);
        Assert.True((await Receive(stock, 10, 100)).Ok);                       // main book
        Assert.True((await stock.ReceiveAsync(part.Value.Id, 4, 25, _clock.Today,
            StockMovementType.Receipt, "TEST", null, null)).Ok);               // spare book

        // Each book got its own default shelf; unscoped, the spare would have
        // landed on the main store's and both valuations would be wrong.
        var warehouses = await db.Warehouses.ToListAsync();
        Assert.Equal(2, warehouses.Count);
        Assert.Equal([_mainDomainId, _spareDomainId], warehouses.Select(x => x.DomainId).Order());
        Assert.All(warehouses, w => Assert.True(w.IsDefault));

        // And the ledger reads one book at a time.
        var mainLedger = await stock.MovementsAsync(null, null, null, _mainDomainId);
        var spareLedger = await stock.MovementsAsync(null, null, null, _spareDomainId);
        Assert.Equal(10, Assert.Single(mainLedger).Quantity);
        Assert.Equal(4, Assert.Single(spareLedger).Quantity);
    }

    [SkippableFact]
    public async Task A_transfer_cannot_cross_stock_books()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        await Receive(NewStock(db), 10, 100);

        var source = await db.Warehouses.SingleAsync(x => x.DomainId == _mainDomainId);
        var workshop = new Warehouse { Name = "Workshop bin", Code = "WS", DomainId = _spareDomainId };
        db.Add(workshop);
        await db.SaveChangesAsync();

        var result = await new TransferService(db, _clock, _user)
            .SaveAsync(new(null, _clock.Today, source.Id, workshop.Id, null, null, [new(_itemId, 1)]));

        // Getting goods from one book to the other is a sale and a purchase,
        // which is also how the money should read.
        Assert.False(result.Ok);
        Assert.Contains("stock book", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task An_items_book_is_fixed_at_creation()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        await Receive(NewStock(db), 10, 100);

        var catalog = new CatalogService(db);
        var item = await catalog.GetItemAsync(_itemId);
        item!.DomainId = _spareDomainId;
        item.Name = "Renamed";

        Assert.True((await catalog.SaveItemAsync(item)).Ok);

        // The rename lands; the move does not. Its stock history, balances and
        // movements all sit in the main book and would have been left behind.
        var saved = await db.Items.AsNoTracking().SingleAsync(x => x.Id == _itemId);
        Assert.Equal("Renamed", saved.Name);
        Assert.Equal(_mainDomainId, saved.DomainId);
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
