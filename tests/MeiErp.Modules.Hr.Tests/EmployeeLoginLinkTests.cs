using MeiErp.Modules.Hr;
using MeiErp.Platform.Identity;
using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace MeiErp.Modules.Hr.Tests;

/// <summary>
/// Linking an employee to a login writes to two modules' tables. These pin that
/// it happens as one unit, because the whole point of the feature is that the
/// two sides cannot be left disagreeing the way two separate screens allowed.
/// </summary>
[Collection("postgres")]
public sealed class EmployeeLoginLinkTests : IAsyncLifetime
{
    private static readonly string BaseConnection = Environment.GetEnvironmentVariable("MEIERP_TEST_DB")
        ?? "Host=127.0.0.1;Username=meierp;Password=DevPassword1!;";
    private readonly string _database = $"mei_hr_link_{Guid.NewGuid():N}";
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 8, 23, 9, 0, 0, TimeSpan.Zero));
    private bool _available;
    private string Connection => BaseConnection + $"Database={_database};";

    private HrDbContext NewDb() => new(
        new DbContextOptionsBuilder<HrDbContext>().UseNpgsql(Connection).Options,
        new TestUser(), _clock);

    private PlatformDbContext NewPlatformDb() => new(
        new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(Connection).Options);

    public async Task InitializeAsync()
    {
        try
        {
            await using var admin = new DbContext(new DbContextOptionsBuilder()
                .UseNpgsql(BaseConnection + "Database=postgres;").Options);
            await admin.Database.ExecuteSqlRawAsync($"CREATE DATABASE \"{_database}\";");

            // Platform first: it owns the shared audit table. EnsureCreated is a
            // no-op once the database has any table at all, so HR then has to be
            // told to create its own outright.
            await using var platformDb = NewPlatformDb();
            await platformDb.Database.EnsureCreatedAsync();

            await using var db = NewDb();
            await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
            await db.EnsureAuditTableForTestsAsync();
            _available = true;
        }
        catch (NpgsqlException) { _available = false; }
    }

    public async Task DisposeAsync()
    {
        if (!_available) return;
        await using var admin = new DbContext(new DbContextOptionsBuilder()
            .UseNpgsql(BaseConnection + "Database=postgres;").Options);
        await admin.Database.ExecuteSqlRawAsync($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE);");
    }

    private static ApplicationUser NewUser(string name, string email) => new()
    {
        Id = Guid.NewGuid().ToString(),
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        FullName = name,
        IsActive = true,
        SecurityStamp = Guid.NewGuid().ToString()
    };

    [SkippableFact]
    public async Task Linking_sets_both_sides_together()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        await using var platformDb = NewPlatformDb();

        var user = NewUser("Ayesha Khan", "ayesha@example.com");
        platformDb.Users.Add(user);
        await platformDb.SaveChangesAsync();

        var employee = new Employee { Code = "EMP-100", FullName = "Ayesha Khan", JoinedOn = _clock.Today };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        var service = new EmployeeService(db, platformDb, _clock);
        var result = await service.LinkLoginAsync(employee.Id, user.Id);

        Assert.True(result.Ok);

        db.ChangeTracker.Clear();
        platformDb.ChangeTracker.Clear();

        var linkedEmployee = await db.Employees.FirstAsync(e => e.Id == employee.Id);
        var linkedUser = await platformDb.Users.FirstAsync(u => u.Id == user.Id);

        Assert.Equal(user.Id, linkedEmployee.UserId);
        Assert.Equal("EMP-100", linkedUser.EmployeeCode);
    }

    [SkippableFact]
    public async Task A_login_already_linked_elsewhere_is_refused()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        await using var platformDb = NewPlatformDb();

        var user = NewUser("Bilal Ahmed", "bilal@example.com");
        platformDb.Users.Add(user);
        await platformDb.SaveChangesAsync();

        var first = new Employee { Code = "EMP-200", FullName = "Bilal Ahmed", JoinedOn = _clock.Today };
        var second = new Employee { Code = "EMP-201", FullName = "Someone Else", JoinedOn = _clock.Today };
        db.Employees.AddRange(first, second);
        await db.SaveChangesAsync();

        var service = new EmployeeService(db, platformDb, _clock);
        Assert.True((await service.LinkLoginAsync(first.Id, user.Id)).Ok);

        var second_attempt = await service.LinkLoginAsync(second.Id, user.Id);

        Assert.True(second_attempt.Failed);
        Assert.Equal("employee.duplicate-login", second_attempt.Code);
    }

    [SkippableFact]
    public async Task Unlinking_clears_both_sides()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        await using var platformDb = NewPlatformDb();

        var user = NewUser("Caleb Nawaz", "caleb@example.com");
        platformDb.Users.Add(user);
        await platformDb.SaveChangesAsync();

        var employee = new Employee { Code = "EMP-300", FullName = "Caleb Nawaz", JoinedOn = _clock.Today };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        var service = new EmployeeService(db, platformDb, _clock);
        await service.LinkLoginAsync(employee.Id, user.Id);

        var result = await service.UnlinkLoginAsync(employee.Id);
        Assert.True(result.Ok);

        db.ChangeTracker.Clear();
        platformDb.ChangeTracker.Clear();

        Assert.Null((await db.Employees.FirstAsync(e => e.Id == employee.Id)).UserId);
        Assert.Null((await platformDb.Users.FirstAsync(u => u.Id == user.Id)).EmployeeCode);
    }

    [SkippableFact]
    public async Task Unlinking_leaves_an_employee_code_someone_set_by_hand()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        await using var platformDb = NewPlatformDb();

        var user = NewUser("Dania Iqbal", "dania@example.com");
        platformDb.Users.Add(user);
        await platformDb.SaveChangesAsync();

        var employee = new Employee { Code = "EMP-400", FullName = "Dania Iqbal", JoinedOn = _clock.Today };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        var service = new EmployeeService(db, platformDb, _clock);
        await service.LinkLoginAsync(employee.Id, user.Id);

        // An administrator since pointed the account at a different staff number
        // from the user screen. Unlinking must not silently discard that.
        user.EmployeeCode = "EMP-OTHER";
        await platformDb.SaveChangesAsync();

        await service.UnlinkLoginAsync(employee.Id);

        platformDb.ChangeTracker.Clear();
        Assert.Equal("EMP-OTHER", (await platformDb.Users.FirstAsync(u => u.Id == user.Id)).EmployeeCode);
    }

    [SkippableFact]
    public async Task Candidates_exclude_logins_already_linked_to_someone_else()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        await using var platformDb = NewPlatformDb();

        var taken = NewUser("Taken Person", "taken@example.com");
        var free = NewUser("Free Person", "free@example.com");
        platformDb.Users.AddRange(taken, free);
        await platformDb.SaveChangesAsync();

        var linked = new Employee { Code = "EMP-500", FullName = "Taken Person", JoinedOn = _clock.Today };
        var subject = new Employee { Code = "EMP-501", FullName = "Needs A Login", JoinedOn = _clock.Today };
        db.Employees.AddRange(linked, subject);
        await db.SaveChangesAsync();

        var service = new EmployeeService(db, platformDb, _clock);
        await service.LinkLoginAsync(linked.Id, taken.Id);

        var candidates = await service.SearchLoginCandidatesAsync(null, subject.Id);

        Assert.DoesNotContain(candidates, c => c.UserId == taken.Id);
        Assert.Contains(candidates, c => c.UserId == free.Id);
    }

    private sealed class TestUser : ICurrentUser
    {
        public string? UserId => "hr-test";
        public string? Name => "HR test";
        public string? Email => null;
        public bool IsAuthenticated => true;
        public bool Can(string permission) => true;
        public bool InModule(string moduleKey) => true;
        public IReadOnlyCollection<string> Roles { get; } = [];
    }
}
