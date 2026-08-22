using MeiErp.Platform.Kernel;
using MeiErp.Platform.Notifications;
using Xunit;

namespace MeiErp.Platform.Notifications.Tests;

/// <summary>
/// What happens when something asks for a person to be told.
///
/// The decisions worth pinning are all about who ends up with what: a person
/// told once rather than twice, a channel that is off recorded as off rather
/// than forgotten, and nothing committed by the notifier itself.
/// </summary>
public sealed class NotificationServiceTests
{
    private static readonly DateTimeOffset At =
        new(2026, 8, 22, 9, 0, 0, TimeSpan.Zero);

    private static NotificationRecipient Person(
        string id, string? email = "someone@mei.local") => new(id, $"User {id}", email);

    private static NotificationRequest Request(params NotificationRecipient[] to) =>
        new(to, NotificationCategories.ApprovalAssigned,
            "PR-26-0001 needs your approval",
            "Rs 40,000 — office chairs.",
            "/finance/requests/1", "finance",
            EventKey: "approval:1:step:1");

    [Fact]
    public async Task Somebody_eligible_twice_is_told_once()
    {
        var store = new FakeStore();
        var service = new NotificationService(
            store, [new FakeChannel("inapp")], new FixedClock(At));

        // Being both the line manager and a standing delegate for the same step
        // is ordinary. Two identical emails about one request reads as broken.
        await service.NotifyAsync(Request(Person("u1"), Person("u1"), Person("u2")));

        Assert.Equal(2, store.Notifications.Count);
        Assert.Equal(["u1", "u2"], store.Notifications.Select(n => n.UserId).Order());
    }

    [Fact]
    public async Task Nothing_is_committed_by_the_notifier()
    {
        var store = new FakeStore();
        var service = new NotificationService(
            store, [new FakeChannel("inapp")], new FixedClock(At));

        await service.NotifyAsync(Request(Person("u1")));

        // The caller commits, so the notification and the approval that raised
        // it land together or not at all. A save here would break that.
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task A_channel_the_person_turned_off_is_recorded_as_suppressed()
    {
        var store = new FakeStore();
        store.Preferences.Add(new NotificationPreference
        {
            UserId = "u1",
            Category = NotificationCategories.ApprovalAssigned,
            Channel = "email",
            Enabled = false
        });

        var service = new NotificationService(
            store, [new FakeChannel("inapp"), new FakeChannel("email")], new FixedClock(At));

        await service.NotifyAsync(Request(Person("u1")));

        var deliveries = store.Notifications.Single().Deliveries;
        var email = deliveries.Single(d => d.Channel == "email");

        // A row saying "suppressed", not a missing row: "we never tried" and
        // "we tried and it bounced" are different answers, and no row at all
        // cannot tell them apart.
        Assert.Equal(DeliveryStatus.Suppressed, email.Status);
        Assert.Equal(DeliveryStatus.Pending, deliveries.Single(d => d.Channel == "inapp").Status);
    }

    [Fact]
    public async Task A_person_with_no_address_is_not_applicable_rather_than_failed()
    {
        var store = new FakeStore();
        var email = new FakeChannel("email") { Unreachable = true };

        var service = new NotificationService(store, [email], new FixedClock(At));

        await service.NotifyAsync(Request(Person("u1", email: null)));

        var delivery = store.Notifications.Single().Deliveries.Single();

        // Retrying this would burn every attempt against something that cannot
        // change without somebody editing the user record.
        Assert.Equal(DeliveryStatus.NotApplicable, delivery.Status);
        Assert.Null(delivery.NextAttemptUtc);
    }

    [Fact]
    public async Task A_preference_overrides_the_channel_default_in_both_directions()
    {
        var store = new FakeStore();
        store.Preferences.Add(new NotificationPreference
        {
            UserId = "u1",
            Category = NotificationCategories.ApprovalAssigned,
            Channel = "quiet",
            Enabled = true
        });

        var service = new NotificationService(
            store, [new FakeChannel("quiet", defaultOn: false)], new FixedClock(At));

        await service.NotifyAsync(Request(Person("u1")));

        // Off by default, explicitly switched on. The opposite direction is
        // covered above; both matter, because a preference that only ever
        // silences is not a preference.
        Assert.Equal(DeliveryStatus.Pending, store.Notifications.Single().Deliveries.Single().Status);
    }

    [Fact]
    public async Task Deciding_a_queued_approval_stands_the_others_down()
    {
        var store = new FakeStore();
        var service = new NotificationService(
            store, [new FakeChannel("inapp")], new FixedClock(At));

        await service.NotifyAsync(Request(Person("u1"), Person("u2"), Person("u3")));

        // u2 looked at theirs before the decision came in.
        var seen = store.Notifications.Single(n => n.UserId == "u2");
        seen.ReadUtc = At.UtcDateTime;

        await service.DismissEventAsync("approval:1:step:1");

        Assert.NotNull(store.Notifications.Single(n => n.UserId == "u1").DismissedUtc);
        Assert.NotNull(store.Notifications.Single(n => n.UserId == "u3").DismissedUtc);

        // Already read, so left alone - dismissing it would rewrite what they saw.
        Assert.Null(seen.DismissedUtc);

        // And the bell is empty for the two who never looked.
        Assert.Equal(0, await service.UnreadCountAsync("u1"));
    }

    [Fact]
    public async Task Reading_one_leaves_the_rest_of_the_bell_alone()
    {
        var store = new FakeStore();
        var service = new NotificationService(
            store, [new FakeChannel("inapp")], new FixedClock(At));

        await service.NotifyAsync(Request(Person("u1")));
        await service.NotifyAsync(Request(Person("u1")));

        Assert.Equal(2, await service.UnreadCountAsync("u1"));

        await service.MarkReadAsync(store.Notifications[0].Id);

        Assert.Equal(1, await service.UnreadCountAsync("u1"));
    }

    [Fact]
    public async Task Telling_nobody_creates_nothing()
    {
        var store = new FakeStore();
        var service = new NotificationService(
            store, [new FakeChannel("inapp")], new FixedClock(At));

        // An empty recipient list reaches here whenever a step resolves to
        // nobody. It must be a no-op, not a row with an empty user id that the
        // dispatcher then tries to deliver.
        var created = await service.NotifyAsync(Request());

        Assert.Empty(created);
        Assert.Empty(store.Notifications);
    }
}
