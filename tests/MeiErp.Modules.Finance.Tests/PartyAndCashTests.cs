using MeiErp.Modules.Finance;
using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MeiErp.Modules.Finance.Tests;

/// <summary>
/// Third parties, petty cash and utilities — all of which move money, and none
/// of which write a balance directly. Every movement here becomes a real
/// voucher, which is what keeps the books balanced.
/// </summary>
[Collection("postgres")]
public sealed class PartyAndCashTests : IAsyncLifetime
{
    private static readonly string BaseConnection =
        Environment.GetEnvironmentVariable("MEIERP_TEST_DB")
        ?? "Host=127.0.0.1;Username=meierp;Password=DevPassword1!;";

    private readonly string _database = $"mei_pc_{Guid.NewGuid():N}";
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero));
    private readonly TestUser _user = new("user-1", "Accountant");

    private bool _available;
    private int _cash, _bank, _expense;

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

            // The headings a party's or box's own account is hung beneath.
            var receivables = new Account { Code = "1600", Name = "Receivables", Type = AccountType.Asset, IsPostable = false };
            var payables = new Account { Code = "2100", Name = "Payables", Type = AccountType.Liability, IsPostable = false };
            var cash = new Account { Code = "1100", Name = "Cash in hand", Type = AccountType.Asset, IsPostable = true };
            var bank = new Account { Code = "1210", Name = "Current account", Type = AccountType.Asset, IsPostable = true };
            var expense = new Account { Code = "5530", Name = "Stationery", Type = AccountType.Expense, IsPostable = true };

            db.Accounts.AddRange(receivables, payables, cash, bank, expense);
            await db.SaveChangesAsync();

            _cash = cash.Id; _bank = bank.Id; _expense = expense.Id;
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

    private ThirdPartyService NewParties(FinanceDbContext db) =>
        new(db, new VoucherService(db, _clock, _user), new AccountService(db));

    private PettyCashService NewPetty(FinanceDbContext db) =>
        new(db, new VoucherService(db, _clock, _user), new AccountService(db));

    private UtilityService NewUtilities(FinanceDbContext db) =>
        new(db, new VoucherService(db, _clock, _user), _clock);

    // ---------- third parties ----------

    [SkippableFact]
    public async Task A_party_gets_an_account_under_the_side_it_belongs_to()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var parties = NewParties(db);

        var owed = await parties.SaveAsync(new ThirdParty { Name = "Mr A", Side = ThirdPartySide.Payable });
        var owing = await parties.SaveAsync(new ThirdParty { Name = "Mr B", Side = ThirdPartySide.Receivable });

        Assert.True(owed.Ok, owed.Error);
        Assert.True(owing.Ok, owing.Error);

        var payableAccount = await db.Accounts.FirstAsync(a => a.Id == owed.Value.AccountId);
        var receivableAccount = await db.Accounts.FirstAsync(a => a.Id == owing.Value.AccountId);

        // The side is the only thing a party decides, and this is what it decides.
        Assert.StartsWith("2100-", payableAccount.Code);
        Assert.Equal(AccountType.Liability, payableAccount.Type);

        Assert.StartsWith("1600-", receivableAccount.Code);
        Assert.Equal(AccountType.Asset, receivableAccount.Type);
    }

    [SkippableFact]
    public async Task Recording_money_posts_a_real_voucher()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var parties = NewParties(db);

        var party = await parties.SaveAsync(new ThirdParty { Name = "Mr A", Side = ThirdPartySide.Payable });

        var posted = await parties.RecordAsync(
            party.Value.Id, PartyMovement.Received, 100_000, _cash, _clock.Today, "Borrowed");

        Assert.True(posted.Ok, posted.Error);

        db.ChangeTracker.Clear();
        var voucher = await db.Vouchers.Include(v => v.Lines).FirstAsync();

        // Nothing writes a party balance directly - it is a balanced entry like
        // everything else.
        Assert.True(voucher.IsBalanced);
        Assert.Equal(VoucherStatus.Posted, voucher.Status);
        Assert.Equal(100_000, voucher.Lines.Single(l => l.AccountId == _cash).Debit);
    }

    [SkippableFact]
    public async Task A_payable_balance_reads_as_what_is_still_owed()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var parties = NewParties(db);

        var party = await parties.SaveAsync(new ThirdParty { Name = "Mr A", Side = ThirdPartySide.Payable });

        // Took 100,000 from them, paid 30,000 back.
        await parties.RecordAsync(party.Value.Id, PartyMovement.Received, 100_000, _cash, _clock.Today, null);
        await parties.RecordAsync(party.Value.Id, PartyMovement.Paid, 30_000, _cash, _clock.Today, null);

        var balance = await parties.BalanceAsync(party.Value.Id);

        // Signed per side, so it reads as outstanding either way rather than
        // as a negative number the reader has to interpret.
        Assert.Equal(70_000, balance);
    }

    [SkippableFact]
    public async Task A_receivable_balance_reads_the_same_way_round()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var parties = NewParties(db);

        var party = await parties.SaveAsync(new ThirdParty { Name = "Mr B", Side = ThirdPartySide.Receivable });

        // Gave them 50,000, got 20,000 back.
        await parties.RecordAsync(party.Value.Id, PartyMovement.Paid, 50_000, _cash, _clock.Today, null);
        await parties.RecordAsync(party.Value.Id, PartyMovement.Received, 20_000, _cash, _clock.Today, null);

        Assert.Equal(30_000, await parties.BalanceAsync(party.Value.Id));
    }

    [SkippableFact]
    public async Task A_statement_names_the_contra_head_not_the_party_itself()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var parties = NewParties(db);

        var party = await parties.SaveAsync(new ThirdParty { Name = "Mr A", Side = ThirdPartySide.Payable });
        await parties.RecordAsync(party.Value.Id, PartyMovement.Received, 100_000, _bank, _clock.Today, "Loan in");

        var statement = await parties.StatementAsync(
            party.Value.Id, _clock.Today.AddDays(-1), _clock.Today);

        var row = Assert.Single(statement!.Rows);

        // The party's own name is on every row and says nothing; which head the
        // money moved through is the useful column.
        Assert.Equal("Current account", row.ContraName);
        Assert.Equal(100_000, row.Received);
        Assert.Equal(100_000, row.Balance);
    }

    [SkippableFact]
    public async Task A_split_entry_says_so_rather_than_naming_one_head()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var parties = NewParties(db);
        var vouchers = new VoucherService(db, _clock, _user);

        var party = await parties.SaveAsync(new ThirdParty { Name = "Mr A", Side = ThirdPartySide.Payable });

        await vouchers.PostSystemVoucherAsync(new SystemVoucher(
            VoucherType.Receipt, _clock.Today, "Split receipt",
            [
                new VoucherLineInput(_cash, 40_000, 0),
                new VoucherLineInput(_bank, 60_000, 0),
                new VoucherLineInput(party.Value.AccountId, 0, 100_000)
            ],
            "finance", "finance.third-party", party.Value.Id, "Mr A"));

        var statement = await parties.StatementAsync(
            party.Value.Id, _clock.Today.AddDays(-1), _clock.Today);

        Assert.Equal("Split — 2 heads", statement!.Rows.Single().ContraName);
    }

    [SkippableFact]
    public async Task The_last_head_is_remembered_but_not_for_a_split()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var parties = NewParties(db);
        var vouchers = new VoucherService(db, _clock, _user);

        var party = await parties.SaveAsync(new ThirdParty { Name = "Mr A", Side = ThirdPartySide.Payable });
        await parties.RecordAsync(party.Value.Id, PartyMovement.Received, 10_000, _bank, _clock.Today, null);

        // Defaults the payment dialog to however they were last settled.
        Assert.Equal(_bank, await parties.LastCashHeadAsync(party.Value.Id));

        await vouchers.PostSystemVoucherAsync(new SystemVoucher(
            VoucherType.Receipt, _clock.Today.AddDays(1), "Split",
            [
                new VoucherLineInput(_cash, 5_000, 0),
                new VoucherLineInput(_bank, 5_000, 0),
                new VoucherLineInput(party.Value.AccountId, 0, 10_000)
            ],
            "finance", "finance.third-party", party.Value.Id, "Mr A"));

        // Null after a split: there is no single head worth reusing, and
        // guessing one would put the next payment somewhere arbitrary.
        Assert.Null(await parties.LastCashHeadAsync(party.Value.Id));
    }

    [SkippableFact]
    public async Task A_party_with_entries_cannot_change_sides()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var parties = NewParties(db);

        var party = await parties.SaveAsync(new ThirdParty { Name = "Mr A", Side = ThirdPartySide.Payable });
        await parties.RecordAsync(party.Value.Id, PartyMovement.Received, 10_000, _cash, _clock.Today, null);

        db.ChangeTracker.Clear();
        var edit = await parties.GetAsync(party.Value.Id);
        edit!.Side = ThirdPartySide.Receivable;

        var result = await parties.SaveAsync(edit);

        // Moving the account between Receivables and Payables would silently
        // restate every balance sheet this party ever appeared on.
        Assert.True(result.Failed);
        Assert.Equal("party.side-locked", result.Code);
    }

    // ---------- petty cash ----------

    [SkippableFact]
    public async Task A_petty_cash_box_cannot_be_overspent()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var petty = NewPetty(db);

        var box = await petty.SaveBoxAsync(new PettyCashBox
        {
            Name = "Front desk", CustodianName = "Rafiq", FloatAmount = 20_000, IsActive = true
        });
        Assert.True(box.Ok, box.Error);

        await petty.TopUpAsync(box.Value.Id, 20_000, _bank, _clock.Today, null);

        var result = await petty.SpendAsync(new PettyCashEntry
        {
            BoxId = box.Value.Id, Date = _clock.Today,
            Amount = 25_000, Description = "Too much", ExpenseAccountId = _expense
        });

        // There is either cash in the tin or there is not. A negative balance
        // means the record has stopped describing the tin.
        Assert.True(result.Failed);
        Assert.Equal("petty.insufficient", result.Code);
    }

    [SkippableFact]
    public async Task A_box_cannot_be_topped_past_its_float()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var petty = NewPetty(db);

        var box = await petty.SaveBoxAsync(new PettyCashBox
        {
            Name = "Front desk", CustodianName = "Rafiq", FloatAmount = 20_000, IsActive = true
        });

        await petty.TopUpAsync(box.Value.Id, 20_000, _bank, _clock.Today, null);

        var result = await petty.TopUpAsync(box.Value.Id, 5_000, _bank, _clock.Today, null);

        // The float is the whole point: topping past it means "what should be in
        // the tin" stops having an answer.
        Assert.True(result.Failed);
        Assert.Equal("petty.over-float", result.Code);
    }

    [SkippableFact]
    public async Task Spending_draws_the_box_down_and_posts_the_expense()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var petty = NewPetty(db);

        var box = await petty.SaveBoxAsync(new PettyCashBox
        {
            Name = "Front desk", CustodianName = "Rafiq", FloatAmount = 20_000, IsActive = true
        });

        await petty.TopUpAsync(box.Value.Id, 20_000, _bank, _clock.Today, null);
        await petty.SpendAsync(new PettyCashEntry
        {
            BoxId = box.Value.Id, Date = _clock.Today,
            Amount = 3_500, Description = "Stationery", ExpenseAccountId = _expense
        });

        Assert.Equal(16_500, await petty.BalanceAsync(box.Value.Id));

        db.ChangeTracker.Clear();
        var expense = await new AccountService(db).BalanceAsync(_expense);
        Assert.Equal(3_500, expense);
    }

    [SkippableFact]
    public async Task A_float_needs_a_custodian()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();

        var result = await NewPetty(db).SaveBoxAsync(new PettyCashBox
        {
            Name = "Nobody's box", FloatAmount = 10_000
        });

        // An unattributed float is one nobody is answerable for.
        Assert.True(result.Failed);
        Assert.Equal("petty.no-custodian", result.Code);
    }

    // ---------- utilities ----------

    [SkippableFact]
    public async Task The_same_month_cannot_be_billed_twice_on_one_connection()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var utilities = NewUtilities(db);

        var connection = await utilities.SaveConnectionAsync(new UtilityConnection
        {
            Name = "Head office electricity", Kind = UtilityKind.Electricity,
            ExpenseAccountId = _expense, IsActive = true
        });

        var august = new DateOnly(2026, 8, 1);

        Assert.True((await utilities.SaveBillAsync(new UtilityBill
        {
            ConnectionId = connection.Value.Id, BillingMonth = august,
            Amount = 45_000, IssuedOn = august, DueOn = august.AddDays(14)
        })).Ok);

        var duplicate = await utilities.SaveBillAsync(new UtilityBill
        {
            ConnectionId = connection.Value.Id, BillingMonth = august,
            Amount = 45_000, IssuedOn = august, DueOn = august.AddDays(14)
        });

        // Entering August twice would quietly double the cost of running it.
        Assert.True(duplicate.Failed);
        Assert.Equal("utility.duplicate-month", duplicate.Code);
    }

    [SkippableFact]
    public async Task A_paid_bill_cannot_be_edited()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var utilities = NewUtilities(db);

        var connection = await utilities.SaveConnectionAsync(new UtilityConnection
        {
            Name = "Gas", Kind = UtilityKind.Gas, ExpenseAccountId = _expense, IsActive = true
        });

        var bill = await utilities.SaveBillAsync(new UtilityBill
        {
            ConnectionId = connection.Value.Id,
            BillingMonth = new DateOnly(2026, 8, 1),
            Amount = 12_000, IssuedOn = _clock.Today, DueOn = _clock.Today.AddDays(10)
        });

        await utilities.PayAsync(bill.Value.Id, _bank, _clock.Today);

        db.ChangeTracker.Clear();
        var bills = await utilities.BillsAsync(null, false);
        var paid = bills.Single();
        paid.Amount = 99_000;

        var result = await utilities.SaveBillAsync(paid);

        // Changing the amount after payment would leave the voucher and the bill
        // disagreeing about what was actually paid.
        Assert.True(result.Failed);
        Assert.Equal("utility.already-paid", result.Code);
    }

    [SkippableFact]
    public async Task Paying_a_bill_charges_the_connection_s_own_head()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var utilities = NewUtilities(db);

        var connection = await utilities.SaveConnectionAsync(new UtilityConnection
        {
            Name = "Water", Kind = UtilityKind.Water, ExpenseAccountId = _expense, IsActive = true
        });

        var bill = await utilities.SaveBillAsync(new UtilityBill
        {
            ConnectionId = connection.Value.Id,
            BillingMonth = new DateOnly(2026, 8, 1),
            Amount = 8_000, IssuedOn = _clock.Today, DueOn = _clock.Today.AddDays(10)
        });

        var paid = await utilities.PayAsync(bill.Value.Id, _bank, _clock.Today);
        Assert.True(paid.Ok, paid.Error);

        db.ChangeTracker.Clear();

        Assert.Equal(8_000, await new AccountService(db).BalanceAsync(_expense));
        Assert.Equal(-8_000, await new AccountService(db).BalanceAsync(_bank));
        Assert.True((await utilities.BillsAsync(null, false)).Single().IsPaid);
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
