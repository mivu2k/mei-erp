using MeiErp.Modules.Repair;
using MeiErp.Modules.Trade;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Host.Services;

/// <summary>
/// Lets the workshop book devices in against the one party master.
///
/// The only code that knows both Repair and Trade exist, which is why it lives
/// in the host - same arrangement as <see cref="RepairJobSource"/> going the
/// other way.
/// </summary>
public sealed class TradeCustomerDirectory(TradeDbContext trade, IPartyService parties)
    : IRepairCustomerDirectory
{
    public async Task<IReadOnlyList<RepairCustomer>> SearchAsync(
        string? term = null, CancellationToken ct = default) =>
        (await parties.ListAsync(customers: true, search: term, ct: ct))
            .Select(Flatten).ToList();

    public async Task<RepairCustomer?> GetAsync(int partyId, CancellationToken ct = default) =>
        await trade.Parties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == partyId, ct) is { } p
            ? Flatten(p) : null;

    public async Task<RepairCustomer> EnsureAsync(
        string name, string? phone, CancellationToken ct = default)
    {
        var clean = name.Trim();

        var existing = await trade.Parties
            .FirstOrDefaultAsync(p => p.Name == clean, ct);

        if (existing is not null)
        {
            // Already known as a supplier and now buying a repair: mark the
            // other side rather than creating a second record for one company.
            if (!existing.IsCustomer)
            {
                existing.IsCustomer = true;
                await trade.SaveChangesAsync(ct);
            }

            return Flatten(existing);
        }

        var created = new Party
        {
            Code = await NextCodeAsync(ct),
            Name = clean,
            Phone = phone,
            IsCustomer = true,
            IsActive = true
        };

        trade.Parties.Add(created);
        await trade.SaveChangesAsync(ct);

        return Flatten(created);
    }

    /// <summary>
    /// A counter walk-in has no code anyone chose, so one is generated. Counted
    /// past deleted rows too, or a code could be handed out twice.
    /// </summary>
    private async Task<string> NextCodeAsync(CancellationToken ct)
    {
        var count = await trade.Parties.IgnoreQueryFilters()
            .CountAsync(p => p.Code.StartsWith("C-"), ct);

        return $"C-{count + 1:D5}";
    }

    private static RepairCustomer Flatten(Party p) => new(p.Id, p.Code, p.Name, p.Phone, p.Email);
}
