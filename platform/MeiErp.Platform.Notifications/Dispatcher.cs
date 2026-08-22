using MeiErp.Platform.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MeiErp.Platform.Notifications;

/// <summary>
/// Claims a batch of due deliveries and hands them back.
///
/// Separate from the dispatcher, and separate from <see cref="INotificationStore"/>,
/// because claiming has to be atomic against other instances of the app. Two
/// processes draining the same queue must not both send the same email, so the
/// claim marks the rows as taken in the same statement that selects them.
/// </summary>
public interface INotificationOutbox
{
    /// <summary>
    /// Take up to <paramref name="batchSize"/> deliveries that are due.
    ///
    /// Claiming increments <c>Attempts</c> and clears <c>NextAttemptUtc</c> in
    /// the same statement that selects the rows, which is what stops a second
    /// dispatcher picking up the same ones. It also means the attempt is counted
    /// <i>before</i> it is made: a send that hangs and takes the process with it
    /// has still used an attempt, so a message that kills the dispatcher cannot
    /// be retried forever.
    ///
    /// <see cref="DueDelivery.Attempts"/> is therefore the count including this
    /// attempt.
    /// </summary>
    Task<IReadOnlyList<DueDelivery>> ClaimDueAsync(
        int batchSize, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>Record the outcome of one attempt.</summary>
    Task SettleAsync(
        int deliveryId, DeliveryStatus status, DateTime? nextAttemptUtc,
        DateTime? sentUtc, string? error, CancellationToken ct = default);

    /// <summary>Deliveries that ran out of attempts, for the dead-letter screen.</summary>
    Task<IReadOnlyList<DueDelivery>> DeadAsync(int take, CancellationToken ct = default);

    /// <summary>Put a dead delivery back in the queue, attempts reset.</summary>
    Task<Result> RetryAsync(int deliveryId, DateTime nowUtc, CancellationToken ct = default);
}

/// <summary>A delivery flattened with the message it carries, so sending needs no second read.</summary>
public sealed record DueDelivery(
    int DeliveryId,
    int NotificationId,
    string Channel,
    string Address,
    int Attempts,
    Notification Notification);

/// <summary>
/// Drains the notification queue.
///
/// Runs in the background and on its own scope: it must not borrow a request's
/// DbContext, because it outlives every request and would otherwise be reading
/// through a context that was disposed underneath it.
/// </summary>
public sealed class NotificationDispatcher(
    IServiceScopeFactory scopes,
    ILogger<NotificationDispatcher> logger) : BackgroundService
{
    /// <summary>
    /// How often to look when the queue came back empty. Short enough that an
    /// approval email feels immediate, long enough not to be a busy loop.
    /// </summary>
    public static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(15);

    public const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Notification dispatcher started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var sent = 0;

            try
            {
                sent = await DrainOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The loop must survive anything, including the database being
                // down. A dispatcher that dies on the first bad night stops
                // sending and nobody finds out until an approval is missed.
                logger.LogError(ex, "Notification dispatch failed; retrying.");
            }

            // A full batch probably means more is waiting, so go straight round
            // again rather than sleeping on a backlog.
            if (sent < BatchSize)
                await Task.Delay(IdleInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// One pass. Public so a test can drive it directly instead of racing the
    /// background loop's timer.
    /// </summary>
    public async Task<int> DrainOnceAsync(CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();

        var outbox = scope.ServiceProvider.GetRequiredService<INotificationOutbox>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var channels = scope.ServiceProvider
            .GetRequiredService<IEnumerable<INotificationChannel>>()
            .ToDictionary(c => c.Key, StringComparer.Ordinal);

        var due = await outbox.ClaimDueAsync(BatchSize, clock.UtcNow, ct);

        foreach (var delivery in due)
            await AttemptAsync(outbox, channels, clock, delivery, ct);

        return due.Count;
    }

    private async Task AttemptAsync(
        INotificationOutbox outbox,
        Dictionary<string, INotificationChannel> channels,
        IClock clock,
        DueDelivery delivery,
        CancellationToken ct)
    {
        var now = clock.UtcNow;

        // Already includes this attempt - ClaimDueAsync counted it when it took
        // the row, so a send that never returns cannot be retried forever.
        var attempts = delivery.Attempts;

        if (!channels.TryGetValue(delivery.Channel, out var channel))
        {
            // A channel that was removed from the build leaves rows behind.
            // Retrying them forever would keep a permanent backlog in the queue.
            await outbox.SettleAsync(
                delivery.DeliveryId, DeliveryStatus.NotApplicable, null, null,
                $"No channel registered under '{delivery.Channel}'.", ct);
            return;
        }

        Result result;
        try
        {
            result = await channel.SendAsync(delivery.Notification, delivery.Address, ct);
        }
        catch (Exception ex)
        {
            // A channel that throws rather than returning a failure is a bug in
            // that channel, but it must not take the whole batch down with it.
            logger.LogError(ex, "Channel {Channel} threw while sending.", delivery.Channel);
            result = Result.Fail(ex.Message, "channel.threw");
        }

        if (result.Ok)
        {
            await outbox.SettleAsync(delivery.DeliveryId, DeliveryStatus.Sent, null, now, null, ct);
            return;
        }

        var (status, next) = RetrySchedule.AfterFailure(attempts, now);

        if (status is DeliveryStatus.Dead)
        {
            logger.LogWarning(
                "Notification {Id} gave up on {Channel} after {Attempts} attempts: {Error}",
                delivery.NotificationId, delivery.Channel, attempts, result.Error);
        }

        await outbox.SettleAsync(delivery.DeliveryId, status, next, null, result.Error, ct);
    }
}
