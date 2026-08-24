using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Inventory;

/// <summary>
/// The serial or batch number printed on the box in somebody's hands. Both
/// land on the serials and batches screen, which is where the unit's history
/// actually reads.
/// </summary>
public sealed class InventoryScanResolver(InventoryDbContext db) : IScanResolver
{
    public string ModuleKey => InventoryModule.Key;

    public async Task<IReadOnlyList<ScanHit>> ResolveAsync(string code, CancellationToken ct = default)
    {
        var hits = new List<ScanHit>();

        var units = await db.StockUnits.AsNoTracking()
            .Include(x => x.Item)
            .Where(x => x.SerialNumber == code)
            .Take(10)
            .ToListAsync(ct);

        foreach (var unit in units)
        {
            hits.Add(new ScanHit(
                unit.SerialNumber,
                $"{unit.Item?.Name} - {unit.Status}",
                "/inventory/tracking",
                ModuleKey,
                InventoryModule.TrackingManage,
                "QrCode2"));
        }

        // A batch number is only unique within its item, so two hits here are
        // normal rather than a data problem.
        var batches = await db.StockBatches.AsNoTracking()
            .Include(x => x.Item)
            .Where(x => x.BatchNumber == code)
            .Take(10)
            .ToListAsync(ct);

        foreach (var batch in batches)
        {
            hits.Add(new ScanHit(
                batch.BatchNumber,
                $"{batch.Item?.Name} - {batch.RemainingQuantity:0.##} left",
                "/inventory/tracking",
                ModuleKey,
                InventoryModule.TrackingManage,
                "Inventory2"));
        }

        return hits;
    }
}
