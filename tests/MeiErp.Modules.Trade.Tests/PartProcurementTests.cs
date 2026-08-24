using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MeiErp.Modules.Trade.Tests;

/// <summary>
/// The workshop's parts buying, moved here from Repair along with the buying
/// itself.
///
/// The two cost figures are what these pin. They are easy to get subtly wrong
/// and both distort margin silently rather than throwing: the weighted average
/// has to be quantity-weighted rather than a mean of prices, and the last cost
/// must only ever move forward in time.
/// </summary>
[Collection("postgres")]
public sealed class PartProcurementTests : IAsyncLifetime
{
    private static readonly string BaseConnection =
        Environment.GetEnvironmentVariable("MEIERP_TEST_DB")
        ?? "Host=127.0.0.1;Username=meierp;Password=DevPassword1!;";

    private readonly string _database = $"mei_trade_{Guid.NewGuid():N}";
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 8, 22, 9, 0, 0, TimeSpan.Zero));
    private readonly SystemUser _user = new("Workshop Tester");

    private bool _available;
    private int _supplierId, _partId;

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

            // The supplier is a row in the one party master now, not a
            // workshop-only list.
            var supplier = new Party { Code = "SUP-1", Name = "Parts Vendor", IsSupplier = true };
            var part = new Part { Sku = "LCD-1", Name = "LCD", SellingPrice = 200 };
            db.AddRange(supplier, part);
            await db.SaveChangesAsync();

            _supplierId = supplier.Id;
            _partId = part.Id;
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

    [SkippableFact]
    public async Task Purchases_keep_quantity_weighted_average_and_newest_last_cost()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        var service = new PartProcurementService(db, _clock, _user);

        Assert.True((await service.ReceiveAsync(new(_supplierId, "INV-1", new DateOnly(2026, 8, 20),
            0, 0, 0, null, [new(_partId, 10, 100, 180, null)]))).Ok);
        Assert.True((await service.ReceiveAsync(new(_supplierId, "INV-2", new DateOnly(2026, 8, 22),
            0, 0, 0, null, [new(_partId, 2, 200, 250, null)]))).Ok);

        db.ChangeTracker.Clear();
        var part = await db.Parts.SingleAsync(x => x.Id == _partId);

        Assert.Equal(12, part.PurchasedQuantity);

        // Weighted by quantity: (10x100 + 2x200) / 12, not the 150 a mean of
        // the two prices would give.
        Assert.Equal(116.6667m, part.AverageCost);
        Assert.Equal(200, part.LastPurchaseCost);
        Assert.Equal(250, part.SellingPrice);
    }

    [SkippableFact]
    public async Task Older_invoice_entered_late_does_not_replace_latest_cost()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        var service = new PartProcurementService(db, _clock, _user);

        await service.ReceiveAsync(new(_supplierId, null, new DateOnly(2026, 8, 22),
            0, 0, 0, null, [new(_partId, 1, 200, null, null)]));

        // Entered afterwards, but dated earlier.
        await service.ReceiveAsync(new(_supplierId, null, new DateOnly(2026, 8, 1),
            0, 0, 0, null, [new(_partId, 1, 50, null, null)]));

        db.ChangeTracker.Clear();
        var part = await db.Parts.SingleAsync(x => x.Id == _partId);

        // The average moves, because it covers every purchase ever made...
        Assert.Equal(125, part.AverageCost);

        // ...but the last cost does not, because it exists to show price drift
        // and rewriting it backwards would hide exactly that.
        Assert.Equal(200, part.LastPurchaseCost);
        Assert.Equal(new DateOnly(2026, 8, 22), part.LastPurchasedOn);
    }

    [SkippableFact]
    public async Task A_purchase_is_refused_against_a_party_who_is_not_a_supplier()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();

        // A customer-only party. The unified master holds both sides, so the
        // side has to be checked rather than assumed by which list they came from.
        var customer = new Party { Code = "CUS-1", Name = "A Customer", IsCustomer = true };
        db.Add(customer);
        await db.SaveChangesAsync();

        var result = await new PartProcurementService(db, _clock, _user).ReceiveAsync(
            new(customer.Id, null, _clock.Today, 0, 0, 0, null, [new(_partId, 1, 10, null, null)]));

        Assert.False(result.Ok);
        Assert.Equal("purchase.not-supplier", result.Code);
    }

    [SkippableFact]
    public async Task Price_history_reads_back_every_purchase_of_a_part()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        var service = new PartProcurementService(db, _clock, _user);

        await service.ReceiveAsync(new(_supplierId, "INV-1", new DateOnly(2026, 8, 1),
            0, 0, 0, null, [new(_partId, 1, 90, null, null)]));
        await service.ReceiveAsync(new(_supplierId, "INV-2", new DateOnly(2026, 8, 20),
            0, 0, 0, null, [new(_partId, 1, 110, null, null)]));

        var history = await service.PriceHistoryAsync(_partId);

        // Oldest first, so a rising cost reads as a rising line.
        Assert.Equal([90m, 110m], history.Select(x => x.UnitCost));
        Assert.All(history, x => Assert.Equal("Parts Vendor", x.Supplier));
    }
}
