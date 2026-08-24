using MeiErp.Modules.Auto;
using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MeiErp.Modules.Auto.Tests;

[Collection("postgres")]
public sealed class FleetTests : IAsyncLifetime
{
    private static readonly string BaseConnection = Environment.GetEnvironmentVariable("MEIERP_TEST_DB")
        ?? "Host=127.0.0.1;Username=meierp;Password=DevPassword1!;";
    private readonly string _database = $"mei_auto_{Guid.NewGuid():N}";
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 8, 22, 9, 0, 0, TimeSpan.Zero));
    private readonly User _user = new();
    private bool _available;
    private string Connection => BaseConnection + $"Database={_database};";

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
        try
        {
            await using var admin = new DbContext(new DbContextOptionsBuilder()
                .UseNpgsql(BaseConnection + "Database=postgres;").Options);
            await admin.Database.ExecuteSqlRawAsync($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE);");
        }
        catch { }
    }

    private AutoDbContext NewDb() => new(
        new DbContextOptionsBuilder<AutoDbContext>().UseNpgsql(Connection).Options, _user, _clock);

    [SkippableFact]
    public async Task Vehicle_keeps_legacy_color_and_notes()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        var saved = await new FleetService(db, _clock).SaveAsync(new Vehicle
            { Registration = "abc-1", Make = "Toyota", Model = "Hilux", Color = "White", Notes = "Pool vehicle" });
        db.ChangeTracker.Clear();
        var found = await new FleetService(db, _clock).GetAsync(saved.Value.Id);
        Assert.Equal("White", found!.Color);
        Assert.Equal("Pool vehicle", found.Notes);
    }

    [SkippableFact]
    public async Task Service_record_can_be_edited_and_recalculates_odometer()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        var fleet = new FleetService(db, _clock);
        var vehicle = await fleet.SaveAsync(new Vehicle { Registration = "ABC-2", Make = "Suzuki", Model = "Swift" });
        var first = await fleet.AddServiceAsync(new VehicleService { VehicleId = vehicle.Value.Id, Date = _clock.Today, Description = "Service", Odometer = 1000, Cost = 100 });
        await fleet.AddServiceAsync(new VehicleService { VehicleId = vehicle.Value.Id, Date = _clock.Today, Description = "Tyres", Odometer = 2000, Cost = 200 });
        first.Value.Odometer = 2500;
        first.Value.Cost = 125;
        var updated = await fleet.UpdateServiceAsync(first.Value);
        db.ChangeTracker.Clear();
        Assert.True(updated.Ok, updated.Error);
        Assert.Equal(2500, (await fleet.GetAsync(vehicle.Value.Id))!.CurrentOdometer);
    }

    [SkippableFact]
    public async Task Deleting_latest_service_restores_previous_odometer()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        var fleet = new FleetService(db, _clock);
        var vehicle = await fleet.SaveAsync(new Vehicle { Registration = "ABC-3", Make = "Honda", Model = "Civic" });
        await fleet.AddServiceAsync(new VehicleService { VehicleId = vehicle.Value.Id, Date = _clock.Today, Description = "First", Odometer = 1000 });
        var latest = await fleet.AddServiceAsync(new VehicleService { VehicleId = vehicle.Value.Id, Date = _clock.Today, Description = "Latest", Odometer = 2000 });
        Assert.True((await fleet.DeleteServiceAsync(latest.Value.Id)).Ok);
        db.ChangeTracker.Clear();
        Assert.Equal(1000, (await fleet.GetAsync(vehicle.Value.Id))!.CurrentOdometer);
    }

    [SkippableFact]
    public async Task Vehicle_requires_the_legacy_model_field()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        var result = await new FleetService(db, _clock).SaveAsync(new Vehicle
            { Registration = "ABC-MISSING", Make = "Toyota" });

        Assert.False(result.Ok);
        Assert.Equal("vehicle.no-model", result.Code);
    }

    [SkippableFact]
    public async Task Upcoming_maintenance_is_visible_from_vehicle_service_due_date()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        var fleet = new FleetService(db, _clock);
        var vehicle = await fleet.SaveAsync(new Vehicle
            { Registration = "ABC-DUE", Make = "Toyota", Model = "Corolla" });
        await fleet.AddServiceAsync(new VehicleService
        {
            VehicleId = vehicle.Value.Id,
            Date = _clock.Today.AddMonths(-1),
            Description = "Inspection",
            Kind = ServiceKind.Inspection,
            NextDueDate = _clock.Today.AddDays(10)
        });

        var upcoming = await fleet.UpcomingServicesAsync();

        Assert.Single(upcoming);
        Assert.Equal("ABC-DUE", upcoming[0].VehicleRegistration);
    }

    private sealed class User : ICurrentUser
    {
        public string? UserId => "fleet-user";
        public string? Name => "Fleet Manager";
        public string? Email => null;
        public bool IsAuthenticated => true;
        public bool Can(string permission) => true;
        public bool InModule(string moduleKey) => true;
        public IReadOnlyCollection<string> Roles { get; } = [];
    }
}
