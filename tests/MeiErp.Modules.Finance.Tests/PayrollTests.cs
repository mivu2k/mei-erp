using MeiErp.Modules.Finance;
using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MeiErp.Modules.Finance.Tests;

/// <summary>
/// Payroll arithmetic, which is where a bug costs people real money and gets
/// noticed on pay day rather than in a report.
///
/// The two things most likely to be quietly wrong are pro-rating and advance
/// recovery — the second because it touches the ledger twice if you are not
/// careful, and taking somebody's money twice is not a rounding error.
/// </summary>
[Collection("postgres")]
public sealed class PayrollTests : IAsyncLifetime
{
    private static readonly string BaseConnection =
        Environment.GetEnvironmentVariable("MEIERP_TEST_DB")
        ?? "Host=127.0.0.1;Username=meierp;Password=DevPassword1!;";

    private readonly string _database = $"mei_pay_{Guid.NewGuid():N}";

    // August has 31 days, which makes the pro-rating arithmetic easy to read.
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 8, 31, 9, 0, 0, TimeSpan.Zero));
    private readonly TestUser _user = new("user-1", "Accountant");

    private bool _available;
    private int _cash, _salaryHead, _payable, _advanceHead, _taxHead;

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
            var salary = new Account { Code = "5210", Name = "Salaries and wages", Type = AccountType.Expense, IsPostable = true };
            var payable = new Account { Code = "2200", Name = "Salaries payable", Type = AccountType.Liability, IsPostable = true };
            var advances = new Account { Code = "1700", Name = "Employee advances", Type = AccountType.Asset, IsPostable = true };
            var tax = new Account { Code = "2300", Name = "Tax payable", Type = AccountType.Liability, IsPostable = true };

            db.Accounts.AddRange(cash, salary, payable, advances, tax);
            await db.SaveChangesAsync();

            _cash = cash.Id; _salaryHead = salary.Id; _payable = payable.Id;
            _advanceHead = advances.Id; _taxHead = tax.Id;
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

    private PayrollService NewService(FinanceDbContext db) =>
        new(db, new VoucherService(db, _clock, _user), _clock);

    private static readonly DateOnly August = new(2026, 8, 1);

    private async Task<PayrollEmployee> AddEmployeeAsync(
        PayrollService service, string code, string name, decimal basic,
        string? userId = null, IReadOnlyList<StructureLineInput>? lines = null)
    {
        var employee = await service.SaveEmployeeAsync(new PayrollEmployee
        {
            Code = code, FullName = name, UserId = userId,
            JoinedOn = new DateOnly(2020, 1, 1), IsActive = true
        });
        Assert.True(employee.Ok, employee.Error);

        var structure = await service.SaveStructureAsync(
            employee.Value.Id, new DateOnly(2020, 1, 1), basic, lines ?? []);
        Assert.True(structure.Ok, structure.Error);

        return employee.Value;
    }

    // ---------- pro-rating ----------

    [SkippableFact]
    public async Task A_full_month_pays_the_whole_basic()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        await AddEmployeeAsync(service, "E-1", "Rafiq", 31_000);

        var run = await service.GenerateAsync(August);
        Assert.True(run.Ok, run.Error);

        var payslip = run.Value.Payslips.Single();
        Assert.Equal(31_000, payslip.Gross);
        Assert.Equal(31_000, payslip.Net);
    }

    [SkippableFact]
    public async Task Basic_is_pro_rated_by_days_actually_worked()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var employee = await AddEmployeeAsync(service, "E-1", "Rafiq", 31_000);

        // 20 of August's 31 days.
        var run = await service.GenerateAsync(August,
            new Dictionary<int, decimal> { [employee.Id] = 20 });

        var payslip = run.Value.Payslips.Single();

        // 31,000 x 20/31 = 20,000 exactly.
        Assert.Equal(20_000, payslip.Gross);
        Assert.Equal(20, payslip.DaysWorked);
        Assert.Equal(31, payslip.DaysInMonth);
    }

    [SkippableFact]
    public async Task An_allowance_is_pro_rated_only_when_its_component_says_so()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var proRated = await service.SaveComponentAsync(new PayComponent
        {
            Name = "Travel allowance", Kind = PayComponentKind.Earning,
            ProRateOnAttendance = true, IsActive = true, AccountId = _salaryHead
        });

        var fixedAllowance = await service.SaveComponentAsync(new PayComponent
        {
            // A phone bill does not shrink because somebody took a day off.
            Name = "Phone allowance", Kind = PayComponentKind.Earning,
            ProRateOnAttendance = false, IsActive = true, AccountId = _salaryHead
        });

        var employee = await AddEmployeeAsync(service, "E-1", "Rafiq", 31_000, null,
        [
            new StructureLineInput(proRated.Value.Id, 3_100),
            new StructureLineInput(fixedAllowance.Value.Id, 2_000)
        ]);

        var run = await service.GenerateAsync(August,
            new Dictionary<int, decimal> { [employee.Id] = 20 });

        var payslip = run.Value.Payslips.Single();

        Assert.Equal(20_000, payslip.Lines.Single(l => l.Name == "Basic salary").Amount);
        Assert.Equal(2_000, payslip.Lines.Single(l => l.Name == "Travel allowance").Amount);
        Assert.Equal(2_000, payslip.Lines.Single(l => l.Name == "Phone allowance").Amount);
    }

    [SkippableFact]
    public async Task A_deduction_is_never_pro_rated()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var tax = await service.SaveComponentAsync(new PayComponent
        {
            Name = "Income tax", Kind = PayComponentKind.Deduction,
            IsActive = true, AccountId = _taxHead
        });

        var employee = await AddEmployeeAsync(service, "E-1", "Rafiq", 31_000, null,
            [new StructureLineInput(tax.Value.Id, 3_000)]);

        var run = await service.GenerateAsync(August,
            new Dictionary<int, decimal> { [employee.Id] = 20 });

        var payslip = run.Value.Payslips.Single();

        // Tax owed is tax owed; a day off does not reduce it.
        Assert.Equal(3_000, payslip.TotalDeductions);
        Assert.Equal(17_000, payslip.Net);
    }

    [SkippableFact]
    public async Task Somebody_with_no_salary_set_is_left_out_rather_than_paid_nothing()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        await AddEmployeeAsync(service, "E-1", "Rafiq", 31_000);

        // On the payroll, but nobody has said what they are paid.
        await service.SaveEmployeeAsync(new PayrollEmployee
        {
            Code = "E-2", FullName = "Newcomer",
            JoinedOn = new DateOnly(2026, 8, 1), IsActive = true
        });

        var run = await service.GenerateAsync(August);

        // A zero payslip looks like a decision. No payslip is a visible gap.
        Assert.Single(run.Value.Payslips);
        Assert.Equal("Rafiq", run.Value.Payslips.Single().EmployeeName);
    }

    // ---------- snapshots ----------

    [SkippableFact]
    public async Task Renaming_a_component_does_not_rewrite_an_existing_payslip()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var component = await service.SaveComponentAsync(new PayComponent
        {
            Name = "Travel allowance", Kind = PayComponentKind.Earning,
            IsActive = true, AccountId = _salaryHead
        });

        await AddEmployeeAsync(service, "E-1", "Rafiq", 31_000, null,
            [new StructureLineInput(component.Value.Id, 2_000)]);

        var run = await service.GenerateAsync(August);
        await service.ApproveAsync(run.Value.Id);

        // The catalog changes after the run was approved.
        db.ChangeTracker.Clear();
        var live = await db.PayComponents.FirstAsync(c => c.Id == component.Value.Id);
        live.Name = "Conveyance";
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var payslip = await db.Payslips.Include(p => p.Lines).FirstAsync();

        // The payslip somebody already received still says what it said.
        Assert.Contains(payslip.Lines, l => l.Name == "Travel allowance");
        Assert.DoesNotContain(payslip.Lines, l => l.Name == "Conveyance");
    }

    [SkippableFact]
    public async Task A_new_salary_supersedes_the_old_one_rather_than_editing_it()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var employee = await AddEmployeeAsync(service, "E-1", "Rafiq", 30_000);

        await service.SaveStructureAsync(
            employee.Id, new DateOnly(2026, 8, 1), 40_000, []);

        // A payslip issued in July has to keep explaining itself.
        var july = await service.CurrentStructureAsync(employee.Id, new DateOnly(2026, 7, 15));
        var august = await service.CurrentStructureAsync(employee.Id, new DateOnly(2026, 8, 15));

        Assert.Equal(30_000, july!.BasicSalary);
        Assert.Equal(40_000, august!.BasicSalary);
        Assert.Equal(new DateOnly(2026, 7, 31), july.EffectiveTo);
    }

    [SkippableFact]
    public async Task An_approved_run_cannot_be_rebuilt()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        await AddEmployeeAsync(service, "E-1", "Rafiq", 31_000);

        var run = await service.GenerateAsync(August);
        await service.ApproveAsync(run.Value.Id);

        var again = await service.GenerateAsync(August);

        // Regenerating would silently restate what people were told they would
        // be paid.
        Assert.True(again.Failed);
        Assert.Equal("payroll.not-draft", again.Code);
    }

    // ---------- posting ----------

    [SkippableFact]
    public async Task Paying_a_run_posts_one_balanced_voucher_for_the_whole_month()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var tax = await service.SaveComponentAsync(new PayComponent
        {
            Name = "Income tax", Kind = PayComponentKind.Deduction,
            IsActive = true, AccountId = _taxHead
        });

        await AddEmployeeAsync(service, "E-1", "Rafiq", 31_000, null,
            [new StructureLineInput(tax.Value.Id, 3_000)]);
        await AddEmployeeAsync(service, "E-2", "Bilal", 20_000, null,
            [new StructureLineInput(tax.Value.Id, 1_000)]);

        var run = await service.GenerateAsync(August);
        await service.ApproveAsync(run.Value.Id);

        var paid = await service.PayAsync(run.Value.Id, _cash, new DateOnly(2026, 8, 31));
        Assert.True(paid.Ok, paid.Error);

        db.ChangeTracker.Clear();

        // One voucher, not one per person: a voucher each would bury the ledger
        // under near-identical entries every month, and the payslips already
        // carry the per-person detail.
        var voucher = await db.Vouchers.Include(v => v.Lines).SingleAsync();
        Assert.True(voucher.IsBalanced);

        var accounts = new AccountService(db);
        Assert.Equal(51_000, await accounts.BalanceAsync(_salaryHead));   // gross cost
        Assert.Equal(-4_000, await accounts.BalanceAsync(_taxHead));      // owed to the taxman
        Assert.Equal(-47_000, await accounts.BalanceAsync(_cash));        // net paid out
    }

    [SkippableFact]
    public async Task The_aggregated_voucher_names_no_individual()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        await AddEmployeeAsync(service, "E-1", "Rafiq", 31_000);

        var run = await service.GenerateAsync(August);
        await service.ApproveAsync(run.Value.Id);
        await service.PayAsync(run.Value.Id, _cash, new DateOnly(2026, 8, 31));

        db.ChangeTracker.Clear();
        var lines = await db.VoucherLines.ToListAsync();

        // An aggregate is not traceable to one person. Stamping a name on it
        // would make the ledger's person filter lie.
        Assert.All(lines, l => Assert.Null(l.PersonId));
    }

    [SkippableFact]
    public async Task A_run_cannot_be_paid_before_it_is_approved()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        await AddEmployeeAsync(service, "E-1", "Rafiq", 31_000);
        var run = await service.GenerateAsync(August);

        var result = await service.PayAsync(run.Value.Id, _cash, new DateOnly(2026, 8, 31));

        Assert.True(result.Failed);
        Assert.Equal("payroll.not-approved", result.Code);
    }

    // ---------- advance recovery ----------

    [SkippableFact]
    public async Task An_advance_marked_for_payroll_recovery_is_deducted()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        await AddEmployeeAsync(service, "E-1", "Rafiq", 31_000, userId: "user-rafiq");
        await SeedRecoverableAdvanceAsync(db, "user-rafiq", 3_000);

        var run = await service.GenerateAsync(August);
        var payslip = run.Value.Payslips.Single();

        Assert.Contains(payslip.Lines, l => l.AdvanceId is not null && l.Amount == 3_000);
        Assert.Equal(28_000, payslip.Net);
    }

    [SkippableFact]
    public async Task Recovery_is_capped_so_net_pay_never_goes_negative()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        await AddEmployeeAsync(service, "E-1", "Rafiq", 31_000, userId: "user-rafiq");

        // They owe far more than a month's pay.
        await SeedRecoverableAdvanceAsync(db, "user-rafiq", 90_000);

        var run = await service.GenerateAsync(August);
        var payslip = run.Value.Payslips.Single();

        // Somebody should never be asked to pay to come to work. The shortfall
        // simply rolls to next month.
        Assert.Equal(31_000, payslip.TotalDeductions);
        Assert.Equal(0, payslip.Net);
    }

    [SkippableFact]
    public async Task Recovery_leaves_room_for_the_other_deductions_first()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var tax = await service.SaveComponentAsync(new PayComponent
        {
            Name = "Income tax", Kind = PayComponentKind.Deduction,
            IsActive = true, AccountId = _taxHead
        });

        await AddEmployeeAsync(service, "E-1", "Rafiq", 31_000, "user-rafiq",
            [new StructureLineInput(tax.Value.Id, 5_000)]);

        await SeedRecoverableAdvanceAsync(db, "user-rafiq", 90_000);

        var run = await service.GenerateAsync(August);
        var payslip = run.Value.Payslips.Single();

        // Tax comes off first; the advance takes only what is left.
        Assert.Equal(26_000, payslip.Lines.Single(l => l.AdvanceId is not null).Amount);
        Assert.Equal(0, payslip.Net);
    }

    [SkippableFact]
    public async Task Paying_the_run_clears_the_advance_without_posting_it_twice()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        await AddEmployeeAsync(service, "E-1", "Rafiq", 31_000, userId: "user-rafiq");
        var advanceId = await SeedRecoverableAdvanceAsync(db, "user-rafiq", 3_000);

        var run = await service.GenerateAsync(August);
        await service.ApproveAsync(run.Value.Id);
        await service.PayAsync(run.Value.Id, _cash, new DateOnly(2026, 8, 31));

        db.ChangeTracker.Clear();

        var advance = await db.Advances.FirstAsync(a => a.Id == advanceId);
        var accounts = new AccountService(db);

        // The payroll voucher credited the advance account; ApplyRecovery only
        // marks it cleared. Doing both would take the money twice, which is the
        // easy bug in this whole flow — and would show up here as a non-zero
        // advance balance.
        Assert.Equal(0, advance.OutstandingDifference);
        Assert.Equal(0, await accounts.BalanceAsync(_advanceHead));

        // 3,000 left as the advance, then 28,000 of net pay: the recovery moved
        // between two heads rather than costing cash a second time.
        Assert.Equal(-31_000, await accounts.BalanceAsync(_cash));
    }

    /// <summary>An advance already settled with the gap left for payroll to recover.</summary>
    private async Task<int> SeedRecoverableAdvanceAsync(
        FinanceDbContext db, string userId, decimal outstanding)
    {
        var advance = new Advance
        {
            Reference = $"ADV-26-{Random.Shared.Next(1000, 9999)}",
            Purpose = "Site visit",
            PersonId = userId,
            PersonName = "Rafiq",
            Amount = outstanding,
            DisbursedAmount = outstanding,
            JustifiedAmount = 0,
            Status = AdvanceStatus.Settled,
            DifferenceHandling = DifferenceHandling.RecoverFromPayroll,
            NeededBy = new DateOnly(2026, 7, 1)
        };

        db.Advances.Add(advance);

        // The disbursement really did put the money on their advance account,
        // so the recovery has something to credit back.
        var vouchers = new VoucherService(db, _clock, _user);
        await vouchers.PostSystemVoucherAsync(new SystemVoucher(
            VoucherType.Payment, new DateOnly(2026, 7, 1), "Advance out",
            [
                new VoucherLineInput(_advanceHead, outstanding, 0, null, userId, "Rafiq"),
                new VoucherLineInput(_cash, 0, outstanding)
            ],
            "finance", "finance.advance", 0, advance.Reference));

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return advance.Id;
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
