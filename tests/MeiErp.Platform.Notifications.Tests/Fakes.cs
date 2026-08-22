using MeiErp.Platform.Kernel;
using MeiErp.Platform.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace MeiErp.Platform.Notifications.Tests;

/// <summary>An in-memory store, so the queue's rules can be tested without a database.</summary>
public sealed class FakeStore : INotificationStore
{
    public List<Notification> Notifications { get; } = [];
    public List<NotificationPreference> Preferences { get; } = [];
    public int Saves { get; private set; }

    private int _nextId = 1;

    public void Add(Notification notification)
    {
        notification.Id = _nextId++;
        Notifications.Add(notification);
    }

    public Task<IReadOnlyList<NotificationPreference>> PreferencesAsync(
        IReadOnlyList<string> userIds, string category, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<NotificationPreference>>(
            [.. Preferences.Where(p => userIds.Contains(p.UserId) && p.Category == category)]);

    public Task<IReadOnlyList<Notification>> UnreadAsync(
        string userId, int take, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Notification>>(
        [
            .. Notifications
                .Where(n => n.UserId == userId && n.ReadUtc is null && n.DismissedUtc is null)
                .OrderByDescending(n => n.CreatedUtc)
                .Take(take)
        ]);

    public Task<int> UnreadCountAsync(string userId, CancellationToken ct = default) =>
        Task.FromResult(Notifications.Count(
            n => n.UserId == userId && n.ReadUtc is null && n.DismissedUtc is null));

    public Task<Notification?> FindAsync(int notificationId, CancellationToken ct = default) =>
        Task.FromResult(Notifications.FirstOrDefault(n => n.Id == notificationId));

    public Task<IReadOnlyList<Notification>> ByEventAsync(
        string eventKey, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Notification>>(
            [.. Notifications.Where(n => n.EventKey == eventKey)]);

    public Task MarkAllReadAsync(string userId, DateTime nowUtc, CancellationToken ct = default)
    {
        foreach (var n in Notifications.Where(n => n.UserId == userId && n.ReadUtc is null))
            n.ReadUtc = nowUtc;

        return Task.CompletedTask;
    }

    public Task SaveAsync(CancellationToken ct = default)
    {
        Saves++;
        return Task.CompletedTask;
    }
}

/// <summary>A channel that records what it was asked to send, and can be told to fail.</summary>
public sealed class FakeChannel(
    string key, bool defaultOn = true, string? addressOverride = null) : INotificationChannel
{
    public string Key { get; } = key;
    public string DisplayName => Key;

    public List<(Notification Notification, string Address)> Sent { get; } = [];

    /// <summary>Number of leading attempts that should fail before one succeeds.</summary>
    public int FailTimes { get; set; }

    public bool Throws { get; set; }

    /// <summary>Set to make the channel report that it cannot reach anyone.</summary>
    public bool Unreachable { get; set; }

    public bool EnabledByDefault(string category) => defaultOn;

    public string? AddressFor(NotificationRecipient recipient) =>
        Unreachable ? null : addressOverride ?? recipient.Email ?? recipient.UserId;

    public Task<Result> SendAsync(
        Notification notification, string address, CancellationToken ct = default)
    {
        if (Throws) throw new InvalidOperationException("channel exploded");

        if (FailTimes > 0)
        {
            FailTimes--;
            return Task.FromResult(Result.Fail("temporary", "fake.fail"));
        }

        Sent.Add((notification, address));
        return Task.FromResult(Result.Success());
    }
}

/// <summary>An in-memory queue mirroring what the EF one does, claim semantics included.</summary>
public sealed class FakeOutbox : INotificationOutbox
{
    public List<NotificationDelivery> Deliveries { get; } = [];
    public Dictionary<int, Notification> Messages { get; } = [];

    public Task<IReadOnlyList<DueDelivery>> ClaimDueAsync(
        int batchSize, DateTime nowUtc, CancellationToken ct = default)
    {
        var due = Deliveries
            .Where(d => d.Status is DeliveryStatus.Pending or DeliveryStatus.Failed
                     && d.NextAttemptUtc is not null
                     && d.NextAttemptUtc <= nowUtc)
            .OrderBy(d => d.NextAttemptUtc)
            .Take(batchSize)
            .ToList();

        var claimed = new List<DueDelivery>(due.Count);

        foreach (var d in due)
        {
            // Same as the real one: the attempt is counted when the row is
            // taken, not when the send returns.
            d.Attempts++;
            d.NextAttemptUtc = null;

            claimed.Add(new DueDelivery(
                d.Id, d.NotificationId, d.Channel, d.Address ?? "", d.Attempts,
                Messages[d.NotificationId]));
        }

        return Task.FromResult<IReadOnlyList<DueDelivery>>(claimed);
    }

    public Task SettleAsync(
        int deliveryId, DeliveryStatus status, DateTime? nextAttemptUtc,
        DateTime? sentUtc, string? error, CancellationToken ct = default)
    {
        var d = Deliveries.First(x => x.Id == deliveryId);
        d.Status = status;
        d.NextAttemptUtc = nextAttemptUtc;
        d.SentUtc = sentUtc;
        d.LastError = error;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DueDelivery>> DeadAsync(int take, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DueDelivery>>(
        [
            .. Deliveries
                .Where(d => d.Status is DeliveryStatus.Dead)
                .Take(take)
                .Select(d => new DueDelivery(
                    d.Id, d.NotificationId, d.Channel, d.Address ?? "", d.Attempts,
                    Messages[d.NotificationId]))
        ]);

    public Task<Result> RetryAsync(int deliveryId, DateTime nowUtc, CancellationToken ct = default)
    {
        var d = Deliveries.First(x => x.Id == deliveryId);
        if (d.Status is not DeliveryStatus.Dead)
            return Task.FromResult(Result.Fail("not dead", "delivery.not-dead"));

        d.Status = DeliveryStatus.Pending;
        d.Attempts = 0;
        d.NextAttemptUtc = nowUtc;
        return Task.FromResult(Result.Success());
    }
}

/// <summary>
/// The smallest thing that satisfies the dispatcher's scope factory. The real
/// container is not needed to prove the drain loop's behaviour, and pulling it
/// in would only add a package.
/// </summary>
public sealed class StubScopes(Dictionary<Type, object> services)
    : IServiceScopeFactory, IServiceScope, IServiceProvider
{
    public IServiceScope CreateScope() => this;
    public IServiceProvider ServiceProvider => this;
    public void Dispose() { }

    public object? GetService(Type serviceType) =>
        services.TryGetValue(serviceType, out var found) ? found : null;
}
