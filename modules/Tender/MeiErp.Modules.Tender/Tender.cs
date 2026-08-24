using MeiErp.Platform.Kernel;
using MeiErp.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeiErp.Modules.Tender;

/// <summary>A bid we are working on or have submitted.</summary>
public class TenderRecord : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    public string Reference { get; set; } = "";
    public string Title { get; set; } = "";

    public string ClientName { get; set; } = "";
    public string? IssuingAuthority { get; set; }
    public string? Department { get; set; }
    public string? Description { get; set; }

    public DateOnly? PublishedOn { get; set; }
    public DateOnly? SubmissionDeadline { get; set; }
    public DateOnly? OpeningDate { get; set; }
    public DateOnly? TechnicalOpeningDate { get; set; }
    public DateOnly? FinancialOpeningDate { get; set; }

    /// <summary>Our own estimate of what the work is worth, before the lines are priced.</summary>
    public decimal? EstimatedValue { get; set; }
    public decimal? TenderFee { get; set; }
    public decimal? EmdAmount { get; set; }
    public bool IsEmdExempted { get; set; }
    public string? EmdExemptionReason { get; set; }
    public decimal? PerformanceGuaranteePercentage { get; set; }
    public decimal? RetentionMoneyPercentage { get; set; }
    public SubmissionMode SubmissionMode { get; set; } = SubmissionMode.Online;
    public string? PortalReference { get; set; }
    public int? BidValidityDays { get; set; }
    public int? OurRank { get; set; }
    public decimal? L1Amount { get; set; }

    public TenderStatus Status { get; set; } = TenderStatus.Identified;

    public string? OwnerUserId { get; set; }
    public string? OwnerName { get; set; }

    public string? Notes { get; set; }
    public decimal? AwardedValue { get; set; }
    public DateOnly? AwardDate { get; set; }
    public string? WorkOrderNumber { get; set; }
    public DateOnly? ContractStartDate { get; set; }
    public DateOnly? ContractEndDate { get; set; }
    public int? CompletionPeriodDays { get; set; }
    public int? DefectLiabilityPeriodMonths { get; set; }
    public string? PaymentTerms { get; set; }
    public string? ContactPerson { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }

    /// <summary>
    /// The schedule of items, and entirely optional. A lump-sum bid carries no
    /// lines rather than one dummy line standing in for the whole thing.
    /// </summary>
    public List<TenderItem> Items { get; set; } = [];

    /// <summary>EMDs, bid bonds and performance guarantees lodged against this tender.</summary>
    public List<Guarantee> Guarantees { get; set; } = [];
    public List<TenderDocument> Documents { get; set; } = [];
    public List<TenderCompetitor> Competitors { get; set; } = [];
    public List<TenderTask> Tasks { get; set; } = [];

    public decimal ItemsTotal => Items.Sum(i => i.LineTotal);

    /// <summary>
    /// Deliberately kept apart from <see cref="EstimatedValue"/> rather than
    /// overwriting it: seeing the two disagree is how a mispriced line gets
    /// caught before submission.
    /// </summary>
    public decimal? VarianceFromEstimate =>
        EstimatedValue is null || Items.Count == 0 ? null : ItemsTotal - EstimatedValue;

    public bool IsOpen => Status is not (TenderStatus.Won or TenderStatus.Lost or TenderStatus.Cancelled);

    public bool IsDeadlineNear(DateOnly today, int withinDays = 7) =>
        IsOpen && SubmissionDeadline is not null
        && SubmissionDeadline <= today.AddDays(withinDays);
}

public enum TenderStatus
{
    Identified = 0,
    Preparing = 1,
    Submitted = 2,
    Opened = 3,
    Won = 4,
    Lost = 5,
    Cancelled = 6,
    TechnicallyQualified = 7,
    Withdrawn = 8
}

public enum SubmissionMode { Online = 0, Offline = 1, Both = 2 }

/// <summary>One priced line on the schedule of items.</summary>
public class TenderItem : AuditableEntity
{
    public int TenderRecordId { get; set; }
    public TenderRecord? Tender { get; set; }

    public string Description { get; set; } = "";
    public string? ItemCode { get; set; }
    public string? Specification { get; set; }
    public decimal Quantity { get; set; } = 1;
    public string Unit { get; set; } = "each";

    public decimal UnitRate { get; set; }
    public decimal? EstimatedRate { get; set; }

    /// <summary>What it costs us, where known.</summary>
    public decimal? CostRate { get; set; }
    public string? Brand { get; set; }
    public string? CountryOfOrigin { get; set; }
    public int? DeliveryDays { get; set; }
    public int SortOrder { get; set; }
    public string? Remarks { get; set; }

    public decimal LineTotal => Quantity * UnitRate;

    /// <summary>Null rather than zero when no cost is known — an unpriced line has no margin.</summary>
    public decimal? Margin => CostRate is null ? null : (UnitRate - CostRate.Value) * Quantity;
}

/// <summary>Money or a bank instrument lodged against a bid.</summary>
public class Guarantee : AuditableEntity
{
    public int TenderRecordId { get; set; }
    public TenderRecord? Tender { get; set; }

    public GuaranteeKind Kind { get; set; }
    public GuaranteeInstrumentType InstrumentType { get; set; } = GuaranteeInstrumentType.BankGuarantee;
    public GuaranteeStatus Status { get; set; } = GuaranteeStatus.Active;

    public string? InstrumentNumber { get; set; }
    public string? BankName { get; set; }
    public string? BranchName { get; set; }
    public string? BankContactPerson { get; set; }
    public string? BankContactPhone { get; set; }

    public decimal Amount { get; set; }

    public DateOnly IssuedOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }

    /// <summary>Set when the money or instrument came back.</summary>
    public DateOnly? ReleasedOn { get; set; }
    public decimal? Charges { get; set; }
    public DateOnly? ClaimPeriodEndDate { get; set; }
    public string? ReleaseReference { get; set; }
    public string? Remarks { get; set; }
    public int? RenewalOfGuaranteeId { get; set; }

    public bool IsOutstanding => ReleasedOn is null;

    /// <summary>Expired and never released — money the company has quietly left with someone.</summary>
    public bool IsExpiredUnreleased(DateOnly today) =>
        ReleasedOn is null && ExpiresOn is not null && ExpiresOn < today;
}

public enum GuaranteeKind
{
    /// <summary>Earnest money deposit.</summary>
    Emd = 0,
    BidBond = 1,
    PerformanceGuarantee = 2,
    AdvanceGuarantee = 3,
    Retention = 4,
    SecurityDeposit = 5,
    Other = 6
}

public enum GuaranteeInstrumentType { BankGuarantee = 0, DemandDraft = 1, FixedDeposit = 2, Cheque = 3, OnlinePayment = 4, InsuranceSuretyBond = 5 }
public enum GuaranteeStatus { Active = 0, Released = 1, Invoked = 2, Expired = 3, Refunded = 4 }

/// <summary>
/// A piece of work being delivered.
///
/// Deliberately standalone — there is no foreign key to a tender. Plenty of work
/// never went to tender, and a project's schedule has nothing to do with a bid's.
/// </summary>
public class Project : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    public string Code { get; set; } = "";
    public string Name { get; set; } = "";

    public string? ClientName { get; set; }
    public string? Description { get; set; }
    public string? Location { get; set; }

    public DateOnly? StartDate { get; set; }
    public DateOnly? TargetEndDate { get; set; }
    public DateOnly? ActualEndDate { get; set; }

    public decimal? ContractValue { get; set; }
    public decimal? Budget { get; set; }
    public int Priority { get; set; }
    public string? ContactPerson { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public string? Notes { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.Planned;

    public string? ManagerUserId { get; set; }
    public string? ManagerName { get; set; }

    public List<ProjectTask> Tasks { get; set; } = [];
    public List<ProjectMilestone> Milestones { get; set; } = [];

    /// <summary>
    /// Averaged from the tasks and never stored — a stored percentage and a task
    /// list disagree the moment either one moves.
    ///
    /// Cancelled tasks are excluded rather than counted as done, so dropping
    /// scope cannot flatter the figure.
    /// </summary>
    public int ProgressPercent
    {
        get
        {
            var counted = Tasks.Where(t => t.Status is not ProjectTaskStatus.Cancelled).ToList();
            return counted.Count == 0 ? 0 : (int)Math.Round(counted.Average(t => t.PercentComplete));
        }
    }

    public bool IsOpen => Status is ProjectStatus.Planned or ProjectStatus.Active or ProjectStatus.OnHold;
}

public enum ProjectStatus
{
    Planned = 0,
    Active = 1,
    OnHold = 2,
    Completed = 3,
    Cancelled = 4
}

public class ProjectTask : AuditableEntity
{
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public string Title { get; set; } = "";
    public string? Description { get; set; }

    public DateOnly? DueDate { get; set; }
    public DateOnly? CompletedOn { get; set; }
    public DateOnly? StartDate { get; set; }

    public ProjectTaskStatus Status { get; set; } = ProjectTaskStatus.NotStarted;

    public int PercentComplete { get; set; }
    public int Priority { get; set; }
    public decimal? EstimatedHours { get; set; }
    public decimal? ActualHours { get; set; }
    public int SortOrder { get; set; }
    public string? Notes { get; set; }

    public string? AssigneeUserId { get; set; }
    public string? AssigneeName { get; set; }

    /// <summary>Overdue skips tasks whose project is closed — nobody chases work on a cancelled job.</summary>
    public bool IsOverdue(DateOnly today) =>
        Status is not (ProjectTaskStatus.Completed or ProjectTaskStatus.Cancelled)
        && DueDate is not null && DueDate < today;
}

public enum ProjectTaskStatus
{
    NotStarted = 0,
    InProgress = 1,
    Completed = 2,
    Cancelled = 3
}

public class TenderDbContext(
    DbContextOptions<TenderDbContext> options, ICurrentUser currentUser, IClock clock)
    : ModuleDbContext(options, currentUser, clock)
{
    protected override string Schema => "tender";

    public DbSet<TenderRecord> Tenders => Set<TenderRecord>();
    public DbSet<TenderItem> TenderItems => Set<TenderItem>();
    public DbSet<Guarantee> Guarantees => Set<Guarantee>();
    public DbSet<TenderDocument> Documents => Set<TenderDocument>();
    public DbSet<TenderCompetitor> Competitors => Set<TenderCompetitor>();
    public DbSet<TenderTask> TenderTasks => Set<TenderTask>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectTask> ProjectTasks => Set<ProjectTask>();
    public DbSet<ProjectMilestone> ProjectMilestones => Set<ProjectMilestone>();
    public DbSet<PhysicalFile> PhysicalFiles => Set<PhysicalFile>();
    public DbSet<FileMovement> FileMovements => Set<FileMovement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TenderRecord>(b =>
        {
            b.Property(t => t.Reference).HasMaxLength(50).IsRequired();
            b.HasIndex(t => t.Reference).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.Property(t => t.Title).HasMaxLength(300).IsRequired();
            b.Property(t => t.ClientName).HasMaxLength(200);
            b.Property(t => t.IssuingAuthority).HasMaxLength(300);
            b.Property(t => t.Department).HasMaxLength(150);
            b.Property(t => t.Description).HasMaxLength(2000);
            b.Property(t => t.EmdExemptionReason).HasMaxLength(500);
            b.Property(t => t.PerformanceGuaranteePercentage).HasPrecision(5, 2);
            b.Property(t => t.RetentionMoneyPercentage).HasPrecision(5, 2);
            b.Property(t => t.PortalReference).HasMaxLength(150);
            b.Property(t => t.L1Amount).HasPrecision(18, 4);
            b.Property(t => t.WorkOrderNumber).HasMaxLength(100);
            b.Property(t => t.PaymentTerms).HasMaxLength(1000);
            b.Property(t => t.ContactPerson).HasMaxLength(200);
            b.Property(t => t.ContactPhone).HasMaxLength(50);
            b.Property(t => t.ContactEmail).HasMaxLength(200);
            b.Property(t => t.Notes).HasMaxLength(2000);

            b.HasMany(t => t.Items).WithOne(i => i.Tender)
             .HasForeignKey(i => i.TenderRecordId).OnDelete(DeleteBehavior.Cascade);

            b.HasMany(t => t.Guarantees).WithOne(g => g.Tender)
             .HasForeignKey(g => g.TenderRecordId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(t => t.Documents).WithOne(d => d.Tender)
             .HasForeignKey(d => d.TenderRecordId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(t => t.Competitors).WithOne(c => c.Tender)
             .HasForeignKey(c => c.TenderRecordId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(t => t.Tasks).WithOne(x => x.Tender)
             .HasForeignKey(x => x.TenderRecordId).OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(t => new { t.Status, t.SubmissionDeadline });

            b.Ignore(t => t.ItemsTotal);
            b.Ignore(t => t.VarianceFromEstimate);
            b.Ignore(t => t.IsOpen);
        });

        modelBuilder.Entity<TenderItem>(b =>
        {
            b.Property(i => i.Description).HasMaxLength(500).IsRequired();
            b.Property(i => i.ItemCode).HasMaxLength(32);
            b.Property(i => i.Specification).HasMaxLength(2000);
            b.Property(i => i.Unit).HasMaxLength(20);
            b.Property(i => i.EstimatedRate).HasPrecision(18, 4);
            b.Property(i => i.Brand).HasMaxLength(200);
            b.Property(i => i.CountryOfOrigin).HasMaxLength(100);
            b.Property(i => i.Remarks).HasMaxLength(1000);
            b.Ignore(i => i.LineTotal);
            b.Ignore(i => i.Margin);
            b.HasQueryFilter(i => !i.Tender!.IsDeleted);
        });

        modelBuilder.Entity<Guarantee>(b =>
        {
            b.Property(g => g.InstrumentNumber).HasMaxLength(60);
            b.Property(g => g.BankName).HasMaxLength(200);
            b.Property(g => g.BranchName).HasMaxLength(200);
            b.Property(g => g.BankContactPerson).HasMaxLength(200);
            b.Property(g => g.BankContactPhone).HasMaxLength(50);
            b.Property(g => g.ReleaseReference).HasMaxLength(100);
            b.Property(g => g.Remarks).HasMaxLength(1000);
            b.HasIndex(g => g.ReleasedOn);
            b.Ignore(g => g.IsOutstanding);
            b.HasQueryFilter(g => !g.Tender!.IsDeleted);
        });

        modelBuilder.Entity<TenderDocument>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(300).IsRequired();
            b.Property(x => x.ReferenceNumber).HasMaxLength(100);
            b.Property(x => x.Notes).HasMaxLength(1000);
            b.HasIndex(x => new { x.TenderRecordId, x.Category });
            b.HasQueryFilter(x => !x.Tender!.IsDeleted);
        });
        modelBuilder.Entity<TenderCompetitor>(b =>
        {
            b.Property(x => x.BidderName).HasMaxLength(300).IsRequired();
            b.Property(x => x.Remarks).HasMaxLength(500);
            b.HasIndex(x => new { x.TenderRecordId, x.Rank });
            b.HasQueryFilter(x => !x.Tender!.IsDeleted);
        });
        modelBuilder.Entity<TenderTask>(b =>
        {
            b.Property(x => x.Title).HasMaxLength(300).IsRequired();
            b.Property(x => x.Description).HasMaxLength(2000);
            b.Property(x => x.AssigneeUserId).HasMaxLength(450);
            b.Property(x => x.AssigneeName).HasMaxLength(200);
            b.Property(x => x.EstimatedHours).HasPrecision(10, 2);
            b.Property(x => x.ActualHours).HasPrecision(10, 2);
            b.Property(x => x.Notes).HasMaxLength(1000);
            b.HasIndex(x => new { x.TenderRecordId, x.DueDate });
            b.HasQueryFilter(x => !x.Tender!.IsDeleted);
        });

        modelBuilder.Entity<Project>(b =>
        {
            b.Property(p => p.Code).HasMaxLength(30).IsRequired();
            b.HasIndex(p => p.Code).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.Property(p => p.Name).HasMaxLength(300).IsRequired();
            b.Property(p => p.ClientName).HasMaxLength(200);
            b.Property(p => p.Description).HasMaxLength(2000);
            b.Property(p => p.Location).HasMaxLength(300);
            b.Property(p => p.Budget).HasPrecision(18, 4);
            b.Property(p => p.ContactPerson).HasMaxLength(200);
            b.Property(p => p.ContactPhone).HasMaxLength(50);
            b.Property(p => p.ContactEmail).HasMaxLength(200);
            b.Property(p => p.Notes).HasMaxLength(2000);

            b.HasMany(p => p.Tasks).WithOne(t => t.Project)
             .HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(p => p.Milestones).WithOne(t => t.Project)
             .HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(p => p.Status);

            // Derived from the tasks. Storing it would let the two disagree.
            b.Ignore(p => p.ProgressPercent);
            b.Ignore(p => p.IsOpen);
        });

        modelBuilder.Entity<ProjectTask>(b =>
        {
            b.Property(t => t.Title).HasMaxLength(300).IsRequired();
            b.Property(t => t.Description).HasMaxLength(2000);
            b.HasIndex(t => new { t.ProjectId, t.DueDate });
            b.Property(t => t.EstimatedHours).HasPrecision(10, 2);
            b.Property(t => t.ActualHours).HasPrecision(10, 2);
            b.Property(t => t.Notes).HasMaxLength(1000);
            b.HasQueryFilter(t => !t.Project!.IsDeleted);
        });
        modelBuilder.Entity<ProjectMilestone>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(300).IsRequired();
            b.HasIndex(x => new { x.ProjectId, x.DueDate });
            b.HasQueryFilter(x => !x.Project!.IsDeleted);
        });
        modelBuilder.Entity<PhysicalFile>(b =>
        {
            b.Property(x => x.FileNumber).HasMaxLength(30).IsRequired();
            b.HasIndex(x => x.FileNumber).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.HasIndex(x => new { x.OwnerType, x.OwnerId }).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.Property(x => x.OwnerReference).HasMaxLength(50).IsRequired();
            b.Property(x => x.OwnerTitle).HasMaxLength(300).IsRequired();
            b.HasMany(x => x.Movements).WithOne(x => x.PhysicalFile).HasForeignKey(x => x.PhysicalFileId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<FileMovement>(b =>
        {
            b.HasIndex(x => new { x.PhysicalFileId, x.MovedOn });
            b.HasQueryFilter(x => !x.PhysicalFile!.IsDeleted);
        });
    }
}

/// <summary>
/// Keeps a task's status, percentage and completion date from contradicting
/// each other. Pure, so the rules can be tested without a database.
/// </summary>
public static class TaskRules
{
    /// <summary>
    /// Reconciles the three fields. Every write path goes through this - that is
    /// the invariant to preserve.
    /// </summary>
    public static void Reconcile(ProjectTask task, DateOnly today)
    {
        switch (task.Status)
        {
            case ProjectTaskStatus.Completed:
                // Completing forces the percentage and stamps the date, so a
                // "done" task can never read as 40%.
                task.PercentComplete = 100;
                task.CompletedOn ??= today;
                break;

            case ProjectTaskStatus.Cancelled:
                task.CompletedOn = null;
                break;

            default:
                // Re-opening clears the completion date and caps the percentage
                // below 100 - otherwise it stays "finished" while being worked on.
                task.CompletedOn = null;
                if (task.PercentComplete >= 100) task.PercentComplete = 99;

                // Any progress at all means it has started.
                if (task.PercentComplete > 0 && task.Status is ProjectTaskStatus.NotStarted)
                    task.Status = ProjectTaskStatus.InProgress;
                break;
        }

        task.PercentComplete = Math.Clamp(task.PercentComplete, 0, 100);
    }
}

public interface ITenderService
{
    Task<IReadOnlyList<TenderRecord>> ListTendersAsync(TenderStatus? status, CancellationToken ct = default);
    Task<TenderRecord?> GetTenderAsync(int id, CancellationToken ct = default);
    Task<Result<TenderRecord>> SaveTenderAsync(TenderRecord tender, CancellationToken ct = default);

    Task<Result<Guarantee>> AddGuaranteeAsync(Guarantee guarantee, CancellationToken ct = default);
    Task<Result> ReleaseGuaranteeAsync(int id, DateOnly releasedOn, CancellationToken ct = default);
    Task<Result<TenderDocument>> SaveDocumentAsync(TenderDocument document, CancellationToken ct = default);
    Task<Result> DeleteDocumentAsync(int id, CancellationToken ct = default);
    Task<Result<TenderCompetitor>> SaveCompetitorAsync(TenderCompetitor competitor, CancellationToken ct = default);
    Task<Result> DeleteCompetitorAsync(int id, CancellationToken ct = default);
    Task<Result<TenderTask>> SaveTenderTaskAsync(TenderTask task, CancellationToken ct = default);
    Task<Result> DeleteTenderTaskAsync(int id, CancellationToken ct = default);

    /// <summary>Guarantees still lodged. Money the company has out with someone else.</summary>
    Task<IReadOnlyList<Guarantee>> OutstandingGuaranteesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Project>> ListProjectsAsync(ProjectStatus? status, CancellationToken ct = default);
    Task<Project?> GetProjectAsync(int id, CancellationToken ct = default);
    Task<Result<Project>> SaveProjectAsync(Project project, CancellationToken ct = default);

    Task<Result<ProjectTask>> SaveTaskAsync(ProjectTask task, CancellationToken ct = default);
    Task<Result> DeleteTaskAsync(int id, CancellationToken ct = default);

    /// <summary>Tasks past their due date, skipping any whose project is closed.</summary>
    Task<IReadOnlyList<ProjectTask>> OverdueTasksAsync(CancellationToken ct = default);
}

public sealed class TenderService(TenderDbContext db, IClock clock, IFileRegistryService files) : ITenderService
{
    public async Task<IReadOnlyList<TenderRecord>> ListTendersAsync(
        TenderStatus? status, CancellationToken ct = default)
    {
        var query = db.Tenders.AsNoTracking()
            .Include(t => t.Items).Include(t => t.Guarantees)
            .Include(t => t.Documents).Include(t => t.Competitors)
            .Include(t => t.Tasks)
            .AsSplitQuery().AsQueryable();

        if (status is not null) query = query.Where(t => t.Status == status);

        return await query.OrderByDescending(t => t.Id).Take(300).ToListAsync(ct);
    }

    public Task<TenderRecord?> GetTenderAsync(int id, CancellationToken ct = default) =>
        db.Tenders.Include(t => t.Items).Include(t => t.Guarantees)
                  .Include(t => t.Documents).Include(t => t.Competitors).AsSplitQuery()
                  .Include(t => t.Tasks)
                  .FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<Result<TenderRecord>> SaveTenderAsync(
        TenderRecord tender, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tender.Reference))
            return Result.Fail<TenderRecord>("A tender needs a reference.", "tender.no-reference");

        if (string.IsNullOrWhiteSpace(tender.Title))
            return Result.Fail<TenderRecord>("A tender needs a title.", "tender.no-title");

        var taken = await db.Tenders
            .AnyAsync(t => t.Reference == tender.Reference && t.Id != tender.Id, ct);

        if (taken)
            return Result.Fail<TenderRecord>($"Reference {tender.Reference} is already in use.", "tender.duplicate");

        if (tender.SubmissionDeadline is not null && tender.PublishedOn is not null
            && tender.SubmissionDeadline < tender.PublishedOn)
        {
            return Result.Fail<TenderRecord>(
                "The submission deadline is before the tender was published.", "tender.bad-dates");
        }

        if (tender.Id == 0)
        {
            db.Tenders.Add(tender);
        }
        else
        {
            var existing = await db.Tenders.FirstOrDefaultAsync(t => t.Id == tender.Id, ct);
            if (existing is null) return Result.Fail<TenderRecord>("That tender no longer exists.", "tender.not-found");
            db.Entry(existing).CurrentValues.SetValues(tender);
        }

        await db.SaveChangesAsync(ct);
        var file = await files.EnsureAsync(FileOwnerType.Tender, tender.Id, ct);
        if (file.Failed) return Result.Fail<TenderRecord>(file.Error!, file.Code);
        return Result.Success(tender);
    }

    public async Task<Result<Guarantee>> AddGuaranteeAsync(
        Guarantee guarantee, CancellationToken ct = default)
    {
        var tender = await db.Tenders.FirstOrDefaultAsync(t => t.Id == guarantee.TenderRecordId, ct);
        if (tender is null) return Result.Fail<Guarantee>("That tender no longer exists.", "tender.not-found");

        if (guarantee.Amount <= 0)
            return Result.Fail<Guarantee>("A guarantee has to be for more than nothing.", "guarantee.bad-amount");

        if (guarantee.ExpiresOn is not null && guarantee.ExpiresOn < guarantee.IssuedOn)
            return Result.Fail<Guarantee>("The expiry date is before the issue date.", "guarantee.bad-dates");

        db.Guarantees.Add(guarantee);
        await db.SaveChangesAsync(ct);
        return Result.Success(guarantee);
    }

    public async Task<Result> ReleaseGuaranteeAsync(
        int id, DateOnly releasedOn, CancellationToken ct = default)
    {
        var guarantee = await db.Guarantees.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (guarantee is null) return Result.Fail("That guarantee no longer exists.", "guarantee.not-found");

        if (guarantee.ReleasedOn is not null)
            return Result.Fail("This has already been released.", "guarantee.already-released");

        if (releasedOn < guarantee.IssuedOn)
            return Result.Fail("It cannot be released before it was issued.", "guarantee.bad-dates");

        guarantee.ReleasedOn = releasedOn;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<TenderDocument>> SaveDocumentAsync(
        TenderDocument document, CancellationToken ct = default)
    {
        var documentValidation = TenderParityRules.ValidateDocument(document);
        if (documentValidation.Failed)
            return Result.Fail<TenderDocument>(documentValidation.Error!, documentValidation.Code);
        if (!await db.Tenders.AnyAsync(t => t.Id == document.TenderRecordId, ct))
            return Result.Fail<TenderDocument>("That tender no longer exists.", "tender.not-found");

        if (document.Id == 0)
            db.Documents.Add(document);
        else
        {
            var existing = await db.Documents.FirstOrDefaultAsync(x => x.Id == document.Id, ct);
            if (existing is null) return Result.Fail<TenderDocument>("That document no longer exists.", "document.not-found");
            if (existing.TenderRecordId != document.TenderRecordId)
                return Result.Fail<TenderDocument>("A document cannot move to another tender.", "document.cannot-move");
            db.Entry(existing).CurrentValues.SetValues(document);
        }
        await db.SaveChangesAsync(ct);
        return Result.Success(document);
    }

    public async Task<Result> DeleteDocumentAsync(int id, CancellationToken ct = default)
    {
        var document = await db.Documents.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (document is null) return Result.Fail("That document no longer exists.", "document.not-found");
        db.Documents.Remove(document);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<TenderCompetitor>> SaveCompetitorAsync(
        TenderCompetitor competitor, CancellationToken ct = default)
    {
        var competitorValidation = TenderParityRules.ValidateCompetitor(competitor);
        if (competitorValidation.Failed)
            return Result.Fail<TenderCompetitor>(competitorValidation.Error!, competitorValidation.Code);
        if (!await db.Tenders.AnyAsync(t => t.Id == competitor.TenderRecordId, ct))
            return Result.Fail<TenderCompetitor>("That tender no longer exists.", "tender.not-found");

        if (competitor.Id == 0)
            db.Competitors.Add(competitor);
        else
        {
            var existing = await db.Competitors.FirstOrDefaultAsync(x => x.Id == competitor.Id, ct);
            if (existing is null) return Result.Fail<TenderCompetitor>("That bidder no longer exists.", "competitor.not-found");
            if (existing.TenderRecordId != competitor.TenderRecordId)
                return Result.Fail<TenderCompetitor>("A bidder cannot move to another tender.", "competitor.cannot-move");
            db.Entry(existing).CurrentValues.SetValues(competitor);
        }
        await db.SaveChangesAsync(ct);
        return Result.Success(competitor);
    }

    public async Task<Result> DeleteCompetitorAsync(int id, CancellationToken ct = default)
    {
        var competitor = await db.Competitors.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (competitor is null) return Result.Fail("That bidder no longer exists.", "competitor.not-found");
        db.Competitors.Remove(competitor);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<TenderTask>> SaveTenderTaskAsync(TenderTask task, CancellationToken ct = default)
    {
        var valid = TenderTaskRules.Validate(task);
        if (valid.Failed) return Result.Fail<TenderTask>(valid.Error!, valid.Code);
        if (!await db.Tenders.AnyAsync(x => x.Id == task.TenderRecordId, ct)) return Result.Fail<TenderTask>("That tender no longer exists.", "tender.not-found");
        if (task.Id == 0) db.TenderTasks.Add(task);
        else
        {
            var existing = await db.TenderTasks.FirstOrDefaultAsync(x => x.Id == task.Id, ct);
            if (existing is null) return Result.Fail<TenderTask>("That task no longer exists.", "task.not-found");
            if (existing.TenderRecordId != task.TenderRecordId) return Result.Fail<TenderTask>("A task cannot move to another tender.", "task.cannot-move");
            db.Entry(existing).CurrentValues.SetValues(task);
            task = existing;
        }
        await db.SaveChangesAsync(ct);
        return Result.Success(task);
    }

    public async Task<Result> DeleteTenderTaskAsync(int id, CancellationToken ct = default)
    {
        var task = await db.TenderTasks.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (task is null) return Result.Fail("That task no longer exists.", "task.not-found");
        db.TenderTasks.Remove(task);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<IReadOnlyList<Guarantee>> OutstandingGuaranteesAsync(
        CancellationToken ct = default) =>
        await db.Guarantees.AsNoTracking()
            .Include(g => g.Tender)
            .Where(g => g.ReleasedOn == null)
            .OrderBy(g => g.ExpiresOn ?? DateOnly.MaxValue)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Project>> ListProjectsAsync(
        ProjectStatus? status, CancellationToken ct = default)
    {
        var query = db.Projects.AsNoTracking().Include(p => p.Tasks).AsQueryable();
        if (status is not null) query = query.Where(p => p.Status == status);
        return await query.OrderByDescending(p => p.Id).Take(300).ToListAsync(ct);
    }

    public Task<Project?> GetProjectAsync(int id, CancellationToken ct = default) =>
        db.Projects.Include(p => p.Tasks).FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Result<Project>> SaveProjectAsync(
        Project project, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(project.Name))
            return Result.Fail<Project>("A project needs a name.", "project.no-name");

        if (string.IsNullOrWhiteSpace(project.Code))
        {
            var count = await db.Projects.IgnoreQueryFilters().CountAsync(ct);
            project.Code = $"P-{clock.Today.Year % 100:D2}-{count + 1:D3}";
        }

        var taken = await db.Projects
            .AnyAsync(p => p.Code == project.Code && p.Id != project.Id, ct);

        if (taken) return Result.Fail<Project>($"Code {project.Code} is already in use.", "project.duplicate");

        if (project.TargetEndDate is not null && project.StartDate is not null
            && project.TargetEndDate < project.StartDate)
        {
            return Result.Fail<Project>("The target end date is before the start date.", "project.bad-dates");
        }

        if (project.Id == 0)
        {
            db.Projects.Add(project);
        }
        else
        {
            var existing = await db.Projects.FirstOrDefaultAsync(p => p.Id == project.Id, ct);
            if (existing is null) return Result.Fail<Project>("That project no longer exists.", "project.not-found");
            db.Entry(existing).CurrentValues.SetValues(project);
        }

        await db.SaveChangesAsync(ct);
        var file = await files.EnsureAsync(FileOwnerType.Project, project.Id, ct);
        if (file.Failed) return Result.Fail<Project>(file.Error!, file.Code);
        return Result.Success(project);
    }

    public async Task<Result<ProjectTask>> SaveTaskAsync(
        ProjectTask task, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(task.Title))
            return Result.Fail<ProjectTask>("A task needs a title.", "task.no-title");

        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == task.ProjectId, ct);
        if (project is null) return Result.Fail<ProjectTask>("That project no longer exists.", "project.not-found");

        // Every write path goes through Reconcile, which is what stops status,
        // percentage and completion date from contradicting each other.
        TaskRules.Reconcile(task, clock.Today);

        if (task.Id == 0)
        {
            db.ProjectTasks.Add(task);
        }
        else
        {
            var existing = await db.ProjectTasks.FirstOrDefaultAsync(t => t.Id == task.Id, ct);
            if (existing is null) return Result.Fail<ProjectTask>("That task no longer exists.", "task.not-found");

            // Ownership is fixed at creation, so a task cannot silently move off
            // somebody's board onto another project.
            if (existing.ProjectId != task.ProjectId)
                return Result.Fail<ProjectTask>("A task cannot be moved to another project.", "task.cannot-move");

            db.Entry(existing).CurrentValues.SetValues(task);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(task);
    }

    public async Task<Result> DeleteTaskAsync(int id, CancellationToken ct = default)
    {
        var task = await db.ProjectTasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null) return Result.Fail("That task no longer exists.", "task.not-found");

        db.ProjectTasks.Remove(task);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<IReadOnlyList<ProjectTask>> OverdueTasksAsync(CancellationToken ct = default)
    {
        var today = clock.Today;

        return await db.ProjectTasks.AsNoTracking()
            .Include(t => t.Project)
            .Where(t => t.DueDate != null && t.DueDate < today
                     && t.Status != ProjectTaskStatus.Completed
                     && t.Status != ProjectTaskStatus.Cancelled
                     // Nobody chases work on a project that has been cancelled
                     // or delivered.
                     && t.Project!.Status != ProjectStatus.Cancelled
                     && t.Project.Status != ProjectStatus.Completed)
            .OrderBy(t => t.DueDate)
            .ToListAsync(ct);
    }
}

public static class TenderModule
{
    public const string Key = "tender";

    public const string TendersView = "tender.tenders.view";
    public const string TendersManage = "tender.tenders.manage";
    public const string GuaranteesManage = "tender.guarantees.manage";
    public const string ProjectsView = "tender.projects.view";
    public const string ProjectsManage = "tender.projects.manage";

    /// <summary>Separate so a team member can progress their own work without re-scoping the project.</summary>
    public const string TasksManage = "tender.tasks.manage";
    public const string MilestonesManage = "tender.milestones.manage";
    public const string FilesView = "tender.files.view";
    public const string FilesManage = "tender.files.manage";
    public const string ReportsView = "tender.reports.view";

    public static ModuleDescriptor Descriptor => new()
    {
        Key = Key,
        Name = "Tender & Projects",
        Description = "Bids, the guarantees lodged against them, and the work being delivered.",
        BasePath = "/tender",
        Icon = "Gavel",
        Color = "#455a64",
        SortOrder = 9,
        Schema = "tender",

        Permissions =
        [
            new(TendersView,      "Tenders",    "See tenders and their schedules"),
            new(TendersManage,    "Tenders",    "Create and edit tenders"),
            new(GuaranteesManage, "Guarantees", "Record and release EMDs and guarantees"),
            new(ProjectsView,     "Projects",   "See projects and their progress"),
            new(ProjectsManage,   "Projects",   "Create and re-scope projects"),
            new(TasksManage,      "Projects",   "Progress tasks on a project board")
            ,new(MilestonesManage,"Projects",   "Maintain project milestones")
            ,new(FilesView,       "Files",      "See and scan the physical file registry")
            ,new(FilesManage,     "Files",      "Issue, return, transfer and archive files")
            ,new(ReportsView,     "Reports",    "View tender and guarantee reports")
        ],

        Nav =
        [
            new("Tenders",  "/tender/tenders", "Gavel", TendersView),
            new("Projects", "/tender/projects", "Assignment", ProjectsView),
            new("File registry", "/tender/files", "Folder", FilesView)
        ],

        RoleTemplates =
        [
            new("Bid Manager", "Runs tenders and the guarantees lodged against them.",
                [TendersView, TendersManage, GuaranteesManage, ReportsView]),

            new("Project Manager", "Runs projects and their task boards.",
                [ProjectsView, ProjectsManage, TasksManage, MilestonesManage, FilesView]),

            new("Project Member", "Progresses their own tasks without re-scoping the project.",
                [ProjectsView, TasksManage]),

            new("Records Clerk", "Tracks tender and project folders.",
                [FilesView, FilesManage])
        ]
    };

    public static IServiceCollection AddTenderModule(
        this IServiceCollection services, IConfiguration config)
    {
        var connection = config.GetConnectionString("Platform")
            ?? throw new InvalidOperationException("No 'Platform' connection string for the Tender module.");

        services.AddDbContext<TenderDbContext>(options =>
            options.UseNpgsql(connection, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__migrations", "tender");
                npgsql.EnableRetryOnFailure(3);
            }));

        services.AddScoped<ITenderService, TenderService>();
        services.AddScoped<IFileRegistryService, FileRegistryService>();
        services.AddScoped<IScanResolver, TenderFileScanResolver>();
        services.AddScoped<IProjectMilestoneService, ProjectMilestoneService>();
        return services;
    }
}

public static class TenderSeederExtensions
{
    public static async Task SeedTenderAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenderDbContext>();
        await db.Database.MigrateAsync();
    }
}
