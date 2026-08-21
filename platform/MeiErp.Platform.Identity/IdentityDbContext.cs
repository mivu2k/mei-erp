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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<ApplicationUser>(b =>
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

        modelBuilder.Entity<ApplicationRole>(b =>
        {
            b.Property(r => r.ModuleKey).HasMaxLength(50);
            b.HasIndex(r => r.ModuleKey);
        });

        modelBuilder.Entity<UserModuleAccess>(b =>
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

        modelBuilder.Entity<Department>(b =>
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

        ConfigureWorkflow(modelBuilder);
    }

    private static void ConfigureWorkflow(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkflowDefinition>(b =>
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

        modelBuilder.Entity<ApprovalRequest>(b =>
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

        modelBuilder.Entity<ApprovalDelegation>(b =>
        {
            b.HasIndex(d => new { d.FromUserId, d.FromDate, d.ToDate });
            b.HasQueryFilter(d => !d.IsDeleted);
        });

        modelBuilder.Entity<ApprovalAction>(b =>
        {
            b.Property(a => a.Comment).HasMaxLength(2000);
            b.HasIndex(a => a.ActedByUserId);

            // Children need the parent's filter restated, or querying actions
            // directly returns rows belonging to soft-deleted requests.
            b.HasQueryFilter(a => !a.Request!.IsDeleted);
        });

        modelBuilder.Entity<ApprovalStepState>()
            .HasQueryFilter(s => !s.Request!.IsDeleted);

        modelBuilder.Entity<WorkflowStep>()
            .HasQueryFilter(s => !s.Definition!.IsDeleted);
    }
}
