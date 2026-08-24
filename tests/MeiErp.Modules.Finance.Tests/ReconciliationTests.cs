using MeiErp.Modules.Finance;
using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MeiErp.Modules.Finance.Tests;

/// <summary>
/// Reconciliation and year close — the two things that turn a running ledger
/// into a set of books somebody has signed off.
/// </summary>
[Collection("postgres")]
public sealed class ReconciliationTests : IAsyncLifetime
{
    private static readonly string BaseConnection =
        Environment.GetEnvironmentVariable("MEIERP_TEST_DB")
        ?? "Host=127.0.0.1;Username=meierp;Password=DevPassword1!;";

    private readonly string _database = $"mei_rec_{Guid.NewGuid():N}";
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 8, 31, 9, 0, 0, TimeSpan.Zero));
    private readonly TestUser _user = new("user-1", "Accountant");

    private bool _available;
    private int _bank, _sales, _rent, _retained;

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
            await db.EnsureAuditTableForTestsAsync();

            var bank = new Account { Code = "1210", Name = "Current account", Type = AccountType.Asset, IsPostable = true };
            var sales = new Account { Code = "4100", Name = "Sales", Type = AccountType.Income, IsPostable = true };
            var rent = new Account { Code = "5510", Name = "Rent", Type = AccountType.Expense, IsPostable = true };
            var retained = new Account { Code = "3200", Name = "Retained earnings", Type = AccountType.Equity, IsPostable = true };

            db.Accounts.AddRange(bank, sales, rent, retained);
            await db.SaveChangesAsync();

            _bank = bank.Id; _sales = sales.Id; _rent = rent.Id; _retained = retained.Id;
            _available = true;
        }
        catch (NpgsqlException) { _available = false; }
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

    private ReconciliationService NewRecon(FinanceDbContext db) =>
        new(db, new AccountService(db), _user, _clock);

    private YearEndService NewYearEnd(FinanceDbContext db) =>
        new(db, new VoucherService(db, _clock, _user), new FinanceReports(db), _user, _clock);

    private async Task PostAsync(
        FinanceDbContext db, DateOnly date, int debit, int credit, decimal amount, string narration)
    {
        var vouchers = new VoucherService(db, _clock, _user);

        var posted = await vouchers.PostSystemVoucherAsync(new SystemVoucher(
            VoucherType.Journal, date, narration,
            [
                new VoucherLineInput(debit, amount, 0, narration),
                new VoucherLineInput(credit, 0, amount, narration)
            ],
            "finance", "test", 0, "TEST"));

        Assert.True(posted.Ok, posted.Error);
    }

    // ---------- reconciliation ----------

    [SkippableFact]
    public async Task Starting_pulls_in_every_posted_entry_up_to_the_statement_date()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();

        await PostAsync(db, new DateOnly(2026, 8, 10), _bank, _sales, 50_000, "Sale");
        await PostAsync(db, new DateOnly(2026, 8, 20), _rent, _bank, 20_000, "Rent");

        // After the statement date - must not appear.
        await PostAsync(db, new DateOnly(2026, 9, 5), _bank, _sales, 99_000, "Next month");

        var recon = await NewRecon(db).StartAsync(_bank, new DateOnly(2026, 8, 31), 30_000);

        Assert.True(recon.Ok, recon.Error);
        Assert.Equal(2, recon.Value.Lines.Count);
        Assert.DoesNotContain(recon.Value.Lines, l => l.Narration == "Next month");
    }

    [SkippableFact]
    public async Task Everything_starts_unticked_so_the_ledger_looks_wrong_until_the_work_is_done()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        await PostAsync(db, new DateOnly(2026, 8, 10), _bank, _sales, 50_000, "Sale");

        var recon = await NewRecon(db).StartAsync(_bank, new DateOnly(2026, 8, 31), 50_000);

        // Ticking is the work. Starting everything ticked would let somebody
        // close a sheet without looking at it.
        Assert.All(recon.Value.Lines, l => Assert.False(l.IsCleared));
        Assert.Equal(50_000, recon.Value.Uncleared);
        Assert.Equal(0, recon.Value.Adjusted);
    }

    [SkippableFact]
    public async Task Ticking_everything_makes_the_adjusted_balance_agree()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewRecon(db);

        await PostAsync(db, new DateOnly(2026, 8, 10), _bank, _sales, 50_000, "Sale");
        await PostAsync(db, new DateOnly(2026, 8, 20), _rent, _bank, 20_000, "Rent");

        var recon = await service.StartAsync(_bank, new DateOnly(2026, 8, 31), 30_000);

        var ticks = recon.Value.Lines.ToDictionary(l => l.Id, _ => true);
        var updated = await service.SetClearedAsync(recon.Value.Id, ticks);

        Assert.True(updated.Value.IsReconciled);
        Assert.Equal(0, updated.Value.Difference);
    }

    [SkippableFact]
    public async Task An_uncleared_cheque_is_exactly_the_difference()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewRecon(db);

        await PostAsync(db, new DateOnly(2026, 8, 10), _bank, _sales, 50_000, "Sale");
        await PostAsync(db, new DateOnly(2026, 8, 28), _rent, _bank, 20_000, "Rent cheque");

        // The bank has seen the sale but not the cheque yet.
        var recon = await service.StartAsync(_bank, new DateOnly(2026, 8, 31), 50_000);

        var ticks = recon.Value.Lines.ToDictionary(
            l => l.Id, l => l.Narration != "Rent cheque");

        var updated = await service.SetClearedAsync(recon.Value.Id, ticks);

        // This is the whole point: the sheet names the cheque that has not been
        // presented, rather than just reporting a number that is out.
        Assert.Equal(-20_000, updated.Value.Uncleared);
        Assert.True(updated.Value.IsReconciled);

        var outstanding = Assert.Single(updated.Value.Lines, l => !l.IsCleared);
        Assert.Equal("Rent cheque", outstanding.Narration);
    }

    [SkippableFact]
    public async Task It_cannot_be_closed_while_it_still_differs()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewRecon(db);

        await PostAsync(db, new DateOnly(2026, 8, 10), _bank, _sales, 50_000, "Sale");

        // The statement says something else entirely and nothing is ticked.
        var recon = await service.StartAsync(_bank, new DateOnly(2026, 8, 31), 12_345);

        var result = await service.CloseAsync(recon.Value.Id);

        // Closing while it differs would record that the account agreed when it
        // did not - the one thing a reconciliation exists to prove.
        Assert.True(result.Failed);
        Assert.Equal("recon.not-balanced", result.Code);
    }

    [SkippableFact]
    public async Task A_closed_sheet_cannot_be_re_ticked()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewRecon(db);

        await PostAsync(db, new DateOnly(2026, 8, 10), _bank, _sales, 50_000, "Sale");

        var recon = await service.StartAsync(_bank, new DateOnly(2026, 8, 31), 50_000);
        await service.SetClearedAsync(recon.Value.Id, recon.Value.Lines.ToDictionary(l => l.Id, _ => true));
        await service.CloseAsync(recon.Value.Id);

        db.ChangeTracker.Clear();

        var result = await service.SetClearedAsync(
            recon.Value.Id, recon.Value.Lines.ToDictionary(l => l.Id, _ => false));

        // A closed sheet is the evidence the account agreed on a date.
        Assert.True(result.Failed);
        Assert.Equal("recon.closed", result.Code);
    }

    [SkippableFact]
    public async Task An_entry_cleared_last_month_does_not_come_back()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewRecon(db);

        await PostAsync(db, new DateOnly(2026, 7, 10), _bank, _sales, 50_000, "July sale");

        var july = await service.StartAsync(_bank, new DateOnly(2026, 7, 31), 50_000);
        await service.SetClearedAsync(july.Value.Id, july.Value.Lines.ToDictionary(l => l.Id, _ => true));
        await service.CloseAsync(july.Value.Id);

        await PostAsync(db, new DateOnly(2026, 8, 10), _bank, _sales, 30_000, "August sale");

        db.ChangeTracker.Clear();
        var august = await service.StartAsync(_bank, new DateOnly(2026, 8, 31), 80_000);

        // Settled is settled. Re-listing it would make every month's sheet
        // longer than the last until it was unusable.
        var line = Assert.Single(august.Value.Lines);
        Assert.Equal("August sale", line.Narration);
    }

    [SkippableFact]
    public async Task Two_reconciliations_cannot_be_open_on_one_account()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewRecon(db);

        await PostAsync(db, new DateOnly(2026, 8, 10), _bank, _sales, 50_000, "Sale");
        await service.StartAsync(_bank, new DateOnly(2026, 8, 31), 50_000);

        var second = await service.StartAsync(_bank, new DateOnly(2026, 9, 30), 50_000);

        // Both would tick the same entries and neither would be trustworthy.
        Assert.True(second.Failed);
        Assert.Equal("recon.already-open", second.Code);
    }

    // ---------- year close ----------

    [SkippableFact]
    public async Task Closing_a_year_sweeps_profit_into_retained_earnings()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();

        await PostAsync(db, new DateOnly(2026, 3, 1), _bank, _sales, 500_000, "Sales");
        await PostAsync(db, new DateOnly(2026, 4, 1), _rent, _bank, 200_000, "Rent");

        var year = new FiscalYear
        {
            Name = "FY 2025-26",
            StartDate = new DateOnly(2025, 7, 1),
            EndDate = new DateOnly(2026, 6, 30)
        };
        db.FiscalYears.Add(year);
        await db.SaveChangesAsync();

        var closed = await NewYearEnd(db).CloseAsync(year.Id);
        Assert.True(closed.Ok, closed.Error);

        db.ChangeTracker.Clear();
        var accounts = new AccountService(db);

        // Income and expense are back to nil so next year starts from zero,
        // and the 300,000 profit is sitting in equity.
        Assert.Equal(0, await accounts.BalanceAsync(_sales));
        Assert.Equal(0, await accounts.BalanceAsync(_rent));
        Assert.Equal(-300_000, await accounts.BalanceAsync(_retained));
    }

    [SkippableFact]
    public async Task Nothing_can_be_posted_into_a_year_once_it_is_closed()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();

        await PostAsync(db, new DateOnly(2026, 3, 1), _bank, _sales, 500_000, "Sales");

        var year = new FiscalYear
        {
            Name = "FY 2025-26",
            StartDate = new DateOnly(2025, 7, 1),
            EndDate = new DateOnly(2026, 6, 30)
        };
        db.FiscalYears.Add(year);
        await db.SaveChangesAsync();

        await NewYearEnd(db).CloseAsync(year.Id);

        db.ChangeTracker.Clear();
        var vouchers = new VoucherService(db, _clock, _user);

        var late = await vouchers.PostSystemVoucherAsync(new SystemVoucher(
            VoucherType.Journal, new DateOnly(2026, 5, 1), "Too late",
            [
                new VoucherLineInput(_bank, 1_000, 0),
                new VoucherLineInput(_sales, 0, 1_000)
            ],
            "finance", "test", 0, "LATE"));

        // A signed-off trial balance has to stay signed off.
        Assert.True(late.Failed);
        Assert.Equal("voucher.period-closed", late.Code);
    }

    [SkippableFact]
    public async Task A_year_with_open_drafts_cannot_be_closed()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var vouchers = new VoucherService(db, _clock, _user);

        await vouchers.SaveDraftAsync(new VoucherInput(
            null, VoucherType.Journal, new DateOnly(2026, 3, 1), "Unfinished",
            [
                new VoucherLineInput(_bank, 1_000, 0),
                new VoucherLineInput(_sales, 0, 1_000)
            ]));

        var year = new FiscalYear
        {
            Name = "FY 2025-26",
            StartDate = new DateOnly(2025, 7, 1),
            EndDate = new DateOnly(2026, 6, 30)
        };
        db.FiscalYears.Add(year);
        await db.SaveChangesAsync();

        var result = await NewYearEnd(db).CloseAsync(year.Id);

        // The draft could never be posted afterwards, so it would be silently
        // lost. Better to refuse and say so.
        Assert.True(result.Failed);
        Assert.Equal("year.open-drafts", result.Code);
    }

    [SkippableFact]
    public async Task A_year_cannot_be_closed_twice()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();

        await PostAsync(db, new DateOnly(2026, 3, 1), _bank, _sales, 100_000, "Sales");

        var year = new FiscalYear
        {
            Name = "FY 2025-26",
            StartDate = new DateOnly(2025, 7, 1),
            EndDate = new DateOnly(2026, 6, 30)
        };
        db.FiscalYears.Add(year);
        await db.SaveChangesAsync();

        var service = NewYearEnd(db);
        await service.CloseAsync(year.Id);

        db.ChangeTracker.Clear();
        var again = await service.CloseAsync(year.Id);

        // A second sweep would move the profit into equity all over again.
        Assert.True(again.Failed);
        Assert.Equal("year.closed", again.Code);
    }

    [SkippableFact]
    public async Task Overlapping_fiscal_years_are_refused()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewYearEnd(db);

        await service.SaveYearAsync(new FiscalYear
        {
            Name = "FY 2025-26",
            StartDate = new DateOnly(2025, 7, 1),
            EndDate = new DateOnly(2026, 6, 30)
        });

        var overlapping = await service.SaveYearAsync(new FiscalYear
        {
            Name = "Overlaps",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31)
        });

        // Otherwise "which period is this in" has two answers, and the
        // closed-period check depends on row order.
        Assert.True(overlapping.Failed);
        Assert.Equal("year.overlap", overlapping.Code);
    }

    [SkippableFact]
    public async Task A_year_with_no_trading_still_locks()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();

        var year = new FiscalYear
        {
            Name = "FY 2024-25",
            StartDate = new DateOnly(2024, 7, 1),
            EndDate = new DateOnly(2025, 6, 30)
        };
        db.FiscalYears.Add(year);
        await db.SaveChangesAsync();

        var closed = await NewYearEnd(db).CloseAsync(year.Id);

        Assert.True(closed.Ok, closed.Error);
        Assert.True(closed.Value.IsClosed);

        // Nothing to sweep, so no closing voucher was invented.
        Assert.Null(closed.Value.ClosingVoucherId);
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
