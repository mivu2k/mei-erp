namespace MeiErp.Modules.Hr;

public sealed record DayContext(DateOnly Date, Shift Shift, bool IsHoliday, LeaveRequest? Leave);

/// <summary>Pure attendance arithmetic: first punch in, last punch out.</summary>
public static class AttendanceCalculator
{
    public static AttendanceDay Build(
        int employeeId, DayContext context, IReadOnlyList<DateTime> punches)
    {
        var ordered = punches.OrderBy(p => p).ToList();
        var day = new AttendanceDay
        {
            EmployeeId = employeeId, Date = context.Date, PunchCount = ordered.Count,
            FirstIn = ordered.Count == 0 ? null : TimeOnly.FromDateTime(ordered[0]),
            LastOut = ordered.Count == 0 ? null : TimeOnly.FromDateTime(ordered[^1])
        };

        if (context.Leave is { Status: LeaveStatus.Approved })
        {
            day.Status = AttendanceStatus.OnLeave;
            day.Source = AttendanceSource.Leave;
            day.LeaveRequestId = context.Leave.Id;
            return day;
        }
        if (context.IsHoliday)
        {
            day.Status = AttendanceStatus.Holiday;
            day.Source = AttendanceSource.Holiday;
            return Overtime(day, context.Shift, ordered);
        }
        if (context.Shift.IsWeeklyOff(context.Date.DayOfWeek))
        {
            day.Status = AttendanceStatus.WeeklyOff;
            day.Source = AttendanceSource.WeeklyOff;
            return Overtime(day, context.Shift, ordered);
        }

        day.Source = AttendanceSource.Device;
        if (ordered.Count == 0) { day.Status = AttendanceStatus.Absent; return day; }
        if (ordered.Count == 1)
        {
            day.Status = AttendanceStatus.Incomplete;
            day.LateMinutes = LateBy(day.FirstIn!.Value, context.Shift);
            return day;
        }

        day.WorkedMinutes = Math.Max(0,
            (int)(ordered[^1] - ordered[0]).TotalMinutes - context.Shift.BreakMinutes);
        day.LateMinutes = LateBy(day.FirstIn!.Value, context.Shift);
        day.EarlyLeaveMinutes = day.LastOut >= context.Shift.EndsAt
            ? 0 : (int)(context.Shift.EndsAt - day.LastOut!.Value).TotalMinutes;
        var overtimeStart = context.Shift.EndsAt.AddMinutes(context.Shift.OvertimeAfterMinutes);
        day.OvertimeMinutes = day.LastOut > overtimeStart
            ? (int)(day.LastOut!.Value - context.Shift.EndsAt).TotalMinutes : 0;
        day.Status = day.WorkedMinutes < context.Shift.MinimumMinutes ? AttendanceStatus.Absent
            : day.WorkedMinutes < context.Shift.HalfDayMinutes ? AttendanceStatus.HalfDay
            : day.LateMinutes > 0 ? AttendanceStatus.Late : AttendanceStatus.Present;
        return day;
    }

    private static AttendanceDay Overtime(AttendanceDay day, Shift shift, List<DateTime> punches)
    {
        if (punches.Count < 2) return day;
        day.WorkedMinutes = Math.Max(0, (int)(punches[^1] - punches[0]).TotalMinutes - shift.BreakMinutes);
        day.OvertimeMinutes = day.WorkedMinutes;
        return day;
    }

    private static int LateBy(TimeOnly arrival, Shift shift) =>
        arrival <= shift.StartsAt.AddMinutes(shift.GraceMinutes)
            ? 0 : (int)(arrival - shift.StartsAt).TotalMinutes;
}
