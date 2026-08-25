using MeiErp.Platform.Identity;
using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MeiErp.Modules.Hr;

/// <summary>A user account eligible to be linked to an employee as their login.</summary>
public sealed record LoginCandidate(string UserId, string FullName, string? Email);

/// <summary>The login currently linked to an employee, if any.</summary>
public sealed record LinkedLogin(string UserId, string FullName, string? Email);

public interface IEmployeeService
{
    Task<IReadOnlyList<Employee>> ListAsync(string? search, bool includeLeavers, CancellationToken ct = default);
    Task<Employee?> GetAsync(int id, CancellationToken ct = default);
    Task<Result<Employee>> SaveAsync(Employee employee, CancellationToken ct = default);
    Task<Result> SetStatusAsync(int id, EmploymentStatus status, DateOnly? leftOn, CancellationToken ct = default);

    Task<IReadOnlyList<LeaveType>> LeaveTypesAsync(CancellationToken ct = default);
    Task<Result> SaveLeaveTypeAsync(LeaveType type, CancellationToken ct = default);

    /// <summary>The employee's currently linked login, if any.</summary>
    Task<LinkedLogin?> LinkedLoginAsync(int employeeId, CancellationToken ct = default);

    /// <summary>Active users who do not already have an employee linked to them.</summary>
    Task<IReadOnlyList<LoginCandidate>> SearchLoginCandidatesAsync(
        string? search, int employeeId, CancellationToken ct = default);

    /// <summary>
    /// Links an employee to a login atomically: sets <see cref="Employee.UserId"/>
    /// and the user's <c>EmployeeCode</c> together, so the two records cannot
    /// drift out of sync the way setting them from two separate admin screens can.
    /// </summary>
    Task<Result> LinkLoginAsync(int employeeId, string userId, CancellationToken ct = default);

    /// <summary>Undoes <see cref="LinkLoginAsync"/>, clearing both sides together.</summary>
    Task<Result> UnlinkLoginAsync(int employeeId, CancellationToken ct = default);
}

public sealed class EmployeeService(HrDbContext db, PlatformDbContext platformDb, IClock clock) : IEmployeeService
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

    public async Task<LinkedLogin?> LinkedLoginAsync(int employeeId, CancellationToken ct = default)
    {
        var userId = await db.Employees
            .Where(e => e.Id == employeeId)
            .Select(e => e.UserId)
            .FirstOrDefaultAsync(ct);

        if (userId is null) return null;

        return await platformDb.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new LinkedLogin(u.Id, u.FullName, u.Email))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<LoginCandidate>> SearchLoginCandidatesAsync(
        string? search, int employeeId, CancellationToken ct = default)
    {
        var linkedElsewhere = await db.Employees
            .Where(e => e.UserId != null && e.Id != employeeId)
            .Select(e => e.UserId!)
            .ToListAsync(ct);

        var query = platformDb.Users.AsNoTracking()
            .Where(u => u.IsActive && !linkedElsewhere.Contains(u.Id));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(u =>
                EF.Functions.ILike(u.FullName, pattern) ||
                (u.Email != null && EF.Functions.ILike(u.Email, pattern)));
        }

        return await query
            .OrderBy(u => u.FullName)
            .Take(20)
            .Select(u => new LoginCandidate(u.Id, u.FullName, u.Email))
            .ToListAsync(ct);
    }

    public async Task<Result> LinkLoginAsync(int employeeId, string userId, CancellationToken ct = default)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null) return Result.Fail("That employee no longer exists.", "employee.not-found");

        var loginTaken = await db.Employees
            .AnyAsync(e => e.UserId == userId && e.Id != employeeId, ct);

        if (loginTaken)
        {
            return Result.Fail(
                "That login is already linked to another employee, which would make " +
                "\"my leave\" ambiguous for them.",
                "employee.duplicate-login");
        }

        var userExists = await platformDb.Users.AnyAsync(u => u.Id == userId, ct);
        if (!userExists) return Result.Fail("That user no longer exists.", "employee.login-not-found");

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            employee.UserId = userId;
            await db.SaveChangesAsync(ct);

            // The user row is written on HR's own connection so it lands in the
            // same transaction. Two contexts mean two connections, which cannot
            // share one - and half a link is exactly what this feature exists to
            // prevent. Same database, so the platform schema is reachable here,
            // as it already is for the shared audit table.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE platform."AspNetUsers" SET "EmployeeCode" = {employee.Code} WHERE "Id" = {userId}""",
                ct);

            await tx.CommitAsync(ct);
        });

        return Result.Success();
    }

    public async Task<Result> UnlinkLoginAsync(int employeeId, CancellationToken ct = default)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null) return Result.Fail("That employee no longer exists.", "employee.not-found");

        if (employee.UserId is null) return Result.Success();

        var userId = employee.UserId;
        var code = employee.Code;

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            employee.UserId = null;
            await db.SaveChangesAsync(ct);

            // Only cleared if it still matches this employee - an administrator
            // may have since pointed the account at a different staff number
            // from the user screen, and that is not ours to discard.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 UPDATE platform."AspNetUsers" SET "EmployeeCode" = NULL
                 WHERE "Id" = {userId} AND "EmployeeCode" = {code}
                 """,
                ct);

            await tx.CommitAsync(ct);
        });

        return Result.Success();
    }
}
