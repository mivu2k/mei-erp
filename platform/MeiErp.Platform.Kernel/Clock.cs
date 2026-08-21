namespace MeiErp.Platform.Kernel;

/// <summary>
/// The only way to ask what time it is.
///
/// On a UTC server running a UTC+5 business, <c>DateTime.Today</c> and
/// <c>DateTime.UtcNow</c> disagree for five hours every night: a voucher entered
/// at 2am lands on yesterday's books. <see cref="Today"/> is the *business* date
/// in the configured zone; <see cref="UtcNow"/> is for timestamps only.
///
/// Entities take a <c>DateOnly today</c> parameter rather than reading this,
/// which is what makes date-boundary behaviour testable with a fixed clock.
/// </summary>
public interface IClock
{
    /// <summary>The business date in the configured time zone.</summary>
    DateOnly Today { get; }

    /// <summary>Wall-clock instant, always UTC. For audit stamps, never for dates.</summary>
    DateTime UtcNow { get; }

    /// <summary>Local business time, for display and for shift arithmetic.</summary>
    DateTimeOffset Now { get; }

    /// <summary>The zone the business runs in.</summary>
    TimeZoneInfo TimeZone { get; }
}

/// <inheritdoc />
public sealed class SystemClock(TimeZoneInfo timeZone) : IClock
{
    public TimeZoneInfo TimeZone { get; } = timeZone;

    public DateTime UtcNow => DateTime.UtcNow;

    public DateTimeOffset Now =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZone);

    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);
}

/// <summary>
/// A clock frozen at a chosen instant, for tests. Date-boundary bugs are only
/// findable when the test can stand at 11:59pm and step over midnight.
/// </summary>
public sealed class FixedClock(DateTimeOffset now, TimeZoneInfo? timeZone = null) : IClock
{
    public TimeZoneInfo TimeZone { get; } = timeZone ?? TimeZoneInfo.Utc;

    public DateTimeOffset Now { get; private set; } = now;

    public DateTime UtcNow => Now.UtcDateTime;

    public DateOnly Today => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(Now, TimeZone).DateTime);

    /// <summary>Move the clock forward. Returns this, so tests can chain.</summary>
    public FixedClock Advance(TimeSpan by)
    {
        Now = Now.Add(by);
        return this;
    }
}
