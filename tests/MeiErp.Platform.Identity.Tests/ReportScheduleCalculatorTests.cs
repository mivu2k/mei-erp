using MeiErp.Platform.Identity;
using Xunit;

namespace MeiErp.Platform.Identity.Tests;

public sealed class ReportScheduleCalculatorTests
{
    private static readonly TimeZoneInfo Karachi = TimeZoneInfo.FindSystemTimeZoneById("Asia/Karachi");

    [Fact]
    public void Daily_run_uses_business_timezone_not_server_date()
    {
        var row = Schedule(ReportScheduleFrequency.Daily, new TimeOnly(8, 0));
        var next = ReportScheduleCalculator.NextUtc(row, new DateTime(2026, 8, 22, 2, 30, 0, DateTimeKind.Utc), Karachi);
        Assert.Equal(new DateTime(2026, 8, 22, 3, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void Daily_run_after_local_time_moves_to_tomorrow()
    {
        var row = Schedule(ReportScheduleFrequency.Daily, new TimeOnly(8, 0));
        var next = ReportScheduleCalculator.NextUtc(row, new DateTime(2026, 8, 22, 3, 1, 0, DateTimeKind.Utc), Karachi);
        Assert.Equal(new DateTime(2026, 8, 23, 3, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void Weekly_run_selects_requested_weekday()
    {
        var row = Schedule(ReportScheduleFrequency.Weekly, new TimeOnly(9, 30)); row.DayOfWeek = DayOfWeek.Monday;
        var next = ReportScheduleCalculator.NextUtc(row, new DateTime(2026, 8, 22, 0, 0, 0, DateTimeKind.Utc), Karachi);
        Assert.Equal(DayOfWeek.Monday, TimeZoneInfo.ConvertTimeFromUtc(next, Karachi).DayOfWeek);
        Assert.Equal(new TimeSpan(9, 30, 0), TimeZoneInfo.ConvertTimeFromUtc(next, Karachi).TimeOfDay);
    }

    [Fact]
    public void Monthly_run_selects_requested_day()
    {
        var row = Schedule(ReportScheduleFrequency.Monthly, new TimeOnly(7, 0)); row.DayOfMonth = 12;
        var next = ReportScheduleCalculator.NextUtc(row, new DateTime(2026, 8, 22, 0, 0, 0, DateTimeKind.Utc), Karachi);
        var local = TimeZoneInfo.ConvertTimeFromUtc(next, Karachi);
        Assert.Equal(new DateTime(2026, 9, 12, 7, 0, 0), local);
    }

    private static ReportSchedule Schedule(ReportScheduleFrequency frequency, TimeOnly at) =>
        new() { Frequency = frequency, RunAtLocal = at, DayOfMonth = 1, DayOfWeek = DayOfWeek.Monday };
}
