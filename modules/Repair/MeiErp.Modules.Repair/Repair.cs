using MeiErp.Platform.Kernel;
using MeiErp.Platform.Persistence;
using MeiErp.Platform.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeiErp.Modules.Repair;

/// <summary>Someone whose equipment we repair.</summary>
public class Customer : AuditableEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// One device on the bench.
///
/// The status is a state machine rather than free text: a job that can be typed
/// into any state is a job nobody can report on.
/// </summary>
public class Job : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    public string Number { get; set; } = "";
    public DateOnly ReceivedOn { get; set; }

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string CustomerName { get; set; } = "";

    public string DeviceType { get; set; } = "";
    public string? Make { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }

    /// <summary>What the customer says is wrong.</summary>
    public string ReportedFault { get; set; } = "";

    /// <summary>What the technician found. Narrative - it prices nothing.</summary>
    public string? Diagnosis { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Received;

    public string? AssignedToUserId { get; set; }
    public string? AssignedToName { get; set; }

    public DateOnly? PromisedOn { get; set; }
    public DateTime? DeliveredUtc { get; set; }

    /// <summary>Who collected it. A delivery note is signed against a name.</summary>
    public string? CollectedBy { get; set; }

    /// <summary>
    /// The priced list of what was done. A quotation is built from these and
    /// from nothing else, which is why <see cref="Diagnosis"/> stays narrative.
    /// </summary>
    public List<WorkItem> WorkItems { get; set; } = [];

    public int? ApprovalRequestId { get; set; }
    public string? DecisionComment { get; set; }

    /// <summary>Only billable lines reach a price.</summary>
    public decimal Total => WorkItems.Where(w => w.IsBillable).Sum(w => w.LineTotal);

    public bool IsOpen => Status is not (JobStatus.Delivered or JobStatus.Cancelled);
}

/// <summary>
/// The pipeline, as a state machine. Delivered and Cancelled are terminal.
/// </summary>
public enum JobStatus
{
    Received = 0,
    Diagnosing = 1,

    /// <summary>Quoted, waiting for the customer to say yes.</summary>
    AwaitingApproval = 2,

    InProgress = 3,
    Completed = 4,
    Delivered = 5,
    Cancelled = 6
}

/// <summary>A part fitted, or an hour worked. What a quotation is built from.</summary>
public class WorkItem : Entity
{
    public int JobId { get; set; }
    public Job? Job { get; set; }

    public WorkItemKind Kind { get; set; }

    public string Description { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }

    /// <summary>What it cost us, where known. Null means no margin, not a nil one.</summary>
    public decimal? UnitCost { get; set; }

    /// <summary>Goodwill and warranty work is recorded but never charged.</summary>
    public bool IsBillable { get; set; } = true;

    public decimal LineTotal => Quantity * UnitPrice;

    /// <summary>Null rather than zero when no cost is known — an unpriced line has no margin.</summary>
    public decimal? Margin => UnitCost is null ? null : (UnitPrice - UnitCost.Value) * Quantity;
}

public enum WorkItemKind
{
    Part = 0,
    Labour = 1,
    Service = 2,
    Other = 3
}

public class RepairDbContext(
    DbContextOptions<RepairDbContext> options, ICurrentUser currentUser, IClock clock)
    : ModuleDbContext(options, currentUser, clock)
{
    protected override string Schema => "repair";

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Customer>(b =>
        {
            b.Property(c => c.Code).HasMaxLength(30).IsRequired();
            b.Property(c => c.Name).HasMaxLength(200).IsRequired();
            b.HasIndex(c => c.Code).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.HasIndex(c => c.Name);
        });

        modelBuilder.Entity<Job>(b =>
        {
            b.Property(j => j.Number).HasMaxLength(30).IsRequired();
            b.HasIndex(j => j.Number).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.Property(j => j.CustomerName).HasMaxLength(200);
            b.Property(j => j.DeviceType).HasMaxLength(100).IsRequired();
            b.Property(j => j.Make).HasMaxLength(60);
            b.Property(j => j.Model).HasMaxLength(60);
            b.Property(j => j.SerialNumber).HasMaxLength(100);
            b.Property(j => j.ReportedFault).HasMaxLength(1000).IsRequired();
            b.Property(j => j.Diagnosis).HasMaxLength(2000);
            b.Property(j => j.CollectedBy).HasMaxLength(200);
            b.Property(j => j.DecisionComment).HasMaxLength(2000);

            b.HasOne(j => j.Customer).WithMany()
             .HasForeignKey(j => j.CustomerId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasMany(j => j.WorkItems).WithOne(w => w.Job)
             .HasForeignKey(w => w.JobId)
             .OnDelete(DeleteBehavior.Cascade);

            // The tracking board reads this way.
            b.HasIndex(j => new { j.Status, j.ReceivedOn });
            b.HasIndex(j => j.SerialNumber);

            b.Ignore(j => j.Total);
            b.Ignore(j => j.IsOpen);
        });

        modelBuilder.Entity<WorkItem>(b =>
        {
            b.Property(w => w.Description).HasMaxLength(300).IsRequired();
            b.Ignore(w => w.LineTotal);
            b.Ignore(w => w.Margin);
            b.HasQueryFilter(w => !w.Job!.IsDeleted);
        });
    }
}

/// <summary>
/// The pipeline's rules, as pure functions.
///
/// Kept out of the service so the transitions can be tested directly - the
/// question "can this job go from here to there" should not need a database.
/// </summary>
public static class JobWorkflow
{
    /// <summary>Which states a job may move to from where it is.</summary>
    public static IReadOnlyList<JobStatus> Next(JobStatus from) => from switch
    {
        JobStatus.Received => [JobStatus.Diagnosing, JobStatus.Cancelled],
        JobStatus.Diagnosing => [JobStatus.AwaitingApproval, JobStatus.InProgress, JobStatus.Cancelled],
        JobStatus.AwaitingApproval => [JobStatus.InProgress, JobStatus.Cancelled],
        JobStatus.InProgress => [JobStatus.Completed, JobStatus.Cancelled],
        JobStatus.Completed => [JobStatus.Delivered, JobStatus.Cancelled],

        // Terminal. A delivered device is with its owner; a cancelled job was
        // never done. Neither can be reopened - raise a new job instead.
        _ => []
    };

    public static bool CanMove(JobStatus from, JobStatus to) => Next(from).Contains(to);

    /// <summary>States a job is still being worked in. A List, not an array — see CLAUDE.md.</summary>
    public static readonly List<JobStatus> Open =
    [
        JobStatus.Received, JobStatus.Diagnosing,
        JobStatus.AwaitingApproval, JobStatus.InProgress, JobStatus.Completed
    ];
}

public interface IRepairService
{
    Task<IReadOnlyList<Job>> ListAsync(JobStatus? status, string? search, CancellationToken ct = default);
    Task<Job?> GetAsync(int id, CancellationToken ct = default);
    Task<Result<Job>> SaveAsync(JobInput input, CancellationToken ct = default);

    /// <summary>Moves a job along the pipeline, refusing anything the state machine disallows.</summary>
    Task<Result<Job>> MoveAsync(int id, JobStatus to, string? note, CancellationToken ct = default);

    Task<Result<Job>> SetWorkItemsAsync(int id, IReadOnlyList<WorkItemInput> items, CancellationToken ct = default);
    Task<Result<Job>> DeliverAsync(int id, string collectedBy, CancellationToken ct = default);

    Task<IReadOnlyList<Customer>> CustomersAsync(string? search, CancellationToken ct = default);
    Task<Result<Customer>> SaveCustomerAsync(Customer customer, CancellationToken ct = default);
}

public sealed record JobInput(
    int? Id, int CustomerId, DateOnly ReceivedOn,
    string DeviceType, string? Make, string? Model, string? SerialNumber,
    string ReportedFault, DateOnly? PromisedOn);

public sealed record WorkItemInput(
    WorkItemKind Kind, string Description, decimal Quantity,
    decimal UnitPrice, decimal? UnitCost, bool IsBillable);

public sealed class RepairService(
    RepairDbContext db, ICurrentUser currentUser, IClock clock) : IRepairService
{
    public async Task<IReadOnlyList<Job>> ListAsync(
        JobStatus? status, string? search, CancellationToken ct = default)
    {
        var query = db.Jobs.AsNoTracking().Include(j => j.WorkItems).AsQueryable();

        if (status is not null) query = query.Where(j => j.Status == status);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(j =>
                EF.Functions.ILike(j.Number, pattern) ||
                EF.Functions.ILike(j.CustomerName, pattern) ||
                (j.SerialNumber != null && EF.Functions.ILike(j.SerialNumber, pattern)));
        }

        return await query.OrderByDescending(j => j.Id).Take(300).ToListAsync(ct);
    }

    public Task<Job?> GetAsync(int id, CancellationToken ct = default) =>
        db.Jobs.Include(j => j.WorkItems).Include(j => j.Customer)
              .FirstOrDefaultAsync(j => j.Id == id, ct);

    public async Task<Result<Job>> SaveAsync(JobInput input, CancellationToken ct = default)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == input.CustomerId, ct);
        if (customer is null) return Result.Fail<Job>("That customer no longer exists.", "job.no-customer");

        if (string.IsNullOrWhiteSpace(input.DeviceType))
            return Result.Fail<Job>("Say what kind of device it is.", "job.no-device");

        if (string.IsNullOrWhiteSpace(input.ReportedFault))
            return Result.Fail<Job>("Record what the customer says is wrong.", "job.no-fault");

        Job job;

        if (input.Id is null or 0)
        {
            job = new Job
            {
                Number = await NextNumberAsync(ct),
                Status = JobStatus.Received
            };
            db.Jobs.Add(job);
        }
        else
        {
            var existing = await db.Jobs.FirstOrDefaultAsync(j => j.Id == input.Id, ct);
            if (existing is null) return Result.Fail<Job>("That job no longer exists.", "job.not-found");

            if (!existing.IsOpen)
            {
                return Result.Fail<Job>(
                    "This job is closed and cannot be edited.", "job.closed");
            }

            job = existing;
        }

        job.CustomerId = customer.Id;
        job.CustomerName = customer.Name;
        job.ReceivedOn = input.ReceivedOn;
        job.DeviceType = input.DeviceType;
        job.Make = input.Make;
        job.Model = input.Model;
        job.SerialNumber = input.SerialNumber;
        job.ReportedFault = input.ReportedFault;
        job.PromisedOn = input.PromisedOn;

        await db.SaveChangesAsync(ct);
        return Result.Success(job);
    }

    public async Task<Result<Job>> MoveAsync(
        int id, JobStatus to, string? note, CancellationToken ct = default)
    {
        var job = await db.Jobs.Include(j => j.WorkItems).FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null) return Result.Fail<Job>("That job no longer exists.", "job.not-found");

        if (!JobWorkflow.CanMove(job.Status, to))
        {
            var allowed = JobWorkflow.Next(job.Status);

            return Result.Fail<Job>(
                allowed.Count == 0
                    ? $"A {job.Status} job is finished and cannot be moved. Raise a new job instead."
                    : $"A {job.Status} job can only move to {string.Join(" or ", allowed)}.",
                "job.bad-transition");
        }

        // Delivery is its own step: it needs a name to sign against, which a
        // plain status change has no way to capture.
        if (to is JobStatus.Delivered)
            return Result.Fail<Job>("Use the delivery step, which records who collected it.", "job.use-deliver");

        if (to is JobStatus.AwaitingApproval && job.WorkItems.Count == 0)
        {
            // Asking a customer to approve nothing wastes their time and ours.
            return Result.Fail<Job>(
                "Add what the work involves before sending it for the customer's approval.",
                "job.nothing-to-quote");
        }

        if (to is JobStatus.Diagnosing && job.AssignedToUserId is null)
        {
            job.AssignedToUserId = currentUser.UserId;
            job.AssignedToName = currentUser.Name;
        }

        job.Status = to;
        if (!string.IsNullOrWhiteSpace(note)) job.DecisionComment = note;

        await db.SaveChangesAsync(ct);
        return Result.Success(job);
    }

    public async Task<Result<Job>> SetWorkItemsAsync(
        int id, IReadOnlyList<WorkItemInput> items, CancellationToken ct = default)
    {
        var job = await db.Jobs.Include(j => j.WorkItems).FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null) return Result.Fail<Job>("That job no longer exists.", "job.not-found");

        if (!job.IsOpen)
            return Result.Fail<Job>("This job is closed and its work cannot be changed.", "job.closed");

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Description))
                return Result.Fail<Job>("Every line needs a description.", "job.no-description");

            if (item.Quantity <= 0)
                return Result.Fail<Job>($"'{item.Description}' needs a quantity greater than nothing.", "job.bad-quantity");

            if (item.UnitPrice < 0)
                return Result.Fail<Job>("A price cannot be negative.", "job.negative-price");
        }

        db.WorkItems.RemoveRange(job.WorkItems);
        job.WorkItems.Clear();

        foreach (var item in items)
        {
            job.WorkItems.Add(new WorkItem
            {
                Kind = item.Kind,
                Description = item.Description,
                Quantity = item.Quantity,

                // Non-billable work never reaches a price, so it cannot leak
                // onto a quotation by accident.
                UnitPrice = item.IsBillable ? item.UnitPrice : 0,
                UnitCost = item.UnitCost,
                IsBillable = item.IsBillable
            });
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(job);
    }

    public async Task<Result<Job>> DeliverAsync(
        int id, string collectedBy, CancellationToken ct = default)
    {
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null) return Result.Fail<Job>("That job no longer exists.", "job.not-found");

        if (job.Status is not JobStatus.Completed)
            return Result.Fail<Job>("Only a completed job can be delivered.", "job.not-completed");

        if (string.IsNullOrWhiteSpace(collectedBy))
        {
            // The delivery note is signed against this. Without it there is no
            // record of who took the device away.
            return Result.Fail<Job>("Record who collected the device.", "job.no-collector");
        }

        job.Status = JobStatus.Delivered;
        job.CollectedBy = collectedBy;
        job.DeliveredUtc = clock.UtcNow;

        await db.SaveChangesAsync(ct);
        return Result.Success(job);
    }

    public async Task<IReadOnlyList<Customer>> CustomersAsync(
        string? search, CancellationToken ct = default)
    {
        var query = db.Customers.AsNoTracking().Where(c => c.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.Name, pattern) ||
                (c.Phone != null && EF.Functions.ILike(c.Phone, pattern)));
        }

        return await query.OrderBy(c => c.Name).Take(200).ToListAsync(ct);
    }

    public async Task<Result<Customer>> SaveCustomerAsync(
        Customer customer, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(customer.Name))
            return Result.Fail<Customer>("A customer needs a name.", "customer.no-name");

        if (string.IsNullOrWhiteSpace(customer.Code))
        {
            var count = await db.Customers.IgnoreQueryFilters().CountAsync(ct);
            customer.Code = $"C-{count + 1:D5}";
        }

        if (customer.Id == 0)
        {
            db.Customers.Add(customer);
        }
        else
        {
            var existing = await db.Customers.FirstOrDefaultAsync(c => c.Id == customer.Id, ct);
            if (existing is null) return Result.Fail<Customer>("That customer no longer exists.", "customer.not-found");
            db.Entry(existing).CurrentValues.SetValues(customer);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(customer);
    }

    private async Task<string> NextNumberAsync(CancellationToken ct)
    {
        var year = clock.Today.Year;
        var stem = $"JOB-{year % 100:D2}-";
        var count = await db.Jobs.IgnoreQueryFilters().CountAsync(j => j.Number.StartsWith(stem), ct);
        return stem + (count + 1).ToString().PadLeft(4, '0');
    }
}

public static class RepairModule
{
    public const string Key = "repair";

    public const string JobsView = "repair.jobs.view";
    public const string JobsManage = "repair.jobs.manage";
    public const string JobsDeliver = "repair.jobs.deliver";
    public const string CustomersManage = "repair.customers.manage";
    public const string CostsView = "repair.costs.view";

    public static ModuleDescriptor Descriptor => new()
    {
        Key = Key,
        Name = "Repair",
        Description = "Devices on the bench, from intake to delivery.",
        BasePath = "/repair",
        Icon = "Build",
        Color = "#f57c00",
        SortOrder = 4,
        Schema = "repair",

        Permissions =
        [
            new(JobsView,        "Jobs",      "See repair jobs and the tracking board"),
            new(JobsManage,      "Jobs",      "Take in devices and record the work"),
            new(JobsDeliver,     "Jobs",      "Hand a device back to its owner"),
            new(CustomersManage, "Customers", "Manage the customer list"),

            // Separate so a supervisor can see throughput without seeing margin.
            new(CostsView,       "Reporting", "See cost and margin on repair work")
        ],

        RoleTemplates =
        [
            new("Technician", "Works on devices and records what was done.",
                [JobsView, JobsManage]),

            new("Service Manager", "Runs the workshop, including delivery and margin.",
                [JobsView, JobsManage, JobsDeliver, CustomersManage, CostsView]),

            new("Front Desk", "Takes devices in and hands them back.",
                [JobsView, JobsManage, JobsDeliver, CustomersManage])
        ]
    };

    public static IServiceCollection AddRepairModule(
        this IServiceCollection services, IConfiguration config)
    {
        var connection = config.GetConnectionString("Platform")
            ?? throw new InvalidOperationException("No 'Platform' connection string for the Repair module.");

        services.AddDbContext<RepairDbContext>(options =>
            options.UseNpgsql(connection, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__migrations", "repair");
                npgsql.EnableRetryOnFailure(3);
            }));

        services.AddScoped<IRepairService, RepairService>();
        return services;
    }
}

public static class RepairSeederExtensions
{
    public static async Task SeedRepairAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RepairDbContext>();
        await db.Database.MigrateAsync();
    }
}
