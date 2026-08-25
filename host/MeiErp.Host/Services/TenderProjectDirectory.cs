using MeiErp.Modules.Finance;
using MeiErp.Modules.Tender;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Host.Services;

/// <summary>
/// Lets Finance charge a payment request to a project Tender owns.
///
/// Finance states what it needs as <see cref="IFinanceProjectDirectory"/>; this
/// is the only code that knows both modules exist, and it lives in the host for
/// that reason - the same arrangement that lets Trade move Inventory's stock.
/// </summary>
public sealed class TenderProjectDirectory(TenderDbContext db) : IFinanceProjectDirectory
{
    public async Task<IReadOnlyList<ProjectOption>> ActiveProjectsAsync(CancellationToken ct = default)
    {
        // Only projects still able to receive spend. Charging a closed one
        // restates a job that has already been reported on.
        var rows = await db.Projects.AsNoTracking()
            .Where(p => p.Status == ProjectStatus.Planned
                     || p.Status == ProjectStatus.Active
                     || p.Status == ProjectStatus.OnHold)
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Code, p.Name })
            .ToListAsync(ct);

        return rows
            .Select(p => new ProjectOption(p.Id.ToString(), $"{p.Code} — {p.Name}"))
            .ToList();
    }
}
