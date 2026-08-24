using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Tender;

/// <summary>A work item attached directly to a tender (the legacy WorkTasks table supported both scopes).</summary>
public sealed class TenderTask : AuditableEntity
{
    public int TenderRecordId { get; set; }
    public TenderRecord? Tender { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public DateOnly? CompletedOn { get; set; }
    public ProjectTaskStatus Status { get; set; } = ProjectTaskStatus.NotStarted;
    public int PercentComplete { get; set; }
    public int Priority { get; set; }
    public decimal? EstimatedHours { get; set; }
    public decimal? ActualHours { get; set; }
    public int SortOrder { get; set; }
    public string? AssigneeUserId { get; set; }
    public string? AssigneeName { get; set; }
    public string? Notes { get; set; }
}

public static class TenderTaskRules
{
    public static Result Validate(TenderTask task)
    {
        if (string.IsNullOrWhiteSpace(task.Title)) return Result.Fail("A task needs a title.", "task.no-title");
        if (task.PercentComplete is < 0 or > 100) return Result.Fail("Task progress must be between 0 and 100.", "task.bad-progress");
        if (task.DueDate is not null && task.StartDate is not null && task.DueDate < task.StartDate) return Result.Fail("Task due date is before its start date.", "task.bad-dates");
        return Result.Success();
    }
}
