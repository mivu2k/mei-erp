using MeiErp.Modules.Finance;
using MeiErp.Platform.Kernel;
using MeiErp.Platform.Workflow;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MeiErp.Modules.Finance.Tests;

/// <summary>
/// Money lent against salary: paid out, scheduled, then taken back a month at a
/// time. The recurring danger is collecting it twice — once in the payroll
/// voucher and again when the instalment is marked repaid.
/// </summary>
[Collection("postgres")]
public sealed class SalaryAdvanceTests : IAsyncLifetime
{
    private static readonly string BaseConnection =
        Environment.GetEnvironmentVariable("MEIERP_TEST_DB")
        ?? "Host=127.0.0.1;Username=meierp;Password=DevPassword1!;";

    private readonly string _database = $"mei_sal_{Guid.NewGuid():N}";
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero));
    private readonly TestUser _user = new("user-1", "Rafiq");

    private bool _available;
    private int _cash, _advanceHead;

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

            var cash = new Account { Code = "1100", Name = "Cash", Type = AccountType.Asset, IsPostable = true };
            var advances = new Account { Code = "1700", Name = "Employee advances", Type = AccountType.Asset, IsPostable = true };

            db.Accounts.AddRange(cash, advances);
            await db.SaveChangesAsync();

            _cash = cash.Id; _advanceHead = advances.Id;
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

    private EmployeeAdvanceService NewService(FinanceDbContext db) =>
        new(db, new VoucherService(db, _clock, _user), new AutoApprove(), _user, _clock);

    private async Task<EmployeeAdvance> DisbursedAsync(
        EmployeeAdvanceService service, FinanceDbContext db, decimal amount, int months)
    {
        var draft = await service.SaveDraftAsync(
            new EmployeeAdvanceInput(null, amount, "School fees", months, null));
        Assert.True(draft.Ok, draft.Error);

        await service.SubmitAsync(draft.Value.Id);

        var live = await db.EmployeeAdvances.FirstAsync(a => a.Id == draft.Value.Id);
        live.Status = EmployeeAdvanceStatus.Approved;
        await db.SaveChangesAsync();

        var paid = await service.DisburseAsync(draft.Value.Id, _cash, _clock.Today);
        Assert.True(paid.Ok, paid.Error);

        return paid.Value;
    }

    // ---------- the schedule ----------

    [Theory]
    [InlineData(12000, 12, 1000)]
    [InlineData(10000, 3, 3333.33)]
    [InlineData(5000, 1, 5000)]
    public void The_monthly_figure_is_the_amount_over_the_term(decimal amount, int months, decimal expected) =>
        Assert.Equal(expected, RepaymentSchedule.Monthly(amount, months));

    [Fact]
    public void The_instalments_add_back_to_the_whole_amount()
    {
        // 10,000 over 3 is 3,333.33 a month, which is 9,999.99. The last one
        // carries the missing penny, or the advance never clears.
        var parts = RepaymentSchedule.Split(10_000, 3);

        Assert.Equal(3, parts.Count);
        Assert.Equal(10_000, parts.Sum());
        Assert.Equal(3_333.34m, parts[^1]);
    }

    // ---------- paying it out ----------

    [SkippableFact]
    public async Task Paying_out_builds_the_schedule_and_moves_money_without_an_expense()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var advance = await DisbursedAsync(service, db, 12_000, 3);

        db.ChangeTracker.Clear();
        var accounts = new AccountService(db);

        // A loan, not a cost: it sits on their advance head until repaid.
        Assert.Equal(12_000, await accounts.BalanceWithChildrenAsync(_advanceHead));
        Assert.Equal(-12_000, await accounts.BalanceAsync(_cash));

        var reloaded = await service.GetAsync(advance.Id);
        Assert.Equal(3, reloaded!.Installments.Count);
        Assert.Equal(12_000, reloaded.Installments.Sum(i => i.Amount));

        // The first deduction is next month: taking it from the salary paid in
        // the same breath as the loan is not what was agreed.
        Assert.Equal(new DateOnly(2026, 9, 1), reloaded.Installments[0].DueDate);
        Assert.Equal(new DateOnly(2026, 11, 1), reloaded.Installments[2].DueDate);
    }

    [SkippableFact]
    public async Task It_goes_onto_the_persons_own_advance_head()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var advance = await DisbursedAsync(service, db, 6_000, 2);

        db.ChangeTracker.Clear();

        var reloaded = await db.EmployeeAdvances.FirstAsync(a => a.Id == advance.Id);
        Assert.NotNull(reloaded.AdvanceAccountId);

        var head = await db.Accounts.FirstAsync(a => a.Id == reloaded.AdvanceAccountId);
        Assert.Equal(_advanceHead, head.ParentId);
        Assert.Equal("user-1", head.PersonId);
    }

    [SkippableFact]
    public async Task An_unapproved_advance_cannot_be_paid_out()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var draft = await service.SaveDraftAsync(
            new EmployeeAdvanceInput(null, 5_000, "Fees", 5, null));

        var paid = await service.DisburseAsync(draft.Value.Id, _cash, _clock.Today);

        Assert.True(paid.Failed);
        Assert.Equal("salary-advance.not-approved", paid.Code);
    }

    // ---------- getting it back ----------

    [SkippableFact]
    public async Task A_repayment_clears_the_oldest_instalments_first()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var advance = await DisbursedAsync(service, db, 12_000, 3);

        var repaid = await service.RepayAsync(advance.Id, 5_000, _cash, _clock.Today);
        Assert.True(repaid.Ok, repaid.Error);

        db.ChangeTracker.Clear();
        var reloaded = await service.GetAsync(advance.Id);

        Assert.Equal(InstallmentStatus.Paid, reloaded!.Installments[0].Status);
        Assert.Equal(InstallmentStatus.PartiallyPaid, reloaded.Installments[1].Status);
        Assert.Equal(InstallmentStatus.Pending, reloaded.Installments[2].Status);

        Assert.Equal(7_000, reloaded.OutstandingBalance);
        Assert.Equal(EmployeeAdvanceStatus.Repaying, reloaded.Status);

        // The debt came down and the cash came back.
        Assert.Equal(7_000, await new AccountService(db).BalanceWithChildrenAsync(_advanceHead));
    }

    [SkippableFact]
    public async Task Repaying_the_last_of_it_settles_the_advance()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var advance = await DisbursedAsync(service, db, 9_000, 3);

        await service.RepayAsync(advance.Id, 9_000, _cash, _clock.Today);

        db.ChangeTracker.Clear();
        var reloaded = await service.GetAsync(advance.Id);

        Assert.Equal(EmployeeAdvanceStatus.Settled, reloaded!.Status);
        Assert.Equal(0, reloaded.OutstandingBalance);
        Assert.All(reloaded.Installments, i => Assert.Equal(InstallmentStatus.Paid, i.Status));
        Assert.Equal(0, await new AccountService(db).BalanceWithChildrenAsync(_advanceHead));
    }

    [SkippableFact]
    public async Task More_cannot_be_repaid_than_is_owed()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var advance = await DisbursedAsync(service, db, 6_000, 2);

        var repaid = await service.RepayAsync(advance.Id, 6_500, _cash, _clock.Today);

        Assert.True(repaid.Failed);
        Assert.Equal("salary-advance.over-repayment", repaid.Code);

        db.ChangeTracker.Clear();
        Assert.Equal(-6_000, await new AccountService(db).BalanceAsync(_cash));
    }

    [SkippableFact]
    public async Task An_advance_already_paid_out_cannot_be_cancelled()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var advance = await DisbursedAsync(service, db, 4_000, 2);

        // Cancelling would leave a balance on their head that nothing is
        // scheduled to recover.
        var cancelled = await service.CancelAsync(advance.Id);

        Assert.True(cancelled.Failed);
        Assert.Equal("salary-advance.already-disbursed", cancelled.Code);
    }

    [SkippableFact]
    public async Task Only_advances_with_something_due_are_offered_to_payroll()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        await DisbursedAsync(service, db, 12_000, 3);

        db.ChangeTracker.Clear();

        // Nothing is due in the month it was taken - the first instalment is
        // dated next month.
        var thisMonth = await service.DueForRecoveryAsync("user-1", new DateOnly(2026, 8, 31));
        Assert.Empty(thisMonth);

        var nextMonth = await service.DueForRecoveryAsync("user-1", new DateOnly(2026, 9, 30));
        Assert.Single(nextMonth);
    }

    private sealed class AutoApprove : IApprovalEngine
    {
        private int _next = 1;

        public Task<Result<ApprovalRequest>> SubmitAsync(
            SubmitApproval request, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(new ApprovalRequest
            {
                Id = _next++,
                DocumentType = request.DocumentType,
                DocumentId = request.DocumentId,
                Status = ApprovalStatus.Pending
            }));

        public Task<Result<ApprovalRequest>> DecideAsync(
            int requestId, ApprovalDecision decision, string? comment, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Result> CancelAsync(int requestId, string? reason, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());

        public Task<Result<ApprovalRequest>> ResubmitAsync(int requestId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ApprovalInboxItem>> InboxAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ApprovalInboxItem>>([]);

        public Task<Result> CanDecideAsync(int requestId, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());


        public Task<IReadOnlyDictionary<int, ApprovalPosition>> PositionsAsync(
            string documentType, IReadOnlyList<int> documentIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<int, ApprovalPosition>>(
                new Dictionary<int, ApprovalPosition>());

        public Task<ApprovalHistory?> HistoryAsync(
            string documentType, int documentId, CancellationToken ct = default) =>
            Task.FromResult<ApprovalHistory?>(null);
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
