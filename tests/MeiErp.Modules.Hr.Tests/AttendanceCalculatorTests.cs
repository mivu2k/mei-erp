using Xunit;

namespace MeiErp.Modules.Hr.Tests;

public sealed class AttendanceCalculatorTests
{
    private static readonly DateOnly Monday = new(2026, 7, 27);
    private static Shift Standard() => new()
    {
        Name = "General", StartsAt = new(9, 0), EndsAt = new(17, 0), GraceMinutes = 15,
        HalfDayMinutes = 240, MinimumMinutes = 60, OvertimeAfterMinutes = 30,
        WeeklyOffMask = 1 << (int)DayOfWeek.Sunday
    };
    private static DateTime At(int hour, int minute, DateOnly? date = null) =>
        (date ?? Monday).ToDateTime(new(hour, minute));
    private static DayContext Context(DateOnly? date = null, bool holiday = false, LeaveRequest? leave = null) =>
        new(date ?? Monday, Standard(), holiday, leave);

    [Fact] public void First_and_last_of_multiple_punches_bracket_the_day()
    {
        var day = AttendanceCalculator.Build(1, Context(), [At(9, 0), At(13, 0), At(13, 40), At(17, 30)]);
        Assert.Equal(new TimeOnly(9, 0), day.FirstIn);
        Assert.Equal(new TimeOnly(17, 30), day.LastOut);
        Assert.Equal(4, day.PunchCount);
    }

    [Theory]
    [InlineData(9, 14, 17, 30, AttendanceStatus.Present)]
    [InlineData(9, 25, 17, 30, AttendanceStatus.Late)]
    [InlineData(9, 0, 12, 0, AttendanceStatus.HalfDay)]
    [InlineData(9, 0, 9, 30, AttendanceStatus.Absent)]
    public void Working_day_is_judged_by_shift_thresholds(
        int inHour, int inMinute, int outHour, int outMinute, AttendanceStatus expected)
    {
        var day = AttendanceCalculator.Build(1, Context(), [At(inHour, inMinute), At(outHour, outMinute)]);
        Assert.Equal(expected, day.Status);
    }

    [Fact] public void A_single_punch_is_incomplete()
    {
        var day = AttendanceCalculator.Build(1, Context(), [At(9, 2)]);
        Assert.Equal(AttendanceStatus.Incomplete, day.Status);
        Assert.Equal(0, day.WorkedMinutes);
    }

    [Fact] public void Approved_leave_outranks_punches()
    {
        var leave = new LeaveRequest { Id = 9, Status = LeaveStatus.Approved };
        var day = AttendanceCalculator.Build(1, Context(leave: leave), [At(11, 0), At(11, 20)]);
        Assert.Equal(AttendanceStatus.OnLeave, day.Status);
        Assert.Equal(AttendanceSource.Leave, day.Source);
        Assert.Equal(9, day.LeaveRequestId);
    }

    [Fact] public void Holiday_and_weekly_off_work_is_overtime()
    {
        var holiday = AttendanceCalculator.Build(1, Context(holiday: true), [At(10, 0), At(14, 0)]);
        var sunday = new DateOnly(2026, 7, 26);
        var off = AttendanceCalculator.Build(1, Context(sunday), [At(10, 0, sunday), At(14, 0, sunday)]);
        Assert.Equal(240, holiday.OvertimeMinutes);
        Assert.Equal(AttendanceStatus.WeeklyOff, off.Status);
        Assert.Equal(240, off.OvertimeMinutes);
    }

    [Fact] public void Late_early_and_overtime_minutes_are_explicit()
    {
        var day = AttendanceCalculator.Build(1, Context(), [At(9, 25), At(18, 0)]);
        Assert.Equal(25, day.LateMinutes);
        Assert.Equal(0, day.EarlyLeaveMinutes);
        Assert.Equal(60, day.OvertimeMinutes);
    }
}
