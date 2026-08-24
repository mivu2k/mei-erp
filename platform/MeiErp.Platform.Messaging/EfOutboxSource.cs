using MeiErp.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Platform.Messaging;

public sealed class EfOutboxSource<TContext>(TContext db, string name)
    : IOutboxSource where TContext : ModuleDbContext
{
    public string Name => name;

    public async Task<IReadOnlyList<PendingOutboxMessage>> PendingAsync(int take, CancellationToken ct = default)
    {
        return await db.Outbox.AsNoTracking()
            .Where(m => m.DispatchedUtc == null && m.DeadLetteredUtc == null)
            .OrderBy(m => m.OccurredUtc).Take(take)
            .Select(m => new PendingOutboxMessage(name, m.Id, m.EventType, m.Payload,
                m.Attempts, m.OccurredUtc, m.CausedByUserId, m.LastError, m.DeadLetteredUtc))
            .ToListAsync(ct);
    }

    public async Task MarkDispatchedAsync(long id, DateTime utcNow, CancellationToken ct = default)
    {
        var row = await db.Outbox.FindAsync([id], ct);
        if (row is null || row.DispatchedUtc is not null) return;
        row.DispatchedUtc = utcNow; row.LastError = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkFailedAsync(long id, string error, DateTime utcNow, int maxAttempts, CancellationToken ct = default)
    {
        var row = await db.Outbox.FindAsync([id], ct);
        if (row is null || row.DispatchedUtc is not null) return;
        row.Attempts++;
        row.LastError = error.Length <= 4000 ? error : error[..4000];
        if (row.Attempts >= maxAttempts) row.DeadLetteredUtc = utcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PendingOutboxMessage>> DeadLettersAsync(CancellationToken ct = default)
    {
        return await db.Outbox.AsNoTracking().Where(m => m.DeadLetteredUtc != null)
            .OrderByDescending(m => m.DeadLetteredUtc)
            .Select(m => new PendingOutboxMessage(name, m.Id, m.EventType, m.Payload,
                m.Attempts, m.OccurredUtc, m.CausedByUserId, m.LastError, m.DeadLetteredUtc))
            .ToListAsync(ct);
    }

    public async Task RetryAsync(long id, CancellationToken ct = default)
    {
        var row = await db.Outbox.FindAsync([id], ct)
            ?? throw new InvalidOperationException("Outbox message not found.");
        row.Attempts = 0; row.LastError = null; row.DeadLetteredUtc = null;
        await db.SaveChangesAsync(ct);
    }
}
