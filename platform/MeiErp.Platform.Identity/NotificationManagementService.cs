using MeiErp.Platform.Kernel;
using MeiErp.Platform.Notifications;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Platform.Identity;

public interface INotificationManagementService
{
    Task<IReadOnlyList<NotificationPreferenceOption>> PreferencesAsync(CancellationToken ct = default);
    Task SavePreferencesAsync(IReadOnlyList<NotificationPreferenceOption> options, CancellationToken ct = default);
    Task<IReadOnlyList<DueDelivery>> DeadAsync(int take = 100, CancellationToken ct = default);
    Task<Result> RetryAsync(int deliveryId, CancellationToken ct = default);
}

public sealed record NotificationPreferenceOption(
    string Category, string CategoryName, string Channel, string ChannelName,
    bool Enabled, bool IsDefault);

public sealed class NotificationManagementService(
    PlatformDbContext db,
    ICurrentUser currentUser,
    IEnumerable<INotificationChannel> channels,
    INotificationOutbox outbox,
    IClock clock) : INotificationManagementService
{
    private static readonly IReadOnlyDictionary<string, string> CategoryNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [NotificationCategories.ApprovalAssigned] = "Approval assigned",
            [NotificationCategories.ApprovalSettled] = "Approval decided",
            [NotificationCategories.ApprovalReminder] = "Approval reminder",
            [NotificationCategories.ApprovalEscalated] = "Approval escalation"
        };

    private readonly IReadOnlyList<INotificationChannel> _channels = [.. channels];

    public async Task<IReadOnlyList<NotificationPreferenceOption>> PreferencesAsync(
        CancellationToken ct = default)
    {
        var userId = RequireUser();
        var saved = await db.NotificationPreferences.AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToListAsync(ct);

        return
        [
            .. from category in NotificationCategories.All
               from channel in _channels
               let configured = saved.FirstOrDefault(
                   p => p.Category == category && p.Channel == channel.Key)
               let byDefault = channel.EnabledByDefault(category)
               select new NotificationPreferenceOption(
                   category, CategoryNames.GetValueOrDefault(category, category),
                   channel.Key, channel.DisplayName,
                   configured?.Enabled ?? byDefault,
                   configured is null)
        ];
    }

    public async Task SavePreferencesAsync(
        IReadOnlyList<NotificationPreferenceOption> options, CancellationToken ct = default)
    {
        var userId = RequireUser();
        var allowedCategories = NotificationCategories.All.ToHashSet(StringComparer.Ordinal);
        var allowedChannels = _channels.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);

        var existing = await db.NotificationPreferences
            .Where(p => p.UserId == userId).ToListAsync(ct);

        foreach (var option in options.Where(o =>
                     allowedCategories.Contains(o.Category) && allowedChannels.Contains(o.Channel)))
        {
            var row = existing.FirstOrDefault(
                p => p.Category == option.Category && p.Channel == option.Channel);
            if (row is null)
            {
                row = new NotificationPreference
                {
                    UserId = userId,
                    Category = option.Category,
                    Channel = option.Channel
                };
                db.NotificationPreferences.Add(row);
            }
            row.Enabled = option.Enabled;
        }

        await db.SaveChangesAsync(ct);
    }

    public Task<IReadOnlyList<DueDelivery>> DeadAsync(int take = 100, CancellationToken ct = default)
    {
        RequirePermission();
        return outbox.DeadAsync(Math.Clamp(take, 1, 500), ct);
    }

    public Task<Result> RetryAsync(int deliveryId, CancellationToken ct = default)
    {
        RequirePermission();
        return outbox.RetryAsync(deliveryId, clock.UtcNow, ct);
    }

    private string RequireUser() => currentUser.UserId
        ?? throw new UnauthorizedAccessException("Sign in to manage notification preferences.");

    private void RequirePermission()
    {
        if (!currentUser.Can(PlatformPermissions.OutboxManage))
            throw new UnauthorizedAccessException("You cannot manage failed deliveries.");
    }
}
