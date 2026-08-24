using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.GatePass;

/// <summary>
/// The number on the printed pass security is holding at the barrier. This is
/// the scan that has to work with a van waiting, so it is the whole reason the
/// screen is one screen and not seven.
/// </summary>
public sealed class GatePassScanResolver(GatePassDbContext db) : IScanResolver
{
    public string ModuleKey => GatePassModule.Key;

    public async Task<IReadOnlyList<ScanHit>> ResolveAsync(string code, CancellationToken ct = default)
    {
        var pass = await db.Passes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Number == code, ct);

        if (pass is null) return [];

        return
        [
            new ScanHit(
                pass.Number,
                $"{pass.Direction} - {pass.PartyName} ({pass.Status})",
                $"/gatepass/passes/{pass.Id}",
                ModuleKey,
                GatePassModule.PassesView,
                "DirectionsCar")
        ];
    }
}
