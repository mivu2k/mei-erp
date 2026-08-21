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
    public const string PurchasingManage = "inventory.purchasing.manage";
    public const string ReceiptPost = "inventory.receipt.post";
    public const string SalesManage = "inventory.sales.manage";
    public const string DeliveryPost = "inventory.delivery.post";
    public const string CostsView = "inventory.costs.view";
    public const string PartiesManage = "inventory.parties.manage";

    public static ModuleDescriptor Descriptor => new()
    {
        Key = Key,
        Name = "Inventory",
        Description = "Items, stock, purchasing and sales.",
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
            new(PartiesManage,    "Parties",    "Manage customers and suppliers"),
            new(PurchasingManage, "Purchasing", "Raise and edit purchase orders"),
            new(ReceiptPost,      "Purchasing", "Receive goods into stock"),
            new(SalesManage,      "Sales",      "Raise and edit sales orders"),

            // Split so the person taking an order need not be the one moving
            // the goods out of the store.
            new(DeliveryPost,     "Sales",      "Issue goods out of stock against an order"),

            new(CostsView,        "Reporting",  "See cost and margin figures")
        ],

        RoleTemplates =
        [
            new("Storekeeper", "Receives and issues stock, and keeps the item list.",
                [ItemsView, ItemsManage, StockAdjust, ReceiptPost, DeliveryPost]),

            new("Buyer", "Raises purchase orders and manages suppliers.",
                [ItemsView, PartiesManage, PurchasingManage, CostsView]),

            new("Sales", "Takes orders from customers.",
                [ItemsView, PartiesManage, SalesManage]),

            new("Inventory Manager", "Everything in the module, including cost and margin.",
                [ItemsView, ItemsManage, StockAdjust, PartiesManage, PurchasingManage,
                 ReceiptPost, SalesManage, DeliveryPost, CostsView])
        ],

        Approvables =
        [
            new(PurchasingService.DocumentType, "Purchase order", "Order value")
        ]
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

        services.AddScoped<IStockService, StockService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IPurchasingService, PurchasingService>();
        services.AddScoped<ISalesService, SalesService>();
        services.AddScoped<IApprovalSink, PurchaseOrderApprovalSink>();

        return services;
    }
}

public sealed class InventorySeeder(InventoryDbContext db)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

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
