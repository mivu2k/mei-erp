using MeiErp.Platform.Kernel;
using MeiErp.Platform.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeiErp.Modules.Inventory;

public static class InventoryModule
{
    public const string Key = "inventory";

    public const string ItemsView = "inventory.items.view";
    public const string ItemsManage = "inventory.items.manage";
    public const string StockAdjust = "inventory.stock.adjust";
    public const string CostsView = "inventory.costs.view";
    public const string WarehousesManage = "inventory.warehouses.manage";
    public const string TransfersManage = "inventory.transfers.manage";
    public const string CountsManage = "inventory.counts.manage";
    public const string TrackingManage = "inventory.tracking.manage";
    public const string ProductsManage = "inventory.products.manage";
    public const string ReturnsPost = "inventory.returns.post";
    public const string ReportsView = "inventory.reports.view";

    /// <summary>Create and rename the stock books themselves. Rare, and destructive to get wrong.</summary>
    public const string DomainsManage = "inventory.domains.manage";

    public static ModuleDescriptor Descriptor => new()
    {
        Key = Key,
        Name = "Inventory",

        // Buying and selling moved out to Sales & Purchase. What is left here
        // is the goods themselves and the stock ledger behind them, kept in two
        // separate books - the main store and the workshop's spares.
        Description = "Items, stock books, warehouses, transfers and counts.",
        BasePath = "/inventory",
        Icon = "Inventory2",
        Color = "#00897b",
        SortOrder = 3,
        Schema = "inventory",

        Permissions =
        [
            new(ItemsView,        "Items",      "See items and stock levels"),
            new(ItemsManage,      "Items",      "Add and edit items"),
            new(StockAdjust,      "Stock",      "Correct a stock figure after a count"),

            new(CostsView,        "Reporting",  "See cost and margin figures")
            ,new(WarehousesManage,"Stock","Manage stock locations")
            ,new(TransfersManage,"Stock","Dispatch and receive warehouse transfers")
            ,new(CountsManage,"Stock","Create, count and post stock takes")
            ,new(TrackingManage,"Stock","Track serialized units and expiring batches")
            ,new(ProductsManage,"Items","Manage product families, models and accessories")
            ,new(ReturnsPost,"Stock","Post customer and supplier returns")
            ,new(ReportsView,"Reporting","Run Inventory reports")
            ,new(DomainsManage,"Stock","Create and rename the stock books (main store, spare parts)")
        ],

        RoleTemplates =
        [
            new("Storekeeper", "Keeps the item list and corrects stock after a count.",
                [ItemsView, ItemsManage, StockAdjust, ReturnsPost]),

            new("Inventory Manager", "Everything in the module, including cost and valuation.",
                [ItemsView, ItemsManage, StockAdjust, CostsView, WarehousesManage, TransfersManage,
                 CountsManage, TrackingManage, ProductsManage, ReturnsPost, ReportsView, DomainsManage])
        ],

        Nav =
        [
            new("Items",              "/inventory/items", "Inventory2", ItemsView),
            new("Products", "/inventory/products", "Category", ProductsManage)
            ,new("Warehouses", "/inventory/warehouses", "Warehouse", WarehousesManage, "Stock control")
            ,new("Transfers", "/inventory/transfers", "SwapHoriz", TransfersManage, "Stock control")
            ,new("Stock counts", "/inventory/counts", "FactCheck", CountsManage, "Stock control")
            ,new("Serials & batches", "/inventory/tracking", "QrCode", TrackingManage, "Stock control")
            ,new("Stock books", "/inventory/domains", "LibraryBooks", DomainsManage, "Stock control")
        ],

    };

    public static IServiceCollection AddInventoryModule(
        this IServiceCollection services, IConfiguration config)
    {
        var connection = config.GetConnectionString("Platform")
            ?? throw new InvalidOperationException("No 'Platform' connection string for the Inventory module.");

        services.AddDbContext<InventoryDbContext>(options =>
            options.UseNpgsql(connection, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__migrations", "inventory");
                npgsql.EnableRetryOnFailure(3);
            }));

        services.AddScoped<IStockDomainService, StockDomainService>();

        // Scoped: the chosen stock book follows the person's circuit, so it
        // sticks as they move between items, transfers and counts.
        services.AddScoped<StockBookContext>();
        services.AddScoped<IStockService, StockService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IWarehouseService, WarehouseService>();
        services.AddScoped<ITransferService, TransferService>();
        services.AddScoped<IInventoryCountService, InventoryCountService>();
        services.AddScoped<IStockTrackingService, StockTrackingService>();
        services.AddScoped<IScanResolver, InventoryScanResolver>();
        services.AddScoped<IProductHierarchyService, ProductHierarchyService>();
        services.AddScoped<IInventoryReturnService, InventoryReturnService>();

        return services;
    }
}

public sealed class InventorySeeder(InventoryDbContext db)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        // The two books the business actually keeps. Seeded before anything
        // else, because every item and every warehouse has to belong to one.
        if (!await db.StockDomains.AnyAsync(ct))
        {
            db.StockDomains.AddRange(
                new StockDomain
                {
                    Code = StockDomainCodes.Main,
                    Name = "Main Store",
                    Description = "Goods the business buys and sells.",
                    IsDefault = true
                },
                new StockDomain
                {
                    Code = StockDomainCodes.Spare,
                    Name = "Spare Parts",
                    Description = "Parts the workshop consumes on repair jobs."
                });

            await db.SaveChangesAsync(ct);
        }

        // Anything created before the books existed belongs to the main store:
        // that is what a single undivided inventory was. Written here rather
        // than in the migration so a restored older backup is repaired too.
        var main = await db.StockDomains
            .Where(x => x.Code == StockDomainCodes.Main)
            .Select(x => x.Id)
            .FirstAsync(ct);

        await db.Items.Where(x => x.DomainId == 0)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.DomainId, main), ct);
        await db.Warehouses.Where(x => x.DomainId == 0)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.DomainId, main), ct);
        await db.StockMovements.Where(x => x.DomainId == 0)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.DomainId, main), ct);

        if (!await db.Categories.AnyAsync(ct))
        {
            db.Categories.AddRange(
                new ItemCategory { Name = "General", Code = "GEN" },
                new ItemCategory { Name = "Spare parts", Code = "SPR" },
                new ItemCategory { Name = "Consumables", Code = "CON" });

            await db.SaveChangesAsync(ct);
        }
    }
}

public static class InventorySeederExtensions
{
    public static async Task SeedInventoryAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await new InventorySeeder(db).SeedAsync();
    }
}
