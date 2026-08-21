using MeiErp.Platform.Kernel;
using MeiErp.Platform.Workflow;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Hr;

public interface ILeaveService
{
    Task<IReadOnlyList<LeaveRequest>> ListAsync(int? employeeId, LeaveStatus? status, CancellationToken ct = default);
    Task<LeaveRequest?> GetAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<LeaveBalance>> BalancesAsync(int employeeId, int year, CancellationToken ct = default);

    /// <summary>Working days between two dates, excluding weekends and holidays.</summary>
    Task<decimal> WorkingDaysAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<Result<LeaveRequest>> SaveDraftAsync(LeaveRequestInput input, CancellationToken ct = default);

    /// <summary>Sends it for approval, holding the days against the balance.</summary>
    Task<Result<LeaveRequest>> SubmitAsync(int id, CancellationToken ct = default);

    Task<Result> CancelAsync(int id, CancellationToken ct = default);

    /// <summary>The employee record for the signed-in person, if they have one.</summary>
    Task<Employee?> MeAsync(CancellationToken ct = default);
}

public sealed record LeaveRequestInput(
    int? Id, int EmployeeId, int LeaveTypeId,
    DateOnly FromDate, DateOnly ToDate,
    string? Reason, string? CoveredByName);

public sealed class LeaveService(
    HrDbContext db,
    IApprovalEngine approvals,
    ICurrentUser currentUser,
    IClock clock) : ILeaveService
{
    /// <summary>The document type this module routes through the approval engine.</summary>
    public const string DocumentType = "hr.leave-request";

    public async Task<IReadOnlyList<LeaveRequest>> ListAsync(
        int? employeeId, LeaveStatus? status, CancellationToken ct = default)
    {
        var query = db.LeaveRequests.AsNoTracking().AsQueryable();

        if (employeeId is not null) query = query.Where(r => r.EmployeeId == employeeId);
        if (status is not null) query = query.Where(r => r.Status == status);

        return await query
            .OrderByDescending(r => r.FromDate)
            .Take(500)
            .ToListAsync(ct);
    }

    public Task<LeaveRequest?> GetAsync(int id, CancellationToken ct = default) =>
        db.LeaveRequests
          .Include(r => r.Employee)
          .Include(r => r.LeaveType)
          .FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<LeaveBalance>> BalancesAsync(
        int employeeId, int year, CancellationToken ct = default) =>
        await db.LeaveBalances
            .AsNoTracking()
            .Include(b => b.LeaveType)
            .Where(b => b.EmployeeId == employeeId && b.Year == year)
            .ToListAsync(ct);

    public async Task<decimal> WorkingDaysAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from) return 0;

        var holidays = await db.Holidays
            .AsNoTracking()
            .Select(h => new { h.Date, h.IsAnnual })
            .ToListAsync(ct);

        // An annual holiday matches on day and month whatever year it was
        // entered against, so it does not need re-entering every January.
        var fixedDates = holidays.Where(h => !h.IsAnnual).Select(h => h.Date).ToHashSet();
        var annual = holidays.Where(h => h.IsAnnual)
                             .Select(h => (h.Date.Month, h.Date.Day)).ToHashSet();

        decimal days = 0;
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            if (fixedDates.Contains(date)) continue;
            if (annual.Contains((date.Month, date.Day))) continue;
            days++;
        }

        return days;
    }

    public async Task<Result<LeaveRequest>> SaveDraftAsync(
        LeaveRequestInput input, CancellationToken ct = default)
    {
        if (input.ToDate < input.FromDate)
            return Result.Fail<LeaveRequest>("The end date is before the start date.", "leave.bad-dates");

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == input.EmployeeId, ct);
        if (employee is null)
            return Result.Fail<LeaveRequest>("That employee no longer exists.", "leave.no-employee");

        if (!employee.IsEmployedOn(input.FromDate))
        {
            return Result.Fail<LeaveRequest>(
                $"{employee.FullName} was not employed on {input.FromDate:d MMM yyyy}.",
                "leave.not-employed");
        }

        var type = await db.LeaveTypes.FirstOrDefaultAsync(t => t.Id == input.LeaveTypeId, ct);
        if (type is null)
            return Result.Fail<LeaveRequest>("That leave type no longer exists.", "leave.no-type");

        // Two overlapping requests would double-count the same days off.
        var clashes = await db.LeaveRequests
            .Where(r => r.EmployeeId == input.EmployeeId
                     && r.Id != (input.Id ?? 0)
                     && (r.Status == LeaveStatus.Pending || r.Status == LeaveStatus.Approved)
                     && r.FromDate <= input.ToDate && r.ToDate >= input.FromDate)
            .Select(r => new { r.Reference, r.FromDate, r.ToDate })
            .FirstOrDefaultAsync(ct);

        if (clashes is not null)
        {
            return Result.Fail<LeaveRequest>(
                $"This overlaps {clashes.Reference} " +
                $"({clashes.FromDate:d MMM} to {clashes.ToDate:d MMM}).",
                "leave.overlap");
        }

        var days = await WorkingDaysAsync(input.FromDate, input.ToDate, ct);
        if (days == 0)
        {
            return Result.Fail<LeaveRequest>(
                "Those dates are all weekends or holidays, so there is no leave to take.",
                "leave.no-working-days");
        }

        LeaveRequest request;

        if (input.Id is null or 0)
        {
            request = new LeaveRequest
            {
                Reference = await NextReferenceAsync(ct),
                EmployeeId = employee.Id,
                EmployeeName = employee.FullName,
                RequestedByUserId = currentUser.UserId ?? "",
                Status = LeaveStatus.Draft
            };
            db.LeaveRequests.Add(request);
        }
        else
        {
            var existing = await db.LeaveRequests.FirstOrDefaultAsync(r => r.Id == input.Id, ct);
            if (existing is null)
                return Result.Fail<LeaveRequest>("That request no longer exists.", "leave.not-found");

            if (existing.Status is not (LeaveStatus.Draft or LeaveStatus.Returned))
            {
                return Result.Fail<LeaveRequest>(
                    "This has already been submitted and cannot be edited. " +
                    "Withdraw it first if it needs changing.",
                    "leave.not-editable");
            }

            request = existing;
        }

        request.LeaveTypeId = type.Id;
        request.LeaveTypeName = type.Name;
        request.FromDate = input.FromDate;
        request.ToDate = input.ToDate;
        request.Days = days;
        request.Reason = input.Reason;
        request.CoveredByName = input.CoveredByName;

        await db.SaveChangesAsync(ct);
        return Result.Success(request);
    }

    public async Task<Result<LeaveRequest>> SubmitAsync(int id, CancellationToken ct = default)
    {
        var request = await db.LeaveRequests
            .Include(r => r.LeaveType)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (request is null)
            return Result.Fail<LeaveRequest>("That request no longer exists.", "leave.not-found");

        if (request.Status is not (LeaveStatus.Draft or LeaveStatus.Returned))
            return Result.Fail<LeaveRequest>("This has already been submitted.", "leave.already-submitted");

        var balance = await EnsureBalanceAsync(request.EmployeeId, request.LeaveTypeId, request.FromDate.Year, ct);

        // Unlimited types (unpaid leave) carry a zero entitlement and are not
        // checked against a balance.
        var unlimited = request.LeaveType!.AnnualEntitlement == 0;

        if (!unlimited && balance.Available < request.Days)
        {
            return Result.Fail<LeaveRequest>(
                $"Only {balance.Available:0.#} days of {request.LeaveTypeName} are left, " +
                $"and this asks for {request.Days:0.#}.",
                "leave.insufficient-balance");
        }

        var employee = await db.Employees
            .Where(e => e.Id == request.EmployeeId)
            .Select(e => new { e.DepartmentId, e.FullName })
            .FirstAsync(ct);

        var submitted = await approvals.SubmitAsync(new SubmitApproval(
            ModuleKey: HrModule.Key,
            DocumentType: DocumentType,
            DocumentId: request.Id,
            DocumentReference: request.Reference,
            Summary: $"{employee.FullName} — {request.Days:0.#} days {request.LeaveTypeName}, " +
                     $"{request.FromDate:d MMM} to {request.ToDate:d MMM yyyy}",
            DocumentUrl: $"/hr/leave/{request.Id}",
            Amount: null,               // leave routes on no amount
            DepartmentId: employee.DepartmentId), ct);

        if (submitted.Failed)
            return Result.Fail<LeaveRequest>(submitted.Error!, submitted.Code);

        request.Status = LeaveStatus.Pending;
        request.ApprovalRequestId = submitted.Value.Id;
        request.SubmittedUtc = clock.UtcNow;
        request.DecisionComment = null;

        // Hold the days now, not on approval. Otherwise two pending requests
        // can each pass the balance check and together overspend it.
        balance.Pending += request.Days;

        await db.SaveChangesAsync(ct);
        return Result.Success(request);
    }

    public async Task<Result> CancelAsync(int id, CancellationToken ct = default)
    {
        var request = await db.LeaveRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null) return Result.Fail("That request no longer exists.", "leave.not-found");

        if (!request.IsOpen)
            return Result.Fail("This has already been decided.", "leave.not-open");

        if (request.Status is LeaveStatus.Pending && request.ApprovalRequestId is not null)
        {
            // The engine calls back into the sink, which releases the held days.
            return await approvals.CancelAsync(request.ApprovalRequestId.Value, "Withdrawn by the requester", ct);
        }

        request.Status = LeaveStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Employee?> MeAsync(CancellationToken ct = default)
    {
        var userId = currentUser.UserId;
        return userId is null
            ? null
            : await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.UserId == userId, ct);
    }

    /// <summary>Creates this year's balance from the type's entitlement the first time it is needed.</summary>
    internal async Task<LeaveBalance> EnsureBalanceAsync(
        int employeeId, int leaveTypeId, int year, CancellationToken ct)
    {
        var balance = await db.LeaveBalances
            .FirstOrDefaultAsync(b => b.EmployeeId == employeeId
                                   && b.LeaveTypeId == leaveTypeId
                                   && b.Year == year, ct);

        if (balance is not null) return balance;

        var entitlement = await db.LeaveTypes
            .Where(t => t.Id == leaveTypeId)
            .Select(t => t.AnnualEntitlement)
            .FirstAsync(ct);

        balance = new LeaveBalance
        {
            EmployeeId = employeeId,
            LeaveTypeId = leaveTypeId,
            Year = year,
            Entitled = entitlement
        };

        db.LeaveBalances.Add(balance);
        return balance;
    }

    private async Task<string> NextReferenceAsync(CancellationToken ct)
    {
        var year = clock.Today.Year;
        var prefix = $"LV-{year % 100:D2}-";

        // Counting is adequate here: leave volume is low, and the unique index
        // on Reference is what actually guarantees correctness under a race.
        var count = await db.LeaveRequests
            .IgnoreQueryFilters()
            .CountAsync(r => r.Reference.StartsWith(prefix), ct);

        return prefix + (count + 1).ToString().PadLeft(4, '0');
    }
}
