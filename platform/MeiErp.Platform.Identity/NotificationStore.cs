using MeiErp.Platform.Kernel;
using MeiErp.Platform.Notifications;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Platform.Identity;

/// <summary>
/// The notification tables, on the platform context.
///
/// Sharing the context is the whole point: <see cref="Add"/> only stages, and
/// whoever raised the notification commits it alongside their own change.
/// </summary>
public sealed class NotificationStore(PlatformDbContext db) : INotificationStore
{
    public void Add(Notification notification) => db.Notifications.Add(notification);

    public async Task<IReadOnlyList<NotificationPreference>> PreferencesAsync(
        IReadOnlyList<string> userIds, string category, CancellationToken ct = default)
    {
        // A List, not an array: an array's Contains binds to the ReadOnlySpan
        // overload inside an EF predicate and throws at query time. The previous
        // platform hit this repeatedly.
        var ids = userIds.ToList();

        return await db.NotificationPreferences
            .AsNoTracking()
            .Where(p => ids.Contains(p.UserId) && p.Category == category)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Notification>> UnreadAsync(
        string userId, int take, CancellationToken ct = default) =>
        await db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId && n.ReadUtc == null && n.DismissedUtc == null)
            .OrderByDescending(n => n.CreatedUtc)
            .Take(take)
            .ToListAsync(ct);

    public Task<int> UnreadCountAsync(string userId, CancellationToken ct = default) =>
        db.Notifications
            .CountAsync(n => n.UserId == userId && n.ReadUtc == null && n.DismissedUtc == null, ct);

    public async Task<Notification?> FindAsync(int notificationId, CancellationToken ct = default) =>
        await db.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId, ct);

    public async Task<IReadOnlyList<Notification>> ByEventAsync(
        string eventKey, CancellationToken ct = default) =>
        await db.Notifications.Where(n => n.EventKey == eventKey).ToListAsync(ct);

    public Task MarkAllReadAsync(string userId, DateTime nowUtc, CancellationToken ct = default) =>
        db.Notifications
            .Where(n => n.UserId == userId && n.ReadUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadUtc, nowUtc), ct);

    public Task SaveAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

/// <summary>
/// The dispatcher's side of the notification tables.
///
/// Claiming is the only interesting part. It has to be atomic against other app
/// instances, because two dispatchers that both pick up the same row send the
/// same email twice - and a duplicate approval email is exactly the sort of
/// thing that makes people stop reading them.
/// </summary>
public sealed class NotificationOutbox(PlatformDbContext db) : INotificationOutbox
{
    public async Task<IReadOnlyList<DueDelivery>> ClaimDueAsync(
        int batchSize, DateTime nowUtc, CancellationToken ct = default)
    {
        // FOR UPDATE SKIP LOCKED is what makes this safe with more than one app
        // instance: each dispatcher takes rows nobody else has locked instead of
        // queueing behind them. The UPDATE counts the attempt and clears the due
        // time in the same statement, so a claimed row cannot be claimed again.
        var ids = await db.Database
            .SqlQuery<int>($"""
                UPDATE platform."NotificationDeliveries" AS d
                   SET "Attempts"       = d."Attempts" + 1,
                       "NextAttemptUtc" = NULL
                 WHERE d."Id" IN (
                       SELECT c."Id"
                         FROM platform."NotificationDeliveries" AS c
                        WHERE c."Status" IN (0, 2)
                          AND c."NextAttemptUtc" IS NOT NULL
                          AND c."NextAttemptUtc" <= {nowUtc}
                        ORDER BY c."NextAttemptUtc"
                        LIMIT {batchSize}
                        FOR UPDATE SKIP LOCKED)
             RETURNING d."Id"
             """)
            .ToListAsync(ct);

        if (ids.Count == 0) return [];

        var rows = await db.NotificationDeliveries
            .AsNoTracking()
            .Include(d => d.Notification)
            .Where(d => ids.Contains(d.Id))
            .ToListAsync(ct);

        return
        [
            .. rows
                .Where(d => d.Notification is not null && d.Address is not null)
                .Select(d => new DueDelivery(
                    d.Id, d.NotificationId, d.Channel, d.Address!, d.Attempts, d.Notification!))
        ];
    }

    public Task SettleAsync(
        int deliveryId, DeliveryStatus status, DateTime? nextAttemptUtc,
        DateTime? sentUtc, string? error, CancellationToken ct = default) =>
        db.NotificationDeliveries
            .Where(d => d.Id == deliveryId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, status)
                .SetProperty(d => d.NextAttemptUtc, nextAttemptUtc)
                .SetProperty(d => d.SentUtc, sentUtc)
                // Kept even after a later attempt succeeds: "it worked on the
                // third try" is worth knowing when the mail server is blamed.
                .SetProperty(d => d.LastError, error), ct);

    public async Task<IReadOnlyList<DueDelivery>> DeadAsync(int take, CancellationToken ct = default)
    {
        var rows = await db.NotificationDeliveries
            .AsNoTracking()
            .Include(d => d.Notification)
            .Where(d => d.Status == DeliveryStatus.Dead)
            .OrderByDescending(d => d.Id)
            .Take(take)
            .ToListAsync(ct);

        return
        [
            .. rows
                .Where(d => d.Notification is not null)
                .Select(d => new DueDelivery(
                    d.Id, d.NotificationId, d.Channel, d.Address ?? "", d.Attempts, d.Notification!))
        ];
    }

    public async Task<Result> RetryAsync(int deliveryId, DateTime nowUtc, CancellationToken ct = default)
    {
        var delivery = await db.NotificationDeliveries
            .FirstOrDefaultAsync(d => d.Id == deliveryId, ct);

        if (delivery is null)
            return Result.Fail("That delivery no longer exists.", "delivery.not-found");

        if (delivery.Status is not DeliveryStatus.Dead)
        {
            // Re-queueing something still in flight would send it twice.
            return Result.Fail(
                "Only a delivery that has given up can be retried.", "delivery.not-dead");
        }

        delivery.Status = DeliveryStatus.Pending;
        delivery.Attempts = 0;
        delivery.NextAttemptUtc = nowUtc;

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
