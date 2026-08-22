namespace MeiErp.Platform.Notifications;

/// <summary>
/// When to try a failed delivery again, and when to stop.
///
/// Pure and static, for the same reason <c>WorkflowRouter</c> is: the awkward
/// cases here are all about time - the boundary where the last attempt is used
/// up, the cap that stops a doubling backoff drifting to a week - and none of
/// them are testable if the rule reads the clock itself.
/// </summary>
public static class RetrySchedule
{
    /// <summary>
    /// Five attempts spans roughly half an hour of backoff. Long enough to ride
    /// out a mail server restart, short enough that a genuinely broken setup
    /// shows up in the dead-letter list the same morning rather than next week.
    /// </summary>
    public const int MaxAttempts = 5;

    /// <summary>
    /// Doubling from one minute: 1, 2, 4, 8, 16. Capped so that raising
    /// <see cref="MaxAttempts"/> later cannot silently push the last retry days
    /// out.
    /// </summary>
    public static readonly TimeSpan FirstDelay = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The delay before attempt number <paramref name="attemptsSoFar"/> + 1.
    /// </summary>
    public static TimeSpan DelayAfter(int attemptsSoFar)
    {
        if (attemptsSoFar < 1) return FirstDelay;

        // Shift rather than Math.Pow: at attemptsSoFar around 30 the double
        // overflows into infinity and the cast below becomes MinValue, which
        // would schedule the retry in the past and spin.
        var doublings = Math.Min(attemptsSoFar - 1, 16);
        var minutes = FirstDelay.TotalMinutes * (1L << doublings);

        return minutes >= MaxDelay.TotalMinutes
            ? MaxDelay
            : TimeSpan.FromMinutes(minutes);
    }

    /// <summary>
    /// What a delivery becomes after a failed attempt.
    ///
    /// <paramref name="attemptsSoFar"/> counts the attempt that just failed, so
    /// the caller increments first and asks second.
    /// </summary>
    public static (DeliveryStatus Status, DateTime? NextAttemptUtc) AfterFailure(
        int attemptsSoFar, DateTime nowUtc)
    {
        if (attemptsSoFar >= MaxAttempts)
        {
            // Dead, not Failed, and with no next attempt: a row that still
            // carried a due time would be picked up forever by a dispatcher
            // that only filters on the clock.
            return (DeliveryStatus.Dead, null);
        }

        return (DeliveryStatus.Failed, nowUtc + DelayAfter(attemptsSoFar));
    }
}
