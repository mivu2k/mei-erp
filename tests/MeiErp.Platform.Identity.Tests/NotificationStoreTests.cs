using MeiErp.Platform.Identity;
using MeiErp.Platform.Kernel;
using MeiErp.Platform.Notifications;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MeiErp.Platform.Identity.Tests;

/// <summary>
/// The notification tables against a real PostgreSQL.
///
/// These exist mainly for <see cref="NotificationOutbox.ClaimDueAsync"/>, which is
/// hand-written SQL using FOR UPDATE SKIP LOCKED. An in-memory fake cannot tell
/// you whether that statement parses, whether the status filter matches the
/// enum's integers, or whether claiming really stops a second dispatcher taking
/// the same row - and every one of those failures ends in a duplicate email.
/// </summary>
[Collection("postgres")]
public sealed class NotificationStoreTests : IAsyncLifetime
{
    private static readonly string BaseConnection =
        Environment.GetEnvironmentVariable("MEIERP_TEST_DB")
        ?? "Host=127.0.0.1;Username=meierp;Password=DevPassword1!;";

    private static readonly DateTimeOffset At =
        new(2026, 8, 22, 9, 0, 0, TimeSpan.Zero);

    private readonly string _database = $"mei_notif_{Guid.NewGuid():N}";
    private bool _available;

    private string Connection => BaseConnection + $"Database={_database};";

    private PlatformDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(Connection).Options);

    public async Task InitializeAsync()
    {
        try
        {
            await using (var admin = new DbContext(
                new DbContextOptionsBuilder().UseNpgsql(BaseConnection + "Database=postgres;").Options))
            {
                await admin.Database.ExecuteSqlRawAsync($"CREATE DATABASE \"{_database}\";");
            }

            await using var db = NewContext();
            await db.Database.MigrateAsync();

            _available = true;
        }
        catch (NpgsqlException)
        {
            _available = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (!_available) return;

        try
        {
            await using var admin = new DbContext(
                new DbContextOptionsBuilder().UseNpgsql(BaseConnection + "Database=postgres;").Options);
            await admin.Database.ExecuteSqlRawAsync(
                $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE);");
        }
        catch { /* a stray throwaway database is harmless */ }
    }

    private static NotificationRequest Request(params string[] userIds) =>
        new([.. userIds.Select(id => new NotificationRecipient(id, $"User {id}", $"{id}@mei.local"))],
            NotificationCategories.ApprovalAssigned,
            "PR-26-0001 needs your approval",
            "Rs 40,000 — office chairs.",
            "/finance/requests/1", "finance",
            EventKey: "approval:finance.request:1:step:1");

    /// <summary>The two channels the app really registers, so defaults match production.</summary>
    private static INotificationChannel[] Channels() =>
        [new InAppChannel(), new AlwaysReachableChannel("email")];

    [SkippableFact]
    public async Task A_staged_notification_is_only_there_after_the_caller_commits()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using (var db = NewContext())
        {
            var service = new NotificationService(
                new NotificationStore(db), Channels(), new FixedClock(At));

            await service.NotifyAsync(Request("u1"));

            // Nothing committed yet - this context is thrown away without saving,
            // exactly as it would be if the approval that raised it had failed.
        }

        await using var fresh = NewContext();
        Assert.Empty(await fresh.Notifications.ToListAsync());
    }

    [SkippableFact]
    public async Task Committing_writes_the_message_and_a_row_per_channel()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using (var db = NewContext())
        {
            var service = new NotificationService(
                new NotificationStore(db), Channels(), new FixedClock(At));

            await service.NotifyAsync(Request("u1", "u2"));
            await db.SaveChangesAsync();
        }

        await using var fresh = NewContext();

        var notifications = await fresh.Notifications
            .Include(n => n.Deliveries)
            .OrderBy(n => n.UserId)
            .ToListAsync();

        Assert.Equal(2, notifications.Count);
        Assert.All(notifications, n => Assert.Equal(2, n.Deliveries.Count));
        Assert.Equal("finance", notifications[0].ModuleKey);
    }

    [SkippableFact]
    public async Task Claiming_takes_a_due_delivery_once_and_only_once()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await Seed("u1");

        await using var db = NewContext();
        var outbox = new NotificationOutbox(db);

        var first = await outbox.ClaimDueAsync(10, At.UtcDateTime);
        Assert.Equal(2, first.Count);

        // Every claimed row has had its attempt counted and its due time cleared.
        Assert.All(first, d => Assert.Equal(1, d.Attempts));

        // A second dispatcher - or the same one going round again - must find
        // nothing. Two processes sending the same email is the failure this
        // statement exists to prevent.
        Assert.Empty(await outbox.ClaimDueAsync(10, At.UtcDateTime));
    }

    [SkippableFact]
    public async Task Claiming_ignores_deliveries_that_are_not_due_yet()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await Seed("u1");

        await using var db = NewContext();
        var outbox = new NotificationOutbox(db);

        // A minute before the rows became due.
        Assert.Empty(await outbox.ClaimDueAsync(10, At.UtcDateTime.AddMinutes(-1)));
    }

    [SkippableFact]
    public async Task A_suppressed_delivery_is_never_claimed()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using (var db = NewContext())
        {
            db.NotificationPreferences.Add(new NotificationPreference
            {
                UserId = "u1",
                Category = NotificationCategories.ApprovalAssigned,
                Channel = "email",
                Enabled = false
            });
            await db.SaveChangesAsync();

            var service = new NotificationService(
                new NotificationStore(db), Channels(), new FixedClock(At));

            await service.NotifyAsync(Request("u1"));
            await db.SaveChangesAsync();
        }

        await using var fresh = NewContext();
        var claimed = await new NotificationOutbox(fresh).ClaimDueAsync(10, At.UtcDateTime);

        // The status filter in the raw SQL has to match the enum's integers.
        // If it did not, a suppressed row would be picked up and the person's
        // opt-out would silently do nothing.
        Assert.Equal("inapp", Assert.Single(claimed).Channel);
    }

    [SkippableFact]
    public async Task Settling_a_failure_puts_it_back_and_settling_a_send_does_not()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await Seed("u1");

        await using var db = NewContext();
        var outbox = new NotificationOutbox(db);

        var claimed = await outbox.ClaimDueAsync(10, At.UtcDateTime);
        var email = claimed.First(d => d.Channel == "email");
        var inapp = claimed.First(d => d.Channel == "inapp");

        var retryAt = At.UtcDateTime.AddMinutes(1);
        await outbox.SettleAsync(email.DeliveryId, DeliveryStatus.Failed, retryAt, null, "smtp down");
        await outbox.SettleAsync(inapp.DeliveryId, DeliveryStatus.Sent, null, At.UtcDateTime, null);

        // Nothing is due at the moment of settling...
        Assert.Empty(await outbox.ClaimDueAsync(10, At.UtcDateTime));

        // ...and only the failed one comes back once its backoff has elapsed.
        var again = await outbox.ClaimDueAsync(10, retryAt);
        Assert.Equal("email", Assert.Single(again).Channel);
        Assert.Equal(2, again[0].Attempts);
    }

    [SkippableFact]
    public async Task A_dead_delivery_is_listed_and_can_be_put_back()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await Seed("u1");

        await using var db = NewContext();
        var outbox = new NotificationOutbox(db);

        var claimed = await outbox.ClaimDueAsync(10, At.UtcDateTime);
        var email = claimed.First(d => d.Channel == "email");

        await outbox.SettleAsync(email.DeliveryId, DeliveryStatus.Dead, null, null, "gave up");

        var dead = await outbox.DeadAsync(10);
        Assert.Equal(email.DeliveryId, Assert.Single(dead).DeliveryId);

        Assert.True((await outbox.RetryAsync(email.DeliveryId, At.UtcDateTime)).Ok);

        // Back in the queue with a clean slate, so a mail server fixed at noon
        // does not immediately exhaust the attempts it burned overnight.
        var back = Assert.Single(await outbox.ClaimDueAsync(10, At.UtcDateTime));
        Assert.Equal(1, back.Attempts);
    }

    [SkippableFact]
    public async Task The_bell_counts_only_what_is_unread_and_undismissed()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewContext();
        var store = new NotificationStore(db);
        var service = new NotificationService(store, Channels(), new FixedClock(At));

        await service.NotifyAsync(Request("u1", "u2", "u3"));
        await db.SaveChangesAsync();

        Assert.Equal(1, await service.UnreadCountAsync("u1"));

        // u1 reads theirs; the other two are stood down when the approval settles.
        var mine = (await service.UnreadAsync("u1")).Single();
        await service.MarkReadAsync(mine.Id);

        await service.DismissEventAsync("approval:finance.request:1:step:1");
        await db.SaveChangesAsync();

        Assert.Equal(0, await service.UnreadCountAsync("u1"));
        Assert.Equal(0, await service.UnreadCountAsync("u2"));
        Assert.Equal(0, await service.UnreadCountAsync("u3"));
    }

    private async Task Seed(string userId)
    {
        await using var db = NewContext();

        var service = new NotificationService(
            new NotificationStore(db), Channels(), new FixedClock(At));

        await service.NotifyAsync(Request(userId));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Stands in for the email channel. The real one reports no address unless
    /// SMTP is configured, which would leave nothing for the outbox to claim.
    /// </summary>
    private sealed class AlwaysReachableChannel(string key) : INotificationChannel
    {
        public string Key { get; } = key;
        public string DisplayName => Key;
        public bool EnabledByDefault(string category) => true;
        public string? AddressFor(NotificationRecipient recipient) => recipient.Email;

        public Task<Result> SendAsync(
            Notification notification, string address, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());
    }
}
