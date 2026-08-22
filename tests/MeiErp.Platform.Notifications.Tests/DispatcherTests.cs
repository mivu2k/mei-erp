using MeiErp.Platform.Kernel;
using MeiErp.Platform.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MeiErp.Platform.Notifications.Tests;

/// <summary>
/// The backoff rules. Pure and static, so the awkward cases - the boundary
/// where the last attempt is used up, the cap that stops a doubling delay
/// drifting to a week - can be asserted directly instead of waited for.
/// </summary>
public sealed class RetryScheduleTests
{
    private static readonly DateTime Now = new(2026, 8, 22, 9, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(5, 16)]
    public void The_delay_doubles(int attemptsSoFar, int expectedMinutes) =>
        Assert.Equal(
            TimeSpan.FromMinutes(expectedMinutes),
            RetrySchedule.DelayAfter(attemptsSoFar));

    [Fact]
    public void The_delay_is_capped()
    {
        // Without the cap, raising MaxAttempts later would silently push the
        // last retry days out.
        Assert.Equal(RetrySchedule.MaxDelay, RetrySchedule.DelayAfter(12));
        Assert.Equal(RetrySchedule.MaxDelay, RetrySchedule.DelayAfter(60));
    }

    [Fact]
    public void An_absurd_attempt_count_does_not_overflow_into_the_past()
    {
        // Math.Pow would have gone to infinity here and the cast would land on
        // MinValue, scheduling the retry before now and spinning the dispatcher.
        var next = RetrySchedule.AfterFailure(int.MaxValue, Now);

        Assert.Equal(DeliveryStatus.Dead, next.Status);
        Assert.Null(next.NextAttemptUtc);
    }

    [Fact]
    public void A_failure_short_of_the_limit_is_scheduled_again()
    {
        var (status, next) = RetrySchedule.AfterFailure(1, Now);

        Assert.Equal(DeliveryStatus.Failed, status);
        Assert.Equal(Now.AddMinutes(1), next);
    }

    [Fact]
    public void The_last_attempt_is_dead_with_nothing_scheduled()
    {
        var (status, next) = RetrySchedule.AfterFailure(RetrySchedule.MaxAttempts, Now);

        Assert.Equal(DeliveryStatus.Dead, status);

        // Nothing scheduled, or a dispatcher that filters only on the clock
        // would pick it up forever.
        Assert.Null(next);
    }
}

/// <summary>
/// Draining the queue: what a send that works, fails, or explodes does to the
/// row behind it.
/// </summary>
public sealed class DispatcherTests
{
    private static readonly DateTimeOffset At =
        new(2026, 8, 22, 9, 0, 0, TimeSpan.Zero);

    private static (NotificationDispatcher Dispatcher, FakeOutbox Queue, FixedClock Clock)
        Build(params INotificationChannel[] channels)
    {
        var queue = new FakeOutbox();
        var clock = new FixedClock(At);

        var scopes = new StubScopes(new Dictionary<Type, object>
        {
            [typeof(INotificationOutbox)] = queue,
            [typeof(IClock)] = clock,
            [typeof(IEnumerable<INotificationChannel>)] = channels
        });

        return (new NotificationDispatcher(scopes, NullLogger<NotificationDispatcher>.Instance),
                queue, clock);
    }

    private static void Enqueue(FakeOutbox queue, string channel, int id = 1)
    {
        var notification = new Notification
        {
            Id = id,
            UserId = "u1",
            Category = NotificationCategories.ApprovalAssigned,
            Subject = "Needs your approval",
            Body = "Rs 40,000.",
            CreatedUtc = At.UtcDateTime
        };

        queue.Messages[id] = notification;
        queue.Deliveries.Add(new NotificationDelivery
        {
            Id = id,
            NotificationId = id,
            Channel = channel,
            Address = "someone@mei.local",
            Status = DeliveryStatus.Pending,
            NextAttemptUtc = At.UtcDateTime
        });
    }

    [Fact]
    public async Task A_delivered_message_is_marked_sent_and_not_picked_up_again()
    {
        var channel = new FakeChannel("email");
        var (dispatcher, queue, _) = Build(channel);
        Enqueue(queue, "email");

        Assert.Equal(1, await dispatcher.DrainOnceAsync());

        var delivery = queue.Deliveries.Single();
        Assert.Equal(DeliveryStatus.Sent, delivery.Status);
        Assert.NotNull(delivery.SentUtc);
        Assert.Single(channel.Sent);

        // The second pass must find nothing: a sent row with a due time still
        // on it would be sent again every fifteen seconds.
        Assert.Equal(0, await dispatcher.DrainOnceAsync());
        Assert.Single(channel.Sent);
    }

    [Fact]
    public async Task A_failure_is_retried_and_eventually_succeeds()
    {
        var channel = new FakeChannel("email") { FailTimes = 2 };
        var (dispatcher, queue, clock) = Build(channel);
        Enqueue(queue, "email");

        await dispatcher.DrainOnceAsync();
        Assert.Equal(DeliveryStatus.Failed, queue.Deliveries.Single().Status);
        Assert.Empty(channel.Sent);

        // Nothing is due yet, so a pass before the backoff elapses does nothing.
        Assert.Equal(0, await dispatcher.DrainOnceAsync());

        clock.Advance(TimeSpan.FromMinutes(2));
        await dispatcher.DrainOnceAsync();

        clock.Advance(TimeSpan.FromMinutes(5));
        await dispatcher.DrainOnceAsync();

        Assert.Equal(DeliveryStatus.Sent, queue.Deliveries.Single().Status);
        Assert.Single(channel.Sent);
    }

    [Fact]
    public async Task A_channel_that_never_works_gives_up_rather_than_retrying_forever()
    {
        var channel = new FakeChannel("email") { FailTimes = int.MaxValue };
        var (dispatcher, queue, clock) = Build(channel);
        Enqueue(queue, "email");

        for (var i = 0; i < RetrySchedule.MaxAttempts + 3; i++)
        {
            await dispatcher.DrainOnceAsync();
            clock.Advance(TimeSpan.FromHours(1));
        }

        var delivery = queue.Deliveries.Single();
        Assert.Equal(DeliveryStatus.Dead, delivery.Status);
        Assert.Equal(RetrySchedule.MaxAttempts, delivery.Attempts);
        Assert.NotNull(delivery.LastError);

        // And it shows up for somebody to look at, rather than vanishing.
        Assert.Single(await queue.DeadAsync(10));
    }

    [Fact]
    public async Task A_channel_that_throws_is_treated_as_a_failure_not_a_crash()
    {
        var exploding = new FakeChannel("email") { Throws = true };
        var working = new FakeChannel("inapp");

        var (dispatcher, queue, _) = Build(exploding, working);
        Enqueue(queue, "email", id: 1);
        Enqueue(queue, "inapp", id: 2);

        await dispatcher.DrainOnceAsync();

        // A channel throwing rather than returning a failure is a bug in that
        // channel, but it must not take the rest of the batch with it.
        Assert.Equal(DeliveryStatus.Failed, queue.Deliveries.Single(d => d.Channel == "email").Status);
        Assert.Equal(DeliveryStatus.Sent, queue.Deliveries.Single(d => d.Channel == "inapp").Status);
        Assert.Single(working.Sent);
    }

    [Fact]
    public async Task A_delivery_for_a_channel_that_no_longer_exists_is_not_retried()
    {
        var (dispatcher, queue, _) = Build(new FakeChannel("inapp"));
        Enqueue(queue, "whatsapp");

        await dispatcher.DrainOnceAsync();

        // Rows outlive a channel being removed from the build. Retrying them
        // forever would keep a permanent backlog in the queue.
        var delivery = queue.Deliveries.Single();
        Assert.Equal(DeliveryStatus.NotApplicable, delivery.Status);
        Assert.Null(delivery.NextAttemptUtc);
    }

    [Fact]
    public async Task Claiming_counts_the_attempt_so_a_send_that_never_returns_still_burns_one()
    {
        var (_, queue, clock) = Build(new FakeChannel("email"));
        Enqueue(queue, "email");

        var claimed = await queue.ClaimDueAsync(10, clock.UtcNow);

        Assert.Equal(1, claimed.Single().Attempts);

        // Claimed rows are no longer due, which is what stops a second
        // dispatcher sending the same email.
        Assert.Empty(await queue.ClaimDueAsync(10, clock.UtcNow));
    }

    [Fact]
    public async Task A_dead_delivery_can_be_put_back_but_a_live_one_cannot()
    {
        var (_, queue, clock) = Build(new FakeChannel("email"));
        Enqueue(queue, "email");

        var live = queue.Deliveries.Single();

        // Re-queueing something still in flight would send it twice.
        Assert.True((await queue.RetryAsync(live.Id, clock.UtcNow)).Failed);

        live.Status = DeliveryStatus.Dead;
        Assert.True((await queue.RetryAsync(live.Id, clock.UtcNow)).Ok);
        Assert.Equal(DeliveryStatus.Pending, live.Status);
        Assert.Equal(0, live.Attempts);
    }
}
