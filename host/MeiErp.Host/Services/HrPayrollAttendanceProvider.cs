using MeiErp.Modules.Finance;
using MeiErp.Modules.Hr;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Host.Services;

/// <summary>Composes HR attendance into Finance payroll without either module referencing the other.</summary>
public sealed class HrPayrollAttendanceProvider(
    HrDbContext hr, IAttendanceSyncService sync) : IPayrollAttendanceProvider
{
    public async Task<IReadOnlyDictionary<string, decimal>> PayableDaysByEmployeeCodeAsync(
        DateOnly month, CancellationToken ct = default)
    {
        var from = new DateOnly(month.Year, month.Month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        await sync.RebuildAsync(from, to, null, ct);
        var employees = await hr.Employees.AsNoTracking()
            .Where(e => e.JoinedOn <= to && (e.LeftOn == null || e.LeftOn >= from))
            .Select(e => new { e.Id, e.Code }).ToListAsync(ct);
        var ids = employees.Select(e => e.Id).ToList();
        var days = await hr.AttendanceDays.AsNoTracking()
            .Where(d => ids.Contains(d.EmployeeId) && d.Date >= from && d.Date <= to)
            .Select(d => new { d.EmployeeId, d.Status }).ToListAsync(ct);

        return employees.ToDictionary(e => e.Code, e => days.Where(d => d.EmployeeId == e.Id).Sum(d =>
            d.Status == AttendanceStatus.HalfDay ? 0.5m
            : d.Status is AttendanceStatus.Present or AttendanceStatus.Late or AttendanceStatus.OnLeave
                or AttendanceStatus.Holiday or AttendanceStatus.WeeklyOff ? 1m : 0m),
            StringComparer.OrdinalIgnoreCase);
    }
}
