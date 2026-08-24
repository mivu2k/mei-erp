using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Hr;

public interface IAttendanceSyncService
{
    Task<int> RebuildAsync(DateOnly from, DateOnly to, int? employeeId = null,
        CancellationToken ct = default);
}

/// <summary>Rebuilds derived days from immutable punches, leave, holidays and shifts.</summary>
public sealed class AttendanceSyncService(HrDbContext db) : IAttendanceSyncService
{
    public async Task<int> RebuildAsync(
        DateOnly from, DateOnly to, int? employeeId = null, CancellationToken ct = default)
    {
        if (to < from) (from, to) = (to, from);
        var fallback = await db.Shifts.AsNoTracking().FirstOrDefaultAsync(s => s.IsDefault, ct)
            ?? await db.Shifts.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? new Shift { Name = "General" };
        var shifts = await db.Shifts.AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        var employees = await db.Employees.AsNoTracking()
            .Where(e => employeeId == null || e.Id == employeeId)
            .Select(e => new { e.Id, e.ShiftId, e.JoinedOn, e.LeftOn }).ToListAsync(ct);

        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var punches = await db.AttendancePunches.AsNoTracking()
            .Where(p => p.PunchedAt >= start && p.PunchedAt < end
                     && (employeeId == null || p.EmployeeId == employeeId))
            .Select(p => new { p.EmployeeId, p.PunchedAt }).ToListAsync(ct);
        var byDay = punches.GroupBy(p => (p.EmployeeId, DateOnly.FromDateTime(p.PunchedAt)))
            .ToDictionary(g => g.Key, g => g.Select(p => p.PunchedAt).ToList());

        var holidayRows = await db.Holidays.AsNoTracking().ToListAsync(ct);
        bool IsHoliday(DateOnly date) => holidayRows.Any(h =>
            (!h.IsAnnual && h.Date == date) ||
            (h.IsAnnual && h.Date.Month == date.Month && h.Date.Day == date.Day));

        var leaves = await db.LeaveRequests.AsNoTracking()
            .Where(l => l.Status == LeaveStatus.Approved && l.FromDate <= to && l.ToDate >= from
                     && (employeeId == null || l.EmployeeId == employeeId)).ToListAsync(ct);
        var existing = await db.AttendanceDays
            .Where(d => d.Date >= from && d.Date <= to
                     && (employeeId == null || d.EmployeeId == employeeId)).ToListAsync(ct);
        var indexed = existing.ToDictionary(d => (d.EmployeeId, d.Date));
        var rebuilt = 0;

        foreach (var employee in employees)
        {
            var shift = employee.ShiftId is { } shiftId && shifts.TryGetValue(shiftId, out var assigned)
                ? assigned : fallback;
            for (var date = from; date <= to; date = date.AddDays(1))
            {
                if (date < employee.JoinedOn || employee.LeftOn is { } left && date > left) continue;
                indexed.TryGetValue((employee.Id, date), out var current);
                if (current?.Source is AttendanceSource.Manual) continue;
                var leave = leaves.FirstOrDefault(l =>
                    l.EmployeeId == employee.Id && l.FromDate <= date && l.ToDate >= date);
                var computed = AttendanceCalculator.Build(employee.Id,
                    new(date, shift, IsHoliday(date), leave),
                    byDay.GetValueOrDefault((employee.Id, date), []));

                if (current is null) db.AttendanceDays.Add(computed);
                else Copy(computed, current);
                rebuilt++;
            }
        }
        await db.SaveChangesAsync(ct);
        return rebuilt;
    }

    private static void Copy(AttendanceDay from, AttendanceDay to)
    {
        to.FirstIn = from.FirstIn; to.LastOut = from.LastOut; to.PunchCount = from.PunchCount;
        to.Status = from.Status; to.Source = from.Source; to.WorkedMinutes = from.WorkedMinutes;
        to.LateMinutes = from.LateMinutes; to.EarlyLeaveMinutes = from.EarlyLeaveMinutes;
        to.OvertimeMinutes = from.OvertimeMinutes; to.LeaveRequestId = from.LeaveRequestId;
    }
}
