using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Finance;

/// <summary>
/// Agreeing a bank account's ledger balance with its statement.
///
/// The point is not the closing figure — it is the list of entries that have
/// not cleared yet. A reconciliation that only stores a number tells you
/// nothing next month about which cheque never got banked.
/// </summary>
public class Reconciliation : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    public int AccountId { get; set; }
    public Account? Account { get; set; }

    /// <summary>The statement date being reconciled to.</summary>
    public DateOnly StatementDate { get; set; }

    /// <summary>What the bank says the balance is.</summary>
    public decimal StatementBalance { get; set; }

    /// <summary>What the ledger said at the moment it was reconciled.</summary>
    public decimal LedgerBalance { get; set; }

    public bool IsClosed { get; set; }
    public DateTime? ClosedUtc { get; set; }
    public string? ClosedBy { get; set; }

    public string? Notes { get; set; }

    public List<ReconciliationLine> Lines { get; set; } = [];

    /// <summary>Ledger entries the bank has not shown yet.</summary>
    public decimal Uncleared => Lines.Where(l => !l.IsCleared).Sum(l => l.Signed);

    /// <summary>
    /// The ledger balance adjusted for what has not cleared. This is the figure
    /// that should equal the statement.
    /// </summary>
    public decimal Adjusted => LedgerBalance - Uncleared;

    public decimal Difference => Adjusted - StatementBalance;

    /// <summary>
    /// Compared exactly. A reconciliation that is "close enough" is not one.
    /// </summary>
    public bool IsReconciled => Difference == 0;
}

/// <summary>One ledger entry, ticked or not ticked against the statement.</summary>
public class ReconciliationLine : Entity
{
    public int ReconciliationId { get; set; }
    public Reconciliation? Reconciliation { get; set; }

    public int VoucherLineId { get; set; }

    /// <summary>Snapshotted so the sheet still reads after a voucher is renamed.</summary>
    public DateOnly Date { get; set; }
    public string VoucherNumber { get; set; } = "";
    public string Narration { get; set; } = "";

    public decimal Debit { get; set; }
    public decimal Credit { get; set; }

    /// <summary>Ticked when it appears on the statement.</summary>
    public bool IsCleared { get; set; }

    public decimal Signed => Debit - Credit;
}

public interface IReconciliationService
{
    Task<IReadOnlyList<Reconciliation>> ListAsync(int? accountId, CancellationToken ct = default);
    Task<Reconciliation?> GetAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Starts a reconciliation, pulling in every posted entry on the account up
    /// to the statement date that a previous reconciliation has not already
    /// cleared.
    /// </summary>
    Task<Result<Reconciliation>> StartAsync(
        int accountId, DateOnly statementDate, decimal statementBalance, CancellationToken ct = default);

    Task<Result<Reconciliation>> SetClearedAsync(
        int id, IReadOnlyDictionary<int, bool> cleared, CancellationToken ct = default);

    /// <summary>Closes it. Refused unless it actually agrees.</summary>
    Task<Result<Reconciliation>> CloseAsync(int id, CancellationToken ct = default);
}

public sealed class ReconciliationService(
    FinanceDbContext db, IAccountService accounts, ICurrentUser currentUser, IClock clock)
    : IReconciliationService
{
    public async Task<IReadOnlyList<Reconciliation>> ListAsync(
        int? accountId, CancellationToken ct = default)
    {
        var query = db.Reconciliations.AsNoTracking()
            .Include(r => r.Account)
            .Include(r => r.Lines)
            .AsQueryable();

        if (accountId is not null) query = query.Where(r => r.AccountId == accountId);

        return await query.OrderByDescending(r => r.StatementDate).Take(120).ToListAsync(ct);
    }

    public Task<Reconciliation?> GetAsync(int id, CancellationToken ct = default) =>
        db.Reconciliations
          .Include(r => r.Account)
          .Include(r => r.Lines)
          .FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<Result<Reconciliation>> StartAsync(
        int accountId, DateOnly statementDate, decimal statementBalance,
        CancellationToken ct = default)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null) return Result.Fail<Reconciliation>("That account no longer exists.", "recon.no-account");

        var openAlready = await db.Reconciliations
            .AnyAsync(r => r.AccountId == accountId && !r.IsClosed, ct);

        if (openAlready)
        {
            // Two open sheets on one account would each tick the same entries,
            // and neither would be trustworthy.
            return Result.Fail<Reconciliation>(
                $"{account.Name} already has a reconciliation open. Finish or delete that one first.",
                "recon.already-open");
        }

        var duplicate = await db.Reconciliations
            .AnyAsync(r => r.AccountId == accountId && r.StatementDate == statementDate, ct);

        if (duplicate)
        {
            return Result.Fail<Reconciliation>(
                $"{account.Name} has already been reconciled to {statementDate:d MMM yyyy}.",
                "recon.duplicate");
        }

        // Anything a previous reconciliation ticked is settled and does not come
        // back; only what is genuinely still outstanding is pulled in.
        var alreadyCleared = await db.ReconciliationLines
            .Where(l => l.Reconciliation!.AccountId == accountId
                     && l.Reconciliation.IsClosed
                     && l.IsCleared)
            .Select(l => l.VoucherLineId)
            .ToListAsync(ct);

        var entries = await db.VoucherLines
            .Where(l => l.AccountId == accountId
                     && l.Voucher!.Status == VoucherStatus.Posted
                     && l.Voucher.Date <= statementDate
                     && !alreadyCleared.Contains(l.Id))
            .Select(l => new
            {
                l.Id,
                l.Voucher!.Date,
                l.Voucher.Number,
                l.Voucher.Narration,
                LineNarration = l.Narration,
                l.Debit,
                l.Credit
            })
            .OrderBy(l => l.Date).ThenBy(l => l.Id)
            .ToListAsync(ct);

        var reconciliation = new Reconciliation
        {
            AccountId = accountId,
            StatementDate = statementDate,
            StatementBalance = statementBalance,
            LedgerBalance = await accounts.BalanceAsync(accountId, statementDate, ct)
        };

        foreach (var entry in entries)
        {
            reconciliation.Lines.Add(new ReconciliationLine
            {
                VoucherLineId = entry.Id,
                Date = entry.Date,
                VoucherNumber = entry.Number,
                Narration = entry.LineNarration ?? entry.Narration,
                Debit = entry.Debit,
                Credit = entry.Credit,

                // Everything starts unticked. Ticking is the work.
                IsCleared = false
            });
        }

        db.Reconciliations.Add(reconciliation);
        await db.SaveChangesAsync(ct);

        return Result.Success(reconciliation);
    }

    public async Task<Result<Reconciliation>> SetClearedAsync(
        int id, IReadOnlyDictionary<int, bool> cleared, CancellationToken ct = default)
    {
        var reconciliation = await db.Reconciliations
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (reconciliation is null)
            return Result.Fail<Reconciliation>("That reconciliation no longer exists.", "recon.not-found");

        var closed = db.Entry(reconciliation).OriginalValues.GetValue<bool>(nameof(Reconciliation.IsClosed));

        if (closed)
        {
            // A closed sheet is the evidence that the account agreed on a date.
            // Editing it afterwards destroys that.
            return Result.Fail<Reconciliation>(
                "This reconciliation is closed. Start a new one for the next statement.",
                "recon.closed");
        }

        foreach (var line in reconciliation.Lines)
        {
            if (cleared.TryGetValue(line.Id, out var tick)) line.IsCleared = tick;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(reconciliation);
    }

    public async Task<Result<Reconciliation>> CloseAsync(int id, CancellationToken ct = default)
    {
        var reconciliation = await db.Reconciliations
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (reconciliation is null)
            return Result.Fail<Reconciliation>("That reconciliation no longer exists.", "recon.not-found");

        var closed = db.Entry(reconciliation).OriginalValues.GetValue<bool>(nameof(Reconciliation.IsClosed));
        if (closed) return Result.Fail<Reconciliation>("This is already closed.", "recon.closed");

        if (!reconciliation.IsReconciled)
        {
            // Closing while it still differs would record that the account
            // agreed when it did not — which is the one thing a reconciliation
            // exists to prove.
            return Result.Fail<Reconciliation>(
                $"This is still out by {reconciliation.Difference:N2}. " +
                "Tick the entries that appear on the statement, or find what is missing.",
                "recon.not-balanced");
        }

        reconciliation.IsClosed = true;
        reconciliation.ClosedUtc = clock.UtcNow;
        reconciliation.ClosedBy = currentUser.UserId;

        await db.SaveChangesAsync(ct);
        return Result.Success(reconciliation);
    }
}

/// <summary>
/// Closing a fiscal year: income and expense are swept into retained earnings
/// and the period is locked.
/// </summary>
public interface IYearEndService
{
    Task<IReadOnlyList<FiscalYear>> YearsAsync(CancellationToken ct = default);
    Task<Result<FiscalYear>> SaveYearAsync(FiscalYear year, CancellationToken ct = default);

    /// <summary>
    /// Sweeps the year's income and expense into retained earnings and locks
    /// it, so nothing can be posted into it afterwards.
    /// </summary>
    Task<Result<FiscalYear>> CloseAsync(int yearId, CancellationToken ct = default);
}

public sealed class YearEndService(
    FinanceDbContext db, IVoucherService vouchers, IFinanceReports reports,
    ICurrentUser currentUser, IClock clock) : IYearEndService
{
    private const string RetainedEarningsCode = "3200";

    public async Task<IReadOnlyList<FiscalYear>> YearsAsync(CancellationToken ct = default) =>
        await db.FiscalYears.AsNoTracking().OrderByDescending(y => y.StartDate).ToListAsync(ct);

    public async Task<Result<FiscalYear>> SaveYearAsync(
        FiscalYear year, CancellationToken ct = default)
    {
        if (year.EndDate <= year.StartDate)
            return Result.Fail<FiscalYear>("The year ends before it starts.", "year.bad-dates");

        var overlapping = await db.FiscalYears
            .AnyAsync(y => y.Id != year.Id
                        && y.StartDate <= year.EndDate
                        && y.EndDate >= year.StartDate, ct);

        if (overlapping)
        {
            // Overlapping years make "which period is this in" unanswerable,
            // and the closed-period check would then depend on row order.
            return Result.Fail<FiscalYear>(
                "That overlaps an existing fiscal year.", "year.overlap");
        }

        if (year.Id == 0)
        {
            db.FiscalYears.Add(year);
        }
        else
        {
            var existing = await db.FiscalYears.FirstOrDefaultAsync(y => y.Id == year.Id, ct);
            if (existing is null) return Result.Fail<FiscalYear>("That year no longer exists.", "year.not-found");

            var wasClosed = db.Entry(existing).OriginalValues.GetValue<bool>(nameof(FiscalYear.IsClosed));
            if (wasClosed) return Result.Fail<FiscalYear>("A closed year cannot be edited.", "year.closed");

            db.Entry(existing).CurrentValues.SetValues(year);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(year);
    }

    public async Task<Result<FiscalYear>> CloseAsync(int yearId, CancellationToken ct = default)
    {
        var year = await db.FiscalYears.FirstOrDefaultAsync(y => y.Id == yearId, ct);
        if (year is null) return Result.Fail<FiscalYear>("That year no longer exists.", "year.not-found");

        var wasClosed = db.Entry(year).OriginalValues.GetValue<bool>(nameof(FiscalYear.IsClosed));
        if (wasClosed) return Result.Fail<FiscalYear>("This year is already closed.", "year.closed");

        var drafts = await db.Vouchers
            .CountAsync(v => v.Status == VoucherStatus.Draft
                          && v.Date >= year.StartDate && v.Date <= year.EndDate, ct);

        if (drafts > 0)
        {
            // A draft left behind can never be posted once the year locks, so
            // it would be silently lost.
            return Result.Fail<FiscalYear>(
                $"{drafts} draft {(drafts == 1 ? "voucher is" : "vouchers are")} still open in this year. " +
                "Post or delete them first — once the year is closed they can never be posted.",
                "year.open-drafts");
        }

        var retained = await db.Accounts.FirstOrDefaultAsync(a => a.Code == RetainedEarningsCode, ct);
        if (retained is null)
            return Result.Fail<FiscalYear>($"The {RetainedEarningsCode} retained earnings head is missing.", "year.no-retained");

        var statement = await reports.IncomeStatementAsync(year.StartDate, year.EndDate, ct);

        var lines = new List<VoucherLineInput>();

        // Every income head is debited back to nil, every expense head credited
        // back to nil, and the net lands in retained earnings. Next year starts
        // from zero, which is the whole point of closing.
        foreach (var income in statement.Income.Where(l => l.Amount != 0))
        {
            var account = await db.Accounts.FirstAsync(a => a.Code == income.Code, ct);
            lines.Add(new VoucherLineInput(account.Id, income.Amount, 0, "Year-end close"));
        }

        foreach (var expense in statement.Expenses.Where(l => l.Amount != 0))
        {
            var account = await db.Accounts.FirstAsync(a => a.Code == expense.Code, ct);
            lines.Add(new VoucherLineInput(account.Id, 0, expense.Amount, "Year-end close"));
        }

        if (lines.Count == 0)
        {
            // Nothing traded. Lock it anyway - an empty year is still closed.
            year.IsClosed = true;
            year.ClosedUtc = clock.UtcNow;
            year.ClosedBy = currentUser.UserId;
            await db.SaveChangesAsync(ct);
            return Result.Success(year);
        }

        var profit = statement.NetProfit;

        lines.Add(profit >= 0
            ? new VoucherLineInput(retained.Id, 0, profit, "Profit for the year")
            : new VoucherLineInput(retained.Id, -profit, 0, "Loss for the year"));

        // Dated the last day of the year, so it falls inside the period it
        // closes rather than in the next one.
        var posted = await vouchers.PostSystemVoucherAsync(new SystemVoucher(
            Type: VoucherType.Closing,
            Date: year.EndDate,
            Narration: $"Closing {year.Name}",
            Lines: lines,
            Module: FinanceModule.Key,
            DocumentType: "finance.year-close",
            DocumentId: year.Id,
            DocumentReference: year.Name), ct);

        if (posted.Failed) return Result.Fail<FiscalYear>(posted.Error!, posted.Code);

        // Locked only after the closing entry is safely in, so a failure part
        // way through does not leave a year that cannot be closed or posted to.
        year.IsClosed = true;
        year.ClosedUtc = clock.UtcNow;
        year.ClosedBy = currentUser.UserId;
        year.ClosingVoucherId = posted.Value.Id;

        await db.SaveChangesAsync(ct);
        return Result.Success(year);
    }
}
