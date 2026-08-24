using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Tender;

public enum MilestoneStatus { Pending, Achieved, Missed, Cancelled }
public class ProjectMilestone : AuditableEntity
{
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateOnly DueDate { get; set; }
    public DateOnly? AchievedDate { get; set; }
    public MilestoneStatus Status { get; set; }
    public decimal? PaymentAmount { get; set; }
    public int SortOrder { get; set; }
    public string? Notes { get; set; }
    public bool IsOverdueOn(DateOnly today) => Status == MilestoneStatus.Pending && DueDate < today;
}

public static class MilestoneRules
{
    public static Result Reconcile(ProjectMilestone row, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(row.Name)) return Result.Fail("A milestone needs a name.", "milestone.no-name");
        if (row.PaymentAmount < 0) return Result.Fail("Payment amount cannot be negative.", "milestone.bad-amount");
        if (row.Status == MilestoneStatus.Achieved) row.AchievedDate ??= today;
        else row.AchievedDate = null;
        return Result.Success();
    }
}

public interface IProjectMilestoneService
{
    Task<IReadOnlyList<ProjectMilestone>> ListAsync(int projectId, CancellationToken ct = default);
    Task<IReadOnlyList<ProjectMilestone>> UpcomingAsync(int days = 30, CancellationToken ct = default);
    Task<Result<ProjectMilestone>> SaveAsync(ProjectMilestone row, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
}

public sealed class ProjectMilestoneService(TenderDbContext db, IClock clock) : IProjectMilestoneService
{
    public async Task<IReadOnlyList<ProjectMilestone>> ListAsync(int projectId, CancellationToken ct = default) => await db.ProjectMilestones.AsNoTracking().Where(x => x.ProjectId == projectId).OrderBy(x => x.SortOrder).ThenBy(x => x.DueDate).ToListAsync(ct);
    public async Task<IReadOnlyList<ProjectMilestone>> UpcomingAsync(int days = 30, CancellationToken ct = default) { var end = clock.Today.AddDays(days); return await db.ProjectMilestones.AsNoTracking().Include(x => x.Project).Where(x => x.Status == MilestoneStatus.Pending && x.DueDate <= end && x.Project!.Status != ProjectStatus.Cancelled && x.Project.Status != ProjectStatus.Completed).OrderBy(x => x.DueDate).ToListAsync(ct); }
    public async Task<Result<ProjectMilestone>> SaveAsync(ProjectMilestone row, CancellationToken ct = default)
    {
        var valid = MilestoneRules.Reconcile(row, clock.Today); if (valid.Failed) return Result.Fail<ProjectMilestone>(valid.Error!, valid.Code);
        if (!await db.Projects.AnyAsync(x => x.Id == row.ProjectId, ct)) return Result.Fail<ProjectMilestone>("Project not found.", "project.not-found");
        if (row.Id == 0) { if (row.SortOrder == 0) row.SortOrder = await db.ProjectMilestones.Where(x => x.ProjectId == row.ProjectId).CountAsync(ct) + 1; db.Add(row); }
        else { var existing = await db.ProjectMilestones.FirstOrDefaultAsync(x => x.Id == row.Id, ct); if (existing is null) return Result.Fail<ProjectMilestone>("Milestone not found.", "milestone.not-found"); if (existing.ProjectId != row.ProjectId) return Result.Fail<ProjectMilestone>("A milestone cannot move to another project.", "milestone.cannot-move"); db.Entry(existing).CurrentValues.SetValues(row); row = existing; }
        await db.SaveChangesAsync(ct); return Result.Success(row);
    }
    public async Task<Result> DeleteAsync(int id, CancellationToken ct = default) { var row = await db.ProjectMilestones.FindAsync([id], ct); if (row is null) return Result.Fail("Milestone not found.", "milestone.not-found"); db.Remove(row); await db.SaveChangesAsync(ct); return Result.Success(); }
}
