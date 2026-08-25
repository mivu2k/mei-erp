using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Finance;

public interface IPayrollService
{
    Task<IReadOnlyList<PayrollEmployee>> EmployeesAsync(bool includeLeavers, CancellationToken ct = default);
    Task<Result<PayrollEmployee>> SaveEmployeeAsync(PayrollEmployee employee, CancellationToken ct = default);

    Task<IReadOnlyList<PayComponent>> ComponentsAsync(CancellationToken ct = default);
    Task<Result<PayComponent>> SaveComponentAsync(PayComponent component, CancellationToken ct = default);

    Task<SalaryStructure?> CurrentStructureAsync(int employeeId, DateOnly on, CancellationToken ct = default);

    /// <summary>
    /// Saves a new structure and closes the previous one the day before it
    /// starts, rather than editing it — a payslip issued last month has to keep
    /// explaining itself.
    /// </summary>
    Task<Result<SalaryStructure>> SaveStructureAsync(
        int employeeId, DateOnly effectiveFrom, decimal basic,
        IReadOnlyList<StructureLineInput> lines, CancellationToken ct = default);

    Task<IReadOnlyList<PayrollRun>> RunsAsync(CancellationToken ct = default);
    Task<PayrollRun?> GetRunAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Builds or rebuilds a month's payslips from the current structures.
    /// Only a draft can be generated, so an approved run cannot silently change.
    /// </summary>
    Task<Result<PayrollRun>> GenerateAsync(
        DateOnly month, IReadOnlyDictionary<int, decimal>? daysWorked = null,
        CancellationToken ct = default);

    Task<Result<PayrollRun>> ApproveAsync(int runId, CancellationToken ct = default);

    /// <summary>
    /// Posts the run as one aggregated voucher and marks it paid.
    /// </summary>
    Task<Result<PayrollRun>> PayAsync(
        int runId, int cashAccountId, DateOnly date, CancellationToken ct = default);

    Task<IReadOnlyList<Payslip>> PayslipsForAsync(string userId, CancellationToken ct = default);
}

/// <summary>Attendance seam implemented by the composing host, so Finance never references HR.</summary>
public interface IPayrollAttendanceProvider
{
    Task<IReadOnlyDictionary<string, decimal>> PayableDaysByEmployeeCodeAsync(
        DateOnly month, CancellationToken ct = default);
}

public sealed record StructureLineInput(int ComponentId, decimal Amount);

public sealed class PayrollService(
    FinanceDbContext db, IVoucherService vouchers, IClock clock,
    IPayrollAttendanceProvider? attendance = null) : IPayrollService
{
    private const string DefaultSalaryHead = "5210";
    private const string SalariesPayableHead = "2200";
    private const string AdvanceHead = "1700";

    // ---------------------------------------------------------------- people

    public async Task<IReadOnlyList<PayrollEmployee>> EmployeesAsync(
        bool includeLeavers, CancellationToken ct = default)
    {
        var query = db.PayrollEmployees.AsNoTracking().AsQueryable();
        if (!includeLeavers) query = query.Where(e => e.IsActive);
        return await query.OrderBy(e => e.FullName).ToListAsync(ct);
    }

    public async Task<Result<PayrollEmployee>> SaveEmployeeAsync(
        PayrollEmployee employee, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(employee.FullName))
            return Result.Fail<PayrollEmployee>("An employee needs a name.", "payroll.no-name");

        if (string.IsNullOrWhiteSpace(employee.Code))
            return Result.Fail<PayrollEmployee>("An employee needs a staff number.", "payroll.no-code");

        var taken = await db.PayrollEmployees
            .AnyAsync(e => e.Code == employee.Code && e.Id != employee.Id, ct);

        if (taken)
        {
            // Two people on one staff number would merge their payslips.
            return Result.Fail<PayrollEmployee>(
                $"Staff number {employee.Code} already belongs to somebody else.",
                "payroll.duplicate-code");
        }

        if (employee.Id == 0)
        {
            db.PayrollEmployees.Add(employee);
        }
        else
        {
            var existing = await db.PayrollEmployees.FirstOrDefaultAsync(e => e.Id == employee.Id, ct);
            if (existing is null) return Result.Fail<PayrollEmployee>("That employee no longer exists.", "payroll.not-found");
            db.Entry(existing).CurrentValues.SetValues(employee);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(employee);
    }

    public async Task<IReadOnlyList<PayComponent>> ComponentsAsync(CancellationToken ct = default) =>
        await db.PayComponents.AsNoTracking()
            .Include(c => c.Account)
            .Where(c => c.IsActive)
            .OrderBy(c => c.Kind).ThenBy(c => c.Name)
            .ToListAsync(ct);

    public async Task<Result<PayComponent>> SaveComponentAsync(
        PayComponent component, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(component.Name))
            return Result.Fail<PayComponent>("A component needs a name.", "payroll.no-component-name");

        if (component.Id == 0)
        {
            db.PayComponents.Add(component);
        }
        else
        {
            var existing = await db.PayComponents.FirstOrDefaultAsync(c => c.Id == component.Id, ct);
            if (existing is null) return Result.Fail<PayComponent>("That component no longer exists.", "payroll.no-component");
            db.Entry(existing).CurrentValues.SetValues(component);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(component);
    }

    // ---------------------------------------------------------------- structures

    public Task<SalaryStructure?> CurrentStructureAsync(
        int employeeId, DateOnly on, CancellationToken ct = default) =>
        db.SalaryStructures
          .Include(s => s.Lines).ThenInclude(l => l.Component)
          .Where(s => s.EmployeeId == employeeId
                   && s.EffectiveFrom <= on
                   && (s.EffectiveTo == null || s.EffectiveTo >= on))
          .OrderByDescending(s => s.EffectiveFrom)
          .FirstOrDefaultAsync(ct);

    public async Task<Result<SalaryStructure>> SaveStructureAsync(
        int employeeId, DateOnly effectiveFrom, decimal basic,
        IReadOnlyList<StructureLineInput> lines, CancellationToken ct = default)
    {
        var employee = await db.PayrollEmployees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null) return Result.Fail<SalaryStructure>("That employee no longer exists.", "payroll.not-found");

        if (basic <= 0)
            return Result.Fail<SalaryStructure>("A basic salary has to be more than nothing.", "payroll.bad-basic");

        // Close whatever was in force the day before this one starts, rather
        // than editing it. Payslips already issued against it must keep
        // explaining themselves.
        var previous = await db.SalaryStructures
            .Where(s => s.EmployeeId == employeeId && s.EffectiveTo == null
                     && s.EffectiveFrom < effectiveFrom)
            .OrderByDescending(s => s.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

        if (previous is not null) previous.EffectiveTo = effectiveFrom.AddDays(-1);

        var structure = new SalaryStructure
        {
            EmployeeId = employeeId,
            EffectiveFrom = effectiveFrom,
            BasicSalary = basic
        };

        foreach (var line in lines.Where(l => l.Amount != 0))
            structure.Lines.Add(new SalaryLine { ComponentId = line.ComponentId, Amount = line.Amount });

        db.SalaryStructures.Add(structure);
        await db.SaveChangesAsync(ct);

        return Result.Success(structure);
    }

    // ---------------------------------------------------------------- runs

    public async Task<IReadOnlyList<PayrollRun>> RunsAsync(CancellationToken ct = default) =>
        await db.PayrollRuns.AsNoTracking()
            .Include(r => r.Payslips).ThenInclude(p => p.Lines)
            .OrderByDescending(r => r.Month)
            .Take(60)
            .ToListAsync(ct);

    public Task<PayrollRun?> GetRunAsync(int id, CancellationToken ct = default) =>
        db.PayrollRuns
          .Include(r => r.Payslips).ThenInclude(p => p.Lines)
          .FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<Result<PayrollRun>> GenerateAsync(
        DateOnly month, IReadOnlyDictionary<int, decimal>? daysWorked = null,
        CancellationToken ct = default)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(first.Year, first.Month);
        var last = first.AddDays(daysInMonth - 1);

        var run = await db.PayrollRuns
            .Include(r => r.Payslips).ThenInclude(p => p.Lines)
            .FirstOrDefaultAsync(r => r.Month == first, ct);

        if (run is not null)
        {
            var status = db.Entry(run).OriginalValues.GetValue<PayrollRunStatus>(nameof(PayrollRun.Status));

            if (status is not PayrollRunStatus.Draft)
            {
                // Regenerating an approved run would silently restate what
                // people were told they would be paid.
                return Result.Fail<PayrollRun>(
                    $"The run for {first:MMMM yyyy} has been {status.ToString().ToLowerInvariant()} " +
                    "and cannot be rebuilt.",
                    "payroll.not-draft");
            }

            foreach (var payslip in run.Payslips)
                db.PayslipLines.RemoveRange(payslip.Lines);

            db.Payslips.RemoveRange(run.Payslips);
            run.Payslips.Clear();
        }
        else
        {
            run = new PayrollRun
            {
                Reference = $"PR-{first:yyyy-MM}",
                Month = first,
                Status = PayrollRunStatus.Draft
            };
            db.PayrollRuns.Add(run);
        }

        var employees = await db.PayrollEmployees
            .Where(e => e.IsActive)
            .ToListAsync(ct);

        var attendanceDays = daysWorked is null && attendance is not null
            ? await attendance.PayableDaysByEmployeeCodeAsync(first, ct)
            : null;

        var defaultHead = await db.Accounts.FirstOrDefaultAsync(a => a.Code == DefaultSalaryHead, ct);
        if (defaultHead is null)
            return Result.Fail<PayrollRun>($"The {DefaultSalaryHead} salary head is missing.", "payroll.no-salary-head");

        foreach (var employee in employees)
        {
            // Somebody who joined mid-month or left mid-month is only paid for
            // the part they were employed.
            if (!employee.IsEmployedOn(last) && !employee.IsEmployedOn(first)) continue;

            var structure = await CurrentStructureAsync(employee.Id, last, ct);
            if (structure is null) continue;   // nobody has set their pay yet

            var worked = daysWorked?.GetValueOrDefault(employee.Id)
                ?? attendanceDays?.GetValueOrDefault(employee.Code)
                ?? daysInMonth;
            worked = Math.Clamp(worked, 0, daysInMonth);

            var payslip = new Payslip
            {
                EmployeeId = employee.Id,
                EmployeeCode = employee.Code,
                EmployeeName = employee.FullName,
                UserId = employee.UserId,
                DepartmentId = employee.DepartmentId,
                BasicSalary = structure.BasicSalary,
                DaysWorked = worked,
                DaysInMonth = daysInMonth,
                SalaryAccountId = employee.SalaryAccountId ?? defaultHead.Id
            };

            var factor = daysInMonth == 0 ? 1 : worked / daysInMonth;

            // Basic is always pro-rated by attendance.
            payslip.Lines.Add(new PayslipLine
            {
                Name = "Basic salary",
                Kind = PayComponentKind.Earning,
                Amount = Round(structure.BasicSalary * factor),
                AccountId = payslip.SalaryAccountId
            });

            foreach (var line in structure.Lines)
            {
                var component = line.Component
                    ?? await db.PayComponents.FirstOrDefaultAsync(c => c.Id == line.ComponentId, ct);

                if (component is null || !component.IsActive) continue;

                // Allowances are pro-rated only if the component says so: a
                // fixed phone allowance is not reduced by a day off.
                var amount = component.Kind is PayComponentKind.Earning && component.ProRateOnAttendance
                    ? Round(line.Amount * factor)
                    : Round(line.Amount);

                payslip.Lines.Add(new PayslipLine
                {
                    ComponentId = component.Id,

                    // Snapshotted, so renaming the component later cannot
                    // rewrite a payslip somebody already received.
                    Name = component.Name,
                    Kind = component.Kind,
                    Amount = amount,
                    AccountId = component.AccountId ?? payslip.SalaryAccountId
                });
            }

            await AddAdvanceRecoveryAsync(payslip, employee, run.Month, ct);

            run.Payslips.Add(payslip);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(run);
    }

    /// <summary>
    /// Recovers outstanding advances the employee agreed to have taken from
    /// their salary — capped at what is left after everything else, so net pay
    /// can never go negative. The shortfall simply rolls to next month.
    /// </summary>
    private async Task AddAdvanceRecoveryAsync(
        Payslip payslip, PayrollEmployee employee, DateOnly month, CancellationToken ct)
    {
        if (employee.UserId is null) return;

        var recoverable = await db.PaymentRequests
            .Where(a => a.Kind == PaymentRequestKind.Advance
                     && a.RequestedByUserId == employee.UserId
                     && a.Status == PaymentRequestStatus.Settled
                     && a.DifferenceHandling == DifferenceHandling.RecoverFromPayroll)
            .ToListAsync(ct);

        var outstanding = recoverable
            .Where(a => a.OutstandingDifference > 0)
            .OrderBy(a => a.Id)
            .ToList();

        if (outstanding.Count == 0) return;

        var earnings = payslip.Lines.Where(l => l.Kind == PayComponentKind.Earning).Sum(l => l.Amount);
        var deductions = payslip.Lines.Where(l => l.Kind == PayComponentKind.Deduction).Sum(l => l.Amount);
        var headroom = earnings - deductions;

        foreach (var advance in outstanding)
        {
            if (headroom <= 0) break;

            var take = Math.Min(advance.OutstandingDifference, headroom);

            payslip.Lines.Add(new PayslipLine
            {
                Name = $"Advance recovery — {advance.Reference}",
                Kind = PayComponentKind.Deduction,
                Amount = Round(take),
                AdvanceId = advance.Id
            });

            headroom -= take;
        }

        await AddSalaryAdvanceRecoveryAsync(payslip, employee, month, headroom, ct);
    }

    /// <summary>
    /// Instalments due on a salary advance, taken in the same run and under the
    /// same cap: an advance is not a reason for somebody to be paid nothing.
    /// </summary>
    private async Task AddSalaryAdvanceRecoveryAsync(
        Payslip payslip, PayrollEmployee employee, DateOnly month, decimal headroom, CancellationToken ct)
    {
        if (employee.UserId is null || headroom <= 0) return;

        var advances = await db.EmployeeAdvances
            .Include(a => a.Installments)
            .Where(a => a.PersonId == employee.UserId
                     && (a.Status == EmployeeAdvanceStatus.Disbursed
                      || a.Status == EmployeeAdvanceStatus.Repaying))
            .OrderBy(a => a.Id)
            .ToListAsync(ct);

        foreach (var advance in advances)
        {
            if (headroom <= 0) break;

            // Everything already due, so a month the run was skipped is caught
            // up rather than quietly written off.
            var owing = advance.Installments
                .Where(i => i.Status != InstallmentStatus.Paid && i.DueDate <= month)
                .Sum(i => i.Outstanding);

            if (owing <= 0) continue;

            var take = Math.Min(Math.Min(owing, advance.OutstandingBalance), headroom);
            if (take <= 0) continue;

            payslip.Lines.Add(new PayslipLine
            {
                Name = $"Salary advance — {advance.Reference}",
                Kind = PayComponentKind.Deduction,
                Amount = Round(take),

                // Credited straight to their advance head, so the debt comes
                // down as the deduction is made. Left to the default it would
                // land on salaries payable and the advance would never clear.
                AccountId = advance.AdvanceAccountId,
                EmployeeAdvanceId = advance.Id
            });

            headroom -= take;
        }
    }

    public async Task<Result<PayrollRun>> ApproveAsync(int runId, CancellationToken ct = default)
    {
        var run = await db.PayrollRuns
            .Include(r => r.Payslips).ThenInclude(p => p.Lines)
            .FirstOrDefaultAsync(r => r.Id == runId, ct);

        if (run is null) return Result.Fail<PayrollRun>("That run no longer exists.", "payroll.no-run");

        var status = db.Entry(run).OriginalValues.GetValue<PayrollRunStatus>(nameof(PayrollRun.Status));

        if (status is not PayrollRunStatus.Draft)
            return Result.Fail<PayrollRun>("This run has already been approved.", "payroll.not-draft");

        if (run.Payslips.Count == 0)
            return Result.Fail<PayrollRun>("This run has no payslips in it.", "payroll.empty");

        var negative = run.Payslips.FirstOrDefault(p => p.Net < 0);
        if (negative is not null)
        {
            // Should be impossible given the recovery cap, but a negative net
            // means somebody would be asked to pay to come to work.
            return Result.Fail<PayrollRun>(
                $"{negative.EmployeeName} has a negative net pay of {negative.Net:N2}. " +
                "Check their deductions.",
                "payroll.negative-net");
        }

        run.Status = PayrollRunStatus.Approved;
        run.ApprovedUtc = clock.UtcNow;

        await db.SaveChangesAsync(ct);
        return Result.Success(run);
    }

    public async Task<Result<PayrollRun>> PayAsync(
        int runId, int cashAccountId, DateOnly date, CancellationToken ct = default)
    {
        var run = await db.PayrollRuns
            .Include(r => r.Payslips).ThenInclude(p => p.Lines)
            .FirstOrDefaultAsync(r => r.Id == runId, ct);

        if (run is null) return Result.Fail<PayrollRun>("That run no longer exists.", "payroll.no-run");

        var status = db.Entry(run).OriginalValues.GetValue<PayrollRunStatus>(nameof(PayrollRun.Status));

        if (status is not PayrollRunStatus.Approved)
            return Result.Fail<PayrollRun>("Only an approved run can be paid.", "payroll.not-approved");

        var advanceHead = await db.Accounts.FirstOrDefaultAsync(a => a.Code == AdvanceHead, ct);
        if (advanceHead is null)
            return Result.Fail<PayrollRun>($"The {AdvanceHead} employee advances head is missing.", "payroll.no-advance-head");

        var lines = new List<VoucherLineInput>();

        // One voucher for the whole run, aggregated by head. A voucher per
        // person would bury the ledger under hundreds of near-identical entries
        // every month, and the payslips already carry the per-person detail.
        //
        // Deliberately no PersonId on these lines: the aggregate is not
        // traceable to one person, and pretending otherwise would make the
        // ledger's person filter lie.
        foreach (var group in run.Payslips
                     .SelectMany(p => p.Lines.Where(l => l.Kind == PayComponentKind.Earning)
                                             .Select(l => new { l.AccountId, l.Amount }))
                     .GroupBy(l => l.AccountId))
        {
            var total = group.Sum(l => l.Amount);
            if (total == 0) continue;

            lines.Add(new VoucherLineInput(
                group.Key ?? 0, total, 0, $"Payroll {run.Month:MMMM yyyy}"));
        }

        // Advance recoveries credit the advance account, which is what actually
        // clears what people owe. ApplyRecovery below posts nothing further -
        // doing both would take the money twice.
        var advanceRecovery = run.Payslips
            .SelectMany(p => p.Lines)
            .Where(l => l.AdvanceId is not null)
            .Sum(l => l.Amount);

        if (advanceRecovery > 0)
        {
            lines.Add(new VoucherLineInput(
                advanceHead.Id, 0, advanceRecovery, $"Advance recovery {run.Month:MMMM yyyy}"));
        }

        // Every other deduction is owed to somebody: tax, a fund, a loan.
        foreach (var group in run.Payslips
                     .SelectMany(p => p.Lines.Where(l => l.Kind == PayComponentKind.Deduction
                                                      && l.AdvanceId is null))
                     .GroupBy(l => l.AccountId))
        {
            var total = group.Sum(l => l.Amount);
            if (total == 0) continue;

            var head = group.Key ?? await PayableHeadIdAsync(ct);
            lines.Add(new VoucherLineInput(head, 0, total, $"Payroll deductions {run.Month:MMMM yyyy}"));
        }

        lines.Add(new VoucherLineInput(
            cashAccountId, 0, run.TotalNet, $"Net pay {run.Month:MMMM yyyy}"));

        var posted = await vouchers.PostSystemVoucherAsync(new SystemVoucher(
            Type: VoucherType.Payment,
            Date: date,
            Narration: $"Payroll for {run.Month:MMMM yyyy} — {run.Payslips.Count} " +
                       $"{(run.Payslips.Count == 1 ? "person" : "people")}",
            Lines: lines,
            Module: FinanceModule.Key,
            DocumentType: "finance.payroll",
            DocumentId: run.Id,
            DocumentReference: run.Reference), ct);

        if (posted.Failed) return Result.Fail<PayrollRun>(posted.Error!, posted.Code);

        await ApplyRecoveryAsync(run, ct);
        await ApplySalaryAdvanceRecoveryAsync(run, ct);

        run.Status = PayrollRunStatus.Paid;
        run.VoucherId = posted.Value.Id;
        run.PaidUtc = clock.UtcNow;

        await db.SaveChangesAsync(ct);
        return Result.Success(run);
    }

    /// <summary>
    /// Marks recovered advances as cleared. Posts nothing: the payroll voucher
    /// already credited the advance account, and double-posting here is the
    /// easy bug in this whole flow.
    /// </summary>
    private async Task ApplyRecoveryAsync(PayrollRun run, CancellationToken ct)
    {
        var recoveries = run.Payslips
            .SelectMany(p => p.Lines)
            .Where(l => l.AdvanceId is not null)
            .GroupBy(l => l.AdvanceId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));

        if (recoveries.Count == 0) return;

        var ids = recoveries.Keys.ToList();
        var advances = await db.PaymentRequests.Where(a => ids.Contains(a.Id)).ToListAsync(ct);

        foreach (var advance in advances)
            advance.ClearedDifference += recoveries[advance.Id];
    }

    /// <summary>
    /// Marks salary-advance instalments as repaid. Posts nothing, for the same
    /// reason: the payroll voucher already credited the advance head, and
    /// posting again here would collect the money twice.
    /// </summary>
    private async Task ApplySalaryAdvanceRecoveryAsync(PayrollRun run, CancellationToken ct)
    {
        var recoveries = run.Payslips
            .SelectMany(p => p.Lines)
            .Where(l => l.EmployeeAdvanceId is not null)
            .GroupBy(l => l.EmployeeAdvanceId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));

        if (recoveries.Count == 0) return;

        var ids = recoveries.Keys.ToList();

        var advances = await db.EmployeeAdvances
            .Include(a => a.Installments)
            .Where(a => ids.Contains(a.Id))
            .ToListAsync(ct);

        foreach (var advance in advances)
        {
            EmployeeAdvanceService.ApplyToSchedule(
                advance, recoveries[advance.Id], run.Month, voucherId: null);
        }
    }

    private async Task<int> PayableHeadIdAsync(CancellationToken ct)
    {
        var head = await db.Accounts.FirstOrDefaultAsync(a => a.Code == SalariesPayableHead, ct);
        return head?.Id ?? 0;
    }

    public async Task<IReadOnlyList<Payslip>> PayslipsForAsync(
        string userId, CancellationToken ct = default) =>
        await db.Payslips.AsNoTracking()
            .Include(p => p.Lines)
            .Include(p => p.Run)
            .Where(p => p.UserId == userId && p.Run!.Status == PayrollRunStatus.Paid)
            .OrderByDescending(p => p.Run!.Month)
            .Take(36)
            .ToListAsync(ct);

    /// <summary>Money is rounded to the rupee on a payslip; fractions of a rupee are not paid.</summary>
    private static decimal Round(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);
}
