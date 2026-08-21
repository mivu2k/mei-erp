using MeiErp.Modules.Finance;
using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MeiErp.Modules.Finance.Tests;

/// <summary>
/// The ledger rules.
///
/// This is the code where a bug costs real money and stays wrong quietly, so
/// the guarantees are pinned rather than assumed: entries balance, posted
/// entries are immutable, closed periods stay closed, and an account with
/// history can never be deleted out from under it.
/// </summary>
[Collection("postgres")]
public sealed class LedgerTests : IAsyncLifetime
{
    private static readonly string BaseConnection =
        Environment.GetEnvironmentVariable("MEIERP_TEST_DB")
        ?? "Host=127.0.0.1;Username=meierp;Password=DevPassword1!;";

    private readonly string _database = $"mei_fin_{Guid.NewGuid():N}";
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero));
    private readonly TestUser _user = new("user-1", "Accountant");

    private bool _available;
    private int _cash, _salaries, _sales, _heading;

    private string Connection => BaseConnection + $"Database={_database};";

    public async Task InitializeAsync()
    {
        try
        {
            await using (var admin = new DbContext(new DbContextOptionsBuilder()
                .UseNpgsql(BaseConnection + "Database=postgres;").Options))
            {
                await admin.Database.ExecuteSqlRawAsync($"CREATE DATABASE \"{_database}\";");
            }

            await using var db = NewDb();
            await db.Database.EnsureCreatedAsync();

            var cash = new Account { Code = "1100", Name = "Cash", Type = AccountType.Asset, IsPostable = true };
            var salaries = new Account { Code = "5210", Name = "Salaries", Type = AccountType.Expense, IsPostable = true };
            var sales = new Account { Code = "4100", Name = "Sales", Type = AccountType.Income, IsPostable = true };
            var heading = new Account { Code = "5000", Name = "Expenses", Type = AccountType.Expense, IsPostable = false };

            db.Accounts.AddRange(cash, salaries, sales, heading);
            await db.SaveChangesAsync();

            _cash = cash.Id; _salaries = salaries.Id; _sales = sales.Id; _heading = heading.Id;
            _available = true;
        }
        catch (NpgsqlException)
        {
            _available = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (!_available) return;
        try
        {
            await using var admin = new DbContext(new DbContextOptionsBuilder()
                .UseNpgsql(BaseConnection + "Database=postgres;").Options);
            await admin.Database.ExecuteSqlRawAsync($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE);");
        }
        catch { /* a stray throwaway database is harmless */ }
    }

    private FinanceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FinanceDbContext>().UseNpgsql(Connection).Options, _user, _clock);

    private VoucherService NewService(FinanceDbContext db) => new(db, _clock, _user);

    private VoucherInput Entry(decimal amount, DateOnly? date = null) => new(
        null, VoucherType.Journal, date ?? _clock.Today, "Salary payment",
        [
            new VoucherLineInput(_salaries, amount, 0),
            new VoucherLineInput(_cash, 0, amount)
        ]);

    // ---------- balancing ----------

    [SkippableFact]
    public async Task An_unbalanced_entry_cannot_be_posted()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var draft = await service.SaveDraftAsync(new VoucherInput(
            null, VoucherType.Journal, _clock.Today, "Wrong",
            [
                new VoucherLineInput(_salaries, 1000, 0),
                new VoucherLineInput(_cash, 0, 900)
            ]));
        Assert.True(draft.Ok, draft.Error);

        var posted = await service.PostAsync(draft.Value.Id);

        // The single guarantee the whole module exists to provide.
        Assert.True(posted.Failed);
        Assert.Equal("voucher.unbalanced", posted.Code);
    }

    [SkippableFact]
    public async Task A_module_cannot_push_an_unbalanced_entry_into_the_books()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var result = await service.PostSystemVoucherAsync(new SystemVoucher(
            VoucherType.Payment, _clock.Today, "From a buggy module",
            [
                new VoucherLineInput(_salaries, 500, 0),
                new VoucherLineInput(_cash, 0, 400)
            ],
            "inventory", "inventory.receipt", 1, "GR-001"));

        // One bad caller must not be able to corrupt the ledger.
        Assert.True(result.Failed);
        Assert.Equal("voucher.unbalanced", result.Code);
    }

    [SkippableFact]
    public async Task A_line_cannot_be_both_a_debit_and_a_credit()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var result = await service.SaveDraftAsync(new VoucherInput(
            null, VoucherType.Journal, _clock.Today, "Confused",
            [new VoucherLineInput(_salaries, 100, 100)]));

        Assert.True(result.Failed);
        Assert.Equal("voucher.both-sides", result.Code);
    }

    [SkippableFact]
    public async Task A_negative_amount_is_refused()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        // A negative debit is a credit wearing a disguise, and it makes every
        // total ambiguous.
        var result = await service.SaveDraftAsync(new VoucherInput(
            null, VoucherType.Journal, _clock.Today, "Negative",
            [new VoucherLineInput(_salaries, -100, 0)]));

        Assert.True(result.Failed);
        Assert.Equal("voucher.negative", result.Code);
    }

    [SkippableFact]
    public async Task A_heading_cannot_be_posted_to()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        // A parent with its own balance double-counts against its children.
        var result = await service.SaveDraftAsync(new VoucherInput(
            null, VoucherType.Journal, _clock.Today, "To a heading",
            [
                new VoucherLineInput(_heading, 100, 0),
                new VoucherLineInput(_cash, 0, 100)
            ]));

        Assert.True(result.Failed);
        Assert.Equal("voucher.not-postable", result.Code);
    }

    [SkippableFact]
    public async Task A_one_sided_entry_is_refused()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var draft = await service.SaveDraftAsync(new VoucherInput(
            null, VoucherType.Journal, _clock.Today, "One side only",
            [new VoucherLineInput(_salaries, 100, 0)]));

        var posted = await service.PostAsync(draft.Value.Id);

        Assert.True(posted.Failed);
        Assert.Equal("voucher.too-few-lines", posted.Code);
    }

    // ---------- immutability ----------

    [SkippableFact]
    public async Task A_posted_voucher_cannot_be_edited()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var draft = await service.SaveDraftAsync(Entry(1000));
        await service.PostAsync(draft.Value.Id);

        var edit = await service.SaveDraftAsync(new VoucherInput(
            draft.Value.Id, VoucherType.Journal, _clock.Today, "Changed my mind",
            [
                new VoucherLineInput(_salaries, 2000, 0),
                new VoucherLineInput(_cash, 0, 2000)
            ]));

        // Editing a posted entry makes every report printed before it a lie.
        Assert.True(edit.Failed);
        Assert.Equal("voucher.posted-immutable", edit.Code);
    }

    [SkippableFact]
    public async Task A_posted_voucher_cannot_be_deleted()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var draft = await service.SaveDraftAsync(Entry(1000));
        await service.PostAsync(draft.Value.Id);

        var deleted = await service.DeleteDraftAsync(draft.Value.Id);

        Assert.True(deleted.Failed);
        Assert.Equal("voucher.posted-immutable", deleted.Code);
    }

    [SkippableFact]
    public async Task Reversing_leaves_the_original_untouched_and_nets_to_nothing()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var draft = await service.SaveDraftAsync(Entry(1000));
        var posted = await service.PostAsync(draft.Value.Id);

        var reversal = await service.ReverseAsync(posted.Value.Id, "Wrong account");
        Assert.True(reversal.Ok, reversal.Error);

        db.ChangeTracker.Clear();

        var original = await db.Vouchers.Include(v => v.Lines)
            .FirstAsync(v => v.Id == posted.Value.Id);
        var contra = await db.Vouchers.Include(v => v.Lines)
            .FirstAsync(v => v.Id == reversal.Value.Id);

        // The original's own lines are exactly as they were.
        Assert.Equal(1000, original.Lines.Single(l => l.AccountId == _salaries).Debit);
        Assert.Equal(VoucherStatus.Reversed, original.Status);
        Assert.Equal(contra.Id, original.ReversedByVoucherId);

        // And the pair cancels out.
        Assert.Equal(1000, contra.Lines.Single(l => l.AccountId == _salaries).Credit);
        Assert.Equal(
            original.Lines.Sum(l => l.SignedAmount) + contra.Lines.Sum(l => l.SignedAmount), 0);
    }

    [SkippableFact]
    public async Task A_voucher_cannot_be_reversed_twice()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var draft = await service.SaveDraftAsync(Entry(1000));
        var posted = await service.PostAsync(draft.Value.Id);
        await service.ReverseAsync(posted.Value.Id, "First");

        var again = await service.ReverseAsync(posted.Value.Id, "Second");

        // Two reversals would leave the books out by the amount, in the
        // opposite direction.
        Assert.True(again.Failed);
        Assert.Equal("voucher.already-reversed", again.Code);
    }

    // ---------- closed periods ----------

    [SkippableFact]
    public async Task Nothing_can_be_posted_into_a_closed_year()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();

        db.FiscalYears.Add(new FiscalYear
        {
            Name = "FY 2025-26",
            StartDate = new DateOnly(2025, 7, 1),
            EndDate = new DateOnly(2026, 6, 30),
            IsClosed = true
        });
        await db.SaveChangesAsync();

        var service = NewService(db);

        var draft = await service.SaveDraftAsync(Entry(1000, new DateOnly(2026, 3, 15)));
        var posted = await service.PostAsync(draft.Value.Id);

        // A signed-off trial balance must stay signed off.
        Assert.True(posted.Failed);
        Assert.Equal("voucher.period-closed", posted.Code);
    }

    // ---------- reports ----------

    [SkippableFact]
    public async Task The_trial_balance_balances_and_ignores_drafts()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);
        var reports = new FinanceReports(db);

        var posted = await service.SaveDraftAsync(Entry(1000));
        await service.PostAsync(posted.Value.Id);

        // A draft is not in the books and must not appear anywhere.
        await service.SaveDraftAsync(Entry(9999));

        var trial = await reports.TrialBalanceAsync(_clock.Today);

        Assert.True(trial.IsBalanced);
        Assert.Equal(1000, trial.TotalDebit);
        Assert.Equal(1000, trial.TotalCredit);
        Assert.DoesNotContain(trial.Rows, r => r.Debit == 9999 || r.Credit == 9999);
    }

    [SkippableFact]
    public async Task The_income_statement_reads_in_positive_numbers()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);
        var reports = new FinanceReports(db);

        // Sale of 5,000 received in cash.
        var sale = await service.SaveDraftAsync(new VoucherInput(
            null, VoucherType.Receipt, _clock.Today, "Sale",
            [
                new VoucherLineInput(_cash, 5000, 0),
                new VoucherLineInput(_sales, 0, 5000)
            ]));
        await service.PostAsync(sale.Value.Id);

        var wages = await service.SaveDraftAsync(Entry(2000));
        await service.PostAsync(wages.Value.Id);

        var statement = await reports.IncomeStatementAsync(
            _clock.Today.AddDays(-30), _clock.Today);

        // Income is credit-natured; the statement flips it so a reader sees
        // 5,000 rather than -5,000.
        Assert.Equal(5000, statement.TotalIncome);
        Assert.Equal(2000, statement.TotalExpenses);
        Assert.Equal(3000, statement.NetProfit);
    }

    [SkippableFact]
    public async Task The_balance_sheet_balances_before_the_year_is_closed()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);
        var reports = new FinanceReports(db);

        var sale = await service.SaveDraftAsync(new VoucherInput(
            null, VoucherType.Receipt, _clock.Today, "Sale",
            [
                new VoucherLineInput(_cash, 5000, 0),
                new VoucherLineInput(_sales, 0, 5000)
            ]));
        await service.PostAsync(sale.Value.Id);

        var sheet = await reports.BalanceSheetAsync(_clock.Today);

        // Profit is earned but not yet closed into equity. Without carrying it
        // as retained-this-period, the sheet fails to balance for the whole of
        // every year until year-end.
        Assert.Equal(5000, sheet.TotalAssets);
        Assert.Equal(5000, sheet.RetainedThisPeriod);
        Assert.True(sheet.IsBalanced);
    }

    [SkippableFact]
    public async Task A_ledger_row_names_the_contra_head_not_its_own()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);
        var reports = new FinanceReports(db);

        var draft = await service.SaveDraftAsync(Entry(1000));
        await service.PostAsync(draft.Value.Id);

        var ledger = await reports.LedgerAsync(
            _cash, _clock.Today.AddDays(-1), _clock.Today);

        var row = Assert.Single(ledger.Rows);

        // "Cash" on every row of the cash ledger says nothing. Where the money
        // went is the useful column.
        Assert.Equal("Salaries", row.ContraAccounts);
        Assert.Equal(1000, row.Credit);
        Assert.Equal(-1000, row.ClosingOrRunning());
    }

    [SkippableFact]
    public async Task A_split_entry_says_so_rather_than_naming_one_head()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);
        var reports = new FinanceReports(db);

        var draft = await service.SaveDraftAsync(new VoucherInput(
            null, VoucherType.Payment, _clock.Today, "Two things at once",
            [
                new VoucherLineInput(_salaries, 600, 0),
                new VoucherLineInput(_sales, 400, 0),
                new VoucherLineInput(_cash, 0, 1000)
            ]));
        await service.PostAsync(draft.Value.Id);

        var ledger = await reports.LedgerAsync(_cash, _clock.Today.AddDays(-1), _clock.Today);

        // Inventing a single contra head for a multi-line voucher would be a lie.
        Assert.Equal("Split — 2 heads", ledger.Rows.Single().ContraAccounts);
    }

    // ---------- accounts ----------

    [SkippableFact]
    public async Task An_account_with_entries_cannot_be_deleted()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);
        var accounts = new AccountService(db);

        var draft = await service.SaveDraftAsync(Entry(1000));
        await service.PostAsync(draft.Value.Id);

        var deleted = await accounts.DeleteAsync(_salaries);

        // This is what keeps the soft-delete filter on Account from ever hiding
        // voucher lines and silently unbalancing the trial balance. See the note
        // in FinanceDbContext.
        Assert.True(deleted.Failed);
        Assert.Equal("account.has-entries", deleted.Code);
    }

    [SkippableFact]
    public async Task A_system_account_cannot_be_deleted()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();

        var retained = new Account
        {
            Code = "3200", Name = "Retained earnings",
            Type = AccountType.Equity, IsPostable = true, IsSystem = true
        };
        db.Accounts.Add(retained);
        await db.SaveChangesAsync();

        var accounts = new AccountService(db);
        var deleted = await accounts.DeleteAsync(retained.Id);

        Assert.True(deleted.Failed);
        Assert.Equal("account.system", deleted.Code);
    }

    private sealed class TestUser(string id, string name) : ICurrentUser
    {
        public string? UserId { get; } = id;
        public string? Name { get; } = name;
        public string? Email => null;
        public bool IsAuthenticated => true;
        public bool Can(string permission) => true;
        public bool InModule(string moduleKey) => true;
        public IReadOnlyCollection<string> Roles { get; } = [];
    }
}

internal static class LedgerRowExtensions
{
    /// <summary>Reads the running balance, named for what the assertion means.</summary>
    public static decimal ClosingOrRunning(this LedgerRow row) => row.RunningBalance;
}
