using MeiErp.Modules.Finance;
using MeiErp.Platform.Kernel;
using MeiErp.Platform.Workflow;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MeiErp.Modules.Finance.Tests;

/// <summary>
/// What happens when a payment request is actually paid: who decides the
/// expense head, what the voucher carries, and whether the receipt survives.
/// </summary>
[Collection("postgres")]
public sealed class PaymentRequestPayTests : IAsyncLifetime
{
    private static readonly string BaseConnection =
        Environment.GetEnvironmentVariable("MEIERP_TEST_DB")
        ?? "Host=127.0.0.1;Username=meierp;Password=DevPassword1!;";

    private readonly string _database = $"mei_prpay_{Guid.NewGuid():N}";
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero));
    private readonly TestUser _user = new("user-1", "Rafiq");

    private bool _available;
    private int _cash, _travel, _meals;

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
            var travel = new Account { Code = "5220", Name = "Staff travel", Type = AccountType.Expense, IsPostable = true };
            var meals = new Account { Code = "5230", Name = "Entertainment", Type = AccountType.Expense, IsPostable = true };

            db.Accounts.AddRange(cash, travel, meals);
            await db.SaveChangesAsync();

            _cash = cash.Id; _travel = travel.Id; _meals = meals.Id;
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

    private PaymentRequestService NewService(FinanceDbContext db) =>
        new(db, new VoucherService(db, _clock, _user), new AutoApprove(), _user, _clock);

    /// <summary>An approved, itemized request with no expense heads chosen yet.</summary>
    private async Task<PaymentRequest> ApprovedAsync(
        PaymentRequestService service, FinanceDbContext db,
        IReadOnlyList<PaymentRequestLineInput> lines, string? projectId = null)
    {
        var draft = await service.SaveDraftAsync(new PaymentRequestInput(
            null, "Site trip", null, 0, null, "Rafiq", _clock.Today, "dept-1",
            false, lines, projectId, projectId is null ? null : "P-1 — Depot"));

        Assert.True(draft.Ok, draft.Error);

        await service.SubmitAsync(draft.Value.Id);

        var live = await db.PaymentRequests.FirstAsync(r => r.Id == draft.Value.Id);
        live.Status = PaymentRequestStatus.Approved;
        await db.SaveChangesAsync();

        return live;
    }

    [SkippableFact]
    public async Task A_requester_is_offered_only_the_categories_tagged_for_them()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();

        db.Accounts.AddRange(
            new Account { Code = "5410", Name = "Director travel", Type = AccountType.Expense, IsPostable = true, Audience = ExpenseAudience.Director },
            new Account { Code = "5530", Name = "Stationery", Type = AccountType.Expense, IsPostable = true, Audience = ExpenseAudience.Everyone },
            new Account { Code = "5215", Name = "Salaries", Type = AccountType.Expense, IsPostable = true },
            new Account { Code = "5001", Name = "All expenses", Type = AccountType.Expense, IsPostable = false, Audience = ExpenseAudience.Everyone });

        await db.SaveChangesAsync();

        var accounts = new AccountService(db);

        var staff = (await accounts.CategoriesAsync(ExpenseAudience.Staff))
            .Select(a => a.Name).ToList();
        var director = (await accounts.CategoriesAsync(ExpenseAudience.Director))
            .Select(a => a.Name).ToList();

        // Anything tagged for everyone reaches both.
        Assert.Contains("Stationery", staff);
        Assert.Contains("Stationery", director);

        // ...and a director head is never offered to staff.
        Assert.Contains("Director travel", director);
        Assert.DoesNotContain("Director travel", staff);

        // An untagged head is nobody's category, so the picker stays short.
        Assert.DoesNotContain("Salaries", staff);
        Assert.DoesNotContain("Salaries", director);

        // A heading cannot be posted to, so it cannot be a category either.
        Assert.DoesNotContain("All expenses", staff);
    }

    [SkippableFact]
    public async Task The_accountant_can_choose_the_head_the_raiser_left_blank()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        // The person claiming a taxi fare is not expected to know the chart.
        var request = await ApprovedAsync(service, db,
        [
            new PaymentRequestLineInput("Travel", 1_200, "Taxi to site", null, null),
            new PaymentRequestLineInput("Meals", 800, "Client lunch", null, null)
        ]);

        db.ChangeTracker.Clear();
        var lines = await db.PaymentRequestLines
            .Where(l => l.PaymentRequestId == request.Id).OrderBy(l => l.Id).ToListAsync();

        var paid = await service.PayAsync(request.Id, _cash, _clock.Today,
            new Dictionary<int, int> { [lines[0].Id] = _travel, [lines[1].Id] = _meals });

        Assert.True(paid.Ok, paid.Error);

        db.ChangeTracker.Clear();
        var accounts = new AccountService(db);

        Assert.Equal(1_200, await accounts.BalanceAsync(_travel));
        Assert.Equal(800, await accounts.BalanceAsync(_meals));
        Assert.Equal(-2_000, await accounts.BalanceAsync(_cash));
    }

    [SkippableFact]
    public async Task Paying_is_refused_while_any_line_is_unclassified()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var request = await ApprovedAsync(service, db,
        [
            new PaymentRequestLineInput("Travel", 1_200, "Taxi", null, null),
            new PaymentRequestLineInput("Meals", 800, "Lunch", null, null)
        ]);

        db.ChangeTracker.Clear();
        var lines = await db.PaymentRequestLines
            .Where(l => l.PaymentRequestId == request.Id).OrderBy(l => l.Id).ToListAsync();

        // Only one of the two classified: posting the rest to a guess is how a
        // travel budget quietly absorbs everything nobody looked at.
        var paid = await service.PayAsync(request.Id, _cash, _clock.Today,
            new Dictionary<int, int> { [lines[0].Id] = _travel });

        Assert.True(paid.Failed);
        Assert.Equal("request.no-line-expense-head", paid.Code);

        db.ChangeTracker.Clear();
        Assert.Equal(0, await new AccountService(db).BalanceAsync(_cash));
    }

    [SkippableFact]
    public async Task A_head_the_raiser_did_choose_is_kept()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var request = await ApprovedAsync(service, db,
            [new PaymentRequestLineInput("Travel", 1_500, "Taxi", null, _travel)]);

        // Nothing reclassified, so what they chose stands.
        var paid = await service.PayAsync(request.Id, _cash, _clock.Today);

        Assert.True(paid.Ok, paid.Error);

        db.ChangeTracker.Clear();
        Assert.Equal(1_500, await new AccountService(db).BalanceAsync(_travel));
    }

    [SkippableFact]
    public async Task Project_and_department_ride_on_every_expense_line()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var request = await ApprovedAsync(service, db,
            [new PaymentRequestLineInput("Travel", 1_500, "Taxi", null, _travel)],
            projectId: "7");

        var paid = await service.PayAsync(request.Id, _cash, _clock.Today);
        Assert.True(paid.Ok, paid.Error);

        db.ChangeTracker.Clear();

        // A spend report that had to join back to the request would miss every
        // voucher raised by hand, so the tags live on the line.
        var expenseLine = await db.VoucherLines
            .FirstAsync(l => l.VoucherId == paid.Value.VoucherId && l.AccountId == _travel);

        Assert.Equal("7", expenseLine.ProjectId);
        Assert.Equal("dept-1", expenseLine.DepartmentId);
    }

    [SkippableFact]
    public async Task A_receipt_stays_with_the_line_it_belongs_to()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        byte[] receipt = [0x25, 0x50, 0x44, 0x46];   // "%PDF"

        await ApprovedAsync(service, db,
        [
            new PaymentRequestLineInput("Travel", 1_500, "Taxi", null, _travel,
                receipt, "taxi.pdf", "application/pdf")
        ]);

        db.ChangeTracker.Clear();

        var line = await db.PaymentRequestLines.FirstAsync();
        var fetched = await service.AttachmentAsync(line.Id);

        Assert.NotNull(fetched);
        Assert.Equal(receipt, fetched.Attachment);
        Assert.Equal("taxi.pdf", fetched.AttachmentName);
        Assert.Equal("application/pdf", fetched.AttachmentContentType);
        Assert.True(fetched.HasAttachment);
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
