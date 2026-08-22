using MeiErp.Platform.Kernel;

namespace MeiErp.Platform.Notifications;

/// <inheritdoc />
public sealed class NotificationService(
    INotificationStore store,
    IEnumerable<INotificationChannel> channels,
    IClock clock) : INotifier
{
    private readonly IReadOnlyList<INotificationChannel> _channels = [.. channels];

    public async Task<IReadOnlyList<Notification>> NotifyAsync(
        NotificationRequest request, CancellationToken ct = default)
    {
        // One person told once. Somebody can easily be both the line manager and
        // a standing delegate for the same step, and two identical emails about
        // one request reads as a broken system.
        var recipients = request.Recipients
            .Where(r => !string.IsNullOrWhiteSpace(r.UserId))
            .GroupBy(r => r.UserId, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        if (recipients.Count == 0) return [];

        var preferences = await store.PreferencesAsync(
            [.. recipients.Select(r => r.UserId)], request.Category, ct);

        var now = clock.UtcNow;
        var created = new List<Notification>(recipients.Count);

        foreach (var recipient in recipients)
        {
            var notification = new Notification
            {
                UserId = recipient.UserId,
                Category = request.Category,
                Subject = request.Subject,
                Body = request.Body,
                Url = request.Url,
                ModuleKey = request.ModuleKey,
                Priority = request.Priority,
                EventKey = request.EventKey,
                CreatedUtc = now
            };

            foreach (var channel in _channels)
                notification.Deliveries.Add(PlanDelivery(channel, recipient, request, preferences, now));

            store.Add(notification);
            created.Add(notification);
        }

        // Deliberately no SaveAsync: the caller commits, so the notification and
        // whatever raised it land together or not at all.
        return created;
    }

    /// <summary>
    /// Decides up front what each channel will do with this message, and records
    /// that decision as a row.
    ///
    /// A suppressed or unreachable channel gets a row saying so rather than no
    /// row at all. "We never tried" and "we tried and it bounced" are different
    /// answers to the only question anyone asks afterwards, and a missing row
    /// cannot tell them apart.
    /// </summary>
    private static NotificationDelivery PlanDelivery(
        INotificationChannel channel,
        NotificationRecipient recipient,
        NotificationRequest request,
        IReadOnlyList<NotificationPreference> preferences,
        DateTime now)
    {
        var delivery = new NotificationDelivery { Channel = channel.Key };

        var preference = preferences.FirstOrDefault(
            p => p.UserId == recipient.UserId && p.Channel == channel.Key);

        var wanted = preference?.Enabled ?? channel.EnabledByDefault(request.Category);
        if (!wanted)
        {
            delivery.Status = DeliveryStatus.Suppressed;
            return delivery;
        }

        var address = channel.AddressFor(recipient);
        if (string.IsNullOrWhiteSpace(address))
        {
            // No address is a fact about the account, not a transient fault.
            // Retrying it would burn every attempt against something that cannot
            // change without somebody editing the user.
            delivery.Status = DeliveryStatus.NotApplicable;
            return delivery;
        }

        delivery.Address = address;
        delivery.Status = DeliveryStatus.Pending;
        delivery.NextAttemptUtc = now;
        return delivery;
    }

    public async Task DismissEventAsync(string eventKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(eventKey)) return;

        var now = clock.UtcNow;

        foreach (var notification in await store.ByEventAsync(eventKey, ct))
        {
            // Read ones are left alone: dismissing something the person has
            // already looked at would rewrite what they saw.
            if (notification.ReadUtc is null && notification.DismissedUtc is null)
                notification.DismissedUtc = now;
        }

        // No save: this runs inside the decision that settled the approval, and
        // the dismissal must land with it or not at all.
    }

    public Task<IReadOnlyList<Notification>> UnreadAsync(
        string userId, int take = 20, CancellationToken ct = default) =>
        store.UnreadAsync(userId, take, ct);

    public Task<int> UnreadCountAsync(string userId, CancellationToken ct = default) =>
        store.UnreadCountAsync(userId, ct);

    public async Task MarkReadAsync(int notificationId, CancellationToken ct = default)
    {
        var notification = await store.FindAsync(notificationId, ct);
        if (notification is null || notification.ReadUtc is not null) return;

        notification.ReadUtc = clock.UtcNow;
        await store.SaveAsync(ct);
    }

    public Task MarkAllReadAsync(string userId, CancellationToken ct = default) =>
        store.MarkAllReadAsync(userId, clock.UtcNow, ct);
}
