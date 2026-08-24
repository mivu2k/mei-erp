using MeiErp.Modules.Repair;
using MeiErp.Modules.Trade;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Host.Services;

/// <summary>
/// Lets Sales quote a workshop job.
///
/// The only code that knows both Repair and Trade exist, which is why it lives
/// in the host - the same arrangement as <see cref="InventoryStockAdapter"/>.
///
/// The workshop's customer and the party master are separate records for now
/// (Repair still keeps its own customer list), so this matches them by name and
/// hands back a zero party id when there is no match, rather than guessing.
/// </summary>
public sealed class RepairJobSource(RepairDbContext repair, TradeDbContext trade) : ITradeJobSource
{
    public async Task<QuotableJob?> JobAsync(int jobId, CancellationToken ct = default)
    {
        var job = await repair.Jobs.AsNoTracking()
            .Include(j => j.WorkItems)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);

        if (job is null) return null;

        var (partyId, partyName) = await MatchPartyAsync(job.CustomerName, ct);

        return new QuotableJob(
            job.Id,
            job.Number,
            Device(job),
            partyId,
            partyName,
            [.. Billable(job.WorkItems)]);
    }

    public async Task<QuotableJob?> IntakeAsync(int intakeId, CancellationToken ct = default)
    {
        var intake = await repair.RepairIntakes.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == intakeId, ct);

        if (intake is null) return null;

        var jobs = await repair.Jobs.AsNoTracking()
            .Include(j => j.WorkItems)
            .Where(j => j.IntakeId == intakeId)
            .OrderBy(j => j.Id)
            .ToListAsync(ct);

        var customerName = jobs.FirstOrDefault()?.CustomerName ?? "";
        var (partyId, partyName) = await MatchPartyAsync(customerName, ct);

        // One price for the whole intake, but each line still says which
        // machine it belongs to - otherwise a six-device quotation is an
        // unreadable list of "Screen replacement" repeated.
        var lines = jobs.SelectMany(j => Billable(j.WorkItems)
            .Select(l => l with { Description = $"{Device(j)} — {l.Description}" }));

        return new QuotableJob(
            intake.Id,
            intake.Number,
            $"{jobs.Count} {(jobs.Count == 1 ? "device" : "devices")}",
            partyId,
            partyName,
            [.. lines]);
    }

    /// <summary>
    /// Only billable work reaches a quotation. Warranty and goodwill lines are
    /// recorded against the job for the workshop's own reporting, and charging
    /// for them would be exactly wrong.
    /// </summary>
    private static IEnumerable<DocumentLineInput> Billable(IEnumerable<WorkItem> items) =>
        items.Where(w => w.IsBillable)
             .Select(w => new DocumentLineInput(null, null, w.Description, w.Quantity, w.UnitPrice));

    private static string Device(Job job) =>
        string.Join(' ', new[] { job.Make, job.Model, job.DeviceType }
            .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();

    private async Task<(int Id, string Name)> MatchPartyAsync(string customerName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(customerName)) return (0, "");

        var party = await trade.Parties.AsNoTracking()
            .FirstOrDefaultAsync(p => p.IsCustomer && p.Name == customerName, ct);

        return party is null ? (0, customerName) : (party.Id, party.Name);
    }
}
