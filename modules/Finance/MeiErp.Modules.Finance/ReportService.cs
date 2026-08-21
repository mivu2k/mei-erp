using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Finance;

/// <summary>
/// The financial statements.
///
/// Every one of these reads posted vouchers only. A draft is not in the books,
/// and a report that included drafts would disagree with itself the moment
/// somebody abandoned one.
/// </summary>
public interface IFinanceReports
{
    Task<TrialBalance> TrialBalanceAsync(DateOnly asAt, CancellationToken ct = default);
    Task<IncomeStatement> IncomeStatementAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<BalanceSheet> BalanceSheetAsync(DateOnly asAt, CancellationToken ct = default);
    Task<AccountLedger> LedgerAsync(int accountId, DateOnly from, DateOnly to, CancellationToken ct = default);
}

public sealed record TrialBalanceRow(
    string Code, string Name, AccountType Type, decimal Debit, decimal Credit);

public sealed record TrialBalance(
    DateOnly AsAt, IReadOnlyList<TrialBalanceRow> Rows, decimal TotalDebit, decimal TotalCredit)
{
    /// <summary>
    /// If this is ever false, something has written to the ledger without going
    /// through a balanced voucher - which the design is meant to make impossible.
    /// The screen shows it rather than hiding it.
    /// </summary>
    public bool IsBalanced => TotalDebit == TotalCredit;
}

public sealed record StatementLine(string Code, string Name, decimal Amount);

public sealed record IncomeStatement(
    DateOnly From, DateOnly To,
    IReadOnlyList<StatementLine> Income, decimal TotalIncome,
    IReadOnlyList<StatementLine> Expenses, decimal TotalExpenses)
{
    public decimal NetProfit => TotalIncome - TotalExpenses;
}

public sealed record BalanceSheet(
    DateOnly AsAt,
    IReadOnlyList<StatementLine> Assets, decimal TotalAssets,
    IReadOnlyList<StatementLine> Liabilities, decimal TotalLiabilities,
    IReadOnlyList<StatementLine> Equity, decimal TotalEquity,
    decimal RetainedThisPeriod)
{
    /// <summary>Liabilities plus equity plus what has been earned but not yet closed out.</summary>
    public decimal TotalFunding => TotalLiabilities + TotalEquity + RetainedThisPeriod;

    public bool IsBalanced => TotalAssets == TotalFunding;
}

public sealed record LedgerRow(
    DateOnly Date, string VoucherNumber, string Narration,
    decimal Debit, decimal Credit, decimal RunningBalance,
    string? PersonName, string ContraAccounts);

public sealed record AccountLedger(
    string Code, string Name, DateOnly From, DateOnly To,
    decimal OpeningBalance, IReadOnlyList<LedgerRow> Rows, decimal ClosingBalance);

public sealed class FinanceReports(FinanceDbContext db) : IFinanceReports
{
    public async Task<TrialBalance> TrialBalanceAsync(DateOnly asAt, CancellationToken ct = default)
    {
        var totals = await db.VoucherLines
            .Where(l => l.Voucher!.Status == VoucherStatus.Posted && l.Voucher.Date <= asAt)
            .GroupBy(l => l.AccountId)
            .Select(g => new
            {
                AccountId = g.Key,
                Debit = g.Sum(l => l.Debit),
                Credit = g.Sum(l => l.Credit)
            })
            .ToListAsync(ct);

        var ids = totals.Select(t => t.AccountId).ToList();
        var accounts = await db.Accounts
            .Where(a => ids.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);

        var rows = new List<TrialBalanceRow>();

        foreach (var total in totals)
        {
            if (!accounts.TryGetValue(total.AccountId, out var account)) continue;

            var net = total.Debit - total.Credit;
            if (net == 0) continue;   // nets to nothing; showing it is noise

            // Each account appears on one side only - its net position - rather
            // than as gross turnover, which is what a trial balance means.
            rows.Add(new TrialBalanceRow(
                account.Code, account.Name, account.Type,
                net > 0 ? net : 0,
                net < 0 ? -net : 0));
        }

        rows = [.. rows.OrderBy(r => r.Code)];

        return new TrialBalance(asAt, rows, rows.Sum(r => r.Debit), rows.Sum(r => r.Credit));
    }

    public async Task<IncomeStatement> IncomeStatementAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var lines = await PeriodTotalsAsync(from, to, ct);

        // Income is credit-natured, so its natural balance is negative in signed
        // terms. Flipped here so the statement reads in positive numbers.
        var income = lines
            .Where(l => l.Type == AccountType.Income)
            .Select(l => new StatementLine(l.Code, l.Name, -l.Net))
            .Where(l => l.Amount != 0)
            .OrderBy(l => l.Code).ToList();

        var expenses = lines
            .Where(l => l.Type == AccountType.Expense)
            .Select(l => new StatementLine(l.Code, l.Name, l.Net))
            .Where(l => l.Amount != 0)
            .OrderBy(l => l.Code).ToList();

        return new IncomeStatement(
            from, to,
            income, income.Sum(l => l.Amount),
            expenses, expenses.Sum(l => l.Amount));
    }

    public async Task<BalanceSheet> BalanceSheetAsync(DateOnly asAt, CancellationToken ct = default)
    {
        // Everything up to the date, since a balance sheet is cumulative.
        var lines = await PeriodTotalsAsync(DateOnly.MinValue, asAt, ct);

        var assets = lines.Where(l => l.Type == AccountType.Asset)
            .Select(l => new StatementLine(l.Code, l.Name, l.Net))
            .Where(l => l.Amount != 0).OrderBy(l => l.Code).ToList();

        var liabilities = lines.Where(l => l.Type == AccountType.Liability)
            .Select(l => new StatementLine(l.Code, l.Name, -l.Net))
            .Where(l => l.Amount != 0).OrderBy(l => l.Code).ToList();

        var equity = lines.Where(l => l.Type == AccountType.Equity)
            .Select(l => new StatementLine(l.Code, l.Name, -l.Net))
            .Where(l => l.Amount != 0).OrderBy(l => l.Code).ToList();

        // Profit earned but not yet closed into equity. Without this the sheet
        // fails to balance for the whole of every year until close.
        var retained =
            -lines.Where(l => l.Type is AccountType.Income).Sum(l => l.Net)
            - lines.Where(l => l.Type is AccountType.Expense).Sum(l => l.Net);

        return new BalanceSheet(
            asAt,
            assets, assets.Sum(l => l.Amount),
            liabilities, liabilities.Sum(l => l.Amount),
            equity, equity.Sum(l => l.Amount),
            retained);
    }

    public async Task<AccountLedger> LedgerAsync(
        int accountId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var account = await db.Accounts.AsNoTracking().FirstAsync(a => a.Id == accountId, ct);

        var opening = await db.VoucherLines
            .Where(l => l.AccountId == accountId
                     && l.Voucher!.Status == VoucherStatus.Posted
                     && l.Voucher.Date < from)
            .SumAsync(l => l.Debit - l.Credit, ct);

        var entries = await db.VoucherLines
            .Where(l => l.AccountId == accountId
                     && l.Voucher!.Status == VoucherStatus.Posted
                     && l.Voucher.Date >= from && l.Voucher.Date <= to)
            .Select(l => new
            {
                l.Voucher!.Date,
                l.Voucher.Number,
                l.Voucher.Narration,
                LineNarration = l.Narration,
                l.Debit,
                l.Credit,
                l.PersonName,
                l.VoucherId,

                // The other side of the entry. The account's own name is on
                // every row and tells the reader nothing; what they want to
                // know is where the money went or came from.
                Contra = l.Voucher.Lines
                    .Where(o => o.AccountId != accountId)
                    .Select(o => o.AccountName)
                    .ToList()
            })
            .OrderBy(l => l.Date).ThenBy(l => l.VoucherId)
            .ToListAsync(ct);

        var rows = new List<LedgerRow>();
        var running = opening;

        foreach (var entry in entries)
        {
            running += entry.Debit - entry.Credit;

            var contra = entry.Contra.Count switch
            {
                0 => "",
                1 => entry.Contra[0],
                // A multi-line voucher has no single contra head, so it says so
                // rather than inventing one.
                _ => $"Split — {entry.Contra.Count} heads"
            };

            rows.Add(new LedgerRow(
                entry.Date, entry.Number,
                entry.LineNarration ?? entry.Narration,
                entry.Debit, entry.Credit, running,
                entry.PersonName, contra));
        }

        return new AccountLedger(
            account.Code, account.Name, from, to, opening, rows, running);
    }

    private async Task<List<(string Code, string Name, AccountType Type, decimal Net)>>
        PeriodTotalsAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var totals = await db.VoucherLines
            .Where(l => l.Voucher!.Status == VoucherStatus.Posted
                     && l.Voucher.Date >= from && l.Voucher.Date <= to)
            .GroupBy(l => l.AccountId)
            .Select(g => new { AccountId = g.Key, Net = g.Sum(l => l.Debit - l.Credit) })
            .ToListAsync(ct);

        var ids = totals.Select(t => t.AccountId).ToList();
        var accounts = await db.Accounts.Where(a => ids.Contains(a.Id)).ToListAsync(ct);

        return [.. from total in totals
                   join account in accounts on total.AccountId equals account.Id
                   select (account.Code, account.Name, account.Type, total.Net)];
    }
}
