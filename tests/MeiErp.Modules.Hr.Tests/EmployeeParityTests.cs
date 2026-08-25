using MeiErp.Modules.Hr;
using MeiErp.Platform.Identity;
using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MeiErp.Modules.Hr.Tests;

[Collection("postgres")]
public sealed class EmployeeParityTests : IAsyncLifetime
{
    private static readonly string BaseConnection = Environment.GetEnvironmentVariable("MEIERP_TEST_DB")
        ?? "Host=127.0.0.1;Username=meierp;Password=DevPassword1!;";
    private readonly string _database = $"mei_hr_employee_{Guid.NewGuid():N}";
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
            await using var db = NewDb();
            await db.Database.EnsureCreatedAsync();
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

    [SkippableFact]
    public async Task Legacy_employee_details_round_trip_through_the_service()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        await using var platformDb = NewPlatformDb();
        var saved = await new EmployeeService(db, platformDb, _clock).SaveAsync(new Employee
        {
            Code = "EMP-DETAIL",
            FullName = "Ayesha Khan",
            FatherName = "Nadeem Khan",
            DateOfBirth = new DateOnly(1992, 4, 12),
            Gender = Gender.Female,
            MaritalStatus = MaritalStatus.Married,
            AlternatePhone = "03001234567",
            Address = "Main Road",
            City = "Lahore",
            EmergencyContactName = "Nadeem Khan",
            EmergencyContactPhone = "03007654321",
            EmploymentType = EmploymentType.Contract,
            ConfirmedOn = new DateOnly(2024, 8, 1),
            WorkLocation = "Head office",
            BankName = "Meezan Bank",
            BankAccountNumber = "PK00TEST",
            BankAccountTitle = "Ayesha Khan",
            TaxNumber = "NTN-123",
            SocialSecurityNumber = "EOBI-123",
            Notes = "Legacy detail parity"
        });

        db.ChangeTracker.Clear();
        var found = await new EmployeeService(db, platformDb, _clock).GetAsync(saved.Value.Id);

        Assert.Equal("Nadeem Khan", found!.FatherName);
        Assert.Equal(new DateOnly(1992, 4, 12), found.DateOfBirth);
        Assert.Equal(Gender.Female, found.Gender);
        Assert.Equal(MaritalStatus.Married, found.MaritalStatus);
        Assert.Equal(EmploymentType.Contract, found.EmploymentType);
        Assert.Equal("PK00TEST", found.BankAccountNumber);
        Assert.Equal("NTN-123", found.TaxNumber);
        Assert.Equal("Legacy detail parity", found.Notes);
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
