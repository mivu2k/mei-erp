using MeiErp.Platform.Kernel;
using MeiErp.Platform.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeiErp.Modules.Trade;

/// <summary>
/// Registration shared by the Sales and Purchase modules.
///
/// They are two modules to the people using them - separate tiles, separate
/// permissions, separate nav - but one implementation underneath, because they
/// share the party master and the same commercial machinery. Splitting the code
/// as well would recreate exactly the duplication this whole exercise removed:
/// two definitions of a party, two places to fix a pricing rule.
///
/// So: one project, one <c>trade</c> schema, one DbContext, two
/// <see cref="ModuleDescriptor"/>s over the top.
/// </summary>
public static class TradeModule
{
    /// <summary>The one schema behind both modules.</summary>
    public const string Schema = "trade";

    public static IServiceCollection AddTradeModule(
        this IServiceCollection services, IConfiguration config)
    {
        var connection = config.GetConnectionString("Platform")
            ?? throw new InvalidOperationException("No 'Platform' connection string for the Trade modules.");

        services.AddDbContext<TradeDbContext>(options =>
            options.UseNpgsql(connection, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__migrations", Schema);
                npgsql.EnableRetryOnFailure(3);
            }));

        services.AddScoped<IPartyService, PartyService>();
        services.AddScoped<IPurchasingService, PurchasingService>();
        services.AddScoped<ISalesService, SalesService>();
        services.AddScoped<IPartProcurementService, PartProcurementService>();
        services.AddScoped<ITradeDocumentService, TradeDocumentService>();
        services.AddScoped<IScanResolver, SalesScanResolver>();
        services.AddScoped<IScanResolver, PurchaseScanResolver>();
        services.AddScoped<IApprovalSink, QuotationApprovalSink>();
        services.AddScoped<IApprovalSink, InvoiceApprovalSink>();
        services.AddScoped<IApprovalSink, PurchaseOrderApprovalSink>();

        // ITradeStockPort is deliberately NOT registered here. The host wires an
        // adapter over whichever module is holding the stock, so these modules
        // and Inventory stay independently buildable.

        return services;
    }
}

/// <summary>
/// Purchase - everything the business buys.
///
/// Suppliers, purchase orders and goods receipts. An order is a commitment;
/// only the receipt moves stock.
/// </summary>
public static class PurchaseModule
{
    public const string Key = "purchase";

    public const string SuppliersView = "purchase.suppliers.view";
    public const string SuppliersManage = "purchase.suppliers.manage";
    public const string OrdersView = "purchase.orders.view";
    public const string OrdersManage = "purchase.orders.manage";
    public const string QuotationsView = "purchase.quotations.view";
    public const string QuotationsManage = "purchase.quotations.manage";
    public const string InvoicesView = "purchase.invoices.view";
    public const string InvoicesManage = "purchase.invoices.manage";
    public const string ReceiptPost = "purchase.receipt.post";
    public const string CostsView = "purchase.costs.view";

    public static ModuleDescriptor Descriptor => new()
    {
        Key = Key,
        Name = "Purchase",
        Description = "Suppliers, purchase orders and goods receipts.",
        BasePath = "/purchase",
        Icon = "ShoppingCart",
        Color = "#5c6bc0",
        SortOrder = 4,
        Schema = TradeModule.Schema,

        Permissions =
        [
            new(SuppliersView,   "Suppliers", "See suppliers"),
            new(SuppliersManage, "Suppliers", "Add and edit suppliers"),
            new(OrdersView,      "Buying",    "See purchase orders and goods receipts"),
            new(OrdersManage,    "Buying",    "Raise and edit purchase orders"),

            // Split so the person who orders is not necessarily the person who
            // signs the goods in off the van.
            new(ReceiptPost,     "Buying",    "Receive goods into stock against an order"),

            new(QuotationsView,   "Quotations", "See supplier quotations"),
            new(QuotationsManage, "Quotations", "Raise and submit supplier quotations"),
            new(InvoicesView,     "Invoices",   "See supplier invoices"),
            new(InvoicesManage,   "Invoices",   "Enter and post supplier invoices"),

            new(CostsView,       "Reporting", "See cost figures")
        ],

        RoleTemplates =
        [
            new("Buyer", "Raises quotations, orders and invoices, and manages suppliers.",
                [SuppliersView, SuppliersManage, OrdersView, OrdersManage,
                 QuotationsView, QuotationsManage, InvoicesView, InvoicesManage, CostsView]),

            new("Goods Receiver", "Signs goods in, but raises no paperwork.",
                [OrdersView, ReceiptPost]),

            new("Purchase Manager", "Everything in the module, including cost.",
                [SuppliersView, SuppliersManage, OrdersView, OrdersManage, ReceiptPost,
                 QuotationsView, QuotationsManage, InvoicesView, InvoicesManage, CostsView])
        ],

        Nav =
        [
            new("Suppliers",       "/purchase/suppliers",      "Contacts",     SuppliersView),

            // The chain, in the order it happens.
            new("Quotations",      "/purchase/quotations",     "RequestQuote", QuotationsView, "Buying"),
            new("Purchase orders", "/purchase/orders",         "ShoppingCart", OrdersView, "Buying"),
            new("Goods receipts",  "/purchase/goods-receipts", "Inventory",    OrdersView),
            new("Invoices",        "/purchase/invoices",       "ReceiptLong",  InvoicesView, "Buying"),
            new("Supplier returns","/purchase/returns",        "AssignmentReturn", OrdersView, "Buying"),

            // The workshop's parts buying, which used to sit in Repair behind
            // its own supplier list.
            new("Parts",         "/purchase/parts",          "Handyman",  OrdersView, "Workshop parts"),
            new("Part purchases","/purchase/part-purchases", "ReceiptLong", OrdersView, "Workshop parts")
        ],

        Approvables =
        [
            new(PurchasingService.DocumentType, "Purchase order", "Order value"),
            new(TradeDocumentService.QuotationDocumentType, "Quotation", "Quotation value"),
            new(TradeDocumentService.InvoiceDocumentType, "Invoice", "Invoice value")
        ]
    };
}

/// <summary>
/// Sales - everything the business sells.
///
/// Customers, sales orders and deliveries. The mirror image of Purchase: an
/// order is a promise, only the delivery moves stock.
/// </summary>
public static class SalesModule
{
    public const string Key = "sales";

    public const string CustomersView = "sales.customers.view";
    public const string CustomersManage = "sales.customers.manage";
    public const string OrdersView = "sales.orders.view";
    public const string OrdersManage = "sales.orders.manage";
    public const string QuotationsView = "sales.quotations.view";
    public const string QuotationsManage = "sales.quotations.manage";
    public const string InvoicesView = "sales.invoices.view";
    public const string InvoicesManage = "sales.invoices.manage";
    public const string DeliveryPost = "sales.delivery.post";
    public const string MarginView = "sales.margin.view";

    public static ModuleDescriptor Descriptor => new()
    {
        Key = Key,
        Name = "Sales",
        Description = "Customers, sales orders and deliveries.",
        BasePath = "/sales",
        Icon = "PointOfSale",
        Color = "#00897b",
        SortOrder = 5,
        Schema = TradeModule.Schema,

        Permissions =
        [
            new(CustomersView,   "Customers", "See customers"),
            new(CustomersManage, "Customers", "Add and edit customers"),
            new(OrdersView,      "Selling",   "See sales orders and deliveries"),
            new(OrdersManage,    "Selling",   "Raise and edit sales orders"),

            // And likewise, taking the order is not issuing the goods.
            new(DeliveryPost,    "Selling",   "Issue goods out of stock against an order"),

            new(QuotationsView,   "Quotations", "See customer quotations"),
            new(QuotationsManage, "Quotations", "Raise and submit customer quotations"),
            new(InvoicesView,     "Invoices",   "See customer invoices"),
            new(InvoicesManage,   "Invoices",   "Raise and post customer invoices"),

            // Separate from seeing the order at all, so a salesperson can work
            // without seeing what the goods cost the business.
            new(MarginView,      "Reporting", "See cost and margin figures")
        ],

        RoleTemplates =
        [
            new("Salesperson", "Quotes, takes orders and bills customers.",
                [CustomersView, CustomersManage, OrdersView, OrdersManage,
                 QuotationsView, QuotationsManage, InvoicesView, InvoicesManage]),

            new("Dispatcher", "Issues goods against an order, but raises no paperwork.",
                [OrdersView, DeliveryPost]),

            new("Sales Manager", "Everything in the module, including margin.",
                [CustomersView, CustomersManage, OrdersView, OrdersManage, DeliveryPost,
                 QuotationsView, QuotationsManage, InvoicesView, InvoicesManage, MarginView])
        ],

        Nav =
        [
            new("Customers",    "/sales/customers",  "Contacts",      CustomersView),

            // The chain, in the order it happens.
            new("Quotations",   "/sales/quotations", "RequestQuote",  QuotationsView, "Selling"),
            new("Sales orders", "/sales/orders",     "PointOfSale",   OrdersView, "Selling"),
            new("Deliveries",   "/sales/deliveries", "LocalShipping", OrdersView, "Selling"),
            new("Invoices",     "/sales/invoices",   "ReceiptLong",   InvoicesView, "Selling"),
            new("Customer returns", "/sales/returns", "AssignmentReturn", OrdersView, "Selling")
        ],

        Approvables =
        [
            new(TradeDocumentService.QuotationDocumentType, "Quotation", "Quotation value"),
            new(TradeDocumentService.InvoiceDocumentType, "Invoice", "Invoice value")
        ]
    };
}

public sealed class TradeSeeder(TradeDbContext db)
{
    public Task SeedAsync(CancellationToken ct = default) => db.Database.MigrateAsync(ct);
}

public static class TradeSeederExtensions
{
    public static async Task SeedTradeAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TradeDbContext>();
        await new TradeSeeder(db).SeedAsync();
    }
}
