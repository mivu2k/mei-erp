using MeiErp.Platform.Kernel;
using MeiErp.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeiErp.Modules.Auto;

/// <summary>A company vehicle.</summary>
public class Vehicle : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    public string Registration { get; set; } = "";
    public string Make { get; set; } = "";
    public string? Model { get; set; }
    public int? Year { get; set; }

    public string? ChassisNumber { get; set; }
    public string? EngineNumber { get; set; }

    /// <summary>Who normally drives it. A name, not a link - drivers are often not system users.</summary>
    public string? AssignedTo { get; set; }
    public string? DepartmentId { get; set; }

    public DateOnly? PurchasedOn { get; set; }
    public decimal? PurchaseCost { get; set; }

    /// <summary>Latest odometer reading, updated by whichever service record is newest.</summary>
    public int? CurrentOdometer { get; set; }

    public DateOnly? RegistrationExpiry { get; set; }
    public DateOnly? InsuranceExpiry { get; set; }

    public VehicleStatus Status { get; set; } = VehicleStatus.Active;

    public List<VehicleService> Services { get; set; } = [];

    /// <summary>
    /// Whether a date falls due within the warning window. Takes the date as a
    /// parameter rather than reading a clock, so the boundary is testable.
    /// </summary>
    public bool RegistrationDueBy(DateOnly today, int withinDays = 30) =>
        RegistrationExpiry is not null && RegistrationExpiry <= today.AddDays(withinDays);

    public bool InsuranceDueBy(DateOnly today, int withinDays = 30) =>
        InsuranceExpiry is not null && InsuranceExpiry <= today.AddDays(withinDays);
}

public enum VehicleStatus
{
    Active = 0,
    UnderRepair = 1,
    Sold = 2,
    Scrapped = 3
}

/// <summary>One visit to the workshop, or one fuel fill.</summary>
public class VehicleService : AuditableEntity
{
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    public string VehicleRegistration { get; set; } = "";

    public DateOnly Date { get; set; }
    public ServiceKind Kind { get; set; }

    public string Description { get; set; } = "";
    public string? Vendor { get; set; }

    public decimal Cost { get; set; }

    /// <summary>Reading at the time. What makes cost-per-kilometre calculable.</summary>
    public int? Odometer { get; set; }

    /// <summary>When the next one is due, for the reminder list.</summary>
    public DateOnly? NextDueDate { get; set; }
    public int? NextDueOdometer { get; set; }

    public string? InvoiceNumber { get; set; }
}

public enum ServiceKind
{
    Routine = 0,
    Repair = 1,
    Tyres = 2,
    Fuel = 3,
    Insurance = 4,
    Registration = 5,
    Other = 6
}

public class AutoDbContext(
    DbContextOptions<AutoDbContext> options, ICurrentUser currentUser, IClock clock)
    : ModuleDbContext(options, currentUser, clock)
{
    protected override string Schema => "auto";

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<VehicleService> Services => Set<VehicleService>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Vehicle>(b =>
        {
            b.Property(v => v.Registration).HasMaxLength(30).IsRequired();
            b.Property(v => v.Make).HasMaxLength(60).IsRequired();
            b.Property(v => v.Model).HasMaxLength(60);
            b.Property(v => v.AssignedTo).HasMaxLength(200);

            // Two vehicles on one plate would merge their service history.
            b.HasIndex(v => v.Registration).IsUnique().HasFilter("\"IsDeleted\" = false");

            b.HasMany(v => v.Services).WithOne(s => s.Vehicle)
             .HasForeignKey(s => s.VehicleId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(v => v.Status);
        });

        modelBuilder.Entity<VehicleService>(b =>
        {
            b.Property(s => s.Description).HasMaxLength(500).IsRequired();
            b.Property(s => s.Vendor).HasMaxLength(200);
            b.Property(s => s.InvoiceNumber).HasMaxLength(50);
            b.Property(s => s.VehicleRegistration).HasMaxLength(30);

            b.HasIndex(s => new { s.VehicleId, s.Date });

            // Children restate the parent's filter, or a deleted vehicle's
            // services still show up in a cost report.
            b.HasQueryFilter(s => !s.Vehicle!.IsDeleted);
        });
    }
}

public interface IFleetService
{
    Task<IReadOnlyList<Vehicle>> ListAsync(bool includeDisposed, CancellationToken ct = default);
    Task<Vehicle?> GetAsync(int id, CancellationToken ct = default);
    Task<Result<Vehicle>> SaveAsync(Vehicle vehicle, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<VehicleService>> ServicesAsync(int vehicleId, CancellationToken ct = default);
    Task<Result<VehicleService>> AddServiceAsync(VehicleService service, CancellationToken ct = default);

    /// <summary>Vehicles whose registration or insurance falls due soon.</summary>
    Task<IReadOnlyList<Vehicle>> ExpiringAsync(int withinDays = 30, CancellationToken ct = default);

    /// <summary>Total spend per vehicle over a period, for the running-cost report.</summary>
    Task<IReadOnlyList<VehicleCost>> CostsAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}

public sealed record VehicleCost(
    int VehicleId, string Registration, decimal Fuel, decimal Maintenance, decimal Other)
{
    public decimal Total => Fuel + Maintenance + Other;
}

public sealed class FleetService(AutoDbContext db, IClock clock) : IFleetService
{
    public async Task<IReadOnlyList<Vehicle>> ListAsync(
        bool includeDisposed, CancellationToken ct = default)
    {
        var query = db.Vehicles.AsNoTracking().AsQueryable();

        if (!includeDisposed)
            query = query.Where(v => v.Status == VehicleStatus.Active
                                  || v.Status == VehicleStatus.UnderRepair);

        return await query.OrderBy(v => v.Registration).ToListAsync(ct);
    }

    public Task<Vehicle?> GetAsync(int id, CancellationToken ct = default) =>
        db.Vehicles.FirstOrDefaultAsync(v => v.Id == id, ct);

    public async Task<Result<Vehicle>> SaveAsync(Vehicle vehicle, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vehicle.Registration))
            return Result.Fail<Vehicle>("A vehicle needs a registration number.", "vehicle.no-registration");

        if (string.IsNullOrWhiteSpace(vehicle.Make))
            return Result.Fail<Vehicle>("A vehicle needs a make.", "vehicle.no-make");

        vehicle.Registration = vehicle.Registration.Trim().ToUpperInvariant();

        var taken = await db.Vehicles
            .AnyAsync(v => v.Registration == vehicle.Registration && v.Id != vehicle.Id, ct);

        if (taken)
            return Result.Fail<Vehicle>($"{vehicle.Registration} is already on the fleet.", "vehicle.duplicate");

        if (vehicle.Id == 0)
        {
            db.Vehicles.Add(vehicle);
        }
        else
        {
            var existing = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == vehicle.Id, ct);
            if (existing is null) return Result.Fail<Vehicle>("That vehicle no longer exists.", "vehicle.not-found");

            // The odometer is owned by the service records, which is what keeps
            // it moving forward rather than being typed backwards by hand.
            var odometer = db.Entry(existing).OriginalValues.GetValue<int?>(nameof(Vehicle.CurrentOdometer));
            db.Entry(existing).CurrentValues.SetValues(vehicle);
            existing.CurrentOdometer = odometer;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(vehicle);
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken ct = default)
    {
        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (vehicle is null) return Result.Fail("That vehicle no longer exists.", "vehicle.not-found");

        var hasHistory = await db.Services.AnyAsync(s => s.VehicleId == id, ct);
        if (hasHistory)
        {
            // Its running costs are part of the fleet's history, and a disposed
            // vehicle still has to appear in last year's figures.
            return Result.Fail(
                "This vehicle has service history. Mark it Sold or Scrapped instead - " +
                "deleting it would take its running costs out of every past report.",
                "vehicle.has-history");
        }

        db.Vehicles.Remove(vehicle);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<IReadOnlyList<VehicleService>> ServicesAsync(
        int vehicleId, CancellationToken ct = default) =>
        await db.Services.AsNoTracking()
            .Where(s => s.VehicleId == vehicleId)
            .OrderByDescending(s => s.Date)
            .ToListAsync(ct);

    public async Task<Result<VehicleService>> AddServiceAsync(
        VehicleService service, CancellationToken ct = default)
    {
        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == service.VehicleId, ct);
        if (vehicle is null) return Result.Fail<VehicleService>("That vehicle no longer exists.", "vehicle.not-found");

        if (service.Cost < 0)
            return Result.Fail<VehicleService>("A cost cannot be negative.", "service.negative-cost");

        if (string.IsNullOrWhiteSpace(service.Description))
            return Result.Fail<VehicleService>("Say what was done.", "service.no-description");

        if (service.Odometer is not null)
        {
            if (service.Odometer < 0)
                return Result.Fail<VehicleService>("An odometer reading cannot be negative.", "service.bad-odometer");

            // A reading below the last one is a typo, and left in it makes
            // cost-per-kilometre nonsense for every period after it.
            if (vehicle.CurrentOdometer is not null && service.Odometer < vehicle.CurrentOdometer)
            {
                return Result.Fail<VehicleService>(
                    $"The reading {service.Odometer:N0} is below the last recorded " +
                    $"{vehicle.CurrentOdometer:N0}. Check the figure.",
                    "service.odometer-went-backwards");
            }

            vehicle.CurrentOdometer = service.Odometer;
        }

        service.VehicleRegistration = vehicle.Registration;

        // Keep the vehicle's own expiry dates in step, so the reminder list
        // reads from one place rather than scanning history.
        if (service.Kind is ServiceKind.Insurance && service.NextDueDate is not null)
            vehicle.InsuranceExpiry = service.NextDueDate;

        if (service.Kind is ServiceKind.Registration && service.NextDueDate is not null)
            vehicle.RegistrationExpiry = service.NextDueDate;

        db.Services.Add(service);
        await db.SaveChangesAsync(ct);

        return Result.Success(service);
    }

    public async Task<IReadOnlyList<Vehicle>> ExpiringAsync(
        int withinDays = 30, CancellationToken ct = default)
    {
        var cutoff = clock.Today.AddDays(withinDays);

        return await db.Vehicles.AsNoTracking()
            .Where(v => (v.Status == VehicleStatus.Active || v.Status == VehicleStatus.UnderRepair)
                     && ((v.RegistrationExpiry != null && v.RegistrationExpiry <= cutoff)
                      || (v.InsuranceExpiry != null && v.InsuranceExpiry <= cutoff)))
            .OrderBy(v => v.RegistrationExpiry ?? v.InsuranceExpiry)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<VehicleCost>> CostsAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var rows = await db.Services.AsNoTracking()
            .Where(s => s.Date >= from && s.Date <= to)
            .GroupBy(s => new { s.VehicleId, s.VehicleRegistration })
            .Select(g => new
            {
                g.Key.VehicleId,
                g.Key.VehicleRegistration,
                Fuel = g.Where(s => s.Kind == ServiceKind.Fuel).Sum(s => s.Cost),
                Maintenance = g.Where(s => s.Kind == ServiceKind.Routine
                                        || s.Kind == ServiceKind.Repair
                                        || s.Kind == ServiceKind.Tyres).Sum(s => s.Cost),
                Other = g.Where(s => s.Kind == ServiceKind.Insurance
                                  || s.Kind == ServiceKind.Registration
                                  || s.Kind == ServiceKind.Other).Sum(s => s.Cost)
            })
            .ToListAsync(ct);

        return [.. rows
            .Select(r => new VehicleCost(r.VehicleId, r.VehicleRegistration, r.Fuel, r.Maintenance, r.Other))
            .OrderByDescending(r => r.Total)];
    }
}

public static class AutoModule
{
    public const string Key = "auto";

    public const string VehiclesView = "auto.vehicles.view";
    public const string VehiclesManage = "auto.vehicles.manage";
    public const string ServicesManage = "auto.services.manage";

    public static ModuleDescriptor Descriptor => new()
    {
        Key = Key,
        Name = "Fleet",
        Description = "Company vehicles, servicing and running costs.",
        BasePath = "/auto",
        Icon = "DirectionsCar",
        Color = "#5d4037",
        SortOrder = 5,
        Schema = "auto",

        Permissions =
        [
            new(VehiclesView,   "Vehicles", "See the fleet and its running costs"),
            new(VehiclesManage, "Vehicles", "Add and edit vehicles"),
            new(ServicesManage, "Servicing", "Record servicing, fuel and repairs")
        ],

        Nav =
        [
            new("Vehicles", "/auto/vehicles", "DirectionsCar", VehiclesView)
        ],

        RoleTemplates =
        [
            new("Fleet Manager", "Manages vehicles and their service records.",
                [VehiclesView, VehiclesManage, ServicesManage])
        ]
    };

    public static IServiceCollection AddAutoModule(
        this IServiceCollection services, IConfiguration config)
    {
        var connection = config.GetConnectionString("Platform")
            ?? throw new InvalidOperationException("No 'Platform' connection string for the Fleet module.");

        services.AddDbContext<AutoDbContext>(options =>
            options.UseNpgsql(connection, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__migrations", "auto");
                npgsql.EnableRetryOnFailure(3);
            }));

        services.AddScoped<IFleetService, FleetService>();
        return services;
    }
}

public static class AutoSeederExtensions
{
    public static async Task SeedAutoAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoDbContext>();
        await db.Database.MigrateAsync();
    }
}
