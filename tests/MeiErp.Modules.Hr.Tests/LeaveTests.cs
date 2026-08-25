using MeiErp.Modules.Hr;
using MeiErp.Platform.Identity;
using MeiErp.Platform.Kernel;
using MeiErp.Platform.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace MeiErp.Modules.Hr.Tests;

/// <summary>
/// Leave, and the part of it most likely to go wrong: the balance.
///
/// Days are held when a request is submitted and only spent when it is
/// approved. Get that wrong in either direction and someone either loses leave
/// they never took, or takes leave twice.
/// </summary>
[Collection("postgres")]
public sealed class LeaveTests : IAsyncLifetime
{
    private static readonly string BaseConnection =
        Environment.GetEnvironmentVariable("MEIERP_TEST_DB")
        ?? "Host=127.0.0.1;Username=meierp;Password=DevPassword1!;";

    private readonly string _database = $"mei_hr_{Guid.NewGuid():N}";

    // A Friday, so the working-day arithmetic has a weekend to step over.
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero));
    private readonly TestUser _user = new("user-1", "Rafiq");

    private bool _available;
    private int _employeeId;
    private int _annualLeaveId;

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

            // Platform first: submitting falls back to the linked login's
            // department when the employee has none, so those tables have to be
            // there. EnsureCreated is a no-op once any table exists, so HR is
            // then told to create its own outright.
            await using (var platformDb = NewPlatformDb())
            {
                await platformDb.Database.EnsureCreatedAsync();
            }

            await using var db = NewDb();
            await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
            await db.EnsureAuditTableForTestsAsync();

            var type = new LeaveType
            {
                Code = "AL", Name = "Annual leave",
                AnnualEntitlement = 14, IsPaid = true
            };
            db.LeaveTypes.Add(type);

            var employee = new Employee
            {
                Code = "E-001", FullName = "Rafiq", UserId = "user-1",
                JoinedOn = new DateOnly(2020, 1, 1), Status = EmploymentStatus.Active
            };
            db.Employees.Add(employee);

            await db.SaveChangesAsync();

            _annualLeaveId = type.Id;
            _employeeId = employee.Id;
            _available = true;
        }
        catch (NpgsqlException)
        {
            // Only an unreachable server means skip. Catching everything would
            // turn a setup bug into a silent pass.
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
            await admin.Database.ExecuteSqlRawAsync(
                $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE);");
        }
        catch { /* a stray throwaway database is harmless */ }
    }

    private HrDbContext NewDb() =>
        new(new DbContextOptionsBuilder<HrDbContext>().UseNpgsql(Connection).Options, _user, _clock);

    /// <summary>
    /// Only consulted when an employee has no department of their own but does
    /// have a linked login, which none of these fixtures set up.
    /// </summary>
    private PlatformDbContext NewPlatformDb() =>
        new(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(Connection).Options);

    private LeaveService NewService(HrDbContext db, FakeApprovals approvals) =>
        new(db, NewPlatformDb(), approvals, _user, _clock);

    // ---------- working days ----------

    [SkippableFact]
    public async Task Weekends_are_not_counted_as_leave()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db, new FakeApprovals());

        // Friday 21 Aug to Monday 24 Aug: Fri and Mon are working days,
        // the weekend between them is not.
        var days = await service.WorkingDaysAsync(
            new DateOnly(2026, 8, 21), new DateOnly(2026, 8, 24));

        Assert.Equal(2, days);
    }

    [SkippableFact]
    public async Task An_annual_holiday_is_skipped_whatever_year_it_was_entered_against()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();

        // Entered against 2020 but marked annual: it must still apply in 2026,
        // or every holiday needs re-entering each January.
        db.Holidays.Add(new Holiday
        {
            Date = new DateOnly(2020, 8, 14), Name = "Independence Day", IsAnnual = true
        });
        await db.SaveChangesAsync();

        var service = NewService(db, new FakeApprovals());

        // Fri 14 Aug 2026 is the holiday; Thu 13th is the only working day.
        var days = await service.WorkingDaysAsync(
            new DateOnly(2026, 8, 13), new DateOnly(2026, 8, 14));

        Assert.Equal(1, days);
    }

    // ---------- the balance hold ----------

    [SkippableFact]
    public async Task Submitting_holds_the_days_without_spending_them()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db, new FakeApprovals());

        var draft = await service.SaveDraftAsync(new LeaveRequestInput(
            null, _employeeId, _annualLeaveId,
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3), "Family", null));
        Assert.True(draft.Ok, draft.Error);

        var submitted = await service.SubmitAsync(draft.Value.Id);
        Assert.True(submitted.Ok, submitted.Error);

        var balance = await db.LeaveBalances.SingleAsync();

        // Held, not taken - the leave has not happened yet.
        Assert.Equal(3, balance.Pending);
        Assert.Equal(0, balance.Taken);
        Assert.Equal(11, balance.Available);
    }

    [SkippableFact]
    public async Task Two_pending_requests_cannot_overspend_the_same_entitlement()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db, new FakeApprovals());

        // Ten working days, leaving four.
        var first = await service.SaveDraftAsync(new LeaveRequestInput(
            null, _employeeId, _annualLeaveId,
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14), null, null));
        Assert.True((await service.SubmitAsync(first.Value.Id)).Ok);

        // Another ten. Without the hold, this would pass the balance check
        // because the first is not "taken" yet.
        var second = await service.SaveDraftAsync(new LeaveRequestInput(
            null, _employeeId, _annualLeaveId,
            new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 14), null, null));
        Assert.True(second.Ok, second.Error);

        var result = await service.SubmitAsync(second.Value.Id);

        Assert.True(result.Failed);
        Assert.Equal("leave.insufficient-balance", result.Code);
    }

    [SkippableFact]
    public async Task Approval_moves_the_hold_into_days_taken_rather_than_adding_to_it()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db, new FakeApprovals());
        var sink = new LeaveApprovalSink(db, _clock);

        var draft = await service.SaveDraftAsync(new LeaveRequestInput(
            null, _employeeId, _annualLeaveId,
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3), null, null));
        await service.SubmitAsync(draft.Value.Id);

        await sink.OnSettledAsync(draft.Value.Id, ApprovalStatus.Approved, NewApproval());
        await db.SaveChangesAsync();

        var balance = await db.LeaveBalances.SingleAsync();

        // Moved, not added: counting it in both places would silently double
        // the cost of every approved request.
        Assert.Equal(0, balance.Pending);
        Assert.Equal(3, balance.Taken);
        Assert.Equal(11, balance.Available);
    }

    [SkippableFact]
    public async Task Rejection_gives_the_days_back()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db, new FakeApprovals());
        var sink = new LeaveApprovalSink(db, _clock);

        var draft = await service.SaveDraftAsync(new LeaveRequestInput(
            null, _employeeId, _annualLeaveId,
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3), null, null));
        await service.SubmitAsync(draft.Value.Id);

        await sink.OnSettledAsync(draft.Value.Id, ApprovalStatus.Rejected, NewApproval());
        await db.SaveChangesAsync();

        var balance = await db.LeaveBalances.SingleAsync();

        Assert.Equal(0, balance.Pending);
        Assert.Equal(0, balance.Taken);
        Assert.Equal(14, balance.Available);
    }

    [SkippableFact]
    public async Task Returning_releases_the_hold_and_keeps_the_request_alive()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db, new FakeApprovals());
        var sink = new LeaveApprovalSink(db, _clock);

        var draft = await service.SaveDraftAsync(new LeaveRequestInput(
            null, _employeeId, _annualLeaveId,
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3), null, null));
        await service.SubmitAsync(draft.Value.Id);

        await sink.OnSettledAsync(draft.Value.Id, ApprovalStatus.Returned, NewApproval("Pick other dates"));
        await db.SaveChangesAsync();

        var request = await db.LeaveRequests.SingleAsync();
        var balance = await db.LeaveBalances.SingleAsync();

        // Returned is not rejected: the request lives on and can be corrected.
        Assert.Equal(LeaveStatus.Returned, request.Status);
        Assert.True(request.IsOpen);
        Assert.Equal("Pick other dates", request.DecisionComment);
        Assert.Equal(0, balance.Pending);
    }

    // ---------- guards ----------

    [SkippableFact]
    public async Task Overlapping_dates_are_refused()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db, new FakeApprovals());

        var first = await service.SaveDraftAsync(new LeaveRequestInput(
            null, _employeeId, _annualLeaveId,
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3), null, null));
        await service.SubmitAsync(first.Value.Id);

        var overlapping = await service.SaveDraftAsync(new LeaveRequestInput(
            null, _employeeId, _annualLeaveId,
            new DateOnly(2026, 9, 3), new DateOnly(2026, 9, 4), null, null));

        // Nobody is away twice on the same day.
        Assert.True(overlapping.Failed);
        Assert.Equal("leave.overlap", overlapping.Code);
    }

    [SkippableFact]
    public async Task A_request_covering_only_weekends_is_refused()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db, new FakeApprovals());

        // Sat 22 and Sun 23 August 2026.
        var result = await service.SaveDraftAsync(new LeaveRequestInput(
            null, _employeeId, _annualLeaveId,
            new DateOnly(2026, 8, 22), new DateOnly(2026, 8, 23), null, null));

        Assert.True(result.Failed);
        Assert.Equal("leave.no-working-days", result.Code);
    }

    [SkippableFact]
    public async Task A_submitted_request_cannot_be_edited()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db, new FakeApprovals());

        var draft = await service.SaveDraftAsync(new LeaveRequestInput(
            null, _employeeId, _annualLeaveId,
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3), null, null));
        await service.SubmitAsync(draft.Value.Id);

        // Editing dates out from under an approver is how someone approves one
        // thing and authorises another.
        var edit = await service.SaveDraftAsync(new LeaveRequestInput(
            draft.Value.Id, _employeeId, _annualLeaveId,
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 10), null, null));

        Assert.True(edit.Failed);
        Assert.Equal("leave.not-editable", edit.Code);
    }

    [SkippableFact]
    public async Task Unpaid_leave_is_not_checked_against_a_balance()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();

        // Zero entitlement means unlimited - the approver is the control.
        var unpaid = new LeaveType
        {
            Code = "UL", Name = "Unpaid leave", AnnualEntitlement = 0, IsPaid = false
        };
        db.LeaveTypes.Add(unpaid);
        await db.SaveChangesAsync();

        var service = NewService(db, new FakeApprovals());

        var draft = await service.SaveDraftAsync(new LeaveRequestInput(
            null, _employeeId, unpaid.Id,
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null, null));
        Assert.True(draft.Ok, draft.Error);

        var result = await service.SubmitAsync(draft.Value.Id);

        Assert.True(result.Ok, result.Error);
    }

    private static ApprovalRequest NewApproval(string? comment = null) => new()
    {
        Id = 1,
        Actions = comment is null
            ? []
            : [new ApprovalAction
              {
                  Comment = comment,
                  ActedUtc = DateTime.UtcNow,
                  Decision = ApprovalDecision.Returned
              }]
    };

    [SkippableFact]
    public async Task Employee_document_content_and_metadata_round_trip()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db=NewDb();var service=new EmployeeDocumentService(db,_clock);var bytes=new byte[]{1,2,3,4};
        var saved=await service.SaveAsync(new(null,_employeeId,"CNIC",EmployeeDocumentKind.NationalId,new DateOnly(2027,1,1),"front","cnic.pdf","application/pdf",bytes));
        Assert.True(saved.Ok,saved.Error);var file=await service.FileAsync(saved.Value.Id);Assert.Equal(bytes,file!.Content);Assert.Equal("cnic.pdf",file.FileName);
    }

    [SkippableFact]
    public async Task Expiry_register_keeps_overdue_and_upcoming_but_excludes_later_documents()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db=NewDb();var service=new EmployeeDocumentService(db,_clock);
        await service.SaveAsync(new(null,_employeeId,"Expired",EmployeeDocumentKind.Contract,new DateOnly(2026,8,1),null));
        await service.SaveAsync(new(null,_employeeId,"Soon",EmployeeDocumentKind.Licence,new DateOnly(2026,9,1),null));
        await service.SaveAsync(new(null,_employeeId,"Later",EmployeeDocumentKind.Certificate,new DateOnly(2027,9,1),null));
        var rows=await service.ExpiringAsync(60);Assert.Contains(rows,x=>x.Title=="Expired");Assert.Contains(rows,x=>x.Title=="Soon");Assert.DoesNotContain(rows,x=>x.Title=="Later");
    }

    [SkippableFact]
    public async Task Editing_document_metadata_does_not_erase_the_stored_file()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db=NewDb();var service=new EmployeeDocumentService(db,_clock);var saved=await service.SaveAsync(new(null,_employeeId,"Old",EmployeeDocumentKind.Other,null,null,"a.pdf","application/pdf",[9,8]));
        await service.SaveAsync(new(saved.Value.Id,_employeeId,"Renewed",EmployeeDocumentKind.Contract,new DateOnly(2027,1,1),"updated"));
        var file=await service.FileAsync(saved.Value.Id);Assert.Equal("Renewed",file!.Title);Assert.Equal(new byte[]{9,8},file.Content);
    }

    [SkippableFact]
    public async Task Deleted_employee_document_disappears_but_remains_soft_deleted_for_audit()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db=NewDb();var service=new EmployeeDocumentService(db,_clock);var saved=await service.SaveAsync(new(null,_employeeId,"Old",EmployeeDocumentKind.Other,null,null));
        Assert.True((await service.DeleteAsync(saved.Value.Id)).Ok);Assert.Empty(await service.ForEmployeeAsync(_employeeId));
        Assert.True(await db.EmployeeDocuments.IgnoreQueryFilters().AnyAsync(x=>x.Id==saved.Value.Id&&x.IsDeleted));
    }

    /// <summary>
    /// Stands in for the approval engine. The engine's own routing is tested
    /// directly against WorkflowRouter; what matters here is that leave holds
    /// and releases days correctly around it.
    /// </summary>
    private sealed class FakeApprovals : IApprovalEngine
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
