using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Trade;

/// <summary>
/// Selling: the mirror image of buying. An order is a promise, a delivery is
/// what actually moves stock.
/// </summary>
public interface ISalesService
{
    Task<IReadOnlyList<SalesOrder>> ListOrdersAsync(SalesOrderStatus? status, int? bookId = null, CancellationToken ct = default);
    Task<SalesOrder?> GetOrderAsync(int id, CancellationToken ct = default);
    Task<Result<SalesOrder>> SaveOrderAsync(SalesOrderInput input, CancellationToken ct = default);
    Task<Result<SalesOrder>> ConfirmAsync(int id, CancellationToken ct = default);
    Task<Result<Delivery>> DeliverAsync(DeliveryInput input, CancellationToken ct = default);

    Task<IReadOnlyList<Delivery>> ListDeliveriesAsync(CancellationToken ct = default);
    Task<Delivery?> GetDeliveryAsync(int id, CancellationToken ct = default);
}

public sealed record SalesOrderInput(
    int? Id, int PartyId, int BookId, DateOnly Date, string? Notes,
    IReadOnlyList<SalesOrderLineInput> Lines);

public sealed record SalesOrderLineInput(int ItemId, decimal Quantity, decimal UnitPrice);

public sealed record DeliveryInput(
    int SalesOrderId, DateOnly Date, string? CollectedBy, string? Notes,
    IReadOnlyList<DeliveryLineInput> Lines);

/// <param name="SerialNumbers">
/// A serialised line must name exactly the units it ships. Empty for anything
/// not tracked by serial.
/// </param>
public sealed record DeliveryLineInput(
    int ItemId, decimal Quantity, IReadOnlyList<string>? SerialNumbers = null);

public sealed class SalesService(
    TradeDbContext db, ITradeStockPort stock, IClock clock) : ISalesService
{
    public async Task<IReadOnlyList<SalesOrder>> ListOrdersAsync(
        SalesOrderStatus? status, int? bookId = null, CancellationToken ct = default)
    {
        var query = db.SalesOrders.AsNoTracking().Include(o => o.Lines).AsQueryable();
        if (status is not null) query = query.Where(o => o.Status == status);
        if (bookId is not null) query = query.Where(o => o.DomainId == bookId);
        return await query.OrderByDescending(o => o.Id).Take(300).ToListAsync(ct);
    }

    public Task<SalesOrder?> GetOrderAsync(int id, CancellationToken ct = default) =>
        db.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<IReadOnlyList<Delivery>> ListDeliveriesAsync(CancellationToken ct = default) =>
        await db.Deliveries.AsNoTracking().Include(d => d.Lines)
            .OrderByDescending(d => d.Id).Take(300).ToListAsync(ct);

    public Task<Delivery?> GetDeliveryAsync(int id, CancellationToken ct = default) =>
        db.Deliveries.AsNoTracking().Include(d => d.Lines).FirstOrDefaultAsync(d => d.Id == id, ct);

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

        order.PartyId = party.Id;
        order.PartyName = party.Name;
        order.DomainId = input.BookId;
        order.Date = input.Date;
        order.Notes = input.Notes;

        foreach (var line in input.Lines)
        {
            var item = await stock.ItemAsync(line.ItemId, ct);
            if (item is null)
                return Result.Fail<SalesOrder>("One of the lines points at an item that no longer exists.", "so.bad-item");

            if (item.BookId != input.BookId)
                return Result.Fail<SalesOrder>(
                    $"{item.Name} belongs to a different stock book than this order.", "so.wrong-book");

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

        var items = new Dictionary<int, TradeItem>();
        foreach (var id in input.Lines.Select(l => l.ItemId).Distinct())
        {
            if (await stock.ItemAsync(id, ct) is { } found) items[id] = found;
        }

        // Every line is checked - against the order and against stock - before
        // any of it moves. What stops a half-posted delivery is this loop, not
        // a rollback afterwards. Nothing here opens a transaction of its own:
        // the stock movements already run inside one.
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

            var moved = await stock.StageIssueAsync(
                line.ItemId, line.Quantity, input.Date,
                delivery.Number, "delivery", null,
                line.SerialNumbers, input.CollectedBy, ct);

            if (moved.Failed)
            {
                db.ChangeTracker.Clear();
                return Result.Fail<Delivery>(moved.Error!, moved.Code);
            }
        }

        order.Status = order.IsFullyDelivered
            ? SalesOrderStatus.Delivered
            : SalesOrderStatus.PartiallyDelivered;

        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync(ct);
        await stock.SaveAsync(ct);

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

/// <summary>The one customer and supplier master.</summary>
public interface IPartyService
{
    Task<IReadOnlyList<Party>> ListAsync(bool? customers = null, bool? suppliers = null,
        string? search = null, CancellationToken ct = default);
    Task<Party?> GetAsync(int id, CancellationToken ct = default);
    Task<Result<Party>> SaveAsync(Party party, CancellationToken ct = default);
}

public sealed class PartyService(TradeDbContext db) : IPartyService
{
    public async Task<IReadOnlyList<Party>> ListAsync(
        bool? customers = null, bool? suppliers = null, string? search = null,
        CancellationToken ct = default)
    {
        var query = db.Parties.AsNoTracking().AsQueryable();

        if (customers == true) query = query.Where(p => p.IsCustomer);
        if (suppliers == true) query = query.Where(p => p.IsSupplier);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(p =>
                EF.Functions.ILike(p.Name, pattern) || EF.Functions.ILike(p.Code, pattern));
        }

        return await query.OrderBy(p => p.Name).ToListAsync(ct);
    }

    public Task<Party?> GetAsync(int id, CancellationToken ct = default) =>
        db.Parties.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Result<Party>> SaveAsync(Party party, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(party.Name))
            return Result.Fail<Party>("A party needs a name.", "party.no-name");

        if (string.IsNullOrWhiteSpace(party.Code))
            return Result.Fail<Party>("A party needs a code.", "party.no-code");

        // Neither side means the record can never be used on a document, which
        // is a data-entry slip rather than a state anyone wants.
        if (!party.IsCustomer && !party.IsSupplier)
            return Result.Fail<Party>("Mark this party as a customer, a supplier, or both.", "party.no-side");

        if (await db.Parties.AnyAsync(p => p.Code == party.Code && p.Id != party.Id, ct))
            return Result.Fail<Party>($"Code {party.Code} is already in use.", "party.duplicate-code");

        if (party.Id == 0) db.Parties.Add(party);
        else db.Parties.Update(party);

        await db.SaveChangesAsync(ct);
        return Result.Success(party);
    }
}
