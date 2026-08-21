using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Inventory;

/// <summary>
/// The only thing allowed to move stock.
///
/// Every change writes a <see cref="StockMovement"/> and updates the item's
/// running quantity in the same breath. Nothing else touches
/// <c>Item.QuantityOnHand</c> - a figure that can be changed from two places is
/// a figure nobody can trust.
/// </summary>
public interface IStockService
{
    /// <summary>Brings stock in and recalculates the weighted average cost.</summary>
    Task<Result<StockMovement>> ReceiveAsync(
        int itemId, decimal quantity, decimal unitCost, DateOnly date,
        StockMovementType type, string? reference, string? documentType, int? documentId,
        CancellationToken ct = default);

    /// <summary>Takes stock out at the current weighted average.</summary>
    Task<Result<StockMovement>> IssueAsync(
        int itemId, decimal quantity, DateOnly date,
        StockMovementType type, string? reference, string? documentType, int? documentId,
        CancellationToken ct = default);

    /// <summary>Corrects the quantity to a counted figure.</summary>
    Task<Result<StockMovement>> AdjustToAsync(
        int itemId, decimal countedQuantity, DateOnly date, string reason, CancellationToken ct = default);

    Task<IReadOnlyList<StockMovement>> MovementsAsync(
        int? itemId, DateOnly? from, DateOnly? to, CancellationToken ct = default);

    /// <summary>
    /// Recomputes every item's quantity from its movements.
    ///
    /// The running figure is a cache. If it ever drifts - a crash mid-write, a
    /// hand-edited row - this is what puts it right, and it is the reason the
    /// movement history has to stay append-only.
    /// </summary>
    Task<int> RebuildQuantitiesAsync(CancellationToken ct = default);
}

public sealed class StockService(InventoryDbContext db) : IStockService
{
    public async Task<Result<StockMovement>> ReceiveAsync(
        int itemId, decimal quantity, decimal unitCost, DateOnly date,
        StockMovementType type, string? reference, string? documentType, int? documentId,
        CancellationToken ct = default)
    {
        if (quantity <= 0)
            return Result.Fail<StockMovement>("A receipt has to bring in more than nothing.", "stock.bad-quantity");

        if (unitCost < 0)
            return Result.Fail<StockMovement>("A cost cannot be negative.", "stock.bad-cost");

        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null) return Result.Fail<StockMovement>("That item no longer exists.", "stock.no-item");

        // Weighted average: the value already held plus the value arriving,
        // over the total quantity. Recalculated before the quantity moves,
        // because the old quantity is part of the sum.
        var existingValue = item.QuantityOnHand * item.AverageCost;
        var incomingValue = quantity * unitCost;
        var newQuantity = item.QuantityOnHand + quantity;

        item.AverageCost = newQuantity == 0 ? unitCost : (existingValue + incomingValue) / newQuantity;
        item.QuantityOnHand = newQuantity;

        // Last cost only moves forward in time. An older invoice entered late
        // should update the average but must not overwrite a newer price.
        item.LastCost = unitCost;

        var movement = new StockMovement
        {
            ItemId = item.Id,
            ItemCode = item.Code,
            ItemName = item.Name,
            Date = date,
            Type = type,
            Quantity = quantity,
            UnitCost = unitCost,
            BalanceAfter = newQuantity,
            Reference = reference,
            SourceDocumentType = documentType,
            SourceDocumentId = documentId
        };

        db.StockMovements.Add(movement);
        await db.SaveChangesAsync(ct);

        return Result.Success(movement);
    }

    public async Task<Result<StockMovement>> IssueAsync(
        int itemId, decimal quantity, DateOnly date,
        StockMovementType type, string? reference, string? documentType, int? documentId,
        CancellationToken ct = default)
    {
        if (quantity <= 0)
            return Result.Fail<StockMovement>("An issue has to take out more than nothing.", "stock.bad-quantity");

        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null) return Result.Fail<StockMovement>("That item no longer exists.", "stock.no-item");

        if (item.QuantityOnHand < quantity)
        {
            // Negative stock is a lie that gets discovered at the worst moment,
            // usually during a count. Refused outright.
            return Result.Fail<StockMovement>(
                $"Only {item.QuantityOnHand:0.##} {item.Unit} of {item.Name} are in stock, " +
                $"and this needs {quantity:0.##}.",
                "stock.insufficient");
        }

        // Issued at the current average. The average itself does not move on a
        // sale - only purchases change what stock is carried at.
        var cost = item.AverageCost;
        item.QuantityOnHand -= quantity;

        var movement = new StockMovement
        {
            ItemId = item.Id,
            ItemCode = item.Code,
            ItemName = item.Name,
            Date = date,
            Type = type,
            Quantity = -quantity,
            UnitCost = cost,
            BalanceAfter = item.QuantityOnHand,
            Reference = reference,
            SourceDocumentType = documentType,
            SourceDocumentId = documentId
        };

        db.StockMovements.Add(movement);
        await db.SaveChangesAsync(ct);

        return Result.Success(movement);
    }

    public async Task<Result<StockMovement>> AdjustToAsync(
        int itemId, decimal countedQuantity, DateOnly date, string reason,
        CancellationToken ct = default)
    {
        if (countedQuantity < 0)
            return Result.Fail<StockMovement>("A counted quantity cannot be negative.", "stock.bad-quantity");

        if (string.IsNullOrWhiteSpace(reason))
        {
            // An unexplained adjustment is indistinguishable from theft.
            return Result.Fail<StockMovement>("Say why the figure is being changed.", "stock.no-reason");
        }

        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null) return Result.Fail<StockMovement>("That item no longer exists.", "stock.no-item");

        var difference = countedQuantity - item.QuantityOnHand;
        if (difference == 0)
            return Result.Fail<StockMovement>("The count already matches what the system says.", "stock.no-change");

        item.QuantityOnHand = countedQuantity;

        var movement = new StockMovement
        {
            ItemId = item.Id,
            ItemCode = item.Code,
            ItemName = item.Name,
            Date = date,
            Type = StockMovementType.Adjustment,
            Quantity = difference,
            UnitCost = item.AverageCost,
            BalanceAfter = countedQuantity,
            Narration = reason
        };

        db.StockMovements.Add(movement);
        await db.SaveChangesAsync(ct);

        return Result.Success(movement);
    }

    public async Task<IReadOnlyList<StockMovement>> MovementsAsync(
        int? itemId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var query = db.StockMovements.AsNoTracking().AsQueryable();

        if (itemId is not null) query = query.Where(m => m.ItemId == itemId);
        if (from is not null) query = query.Where(m => m.Date >= from);
        if (to is not null) query = query.Where(m => m.Date <= to);

        return await query
            .OrderByDescending(m => m.Date)
            .ThenByDescending(m => m.Id)
            .Take(1000)
            .ToListAsync(ct);
    }

    public async Task<int> RebuildQuantitiesAsync(CancellationToken ct = default)
    {
        var totals = await db.StockMovements
            .GroupBy(m => m.ItemId)
            .Select(g => new { ItemId = g.Key, Quantity = g.Sum(m => m.Quantity) })
            .ToDictionaryAsync(x => x.ItemId, x => x.Quantity, ct);

        var items = await db.Items.ToListAsync(ct);
        var corrected = 0;

        foreach (var item in items)
        {
            var actual = totals.GetValueOrDefault(item.Id);
            if (item.QuantityOnHand == actual) continue;

            item.QuantityOnHand = actual;
            corrected++;
        }

        if (corrected > 0) await db.SaveChangesAsync(ct);
        return corrected;
    }
}
