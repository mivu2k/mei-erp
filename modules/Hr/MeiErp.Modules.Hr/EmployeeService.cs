using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Hr;

public interface IEmployeeService
{
    Task<IReadOnlyList<Employee>> ListAsync(string? search, bool includeLeavers, CancellationToken ct = default);
    Task<Employee?> GetAsync(int id, CancellationToken ct = default);
    Task<Result<Employee>> SaveAsync(Employee employee, CancellationToken ct = default);
    Task<Result> SetStatusAsync(int id, EmploymentStatus status, DateOnly? leftOn, CancellationToken ct = default);

    Task<IReadOnlyList<LeaveType>> LeaveTypesAsync(CancellationToken ct = default);
    Task<Result> SaveLeaveTypeAsync(LeaveType type, CancellationToken ct = default);
}

public sealed class EmployeeService(HrDbContext db, IClock clock) : IEmployeeService
{
    public async Task<IReadOnlyList<Employee>> ListAsync(
        string? search, bool includeLeavers, CancellationToken ct = default)
    {
        var query = db.Employees.AsNoTracking().AsQueryable();

        if (!includeLeavers)
            query = query.Where(e => e.Status == EmploymentStatus.Active
                                  || e.Status == EmploymentStatus.OnLeave);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(e =>
                EF.Functions.ILike(e.FullName, pattern) ||
                EF.Functions.ILike(e.Code, pattern) ||
                (e.Designation != null && EF.Functions.ILike(e.Designation, pattern)));
        }

        return await query.OrderBy(e => e.FullName).ToListAsync(ct);
    }

    public Task<Employee?> GetAsync(int id, CancellationToken ct = default) =>
        db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<Result<Employee>> SaveAsync(Employee employee, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(employee.FullName))
            return Result.Fail<Employee>("An employee needs a name.", "employee.no-name");

        if (string.IsNullOrWhiteSpace(employee.Code))
            return Result.Fail<Employee>("An employee needs a staff number.", "employee.no-code");

        var codeTaken = await db.Employees
            .AnyAsync(e => e.Code == employee.Code && e.Id != employee.Id, ct);

        if (codeTaken)
        {
            // Two people on one staff number merges their leave and attendance.
            return Result.Fail<Employee>(
                $"Staff number {employee.Code} already belongs to someone else.",
                "employee.duplicate-code");
        }

        if (employee.UserId is not null)
        {
            var loginTaken = await db.Employees
                .AnyAsync(e => e.UserId == employee.UserId && e.Id != employee.Id, ct);

            if (loginTaken)
            {
                return Result.Fail<Employee>(
                    "That login is already linked to another employee, which would make " +
                    "\"my leave\" ambiguous for them.",
                    "employee.duplicate-login");
            }
        }

        if (employee.LeftOn is not null && employee.LeftOn < employee.JoinedOn)
            return Result.Fail<Employee>("The leaving date is before the joining date.", "employee.bad-dates");

        if (employee.Id == 0)
        {
            db.Employees.Add(employee);
        }
        else
        {
            var existing = await db.Employees.FirstOrDefaultAsync(e => e.Id == employee.Id, ct);
            if (existing is null)
                return Result.Fail<Employee>("That employee no longer exists.", "employee.not-found");

            db.Entry(existing).CurrentValues.SetValues(employee);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(employee);
    }

    public async Task<Result> SetStatusAsync(
        int id, EmploymentStatus status, DateOnly? leftOn, CancellationToken ct = default)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (employee is null) return Result.Fail("That employee no longer exists.", "employee.not-found");

        var leaving = status is EmploymentStatus.Resigned
            or EmploymentStatus.Terminated or EmploymentStatus.Retired;

        if (leaving)
        {
            var openLeave = await db.LeaveRequests.AnyAsync(
                r => r.EmployeeId == id && r.Status == LeaveStatus.Pending, ct);

            if (openLeave)
            {
                // Their approver would be deciding leave for someone who has
                // already gone, and the days would post against a stale balance.
                return Result.Fail(
                    "This person has leave awaiting approval. Settle it before marking them a leaver.",
                    "employee.open-leave");
            }

            employee.LeftOn = leftOn ?? clock.Today;
        }
        else
        {
            employee.LeftOn = null;
        }

        employee.Status = status;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<IReadOnlyList<LeaveType>> LeaveTypesAsync(CancellationToken ct = default) =>
        await db.LeaveTypes.AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

    public async Task<Result> SaveLeaveTypeAsync(LeaveType type, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(type.Name))
            return Result.Fail("A leave type needs a name.", "leave-type.no-name");

        if (type.Id == 0)
            db.LeaveTypes.Add(type);
        else
        {
            var existing = await db.LeaveTypes.FirstOrDefaultAsync(t => t.Id == type.Id, ct);
            if (existing is null) return Result.Fail("That leave type no longer exists.", "leave-type.not-found");
            db.Entry(existing).CurrentValues.SetValues(type);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
