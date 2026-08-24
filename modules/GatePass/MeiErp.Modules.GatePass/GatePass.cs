using MeiErp.Platform.Kernel;
using MeiErp.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeiErp.Modules.GatePass;

/// <summary>
/// Authority for goods to pass the gate.
///
/// The point of the module is the separation: whoever raises a pass cannot mark
/// the goods through. One person doing both is exactly how things leave without
/// anybody noticing.
/// </summary>
public class Pass : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    public string Number { get; set; } = "";
    public DateOnly Date { get; set; }

    public PassDirection Direction { get; set; }

    /// <summary>Whether the goods are expected back, and by when.</summary>
    public bool IsReturnable { get; set; }
    public DateOnly? ExpectedBack { get; set; }

    /// <summary>Who or where the goods are going to, or coming from.</summary>
    public string PartyName { get; set; } = "";
    public string? PersonPhone { get; set; }
    public string? PersonCnic { get; set; }
    public string? CompanyName { get; set; }
    public string? Department { get; set; }

    public string? VehicleNumber { get; set; }
    public string? DriverName { get; set; }

    public string? Purpose { get; set; }
    public string? Notes { get; set; }
    public string? ReferenceType { get; set; }
    public string? ReferenceNumber { get; set; }

    public string RaisedByUserId { get; set; } = "";
    public string RaisedByName { get; set; } = "";

    public PassStatus Status { get; set; } = PassStatus.Issued;

    /// <summary>Set when security actually let the goods through.</summary>
    public DateTime? ClearedUtc { get; set; }
    public string? ClearedByUserId { get; set; }
    public string? ClearedByName { get; set; }
    public DateTime? CancelledUtc { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? ReturnedUtc { get; set; }
    public string? ReturnReceivedByName { get; set; }

    public List<PassItem> Items { get; set; } = [];

    /// <summary>
    /// Editable only until it leaves Issued. Once security has cleared it, the
    /// paper in their hand and the record must agree.
    /// </summary>
    public bool IsEditable => Status is PassStatus.Issued;

    /// <summary>A returnable pass stays open until every item is ticked back.</summary>
    public bool IsFullyReturned =>
        Items.Count > 0 && Items.All(i => i.ReturnedQuantity >= i.Quantity);

    public bool IsOverdue(DateOnly today) =>
        IsReturnable && ExpectedBack is not null
        && ExpectedBack < today && !IsFullyReturned
        && Status is not (PassStatus.Returned or PassStatus.Cancelled);
}

public enum PassDirection
{
    /// <summary>Goods leaving the premises.</summary>
    Outward = 0,

    /// <summary>Goods arriving.</summary>
    Inward = 1
}

public enum PassStatus
{
    /// <summary>Raised, not yet through the gate.</summary>
    Issued = 0,

    /// <summary>Security has let it through.</summary>
    Cleared = 1,

    /// <summary>Everything that went out has come back.</summary>
    Returned = 2,

    /// <summary>Some of it has come back.</summary>
    PartiallyReturned = 3,

    Cancelled = 4
}

public class PassItem : Entity
{
    public int PassId { get; set; }
    public Pass? Pass { get; set; }

    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "each";

    public string? SerialNumber { get; set; }
    public string? Remarks { get; set; }

    /// <summary>How much has come back. Only meaningful on a returnable pass.</summary>
    public decimal ReturnedQuantity { get; set; }

    public decimal Outstanding => Quantity - ReturnedQuantity;
}

public class GatePassDbContext(
    DbContextOptions<GatePassDbContext> options, ICurrentUser currentUser, IClock clock)
    : ModuleDbContext(options, currentUser, clock)
{
    protected override string Schema => "gatepass";

    public DbSet<Pass> Passes => Set<Pass>();
    public DbSet<PassItem> PassItems => Set<PassItem>();
    public DbSet<DemoIssuance> DemoIssuances => Set<DemoIssuance>();
    public DbSet<DemoIssuanceItem> DemoIssuanceItems => Set<DemoIssuanceItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Pass>(b =>
        {
            b.Property(p => p.Number).HasMaxLength(30).IsRequired();
            b.HasIndex(p => p.Number).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.Property(p => p.PartyName).HasMaxLength(200).IsRequired();
            b.Property(p => p.PersonPhone).HasMaxLength(50);
            b.Property(p => p.PersonCnic).HasMaxLength(30);
            b.Property(p => p.CompanyName).HasMaxLength(200);
            b.Property(p => p.Department).HasMaxLength(150);
            b.Property(p => p.VehicleNumber).HasMaxLength(30);
            b.Property(p => p.DriverName).HasMaxLength(200);
            b.Property(p => p.Purpose).HasMaxLength(500);
            b.Property(p => p.Notes).HasMaxLength(2000);
            b.Property(p => p.ReferenceType).HasMaxLength(100);
            b.Property(p => p.ReferenceNumber).HasMaxLength(100);
            b.Property(p => p.CancellationReason).HasMaxLength(1000);
            b.Property(p => p.ReturnReceivedByName).HasMaxLength(200);

            b.HasMany(p => p.Items).WithOne(i => i.Pass)
             .HasForeignKey(i => i.PassId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(p => new { p.Status, p.Date });

            b.Ignore(p => p.IsEditable);
            b.Ignore(p => p.IsFullyReturned);
        });

        modelBuilder.Entity<PassItem>(b =>
        {
            b.Property(i => i.Description).HasMaxLength(300).IsRequired();
            b.Property(i => i.Unit).HasMaxLength(20);
            b.Property(i => i.SerialNumber).HasMaxLength(100);
            b.Property(i => i.Remarks).HasMaxLength(500);
            b.Ignore(i => i.Outstanding);
            b.HasQueryFilter(i => !i.Pass!.IsDeleted);
        });

        modelBuilder.Entity<DemoIssuance>(b =>
        {
            b.Property(x=>x.Number).HasMaxLength(30).IsRequired(); b.HasIndex(x=>x.Number).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.Property(x=>x.CustomerName).HasMaxLength(200).IsRequired(); b.Property(x=>x.CustomerPhone).HasMaxLength(50);
            b.Property(x=>x.CustomerReference).HasMaxLength(100); b.Property(x=>x.Department).HasMaxLength(150);
            b.Property(x=>x.ReferenceLetter).HasMaxLength(150); b.Property(x=>x.IssuedByUserId).HasMaxLength(450);
            b.Property(x=>x.IssuedByName).HasMaxLength(200); b.Property(x=>x.ReceivedByName).HasMaxLength(200);
            b.Property(x=>x.ReturnCondition).HasMaxLength(1000); b.Property(x=>x.Notes).HasMaxLength(2000);
            b.HasMany(x=>x.Items).WithOne(x=>x.DemoIssuance).HasForeignKey(x=>x.DemoIssuanceId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x=>new{x.Status,x.ExpectedReturnOn});
        });
        modelBuilder.Entity<DemoIssuanceItem>(b =>
        {
            b.Property(x=>x.Description).HasMaxLength(300).IsRequired(); b.Property(x=>x.SerialNumber).HasMaxLength(100);
            b.Property(x=>x.Accessories).HasMaxLength(500); b.Property(x=>x.Remarks).HasMaxLength(500);
            b.HasQueryFilter(x=>!x.DemoIssuance!.IsDeleted);
        });
    }
}

public interface IGatePassService
{
    Task<IReadOnlyList<Pass>> ListAsync(PassStatus? status, CancellationToken ct = default);
    Task<Pass?> GetAsync(int id, CancellationToken ct = default);
    Task<Result<Pass>> SaveAsync(PassInput input, CancellationToken ct = default);

    /// <summary>
    /// Security letting the goods through. Separate from raising the pass, and
    /// gated on a different permission.
    /// </summary>
    Task<Result<Pass>> ClearAsync(int id, CancellationToken ct = default);

    /// <summary>Ticks returned quantities back in. Partial returns are the normal case.</summary>
    Task<Result<Pass>> ReceiveBackAsync(int id, IReadOnlyList<ReturnLine> lines, CancellationToken ct = default);

    Task<Result> CancelAsync(int id, string reason, CancellationToken ct = default);

    /// <summary>Returnable passes past their date with items still out.</summary>
    Task<IReadOnlyList<Pass>> OverdueAsync(CancellationToken ct = default);
}

public sealed record PassInput(
    int? Id, PassDirection Direction, DateOnly Date, string PartyName,
    bool IsReturnable, DateOnly? ExpectedBack,
    string? VehicleNumber, string? DriverName, string? Purpose,
    IReadOnlyList<PassItemInput> Items,
    string? PersonPhone = null, string? PersonCnic = null,
    string? CompanyName = null, string? Department = null,
    string? Notes = null, string? ReferenceType = null,
    string? ReferenceNumber = null);

public sealed record PassItemInput(string Description, decimal Quantity, string Unit, string? SerialNumber);

public sealed record ReturnLine(int ItemId, decimal Quantity);

public sealed class GatePassService(
    GatePassDbContext db, ICurrentUser currentUser, IClock clock) : IGatePassService
{
    public async Task<IReadOnlyList<Pass>> ListAsync(
        PassStatus? status, CancellationToken ct = default)
    {
        var query = db.Passes.AsNoTracking().Include(p => p.Items).AsQueryable();
        if (status is not null) query = query.Where(p => p.Status == status);
        return await query.OrderByDescending(p => p.Id).Take(300).ToListAsync(ct);
    }

    public Task<Pass?> GetAsync(int id, CancellationToken ct = default) =>
        db.Passes.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Result<Pass>> SaveAsync(PassInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.PartyName))
            return Result.Fail<Pass>("Say who the goods are going to or coming from.", "pass.no-party");

        if (input.Items.Count == 0)
            return Result.Fail<Pass>("A pass needs at least one item on it.", "pass.no-items");

        if (input.IsReturnable && input.ExpectedBack is null)
        {
            // A returnable pass with no date is one nobody ever chases.
            return Result.Fail<Pass>(
                "Say when the goods are expected back, or the pass will never show as overdue.",
                "pass.no-return-date");
        }

        Pass pass;

        if (input.Id is null or 0)
        {
            pass = new Pass
            {
                Number = await NextNumberAsync(input.Direction, ct),
                RaisedByUserId = currentUser.UserId ?? "",
                RaisedByName = currentUser.Name ?? "",
                Status = PassStatus.Issued
            };
            db.Passes.Add(pass);
        }
        else
        {
            var existing = await db.Passes.Include(p => p.Items)
                .FirstOrDefaultAsync(p => p.Id == input.Id, ct);

            if (existing is null) return Result.Fail<Pass>("That pass no longer exists.", "pass.not-found");

            if (!existing.IsEditable)
            {
                // Security is holding a printed copy. If the record can still
                // change, the two stop matching and the pass proves nothing.
                return Result.Fail<Pass>(
                    "This pass has already been through the gate and cannot be changed.",
                    "pass.not-editable");
            }

            db.PassItems.RemoveRange(existing.Items);
            existing.Items.Clear();
            pass = existing;
        }

        pass.Direction = input.Direction;
        pass.Date = input.Date;
        pass.PartyName = input.PartyName;
        pass.IsReturnable = input.IsReturnable;
        pass.ExpectedBack = input.IsReturnable ? input.ExpectedBack : null;
        pass.VehicleNumber = input.VehicleNumber;
        pass.DriverName = input.DriverName;
        pass.Purpose = input.Purpose;
        pass.PersonPhone = input.PersonPhone;
        pass.PersonCnic = input.PersonCnic;
        pass.CompanyName = input.CompanyName;
        pass.Department = input.Department;
        pass.Notes = input.Notes;
        pass.ReferenceType = input.ReferenceType;
        pass.ReferenceNumber = input.ReferenceNumber;

        foreach (var item in input.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Description))
                return Result.Fail<Pass>("Every line needs a description.", "pass.no-description");

            if (item.Quantity <= 0)
                return Result.Fail<Pass>($"'{item.Description}' needs a quantity greater than nothing.", "pass.bad-quantity");

            pass.Items.Add(new PassItem
            {
                Description = item.Description,
                Quantity = item.Quantity,
                Unit = string.IsNullOrWhiteSpace(item.Unit) ? "each" : item.Unit,
                SerialNumber = item.SerialNumber
            });
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(pass);
    }

    public async Task<Result<Pass>> ClearAsync(int id, CancellationToken ct = default)
    {
        var pass = await db.Passes.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (pass is null) return Result.Fail<Pass>("That pass no longer exists.", "pass.not-found");

        if (pass.Status is not PassStatus.Issued)
            return Result.Fail<Pass>("This pass has already been through the gate.", "pass.already-cleared");

        // The rule the module exists for. Enforced in the service rather than
        // trusted to the UI, because the UI is the easy half to bypass.
        if (pass.RaisedByUserId == currentUser.UserId)
        {
            return Result.Fail<Pass>(
                "You raised this pass, so you cannot also clear it through the gate. " +
                "Someone on gate duty has to do that.",
                "pass.self-clearance");
        }

        pass.Status = PassStatus.Cleared;
        pass.ClearedUtc = clock.UtcNow;
        pass.ClearedByUserId = currentUser.UserId;
        pass.ClearedByName = currentUser.Name;

        await db.SaveChangesAsync(ct);
        return Result.Success(pass);
    }

    public async Task<Result<Pass>> ReceiveBackAsync(
        int id, IReadOnlyList<ReturnLine> lines, CancellationToken ct = default)
    {
        var pass = await db.Passes.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (pass is null) return Result.Fail<Pass>("That pass no longer exists.", "pass.not-found");

        if (!pass.IsReturnable)
            return Result.Fail<Pass>("These goods were not expected back.", "pass.not-returnable");

        if (pass.Status is PassStatus.Issued)
            return Result.Fail<Pass>("This pass has not been through the gate yet.", "pass.not-cleared");

        if (pass.Status is PassStatus.Cancelled)
            return Result.Fail<Pass>("This pass was cancelled.", "pass.cancelled");

        // Checked in full first, so a bad line does not leave half the pass
        // ticked back.
        foreach (var line in lines)
        {
            var item = pass.Items.FirstOrDefault(i => i.Id == line.ItemId);
            if (item is null)
                return Result.Fail<Pass>("Something was returned that is not on the pass.", "pass.not-on-pass");

            if (line.Quantity <= 0)
                return Result.Fail<Pass>($"'{item.Description}' needs a quantity greater than nothing.", "pass.bad-quantity");

            if (line.Quantity > item.Outstanding)
            {
                return Result.Fail<Pass>(
                    $"'{item.Description}': {line.Quantity:0.##} returned but only " +
                    $"{item.Outstanding:0.##} is still out.",
                    "pass.over-return");
            }
        }

        foreach (var line in lines)
            pass.Items.First(i => i.Id == line.ItemId).ReturnedQuantity += line.Quantity;

        // Stays open until the last item is ticked back - partial returns are
        // the normal case, not an exception.
        pass.Status = pass.IsFullyReturned ? PassStatus.Returned : PassStatus.PartiallyReturned;
        if (pass.IsFullyReturned)
        {
            pass.ReturnedUtc = clock.UtcNow;
            pass.ReturnReceivedByName = currentUser.Name;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(pass);
    }

    public async Task<Result> CancelAsync(int id, string reason, CancellationToken ct = default)
    {
        var pass = await db.Passes.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (pass is null) return Result.Fail("That pass no longer exists.", "pass.not-found");

        if (pass.Status is not PassStatus.Issued)
        {
            // The goods have gone. Cancelling now would erase the only record
            // that they left.
            return Result.Fail(
                "This pass has already been through the gate and cannot be cancelled.",
                "pass.already-cleared");
        }

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Fail("Say why the pass is being cancelled.", "pass.no-reason");

        pass.Status = PassStatus.Cancelled;
        pass.CancelledUtc = clock.UtcNow;
        pass.CancellationReason = reason.Trim();

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<IReadOnlyList<Pass>> OverdueAsync(CancellationToken ct = default)
    {
        var today = clock.Today;

        var candidates = await db.Passes.AsNoTracking()
            .Include(p => p.Items)
            .Where(p => p.IsReturnable
                     && p.ExpectedBack != null && p.ExpectedBack < today
                     && p.Status != PassStatus.Returned
                     && p.Status != PassStatus.Cancelled)
            .ToListAsync(ct);

        // The last filter depends on comparing every item's returned quantity
        // against its own, which is awkward in SQL and cheap at gate volumes.
        return [.. candidates.Where(p => !p.IsFullyReturned)];
    }

    private async Task<string> NextNumberAsync(PassDirection direction, CancellationToken ct)
    {
        var prefix = direction is PassDirection.Outward ? "GPO" : "GPI";
        var year = clock.Today.Year;
        var stem = $"{prefix}-{year % 100:D2}-";

        var count = await db.Passes.IgnoreQueryFilters()
            .CountAsync(p => p.Number.StartsWith(stem), ct);

        return stem + (count + 1).ToString().PadLeft(4, '0');
    }
}

public static class GatePassModule
{
    public const string Key = "gatepass";

    public const string PassesView = "gatepass.passes.view";
    public const string PassesRaise = "gatepass.passes.raise";

    /// <summary>Held by gate security, deliberately not by whoever raises passes.</summary>
    public const string PassesClear = "gatepass.passes.clear";

    public const string PassesReturn = "gatepass.passes.return";
    public const string ReportsView = "gatepass.reports.view";
    public const string DemosView = "gatepass.demos.view";
    public const string DemosManage = "gatepass.demos.manage";
    public const string DemosReturn = "gatepass.demos.return";

    public static ModuleDescriptor Descriptor => new()
    {
        Key = Key,
        Name = "Gate Pass",
        Description = "Authority for goods to enter or leave the premises.",
        BasePath = "/gatepass",
        Icon = "LocalShipping",
        Color = "#388e3c",
        SortOrder = 8,
        Schema = "gatepass",

        Permissions =
        [
            new(PassesView,   "Passes", "See gate passes"),
            new(PassesRaise,  "Passes", "Raise a gate pass"),
            new(PassesClear,  "Gate",   "Clear goods through the gate — held by security, not by the raiser"),
            new(PassesReturn, "Gate",   "Tick returnable goods back in"),
            new(ReportsView,  "Reports", "View outstanding and overdue returnable goods")
            ,new(DemosView,"Demo goods","View demo issuances")
            ,new(DemosManage,"Demo goods","Issue and edit demo goods")
            ,new(DemosReturn,"Demo goods","Record demo goods returning")
        ],

        Nav =
        [
            new("Gate passes", "/gatepass/passes", "LocalShipping", PassesView),
            new("Demo goods", "/gatepass/demos", "Inventory2", DemosView)
        ],

        RoleTemplates =
        [
            new("Gate Security", "Clears goods through the gate and receives returns.",
                [PassesView, PassesClear, PassesReturn, ReportsView, DemosView, DemosReturn]),

            new("Pass Raiser", "Raises passes but cannot clear them.",
                [PassesView, PassesRaise, DemosView, DemosManage])
        ]
    };

    public static IServiceCollection AddGatePassModule(
        this IServiceCollection services, IConfiguration config)
    {
        var connection = config.GetConnectionString("Platform")
            ?? throw new InvalidOperationException("No 'Platform' connection string for the Gate Pass module.");

        services.AddDbContext<GatePassDbContext>(options =>
            options.UseNpgsql(connection, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__migrations", "gatepass");
                npgsql.EnableRetryOnFailure(3);
            }));

        services.AddScoped<IGatePassService, GatePassService>();
        services.AddScoped<IScanResolver, GatePassScanResolver>();
        services.AddScoped<IDemoIssuanceService, DemoIssuanceService>();
        return services;
    }
}

public static class GatePassSeederExtensions
{
    public static async Task SeedGatePassAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatePassDbContext>();
        await db.Database.MigrateAsync();
    }
}
