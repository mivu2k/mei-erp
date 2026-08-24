using MeiErp.Platform.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using MeiErp.Platform.Kernel;

namespace MeiErp.Modules.Hr;

public static class AttendanceReports
{
    public static IServiceCollection AddAttendanceReports(this IServiceCollection services)
    {
        services.AddScoped(sp => new ReportDefinition
        {
            Key = "hr.daily-attendance", Name = "Daily attendance register",
            Description = "Arrival, departure, status, worked time, lateness and overtime.",
            ModuleKey = HrModule.Key, Group = "Attendance", Permission = HrModule.AttendanceView,
            Uses = ReportFilters.AsAtDate | ReportFilters.Department,
            Run = (request, ct) => DailyAsync(sp.GetRequiredService<IAttendanceService>(), sp.GetRequiredService<IClock>(), request, ct)
        });
        services.AddScoped(sp => new ReportDefinition
        {
            Key="hr.expiring-documents",Name="Expiring employee documents",Description="Expired and upcoming contracts, identity records, licences and certificates.",
            ModuleKey=HrModule.Key,Group="Employees",Permission=HrModule.ReportsView,Uses=ReportFilters.AsAtDate,
            Run=async(request,ct)=>
            {
                var today=request.AsAt??sp.GetRequiredService<IClock>().Today;var cutoff=today.AddDays(60);
                var rows=await sp.GetRequiredService<HrDbContext>().EmployeeDocuments.AsNoTracking().Include(x=>x.Employee)
                    .Where(x=>x.ExpiresOn!=null&&x.ExpiresOn<=cutoff).OrderBy(x=>x.ExpiresOn).ToListAsync(ct);
                return new ReportResult{Columns=[new("code","Staff no."),new("employee","Employee",Width:2),new("document","Document",Width:2),new("kind","Kind"),new("expires","Expires",ReportValueKind.Date),new("days","Days left",ReportValueKind.Number)],
                    Rows=[..rows.Select(x=>new ReportRow(new Dictionary<string,object?>{{"code",x.Employee!.Code},{"employee",x.Employee.FullName},{"document",x.Title},{"kind",x.Kind},{"expires",x.ExpiresOn},{"days",x.ExpiresOn!.Value.DayNumber-today.DayNumber}},$"/hr/employees/{x.EmployeeId}/documents"))],Header=[new("Window",$"Through {cutoff:d MMM yyyy}")]};
            }
        });
        services.AddScoped(sp => new ReportDefinition
        {
            Key = "hr.monthly-attendance", Name = "Monthly attendance summary",
            Description = "Payable days and attendance totals by employee.",
            ModuleKey = HrModule.Key, Group = "Attendance", Permission = HrModule.AttendanceView,
            Uses = ReportFilters.AsAtDate | ReportFilters.Department,
            Run = (request, ct) => MonthlyAsync(sp.GetRequiredService<IAttendanceService>(), sp.GetRequiredService<IClock>(), request, ct)
        });
        return services;
    }

    private static async Task<ReportResult> DailyAsync(
        IAttendanceService attendance, IClock clock, ReportRequest request, CancellationToken ct)
    {
        var date = request.AsAt ?? clock.Today;
        var days = await attendance.RegisterAsync(date, request.DepartmentId, ct);
        var columns = new[]
        {
            new ReportColumn("code", "Staff no."), new ReportColumn("employee", "Employee", Width: 2),
            new ReportColumn("status", "Status", ReportValueKind.Status), new ReportColumn("in", "First in"),
            new ReportColumn("out", "Last out"), new ReportColumn("worked", "Worked min", ReportValueKind.Number),
            new ReportColumn("late", "Late min", ReportValueKind.Number), new ReportColumn("overtime", "OT min", ReportValueKind.Number),
            new ReportColumn("source", "Source")
        };
        return new ReportResult
        {
            Columns = columns,
            Rows = [.. days.Select(d => new ReportRow(new Dictionary<string, object?>
            {
                ["code"] = d.Employee?.Code, ["employee"] = d.Employee?.FullName,
                ["status"] = d.Status.ToString(), ["in"] = d.FirstIn?.ToString("HH:mm"),
                ["out"] = d.LastOut?.ToString("HH:mm"), ["worked"] = d.WorkedMinutes,
                ["late"] = d.LateMinutes, ["overtime"] = d.OvertimeMinutes, ["source"] = d.Source.ToString()
            }))],
            Header = [new("Date", date.ToString("d MMMM yyyy"))]
        };
    }

    private static async Task<ReportResult> MonthlyAsync(
        IAttendanceService attendance, IClock clock, ReportRequest request, CancellationToken ct)
    {
        var month = request.AsAt ?? clock.Today;
        var rows = await attendance.MonthlyAsync(month.Year, month.Month, request.DepartmentId, ct);
        var columns = new[]
        {
            new ReportColumn("code", "Staff no."), new ReportColumn("employee", "Employee", Width: 2),
            new ReportColumn("present", "Present", ReportValueKind.Number), new ReportColumn("late", "Late", ReportValueKind.Number),
            new ReportColumn("half", "Half", ReportValueKind.Number), new ReportColumn("absent", "Absent", ReportValueKind.Number),
            new ReportColumn("leave", "Leave", ReportValueKind.Number), new ReportColumn("incomplete", "Incomplete", ReportValueKind.Number),
            new ReportColumn("payable", "Payable days", ReportValueKind.Number), new ReportColumn("worked", "Worked min", ReportValueKind.Number),
            new ReportColumn("overtime", "OT min", ReportValueKind.Number)
        };
        return new ReportResult
        {
            Columns = columns,
            Rows = [.. rows.Select(r => new ReportRow(new Dictionary<string, object?>
            {
                ["code"] = r.EmployeeCode, ["employee"] = r.EmployeeName, ["present"] = r.Present,
                ["late"] = r.Late, ["half"] = r.HalfDays, ["absent"] = r.Absent,
                ["leave"] = r.OnLeave, ["incomplete"] = r.Incomplete, ["payable"] = r.PayableDays,
                ["worked"] = r.WorkedMinutes, ["overtime"] = r.OvertimeMinutes
            }))],
            Header = [new("Month", new DateTime(month.Year, month.Month, 1).ToString("MMMM yyyy"))]
        };
    }
}
