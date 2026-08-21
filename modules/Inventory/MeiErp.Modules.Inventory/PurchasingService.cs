using MeiErp.Platform.Kernel;
using MeiErp.Platform.Workflow;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Inventory;

/// <summary>
/// Buying: an order is a commitment, a receipt is what actually moves stock.
/// Keeping those separate is what makes a partial delivery expressible.
/// </summary>
public interface IPurchasingService
{
    Task<IReadOnlyList<PurchaseOrder>> ListOrdersAsync(PurchaseOrderStatus? status, CancellationToken ct = default);
    Task<PurchaseOrder?> GetOrderAsync(int id, CancellationToken ct = default);
    Task<Result<PurchaseOrder>> SaveOrderAsync(PurchaseOrderInput input, CancellationToken ct = default);
    Task<Result<PurchaseOrder>> SubmitOrderAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Receives goods against an approved order. This is the only thing in
    /// purchasing that moves stock.
    /// </summary>
    Task<Result<GoodsReceipt>> ReceiveAsync(ReceiptInput input, CancellationToken ct = default);
}

public sealed record PurchaseOrderInput(
    int? Id, int PartyId, DateOnly Date, string? Notes,
    IReadOnlyList<PurchaseOrderLineInput> Lines);

public sealed record PurchaseOrderLineInput(int ItemId, decimal Quantity, decimal UnitCost);

public sealed record ReceiptInput(
    int PurchaseOrderId, DateOnly Date, string? Notes,
    IReadOnlyList<ReceiptLineInput> Lines);

public sealed record ReceiptLineInput(int ItemId, decimal Quantity, decimal UnitCost);

public sealed class PurchasingService(
    InventoryDbContext db,
    IStockService stock,
    IApprovalEngine approvals,
    IClock clock) : IPurchasingService
{
    public const string DocumentType = "inventory.purchase-order";

    public async Task<IReadOnlyList<PurchaseOrder>> ListOrdersAsync(
        PurchaseOrderStatus? status, CancellationToken ct = default)
    {
        var query = db.PurchaseOrders.AsNoTracking().Include(o => o.Lines).AsQueryable();
        if (status is not null) query = query.Where(o => o.Status == status);
        return await query.OrderByDescending(o => o.Id).Take(300).ToListAsync(ct);
    }

    public Task<PurchaseOrder?> GetOrderAsync(int id, CancellationToken ct = default) =>
        db.PurchaseOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<Result<PurchaseOrder>> SaveOrderAsync(
        PurchaseOrderInput input, CancellationToken ct = default)
    {
        if (input.Lines.Count == 0)
            return Result.Fail<PurchaseOrder>("An order needs at least one line.", "po.no-lines");

        var party = await db.Parties.FirstOrDefaultAsync(p => p.Id == input.PartyId, ct);
        if (party is null) return Result.Fail<PurchaseOrder>("That supplier no longer exists.", "po.no-party");

        if (!party.IsSupplier)
            return Result.Fail<PurchaseOrder>($"{party.Name} is not marked as a supplier.", "po.not-supplier");

        PurchaseOrder order;

        if (input.Id is null or 0)
        {
            order = new PurchaseOrder
            {
                Number = await NextNumberAsync("PO", ct),
                Status = PurchaseOrderStatus.Draft
            };
            db.PurchaseOrders.Add(order);
        }
        else
        {
            var existing = await db.PurchaseOrders.Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Id == input.Id, ct);

            if (existing is null)
                return Result.Fail<PurchaseOrder>("That order no longer exists.", "po.not-found");

            if (existing.Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Returned))
            {
                // Changing quantities under an approver, or after goods have
                // started arriving, makes the received figures meaningless.
                return Result.Fail<PurchaseOrder>(
                    "This order has been submitted and cannot be edited. Withdraw it first.",
                    "po.not-editable");
            }

            db.PurchaseOrderLines.RemoveRange(existing.Lines);
            existing.Lines.Clear();
            order = existing;
        }

        var items = await LoadItemsAsync(input.Lines.Select(l => l.ItemId), ct);

        order.PartyId = party.Id;
        order.PartyName = party.Name;
        order.Date = input.Date;
        order.Notes = input.Notes;

        foreach (var line in input.Lines)
        {
            if (!items.TryGetValue(line.ItemId, out var item))
                return Result.Fail<PurchaseOrder>("One of the lines points at an item that no longer exists.", "po.bad-item");

            if (line.Quantity <= 0)
                return Result.Fail<PurchaseOrder>($"{item.Name} needs a quantity greater than nothing.", "po.bad-quantity");

            order.Lines.Add(new PurchaseOrderLine
            {
                ItemId = item.Id,
                ItemCode = item.Code,
                ItemName = item.Name,
                Quantity = line.Quantity,
                UnitCost = line.UnitCost
            });
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(order);
    }

    public async Task<Result<PurchaseOrder>> SubmitOrderAsync(int id, CancellationToken ct = default)
    {
        var order = await db.PurchaseOrders.Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

        if (order is null) return Result.Fail<PurchaseOrder>("That order no longer exists.", "po.not-found");

        if (order.Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Returned))
            return Result.Fail<PurchaseOrder>("This has already been submitted.", "po.already-submitted");

        var submitted = await approvals.SubmitAsync(new SubmitApproval(
            ModuleKey: InventoryModule.Key,
            DocumentType: DocumentType,
            DocumentId: order.Id,
            DocumentReference: order.Number,
            Summary: $"{order.PartyName} — {order.Lines.Count} " +
                     $"{(order.Lines.Count == 1 ? "line" : "lines")}, {order.Total:N2}",
            DocumentUrl: $"/inventory/purchase-orders/{order.Id}",

            // Order value drives band routing, so a large order needs more
            // signatures than a small one without any code deciding that.
            Amount: order.Total,
            Currency: "PKR"), ct);

        if (submitted.Failed)
            return Result.Fail<PurchaseOrder>(submitted.Error!, submitted.Code);

        order.Status = PurchaseOrderStatus.Pending;
        order.ApprovalRequestId = submitted.Value.Id;
        order.DecisionComment = null;

        await db.SaveChangesAsync(ct);
        return Result.Success(order);
    }

    public async Task<Result<GoodsReceipt>> ReceiveAsync(
        ReceiptInput input, CancellationToken ct = default)
    {
        var order = await db.PurchaseOrders.Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == input.PurchaseOrderId, ct);

        if (order is null) return Result.Fail<GoodsReceipt>("That order no longer exists.", "po.not-found");

        if (order.Status is not (PurchaseOrderStatus.Approved or PurchaseOrderStatus.PartiallyReceived))
        {
            // Receiving against an unapproved order would let someone commit
            // the company to a purchase by unloading a van.
            return Result.Fail<GoodsReceipt>(
                "Goods can only be received against an approved order.", "po.not-approved");
        }

        if (input.Lines.Count == 0)
            return Result.Fail<GoodsReceipt>("Nothing has been entered as received.", "receipt.no-lines");

        // Checked in full before anything moves. A half-posted receipt would
        // leave stock up and the order's received figures wrong, and unpicking
        // that by hand is exactly the mess this avoids.
        foreach (var line in input.Lines)
        {
            var orderLine = order.Lines.FirstOrDefault(l => l.ItemId == line.ItemId);
            if (orderLine is null)
                return Result.Fail<GoodsReceipt>("Something was received that is not on the order.", "receipt.not-ordered");

            if (line.Quantity <= 0)
                return Result.Fail<GoodsReceipt>($"{orderLine.ItemName} needs a quantity greater than nothing.", "receipt.bad-quantity");

            if (line.Quantity > orderLine.Outstanding)
            {
                return Result.Fail<GoodsReceipt>(
                    $"{orderLine.ItemName}: {line.Quantity:0.##} received but only " +
                    $"{orderLine.Outstanding:0.##} is still outstanding on the order.",
                    "receipt.over-receipt");
            }
        }

        var receipt = new GoodsReceipt
        {
            Number = await NextNumberAsync("GR", ct),
            Date = input.Date,
            PurchaseOrderId = order.Id,
            PartyId = order.PartyId,
            PartyName = order.PartyName,
            Notes = input.Notes
        };

        foreach (var line in input.Lines)
        {
            var orderLine = order.Lines.First(l => l.ItemId == line.ItemId);

            receipt.Lines.Add(new GoodsReceiptLine
            {
                ItemId = line.ItemId,
                ItemCode = orderLine.ItemCode,
                ItemName = orderLine.ItemName,
                Quantity = line.Quantity,
                UnitCost = line.UnitCost
            });

            orderLine.Received += line.Quantity;

            var moved = await stock.ReceiveAsync(
                line.ItemId, line.Quantity, line.UnitCost, input.Date,
                StockMovementType.Receipt, receipt.Number, "goods-receipt", null, ct);

            if (moved.Failed) return Result.Fail<GoodsReceipt>(moved.Error!, moved.Code);
        }

        order.Status = order.IsFullyReceived
            ? PurchaseOrderStatus.Received
            : PurchaseOrderStatus.PartiallyReceived;

        db.GoodsReceipts.Add(receipt);
        await db.SaveChangesAsync(ct);

        return Result.Success(receipt);
    }

    private async Task<Dictionary<int, Item>> LoadItemsAsync(
        IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Items.Where(i => list.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
    }

    private async Task<string> NextNumberAsync(string prefix, CancellationToken ct)
    {
        var year = clock.Today.Year;
        var stem = $"{prefix}-{year % 100:D2}-";

        var count = prefix == "PO"
            ? await db.PurchaseOrders.IgnoreQueryFilters().CountAsync(o => o.Number.StartsWith(stem), ct)
            : await db.GoodsReceipts.IgnoreQueryFilters().CountAsync(r => r.Number.StartsWith(stem), ct);

        return stem + (count + 1).ToString().PadLeft(4, '0');
    }
}

/// <summary>How Inventory hears that a purchase order was decided.</summary>
public sealed class PurchaseOrderApprovalSink(InventoryDbContext db) : IApprovalSink
{
    public string DocumentType => PurchasingService.DocumentType;

    public async Task<Result> OnSettledAsync(
        int documentId, ApprovalStatus status, ApprovalRequest request, CancellationToken ct = default)
    {
        var order = await db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == documentId, ct);
        if (order is null)
            return Result.Fail("The purchase order behind this approval has gone.", "po.not-found");

        order.DecisionComment = request.Actions
            .OrderByDescending(a => a.ActedUtc)
            .Select(a => a.Comment)
            .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

        order.Status = status switch
        {
            ApprovalStatus.Approved => PurchaseOrderStatus.Approved,
            ApprovalStatus.Rejected => PurchaseOrderStatus.Rejected,
            ApprovalStatus.Returned => PurchaseOrderStatus.Returned,
            ApprovalStatus.Cancelled => PurchaseOrderStatus.Cancelled,
            _ => order.Status
        };

        return Result.Success();
    }
}
