using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Repair;

/// <summary>
/// What the workshop's stickers say: a job number, an intake number, or the
/// serial engraved on the device itself - which is the one people actually
/// scan, because it is the only number on a machine that arrived without
/// paperwork.
/// </summary>
public sealed class RepairScanResolver(RepairDbContext db) : IScanResolver
{
    public string ModuleKey => RepairModule.Key;

    public async Task<IReadOnlyList<ScanHit>> ResolveAsync(string code, CancellationToken ct = default)
    {
        var hits = new List<ScanHit>();

        // A serial can sit on more than one job - the same machine comes back.
        // All of them, newest first, rather than whichever one EF happened to
        // hand over.
        var jobs = await db.Jobs.AsNoTracking()
            .Where(x => x.Number == code || x.SerialNumber == code)
            .OrderByDescending(x => x.Id)
            .Take(10)
            .ToListAsync(ct);

        foreach (var job in jobs)
        {
            hits.Add(new ScanHit(
                job.Number,
                $"{job.DeviceType} {job.Make} {job.Model}".Trim() + $" - {job.CustomerName}",
                $"/repair/jobs?search={Uri.EscapeDataString(job.Number)}",
                ModuleKey,
                RepairModule.JobsView,
                "Build"));
        }

        var intake = await db.RepairIntakes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Number == code, ct);

        if (intake is not null)
        {
            hits.Add(new ScanHit(
                intake.Number,
                $"Intake - {intake.CustomerName}",
                $"/repair/intakes?search={Uri.EscapeDataString(intake.Number)}",
                ModuleKey,
                RepairModule.IntakesManage,
                "MoveToInbox"));
        }

        return hits;
    }
}
