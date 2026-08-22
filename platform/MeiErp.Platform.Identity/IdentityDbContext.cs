using MeiErp.Platform.Notifications;
using MeiErp.Platform.Workflow;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Platform.Identity;

/// <summary>
/// The platform schema: identity, departments, the company profile, and the
/// approval engine's own tables.
///
/// The approval engine lives here rather than in its own schema because it
/// routes to users and departments, and those are real foreign keys now. On the
/// previous platform's nine-separate-databases design that was impossible,
/// which is exactly why its approval logic had to be duplicated per module.
/// </summary>
public class PlatformDbContext(DbContextOptions<PlatformDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, string>(options)
{
    public const string SchemaName = "platform";

    public DbSet<UserModuleAccess> ModuleAccess => Set<UserModuleAccess>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();

    public DbSet<WorkflowDefinition> Workflows => Set<WorkflowDefinition>();
    public DbSet<WorkflowStep> WorkflowSteps => Set<WorkflowStep>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<ApprovalStepState> ApprovalStepStates => Set<ApprovalStepState>();
    public DbSet<ApprovalAction> ApprovalActions => Set<ApprovalAction>();
    public DbSet<ApprovalDelegation> ApprovalDelegations => Set<ApprovalDelegation>();

    // Notifications sit on this context rather than one of their own so that a
    // notification is written in the same transaction as the approval that
    // raised it. Two contexts would mean two commits, and an approval that
    // succeeded while its notification rolled back leaves somebody waiting on a
    // queue nobody told them about.
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(SchemaName);

        builder.Entity<ApplicationUser>(b =>
        {
            b.Property(u => u.FullName).HasMaxLength(200).IsRequired();
            b.Property(u => u.EmployeeCode).HasMaxLength(50);
            b.HasIndex(u => u.EmployeeCode).IsUnique()
             .HasFilter("\"EmployeeCode\" IS NOT NULL");

            // Self-reference for the reporting line. Restrict, not cascade:
            // deleting a manager must never take their reports with them.
            b.HasOne(u => u.LineManager)
             .WithMany()
             .HasForeignKey(u => u.LineManagerId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(u => u.Department)
             .WithMany()
             .HasForeignKey(u => u.DepartmentId)
             .OnDelete(DeleteBehavior.SetNull);

            b.HasIndex(u => u.IsActive);
        });

        builder.Entity<ApplicationRole>(b =>
        {
            b.Property(r => r.ModuleKey).HasMaxLength(50);
            b.HasIndex(r => r.ModuleKey);
        });

        builder.Entity<UserModuleAccess>(b =>
        {
            b.Property(a => a.ModuleKey).HasMaxLength(50).IsRequired();
            b.HasOne(a => a.User)
             .WithMany(u => u.ModuleAccess)
             .HasForeignKey(a => a.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            // One override per user per module - two contradictory rows would
            // make "deny wins" depend on row order.
            b.HasIndex(a => new { a.UserId, a.ModuleKey }).IsUnique();
        });

        builder.Entity<Department>(b =>
        {
            b.Property(d => d.Name).HasMaxLength(200).IsRequired();
            b.HasOne(d => d.Parent)
             .WithMany(d => d.Children)
             .HasForeignKey(d => d.ParentId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(d => d.Head)
             .WithMany()
             .HasForeignKey(d => d.HeadUserId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        ConfigureWorkflow(builder);
    }

    private static void ConfigureWorkflow(ModelBuilder builder)
    {
        builder.Entity<WorkflowDefinition>(b =>
        {
            b.Property(w => w.DocumentType).HasMaxLength(100).IsRequired();
            b.Property(w => w.Name).HasMaxLength(200).IsRequired();

            // Only one live revision per document type. Two would make routing
            // depend on whichever the query happened to return first.
            b.HasIndex(w => new { w.DocumentType, w.IsActive })
             .IsUnique()
             .HasFilter("\"IsActive\" = true AND \"IsDeleted\" = false");

            b.HasMany(w => w.Steps)
             .WithOne(s => s.Definition)
             .HasForeignKey(s => s.WorkflowDefinitionId)
             .OnDelete(DeleteBehavior.Cascade);

            b.Property(w => w.Version).HasColumnName("xmin").HasColumnType("xid")
             .ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

            b.HasQueryFilter(w => !w.IsDeleted);
        });

        builder.Entity<ApprovalRequest>(b =>
        {
            b.Property(r => r.ModuleKey).HasMaxLength(50).IsRequired();
            b.Property(r => r.DocumentType).HasMaxLength(100).IsRequired();
            b.Property(r => r.DocumentReference).HasMaxLength(100);
            b.Property(r => r.Summary).HasMaxLength(500);
            b.Property(r => r.Amount).HasColumnType("numeric(18,4)");

            // One live approval per document. Submitting twice would produce two
            // competing routes and two different answers for the same record.
            b.HasIndex(r => new { r.DocumentType, r.DocumentId, r.Status })
             .IsUnique()
             .HasFilter("\"Status\" = 0 AND \"IsDeleted\" = false");

            // The inbox's hot path.
            b.HasIndex(r => new { r.Status, r.CurrentStepOrder });

            b.HasMany(r => r.StepStates)
             .WithOne(s => s.Request)
             .HasForeignKey(s => s.ApprovalRequestId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(r => r.Actions)
             .WithOne(a => a.Request)
             .HasForeignKey(a => a.ApprovalRequestId)
             .OnDelete(DeleteBehavior.Cascade);

            b.Property(r => r.Version).HasColumnName("xmin").HasColumnType("xid")
             .ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

            b.HasQueryFilter(r => !r.IsDeleted);
        });

        builder.Entity<ApprovalDelegation>(b =>
        {
            b.HasIndex(d => new { d.FromUserId, d.FromDate, d.ToDate });
            b.HasQueryFilter(d => !d.IsDeleted);
        });

        builder.Entity<ApprovalAction>(b =>
        {
            b.Property(a => a.Comment).HasMaxLength(2000);
            b.HasIndex(a => a.ActedByUserId);

            // Children need the parent's filter restated, or querying actions
            // directly returns rows belonging to soft-deleted requests.
            b.HasQueryFilter(a => !a.Request!.IsDeleted);
        });

        builder.Entity<ApprovalStepState>()
            .HasQueryFilter(s => !s.Request!.IsDeleted);

        builder.Entity<WorkflowStep>()
            .HasQueryFilter(s => !s.Definition!.IsDeleted);

        builder.Entity<Notification>(b =>
        {
            b.Property(n => n.UserId).HasMaxLength(450).IsRequired();
            b.Property(n => n.Category).HasMaxLength(100).IsRequired();
            b.Property(n => n.Subject).HasMaxLength(300).IsRequired();
            b.Property(n => n.Body).HasMaxLength(4000).IsRequired();
            b.Property(n => n.Url).HasMaxLength(500);
            b.Property(n => n.ModuleKey).HasMaxLength(50);
            b.Property(n => n.EventKey).HasMaxLength(200);

            // The bell's query, on every page render for every signed-in user.
            b.HasIndex(n => new { n.UserId, n.ReadUtc, n.DismissedUtc });

            // Standing down everything one event raised.
            b.HasIndex(n => n.EventKey).HasFilter("\"EventKey\" IS NOT NULL");

            b.HasMany(n => n.Deliveries)
             .WithOne(d => d.Notification!)
             .HasForeignKey(d => d.NotificationId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<NotificationDelivery>(b =>
        {
            b.Property(d => d.Channel).HasMaxLength(50).IsRequired();
            b.Property(d => d.Address).HasMaxLength(300);
            b.Property(d => d.LastError).HasMaxLength(2000);

            // What the dispatcher claims on. Filtered to the two statuses that
            // can still be due, so the index stays small however much history
            // accumulates behind it.
            b.HasIndex(d => new { d.Status, d.NextAttemptUtc })
             .HasFilter("\"Status\" IN (0, 2)");

            // One channel carries a given notification once. A retry updates the
            // row it already has; a second row would be a second email.
            b.HasIndex(d => new { d.NotificationId, d.Channel }).IsUnique();
        });

        builder.Entity<NotificationPreference>(b =>
        {
            b.Property(p => p.UserId).HasMaxLength(450).IsRequired();
            b.Property(p => p.Category).HasMaxLength(100).IsRequired();
            b.Property(p => p.Channel).HasMaxLength(50).IsRequired();

            // Absent means "the channel's default", so there is exactly one
            // answer per person per category per channel or none at all.
            b.HasIndex(p => new { p.UserId, p.Category, p.Channel }).IsUnique();
        });
    }
}
