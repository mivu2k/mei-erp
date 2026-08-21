using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Inventory;

/// <summary>
/// Selling: the mirror image of buying. An order is a promise, a delivery is
/// what actually moves stock.
/// </summary>
public interface ISalesService
{
    Task<IReadOnlyList<SalesOrder>> ListOrdersAsync(SalesOrderStatus? status, CancellationToken ct = default);
    Task<SalesOrder?> GetOrderAsync(int id, CancellationToken ct = default);
    Task<Result<SalesOrder>> SaveOrderAsync(SalesOrderInput input, CancellationToken ct = default);
    Task<Result<SalesOrder>> ConfirmAsync(int id, CancellationToken ct = default);
    Task<Result<Delivery>> DeliverAsync(DeliveryInput input, CancellationToken ct = default);
}

public sealed record SalesOrderInput(
    int? Id, int PartyId, DateOnly Date, string? Notes,
    IReadOnlyList<SalesOrderLineInput> Lines);

public sealed record SalesOrderLineInput(int ItemId, decimal Quantity, decimal UnitPrice);

public sealed record DeliveryInput(
    int SalesOrderId, DateOnly Date, string? CollectedBy, string? Notes,
    IReadOnlyList<DeliveryLineInput> Lines);

public sealed record DeliveryLineInput(int ItemId, decimal Quantity);

public sealed class SalesService(
    InventoryDbContext db, IStockService stock, IClock clock) : ISalesService
{
    public async Task<IReadOnlyList<SalesOrder>> ListOrdersAsync(
        SalesOrderStatus? status, CancellationToken ct = default)
    {
        var query = db.SalesOrders.AsNoTracking().Include(o => o.Lines).AsQueryable();
        if (status is not null) query = query.Where(o => o.Status == status);
        return await query.OrderByDescending(o => o.Id).Take(300).ToListAsync(ct);
    }

    public Task<SalesOrder?> GetOrderAsync(int id, CancellationToken ct = default) =>
        db.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<Result<SalesOrder>> SaveOrderAsync(
        SalesOrderInput input, CancellationToken ct = default)
    {
        if (input.Lines.Count == 0)
            return Result.Fail<SalesOrder>("An order needs at least one line.", "so.no-lines");

        var party = await db.Parties.FirstOrDefaultAsync(p => p.Id == input.PartyId, ct);
        if (party is null) return Result.Fail<SalesOrder>("That customer no longer exists.", "so.no-party");

        if (!party.IsCustomer)
            return Result.Fail<SalesOrder>($"{party.Name} is not marked as a customer.", "so.not-customer");

        SalesOrder order;

        if (input.Id is null or 0)
        {
            order = new SalesOrder
            {
                Number = await NextNumberAsync("SO", ct),
                Status = SalesOrderStatus.Draft
            };
            db.SalesOrders.Add(order);
        }
        else
        {
            var existing = await db.SalesOrders.Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Id == input.Id, ct);

            if (existing is null) return Result.Fail<SalesOrder>("That order no longer exists.", "so.not-found");

            if (existing.Status is not SalesOrderStatus.Draft)
            {
                return Result.Fail<SalesOrder>(
                    "This order is confirmed and cannot be edited.", "so.not-editable");
            }

            db.SalesOrderLines.RemoveRange(existing.Lines);
            existing.Lines.Clear();
            order = existing;
        }

        var ids = input.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);

        order.PartyId = party.Id;
        order.PartyName = party.Name;
        order.Date = input.Date;
        order.Notes = input.Notes;

        foreach (var line in input.Lines)
        {
            if (!items.TryGetValue(line.ItemId, out var item))
                return Result.Fail<SalesOrder>("One of the lines points at an item that no longer exists.", "so.bad-item");

            if (line.Quantity <= 0)
                return Result.Fail<SalesOrder>($"{item.Name} needs a quantity greater than nothing.", "so.bad-quantity");

            order.Lines.Add(new SalesOrderLine
            {
                ItemId = item.Id,
                ItemCode = item.Code,
                ItemName = item.Name,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice
            });
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(order);
    }

    public async Task<Result<SalesOrder>> ConfirmAsync(int id, CancellationToken ct = default)
    {
        var order = await db.SalesOrders.Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

        if (order is null) return Result.Fail<SalesOrder>("That order no longer exists.", "so.not-found");
        if (order.Status is not SalesOrderStatus.Draft)
            return Result.Fail<SalesOrder>("This has already been confirmed.", "so.already-confirmed");

        // Confirming reserves nothing on purpose. A soft reservation the stock
        // figure does not honour is worse than none: two orders can still be
        // promised the same unit while both look safe. Short lines become
        // backorders and are caught at delivery, where stock is really checked.
        order.Status = SalesOrderStatus.Confirmed;

        await db.SaveChangesAsync(ct);
        return Result.Success(order);
    }

    public async Task<Result<Delivery>> DeliverAsync(
        DeliveryInput input, CancellationToken ct = default)
    {
        var order = await db.SalesOrders.Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == input.SalesOrderId, ct);

        if (order is null) return Result.Fail<Delivery>("That order no longer exists.", "so.not-found");

        if (order.Status is not (SalesOrderStatus.Confirmed or SalesOrderStatus.PartiallyDelivered))
            return Result.Fail<Delivery>("Only a confirmed order can be delivered.", "so.not-confirmed");

        if (input.Lines.Count == 0)
            return Result.Fail<Delivery>("Nothing has been entered as delivered.", "delivery.no-lines");

        var ids = input.Lines.Select(l => l.ItemId).ToList();
        var items = await db.Items.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);

        // Every line is checked - against the order and against stock - before
        // any of it moves. What stops a half-posted delivery is this loop, not
        // a rollback afterwards.
        foreach (var line in input.Lines)
        {
            var orderLine = order.Lines.FirstOrDefault(l => l.ItemId == line.ItemId);
            if (orderLine is null)
                return Result.Fail<Delivery>("Something was delivered that is not on the order.", "delivery.not-ordered");

            if (line.Quantity <= 0)
                return Result.Fail<Delivery>($"{orderLine.ItemName} needs a quantity greater than nothing.", "delivery.bad-quantity");

            if (line.Quantity > orderLine.Outstanding)
            {
                return Result.Fail<Delivery>(
                    $"{orderLine.ItemName}: {line.Quantity:0.##} delivered but only " +
                    $"{orderLine.Outstanding:0.##} is still outstanding.",
                    "delivery.over-delivery");
            }

            if (!items.TryGetValue(line.ItemId, out var item))
                return Result.Fail<Delivery>("One of the lines points at an item that no longer exists.", "delivery.bad-item");

            if (item.QuantityOnHand < line.Quantity)
            {
                return Result.Fail<Delivery>(
                    $"Only {item.QuantityOnHand:0.##} {item.Unit} of {item.Name} are in stock, " +
                    $"and this delivery needs {line.Quantity:0.##}.",
                    "delivery.insufficient-stock");
            }
        }

        var delivery = new Delivery
        {
            Number = await NextNumberAsync("DN", ct),
            Date = input.Date,
            SalesOrderId = order.Id,
            PartyId = order.PartyId,
            PartyName = order.PartyName,
            CollectedBy = input.CollectedBy,
            Notes = input.Notes
        };

        foreach (var line in input.Lines)
        {
            var orderLine = order.Lines.First(l => l.ItemId == line.ItemId);
            var item = items[line.ItemId];

            // The cost is snapshotted here, at the moment of posting. The
            // weighted average moves with the next purchase, so a margin worked
            // out live would silently rewrite itself. There is a test for this.
            var costAtDelivery = item.AverageCost;

            delivery.Lines.Add(new DeliveryLine
            {
                ItemId = line.ItemId,
                ItemCode = orderLine.ItemCode,
                ItemName = orderLine.ItemName,
                Quantity = line.Quantity,
                UnitPrice = orderLine.UnitPrice,
                UnitCost = costAtDelivery
            });

            orderLine.Delivered += line.Quantity;
            orderLine.UnitCost = costAtDelivery;

            var moved = await stock.IssueAsync(
                line.ItemId, line.Quantity, input.Date,
                StockMovementType.Delivery, delivery.Number, "delivery", null, ct);

            if (moved.Failed) return Result.Fail<Delivery>(moved.Error!, moved.Code);
        }

        order.Status = order.IsFullyDelivered
            ? SalesOrderStatus.Delivered
            : SalesOrderStatus.PartiallyDelivered;

        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync(ct);

        return Result.Success(delivery);
    }

    private async Task<string> NextNumberAsync(string prefix, CancellationToken ct)
    {
        var year = clock.Today.Year;
        var stem = $"{prefix}-{year % 100:D2}-";

        var count = prefix == "SO"
            ? await db.SalesOrders.IgnoreQueryFilters().CountAsync(o => o.Number.StartsWith(stem), ct)
            : await db.Deliveries.IgnoreQueryFilters().CountAsync(d => d.Number.StartsWith(stem), ct);

        return stem + (count + 1).ToString().PadLeft(4, '0');
    }
}

/// <summary>Items, categories and parties - the master data behind buying and selling.</summary>
public interface ICatalogService
{
    Task<IReadOnlyList<Item>> ItemsAsync(string? search, bool includeInactive, CancellationToken ct = default);
    Task<Item?> GetItemAsync(int id, CancellationToken ct = default);
    Task<Result<Item>> SaveItemAsync(Item item, CancellationToken ct = default);
    Task<Result> DeleteItemAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<Party>> PartiesAsync(bool? customers, bool? suppliers, CancellationToken ct = default);
    Task<Result<Party>> SavePartyAsync(Party party, CancellationToken ct = default);

    /// <summary>Items at or below their reorder level.</summary>
    Task<IReadOnlyList<Item>> ReorderReportAsync(CancellationToken ct = default);
}

public sealed class CatalogService(InventoryDbContext db) : ICatalogService
{
    public async Task<IReadOnlyList<Item>> ItemsAsync(
        string? search, bool includeInactive, CancellationToken ct = default)
    {
        var query = db.Items.AsNoTracking().Include(i => i.Category).AsQueryable();
        if (!includeInactive) query = query.Where(i => i.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(i =>
                EF.Functions.ILike(i.Name, pattern) || EF.Functions.ILike(i.Code, pattern));
        }

        return await query.OrderBy(i => i.Name).ToListAsync(ct);
    }

    public Task<Item?> GetItemAsync(int id, CancellationToken ct = default) =>
        db.Items.FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<Result<Item>> SaveItemAsync(Item item, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(item.Code))
            return Result.Fail<Item>("An item needs a code.", "item.no-code");

        if (string.IsNullOrWhiteSpace(item.Name))
            return Result.Fail<Item>("An item needs a name.", "item.no-name");

        var taken = await db.Items.AnyAsync(i => i.Code == item.Code && i.Id != item.Id, ct);
        if (taken) return Result.Fail<Item>($"Code {item.Code} is already in use.", "item.duplicate-code");

        if (item.Id == 0)
        {
            db.Items.Add(item);
        }
        else
        {
            var existing = await db.Items.FirstOrDefaultAsync(i => i.Id == item.Id, ct);
            if (existing is null) return Result.Fail<Item>("That item no longer exists.", "item.not-found");

            var entry = db.Entry(existing);

            // Quantity and cost are owned by StockService and derived from
            // movements. Letting an edit screen set them would make the stock
            // ledger and the running figure disagree with no way to tell which
            // is right.
            //
            // Read from OriginalValues rather than from `existing`: an edit
            // screen usually hands back the very instance it loaded, so
            // `existing` and `item` are the same tracked object and copying one
            // onto the other would preserve nothing at all.
            var quantity = entry.OriginalValues.GetValue<decimal>(nameof(Item.QuantityOnHand));
            var average = entry.OriginalValues.GetValue<decimal>(nameof(Item.AverageCost));
            var last = entry.OriginalValues.GetValue<decimal?>(nameof(Item.LastCost));

            entry.CurrentValues.SetValues(item);

            existing.QuantityOnHand = quantity;
            existing.AverageCost = average;
            existing.LastCost = last;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(item);
    }

    public async Task<Result> DeleteItemAsync(int id, CancellationToken ct = default)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return Result.Fail("That item no longer exists.", "item.not-found");

        if (item.QuantityOnHand != 0)
        {
            return Result.Fail(
                $"{item.Name} still has {item.QuantityOnHand:0.##} {item.Unit} in stock. " +
                "Deactivate it instead, or bring the stock to nil first.",
                "item.has-stock");
        }

        var moved = await db.StockMovements.AnyAsync(m => m.ItemId == id, ct);
        if (moved)
        {
            // History has to keep resolving.
            return Result.Fail(
                "This item has stock history. Deactivate it instead - deleting it would " +
                "leave those movements pointing at nothing.",
                "item.has-history");
        }

        db.Items.Remove(item);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<IReadOnlyList<Party>> PartiesAsync(
        bool? customers, bool? suppliers, CancellationToken ct = default)
    {
        var query = db.Parties.AsNoTracking().Where(p => p.IsActive);
        if (customers is true) query = query.Where(p => p.IsCustomer);
        if (suppliers is true) query = query.Where(p => p.IsSupplier);
        return await query.OrderBy(p => p.Name).ToListAsync(ct);
    }

    public async Task<Result<Party>> SavePartyAsync(Party party, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(party.Name))
            return Result.Fail<Party>("A party needs a name.", "party.no-name");

        if (!party.IsCustomer && !party.IsSupplier)
        {
            return Result.Fail<Party>(
                "Mark them as a customer, a supplier, or both - otherwise they cannot be used anywhere.",
                "party.no-side");
        }

        if (string.IsNullOrWhiteSpace(party.Code))
            party.Code = party.Name.Length > 8 ? party.Name[..8].ToUpperInvariant() : party.Name.ToUpperInvariant();

        var taken = await db.Parties.AnyAsync(p => p.Code == party.Code && p.Id != party.Id, ct);
        if (taken) return Result.Fail<Party>($"Code {party.Code} is already in use.", "party.duplicate-code");

        if (party.Id == 0)
        {
            db.Parties.Add(party);
        }
        else
        {
            var existing = await db.Parties.FirstOrDefaultAsync(p => p.Id == party.Id, ct);
            if (existing is null) return Result.Fail<Party>("That party no longer exists.", "party.not-found");
            db.Entry(existing).CurrentValues.SetValues(party);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(party);
    }

    public async Task<IReadOnlyList<Item>> ReorderReportAsync(CancellationToken ct = default) =>
        await db.Items.AsNoTracking()
            .Where(i => i.IsActive && i.ReorderLevel > 0 && i.QuantityOnHand <= i.ReorderLevel)
            .OrderBy(i => i.Name)
            .ToListAsync(ct);
}
