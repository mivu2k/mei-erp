using MeiErp.Platform.Kernel;
using MeiErp.Platform.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeiErp.Modules.Hr;

/// <summary>
/// How HR hears that a leave request was approved, rejected or returned.
///
/// This is the seam that lets the module keep its own status enum. The engine
/// never touches <c>LeaveRequest</c> directly; it calls here, inside the same
/// transaction as the approval action, so the two can never disagree.
///
/// It is also what makes migrating flows one at a time possible: every other
/// approval flow gets its own sink and moves independently.
/// </summary>
public sealed class LeaveApprovalSink(HrDbContext db, IClock clock) : IApprovalSink
{
    public string DocumentType => LeaveService.DocumentType;

    public async Task<Result> OnSettledAsync(
        int documentId, ApprovalStatus status, ApprovalRequest request, CancellationToken ct = default)
    {
        var leave = await db.LeaveRequests.FirstOrDefaultAsync(r => r.Id == documentId, ct);
        if (leave is null)
            return Result.Fail("The leave request behind this approval has gone.", "leave.not-found");

        var balance = await db.LeaveBalances.FirstOrDefaultAsync(
            b => b.EmployeeId == leave.EmployeeId
              && b.LeaveTypeId == leave.LeaveTypeId
              && b.Year == leave.FromDate.Year, ct);

        // The last comment on the approval is the reason, and it is what the
        // requester actually needs to read.
        leave.DecisionComment = request.Actions
            .OrderByDescending(a => a.ActedUtc)
            .Select(a => a.Comment)
            .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

        leave.DecidedUtc = clock.UtcNow;

        switch (status)
        {
            case ApprovalStatus.Approved:
                leave.Status = LeaveStatus.Approved;

                // The days were held as Pending at submission; now they are
                // genuinely spent. Moving rather than adding is what stops them
                // being counted twice.
                if (balance is not null)
                {
                    balance.Pending -= leave.Days;
                    balance.Taken += leave.Days;
                }
                break;

            case ApprovalStatus.Rejected:
                leave.Status = LeaveStatus.Rejected;
                if (balance is not null) balance.Pending -= leave.Days;
                break;

            case ApprovalStatus.Returned:
                // Still alive - the requester fixes it and resubmits. The hold
                // is released because the dates may change entirely.
                leave.Status = LeaveStatus.Returned;
                if (balance is not null) balance.Pending -= leave.Days;
                break;

            case ApprovalStatus.Cancelled:
                leave.Status = LeaveStatus.Cancelled;
                if (balance is not null) balance.Pending -= leave.Days;
                break;
        }

        // A negative hold would mean the arithmetic has drifted. Clamp so a bug
        // here cannot hand somebody extra leave, and it stays visible.
        if (balance is not null && balance.Pending < 0) balance.Pending = 0;

        return Result.Success();
    }
}

/// <summary>Registers HR with the platform.</summary>
public static class HrModule
{
    public const string Key = "hr";

    public const string EmployeesView = "hr.employees.view";
    public const string EmployeesManage = "hr.employees.manage";
    public const string LeaveRequest = "hr.leave.request";
    public const string LeaveManage = "hr.leave.manage";
    public const string LeaveTypesManage = "hr.leave-types.manage";

    public static ModuleDescriptor Descriptor => new()
    {
        Key = Key,
        Name = "HR",
        Description = "Staff records, leave and entitlements.",
        BasePath = "/hr",
        Icon = "Badge",
        Color = "#7b1fa2",
        SortOrder = 1,
        Schema = "hr",

        Permissions =
        [
            new(EmployeesView,     "Employees", "See the staff list and employee records"),
            new(EmployeesManage,   "Employees", "Add and edit employee records"),
            new(LeaveRequest,      "Leave",     "Request leave for yourself"),
            new(LeaveManage,       "Leave",     "See and manage everyone's leave"),
            new(LeaveTypesManage,  "Leave",     "Configure leave types and entitlements")
        ],

        RoleTemplates =
        [
            new("HR Manager", "Full access to staff records and everyone's leave.",
                [EmployeesView, EmployeesManage, LeaveRequest, LeaveManage, LeaveTypesManage]),

            new("Employee", "Can see the staff list and request their own leave.",
                [EmployeesView, LeaveRequest])
        ],

        Nav =
        [
            new("Employees", "/hr/employees", "Badge", EmployeesView),
            new("Leave",     "/hr/leave", "EventBusy", LeaveRequest)
        ],

        Approvables =
        [
            new(LeaveService.DocumentType, "Leave request")
        ]
    };

    public static IServiceCollection AddHrModule(
        this IServiceCollection services, IConfiguration config)
    {
        var connection = config.GetConnectionString("Platform")
            ?? throw new InvalidOperationException("No 'Platform' connection string for the HR module.");

        services.AddDbContext<HrDbContext>(options =>
            options.UseNpgsql(connection, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__migrations", "hr");
                npgsql.EnableRetryOnFailure(3);
            }));

        services.AddScoped<ILeaveService, LeaveService>();
        services.AddScoped<IEmployeeService, EmployeeService>();

        // Registered as IApprovalSink so the engine finds it without HR and the
        // engine referencing each other.
        services.AddScoped<IApprovalSink, LeaveApprovalSink>();

        return services;
    }
}
