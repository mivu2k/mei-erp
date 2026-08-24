using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Hr;

public sealed record MonthlyAttendance(
    int EmployeeId, string EmployeeCode, string EmployeeName, string? Department,
    IReadOnlyDictionary<int, AttendanceDay> Days, int Present, int Late, int HalfDays,
    int Absent, int OnLeave, int Incomplete, int WorkedMinutes, int OvertimeMinutes, int LateMinutes)
{
    public decimal PayableDays => Present + Late + OnLeave + HalfDays * 0.5m;
}

public sealed record ManualAttendanceInput(
    int EmployeeId, DateOnly Date, TimeOnly? FirstIn, TimeOnly? LastOut,
    AttendanceStatus Status, string Reason, string? Notes);

public interface IAttendanceService
{
    Task<IReadOnlyList<AttendanceDay>> RegisterAsync(DateOnly date, string? departmentId = null, CancellationToken ct = default);
    Task<IReadOnlyList<AttendanceDay>> EmployeeAsync(int employeeId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<AttendancePunch>> PunchesAsync(int employeeId, DateOnly date, CancellationToken ct = default);
    Task<IReadOnlyList<MonthlyAttendance>> MonthlyAsync(int year, int month, string? departmentId = null, CancellationToken ct = default);
    Task<Result<AttendanceDay>> SaveManualAsync(ManualAttendanceInput input, CancellationToken ct = default);
    Task<Result> RevertAsync(int attendanceDayId, CancellationToken ct = default);
}

public sealed class AttendanceService(
    HrDbContext db, IAttendanceSyncService sync, ICurrentUser currentUser, IClock clock)
    : IAttendanceService
{
    public async Task<IReadOnlyList<AttendanceDay>> RegisterAsync(
        DateOnly date, string? departmentId = null, CancellationToken ct = default)
    {
        var query = db.AttendanceDays.AsNoTracking().Include(d => d.Employee)
            .Include(d => d.LeaveRequest).Where(d => d.Date == date);
        if (departmentId is not null) query = query.Where(d => d.Employee!.DepartmentId == departmentId);
        if (!currentUser.Can(HrModule.AttendanceManage))
            query = query.Where(d => d.Employee!.UserId == currentUser.UserId);
        return await query.OrderBy(d => d.Employee!.FullName).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AttendanceDay>> EmployeeAsync(
        int employeeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (!await MayReadEmployeeAsync(employeeId, ct)) return [];
        return await db.AttendanceDays.AsNoTracking().Include(d => d.LeaveRequest)
            .Where(d => d.EmployeeId == employeeId && d.Date >= from && d.Date <= to)
            .OrderBy(d => d.Date).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AttendancePunch>> PunchesAsync(
        int employeeId, DateOnly date, CancellationToken ct = default)
    {
        if (!await MayReadEmployeeAsync(employeeId, ct)) return [];
        var start = date.ToDateTime(TimeOnly.MinValue);
        var end = date.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return await db.AttendancePunches.AsNoTracking().Include(p => p.AttendanceStation)
            .Where(p => p.EmployeeId == employeeId && p.PunchedAt >= start && p.PunchedAt < end)
            .OrderBy(p => p.PunchedAt).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MonthlyAttendance>> MonthlyAsync(
        int year, int month, string? departmentId = null, CancellationToken ct = default)
    {
        var from = new DateOnly(year, month, 1); var to = from.AddMonths(1).AddDays(-1);
        var employees = await db.Employees.AsNoTracking()
            .Where(e => (departmentId == null || e.DepartmentId == departmentId)
                     && e.JoinedOn <= to && (e.LeftOn == null || e.LeftOn >= from))
            .OrderBy(e => e.FullName).ToListAsync(ct);
        if (!currentUser.Can(HrModule.AttendanceManage))
            employees = [.. employees.Where(e => e.UserId == currentUser.UserId)];
        var ids = employees.Select(e => e.Id).ToList();
        var days = await db.AttendanceDays.AsNoTracking()
            .Where(d => ids.Contains(d.EmployeeId) && d.Date >= from && d.Date <= to).ToListAsync(ct);
        var grouped = days.GroupBy(d => d.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
        return [.. employees.Select(e => BuildMonth(e, grouped.GetValueOrDefault(e.Id, [])))];
    }

    private static MonthlyAttendance BuildMonth(Employee employee, List<AttendanceDay> days)
    {
        int Count(AttendanceStatus status) => days.Count(d => d.Status == status);
        return new(employee.Id, employee.Code, employee.FullName, employee.DepartmentName,
            days.ToDictionary(d => d.Date.Day), Count(AttendanceStatus.Present),
            Count(AttendanceStatus.Late), Count(AttendanceStatus.HalfDay), Count(AttendanceStatus.Absent),
            Count(AttendanceStatus.OnLeave), Count(AttendanceStatus.Incomplete),
            days.Sum(d => d.WorkedMinutes), days.Sum(d => d.OvertimeMinutes), days.Sum(d => d.LateMinutes));
    }

    public async Task<Result<AttendanceDay>> SaveManualAsync(
        ManualAttendanceInput input, CancellationToken ct = default)
    {
        if (!currentUser.Can(HrModule.AttendanceManage))
            return Result.Fail<AttendanceDay>("You cannot correct attendance.", "attendance.forbidden");
        if (string.IsNullOrWhiteSpace(input.Reason))
            return Result.Fail<AttendanceDay>("Give a reason for the correction.", "attendance.reason-required");
        if (input.FirstIn is { } first && input.LastOut is { } last && last < first)
            return Result.Fail<AttendanceDay>("Check-out cannot be before check-in.", "attendance.bad-times");
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == input.EmployeeId, ct);
        if (employee is null) return Result.Fail<AttendanceDay>("Employee not found.", "attendance.no-employee");
        if (!employee.IsEmployedOn(input.Date))
            return Result.Fail<AttendanceDay>("The employee was not employed on that date.", "attendance.not-employed");
        if (input.Date > clock.Today)
            return Result.Fail<AttendanceDay>("Attendance cannot be recorded in the future.", "attendance.future");

        var day = await db.AttendanceDays.FirstOrDefaultAsync(
            d => d.EmployeeId == input.EmployeeId && d.Date == input.Date, ct);
        if (day is null)
        {
            day = new() { EmployeeId = input.EmployeeId, Date = input.Date };
            db.AttendanceDays.Add(day);
        }
        day.FirstIn = input.FirstIn; day.LastOut = input.LastOut; day.Status = input.Status;
        day.Notes = input.Notes; day.Source = AttendanceSource.Manual;
        day.OverriddenById = currentUser.UserId; day.OverriddenByName = currentUser.Name;
        day.OverriddenAtUtc = clock.UtcNow; day.OverrideReason = input.Reason.Trim();
        Recalculate(day, await ShiftForAsync(employee, ct));
        await db.SaveChangesAsync(ct);
        return Result.Success(day);
    }

    public async Task<Result> RevertAsync(int attendanceDayId, CancellationToken ct = default)
    {
        if (!currentUser.Can(HrModule.AttendanceManage))
            return Result.Fail("You cannot correct attendance.", "attendance.forbidden");
        var day = await db.AttendanceDays.FirstOrDefaultAsync(d => d.Id == attendanceDayId, ct);
        if (day is null) return Result.Fail("Attendance day not found.", "attendance.not-found");
        day.Source = AttendanceSource.Device; day.OverriddenById = null; day.OverriddenByName = null;
        day.OverriddenAtUtc = null; day.OverrideReason = null;
        await db.SaveChangesAsync(ct);
        await sync.RebuildAsync(day.Date, day.Date, day.EmployeeId, ct);
        return Result.Success();
    }

    private async Task<Shift> ShiftForAsync(Employee e, CancellationToken ct) =>
        (e.ShiftId is { } id ? await db.Shifts.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct) : null)
        ?? await db.Shifts.AsNoTracking().FirstOrDefaultAsync(s => s.IsDefault, ct)
        ?? new() { Name = "General" };

    private Task<bool> MayReadEmployeeAsync(int employeeId, CancellationToken ct) =>
        currentUser.Can(HrModule.AttendanceManage)
            ? Task.FromResult(true)
            : db.Employees.AnyAsync(e => e.Id == employeeId && e.UserId == currentUser.UserId, ct);

    private static void Recalculate(AttendanceDay day, Shift shift)
    {
        if (day.FirstIn is not { } first || day.LastOut is not { } last)
        { day.WorkedMinutes = day.LateMinutes = day.EarlyLeaveMinutes = day.OvertimeMinutes = 0; return; }
        day.WorkedMinutes = Math.Max(0, (int)(last - first).TotalMinutes - shift.BreakMinutes);
        day.LateMinutes = first <= shift.StartsAt.AddMinutes(shift.GraceMinutes) ? 0 : (int)(first - shift.StartsAt).TotalMinutes;
        day.EarlyLeaveMinutes = last >= shift.EndsAt ? 0 : (int)(shift.EndsAt - last).TotalMinutes;
        day.OvertimeMinutes = last > shift.EndsAt.AddMinutes(shift.OvertimeAfterMinutes)
            ? (int)(last - shift.EndsAt).TotalMinutes : 0;
    }
}
