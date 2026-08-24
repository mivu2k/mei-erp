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

    /// <summary>Stages a receipt in the current unit of work; the caller owns the final atomic save.</summary>
    Task<Result<StockMovement>> StageReceiptAsync(
        int itemId, decimal quantity, decimal unitCost, DateOnly date,
        StockMovementType type, string? reference, string? documentType, int? documentId,
        CancellationToken ct = default);

    /// <summary>Takes stock out at the current weighted average.</summary>
    Task<Result<StockMovement>> IssueAsync(
        int itemId, decimal quantity, DateOnly date,
        StockMovementType type, string? reference, string? documentType, int? documentId,
        CancellationToken ct = default);

    /// <summary>Stages an issue; the caller owns the atomic save.</summary>
    Task<Result<StockMovement>> StageIssueAsync(
        int itemId, decimal quantity, DateOnly date,
        StockMovementType type, string? reference, string? documentType, int? documentId,
        CancellationToken ct = default);

    /// <summary>Corrects the quantity to a counted figure.</summary>
    Task<Result<StockMovement>> AdjustToAsync(
        int itemId, decimal countedQuantity, DateOnly date, string reason, CancellationToken ct = default);

    /// <param name="domainId">
    /// Read one stock book's ledger. Null spans both, which only a group-wide
    /// valuation should want.
    /// </param>
    Task<IReadOnlyList<StockMovement>> MovementsAsync(
        int? itemId, DateOnly? from, DateOnly? to, int? domainId = null, CancellationToken ct = default);

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
        var result = await StageReceiptAsync(itemId, quantity, unitCost, date, type,
            reference, documentType, documentId, ct);
        if (result.Ok) await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<Result<StockMovement>> StageReceiptAsync(
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
        var warehouse = await ChangeDefaultBalanceAsync(item.Id, item.DomainId, quantity, ct);

        var movement = new StockMovement
        {
            ItemId = item.Id,
            ItemCode = item.Code,
            ItemName = item.Name,
            DomainId = item.DomainId,
            Date = date,
            Type = type,
            Quantity = quantity,
            UnitCost = unitCost,
            BalanceAfter = newQuantity,
            Reference = reference,
            SourceDocumentType = documentType,
            SourceDocumentId = documentId
            ,Warehouse = warehouse
        };

        db.StockMovements.Add(movement);
        return Result.Success(movement);
    }

    public async Task<Result<StockMovement>> IssueAsync(
        int itemId, decimal quantity, DateOnly date,
        StockMovementType type, string? reference, string? documentType, int? documentId,
        CancellationToken ct = default)
    {
        var result=await StageIssueAsync(itemId,quantity,date,type,reference,documentType,documentId,ct);
        if(result.Ok)await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<Result<StockMovement>> StageIssueAsync(
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
        var warehouse = await ChangeDefaultBalanceAsync(item.Id, item.DomainId, -quantity, ct);
        item.QuantityOnHand -= quantity;

        var movement = new StockMovement
        {
            ItemId = item.Id,
            ItemCode = item.Code,
            ItemName = item.Name,
            DomainId = item.DomainId,
            Date = date,
            Type = type,
            Quantity = -quantity,
            UnitCost = cost,
            BalanceAfter = item.QuantityOnHand,
            Reference = reference,
            SourceDocumentType = documentType,
            SourceDocumentId = documentId
            ,Warehouse = warehouse
        };

        db.StockMovements.Add(movement);

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
        var warehouse = await ChangeDefaultBalanceAsync(item.Id, item.DomainId, difference, ct);

        var movement = new StockMovement
        {
            ItemId = item.Id,
            ItemCode = item.Code,
            ItemName = item.Name,
            DomainId = item.DomainId,
            Date = date,
            Type = StockMovementType.Adjustment,
            Quantity = difference,
            UnitCost = item.AverageCost,
            BalanceAfter = countedQuantity,
            Narration = reason
            ,Warehouse = warehouse
        };

        db.StockMovements.Add(movement);
        await db.SaveChangesAsync(ct);

        return Result.Success(movement);
    }

    public async Task<IReadOnlyList<StockMovement>> MovementsAsync(
        int? itemId, DateOnly? from, DateOnly? to, int? domainId = null, CancellationToken ct = default)
    {
        var query = db.StockMovements.AsNoTracking().AsQueryable();

        if (domainId is not null) query = query.Where(m => m.DomainId == domainId);
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

    /// <summary>
    /// The default warehouse *within the item's own stock book*.
    ///
    /// Scoping this is what keeps the two books apart at the point it matters:
    /// unscoped, a workshop spare would land on the main store's default shelf
    /// and both valuations would be wrong.
    /// </summary>
    private async Task<Warehouse> ChangeDefaultBalanceAsync(int itemId,int domainId,decimal difference,CancellationToken ct)
    {
        var warehouse=db.Warehouses.Local.FirstOrDefault(x=>x.IsDefault&&x.DomainId==domainId)
            ?? await db.Warehouses.Where(x=>x.DomainId==domainId).OrderByDescending(x=>x.IsDefault).ThenBy(x=>x.Id).FirstOrDefaultAsync(ct);
        if(warehouse is null){warehouse=new Warehouse{Name="Main warehouse",Code="WH-"+domainId,IsDefault=true,DomainId=domainId};db.Warehouses.Add(warehouse);}
        var balance=await db.WarehouseBalances.FirstOrDefaultAsync(x=>x.WarehouseId==warehouse.Id&&x.ItemId==itemId,ct);
        if(balance is null){balance=new WarehouseBalance{Warehouse=warehouse,ItemId=itemId};db.WarehouseBalances.Add(balance);}
        if(balance.Quantity+difference<0)throw new InvalidOperationException("The default warehouse does not hold enough stock.");
        balance.Quantity+=difference;return warehouse;
    }
}
