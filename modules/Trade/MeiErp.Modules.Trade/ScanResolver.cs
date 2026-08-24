using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Trade;

/// <summary>
/// Document numbers on the customer side - the quotation, order, invoice or
/// delivery note somebody is holding across the counter.
///
/// Sales and Purchase are two modules over one schema, so they answer as two
/// resolvers: a person who may see what we sold does not automatically get to
/// see what we paid for it.
/// </summary>
public sealed class SalesScanResolver(TradeDbContext db) : IScanResolver
{
    public string ModuleKey => SalesModule.Key;

    public async Task<IReadOnlyList<ScanHit>> ResolveAsync(string code, CancellationToken ct = default)
    {
        var hits = new List<ScanHit>();

        var quotation = await db.Quotations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Number == code && x.Direction == TradeDirection.Sales, ct);
        if (quotation is not null)
            hits.Add(new ScanHit(quotation.Number, $"Quotation - {quotation.PartyName} ({quotation.Status})",
                $"/sales/quotations/{quotation.Id}", ModuleKey, SalesModule.QuotationsView, "RequestQuote"));

        var order = await db.SalesOrders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Number == code, ct);
        if (order is not null)
            hits.Add(new ScanHit(order.Number, $"Sales order - {order.PartyName} ({order.Status})",
                $"/sales/orders/{order.Id}", ModuleKey, SalesModule.OrdersView, "ShoppingCart"));

        var invoice = await db.Invoices.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Number == code && x.Direction == TradeDirection.Sales, ct);
        if (invoice is not null)
            hits.Add(new ScanHit(invoice.Number, $"Invoice - {invoice.PartyName} ({invoice.Status})",
                $"/sales/invoices/{invoice.Id}", ModuleKey, SalesModule.InvoicesView, "ReceiptLong"));

        // A delivery note has no page of its own; the list is where it reads.
        var delivery = await db.Deliveries.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Number == code, ct);
        if (delivery is not null)
            hits.Add(new ScanHit(delivery.Number, $"Delivery note - {delivery.PartyName}",
                "/sales/deliveries", ModuleKey, SalesModule.OrdersView, "LocalShipping"));

        return hits;
    }
}

/// <summary>
/// Document numbers on the supplier side - our purchase orders and invoices,
/// the goods receipt raised at the loading bay, and the workshop's own parts
/// buying.
/// </summary>
public sealed class PurchaseScanResolver(TradeDbContext db) : IScanResolver
{
    public string ModuleKey => PurchaseModule.Key;

    public async Task<IReadOnlyList<ScanHit>> ResolveAsync(string code, CancellationToken ct = default)
    {
        var hits = new List<ScanHit>();

        var quotation = await db.Quotations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Number == code && x.Direction == TradeDirection.Purchase, ct);
        if (quotation is not null)
            hits.Add(new ScanHit(quotation.Number, $"Supplier quotation - {quotation.PartyName} ({quotation.Status})",
                $"/purchase/quotations/{quotation.Id}", ModuleKey, PurchaseModule.QuotationsView, "RequestQuote"));

        var order = await db.PurchaseOrders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Number == code, ct);
        if (order is not null)
            hits.Add(new ScanHit(order.Number, $"Purchase order - {order.PartyName} ({order.Status})",
                $"/purchase/orders/{order.Id}", ModuleKey, PurchaseModule.OrdersView, "ShoppingBasket"));

        var invoice = await db.Invoices.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Number == code && x.Direction == TradeDirection.Purchase, ct);
        if (invoice is not null)
            hits.Add(new ScanHit(invoice.Number, $"Supplier invoice - {invoice.PartyName} ({invoice.Status})",
                $"/purchase/invoices/{invoice.Id}", ModuleKey, PurchaseModule.InvoicesView, "ReceiptLong"));

        // Receipts and parts purchases read from their lists rather than a page
        // of their own.
        var receipt = await db.GoodsReceipts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Number == code, ct);
        if (receipt is not null)
            hits.Add(new ScanHit(receipt.Number, $"Goods receipt - {receipt.PartyName}",
                "/purchase/goods-receipts", ModuleKey, PurchaseModule.OrdersView, "Inventory"));

        var partPurchase = await db.PartPurchases.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Number == code, ct);
        if (partPurchase is not null)
            hits.Add(new ScanHit(partPurchase.Number, $"Parts purchase - {partPurchase.PartyName}",
                "/purchase/part-purchases", ModuleKey, PurchaseModule.OrdersView, "Handyman"));

        return hits;
    }
}
