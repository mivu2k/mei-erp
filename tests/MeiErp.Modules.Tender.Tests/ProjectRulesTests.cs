using MeiErp.Modules.Tender;
using Xunit;

namespace MeiErp.Modules.Tender.Tests;

/// <summary>
/// The rules that stop a task's status, percentage and completion date from
/// contradicting each other, and the derived project progress that reads them.
/// </summary>
public class ProjectRulesTests
{
    private static readonly DateOnly Today = new(2026, 8, 21);

    [Fact]
    public void Completing_a_task_forces_it_to_a_hundred_percent()
    {
        var task = new ProjectTask
        {
            Title = "Fit the panel",
            Status = ProjectTaskStatus.Completed,
            PercentComplete = 40
        };

        TaskRules.Reconcile(task, Today);

        // A "done" task reading as 40% is the contradiction this prevents.
        Assert.Equal(100, task.PercentComplete);
        Assert.Equal(Today, task.CompletedOn);
    }

    [Fact]
    public void Re_opening_a_task_clears_its_completion_date_and_caps_the_percentage()
    {
        var task = new ProjectTask
        {
            Title = "Fit the panel",
            Status = ProjectTaskStatus.InProgress,
            PercentComplete = 100,
            CompletedOn = new DateOnly(2026, 8, 1)
        };

        TaskRules.Reconcile(task, Today);

        // Otherwise it stays "finished" while somebody is working on it.
        Assert.Null(task.CompletedOn);
        Assert.Equal(99, task.PercentComplete);
    }

    [Fact]
    public void Any_progress_moves_a_task_off_not_started()
    {
        var task = new ProjectTask
        {
            Title = "Fit the panel",
            Status = ProjectTaskStatus.NotStarted,
            PercentComplete = 10
        };

        TaskRules.Reconcile(task, Today);

        Assert.Equal(ProjectTaskStatus.InProgress, task.Status);
    }

    [Fact]
    public void A_percentage_outside_the_range_is_clamped()
    {
        var over = new ProjectTask { Title = "A", Status = ProjectTaskStatus.InProgress, PercentComplete = 250 };
        var under = new ProjectTask { Title = "B", Status = ProjectTaskStatus.InProgress, PercentComplete = -10 };

        TaskRules.Reconcile(over, Today);
        TaskRules.Reconcile(under, Today);

        Assert.Equal(99, over.PercentComplete);
        Assert.Equal(0, under.PercentComplete);
    }

    [Fact]
    public void Project_progress_averages_its_tasks()
    {
        var project = new Project
        {
            Tasks =
            [
                new ProjectTask { Title = "A", PercentComplete = 100, Status = ProjectTaskStatus.Completed },
                new ProjectTask { Title = "B", PercentComplete = 50, Status = ProjectTaskStatus.InProgress },
                new ProjectTask { Title = "C", PercentComplete = 0, Status = ProjectTaskStatus.NotStarted }
            ]
        };

        Assert.Equal(50, project.ProgressPercent);
    }

    [Fact]
    public void Cancelled_tasks_are_excluded_rather_than_counted_as_done()
    {
        var project = new Project
        {
            Tasks =
            [
                new ProjectTask { Title = "A", PercentComplete = 100, Status = ProjectTaskStatus.Completed },
                new ProjectTask { Title = "B", PercentComplete = 0, Status = ProjectTaskStatus.Cancelled }
            ]
        };

        // Counting the cancelled one as done would report 100% - dropping scope
        // must not be able to flatter the figure. Excluding it leaves one task,
        // which is genuinely complete.
        Assert.Equal(100, project.ProgressPercent);

        var partly = new Project
        {
            Tasks =
            [
                new ProjectTask { Title = "A", PercentComplete = 50, Status = ProjectTaskStatus.InProgress },
                new ProjectTask { Title = "B", PercentComplete = 0, Status = ProjectTaskStatus.Cancelled }
            ]
        };

        // Averaging the cancelled zero in would say 25%.
        Assert.Equal(50, partly.ProgressPercent);
    }

    [Fact]
    public void A_project_with_no_tasks_is_at_nil_rather_than_dividing_by_zero()
    {
        Assert.Equal(0, new Project().ProgressPercent);
    }

    [Fact]
    public void A_completed_task_is_never_overdue()
    {
        var task = new ProjectTask
        {
            Title = "A",
            DueDate = new DateOnly(2026, 8, 1),
            Status = ProjectTaskStatus.Completed
        };

        Assert.False(task.IsOverdue(Today));
    }

    [Fact]
    public void An_unfinished_task_past_its_date_is_overdue()
    {
        var task = new ProjectTask
        {
            Title = "A",
            DueDate = new DateOnly(2026, 8, 1),
            Status = ProjectTaskStatus.InProgress
        };

        Assert.True(task.IsOverdue(Today));
    }

    // ---------- tenders ----------

    [Fact]
    public void A_lump_sum_tender_carries_no_lines_and_no_variance()
    {
        var tender = new TenderRecord { Reference = "T-1", Title = "Lump sum", EstimatedValue = 500_000 };

        // The schedule is optional - a dummy line standing in for the whole bid
        // would be worse than none.
        Assert.Empty(tender.Items);
        Assert.Equal(0, tender.ItemsTotal);
        Assert.Null(tender.VarianceFromEstimate);
    }

    [Fact]
    public void The_priced_lines_disagreeing_with_the_estimate_is_surfaced()
    {
        var tender = new TenderRecord
        {
            Reference = "T-1", Title = "Priced", EstimatedValue = 100_000,
            Items = [new TenderItem { Description = "Cable", Quantity = 100, UnitRate = 1200 }]
        };

        // 120,000 priced against a 100,000 estimate. Seeing the gap is how a
        // mispriced line gets caught before submission.
        Assert.Equal(120_000, tender.ItemsTotal);
        Assert.Equal(20_000, tender.VarianceFromEstimate);
    }

    [Fact]
    public void A_guarantee_that_expired_without_being_released_is_flagged()
    {
        var forgotten = new Guarantee
        {
            Amount = 50_000,
            IssuedOn = new DateOnly(2026, 1, 1),
            ExpiresOn = new DateOnly(2026, 6, 30),
            ReleasedOn = null
        };

        var returned = new Guarantee
        {
            Amount = 50_000,
            IssuedOn = new DateOnly(2026, 1, 1),
            ExpiresOn = new DateOnly(2026, 6, 30),
            ReleasedOn = new DateOnly(2026, 7, 1)
        };

        // Money the company has quietly left with someone else.
        Assert.True(forgotten.IsExpiredUnreleased(Today));
        Assert.True(forgotten.IsOutstanding);

        Assert.False(returned.IsExpiredUnreleased(Today));
        Assert.False(returned.IsOutstanding);
    }

    [Fact]
    public void A_file_cannot_be_issued_to_two_people()
    {
        var file = new PhysicalFile { Status = PhysicalFileStatus.Issued, HolderName = "Ali" };
        var result = FileRegistryRules.Validate(file, FileMovementAction.Issued, new(HolderName: "Sara"));
        Assert.True(result.Failed);
        Assert.Equal("file.already-out", result.Code);
    }

    [Fact]
    public void A_file_can_only_be_transferred_while_out()
    {
        var result = FileRegistryRules.Validate(new PhysicalFile { Status = PhysicalFileStatus.InRegistry }, FileMovementAction.Transferred, new(HolderName: "Sara"));
        Assert.Equal("file.not-out", result.Code);
    }

    [Fact]
    public void Days_out_uses_the_latest_issue_or_transfer()
    {
        var file = new PhysicalFile { Status = PhysicalFileStatus.Issued, Movements = [new() { Action = FileMovementAction.Issued, MovedOn = Today.AddDays(-10) }, new() { Action = FileMovementAction.Transferred, MovedOn = Today.AddDays(-3) }] };
        Assert.Equal(3, file.DaysOutOn(Today));
    }

    [Fact]
    public void Achieving_and_reopening_a_milestone_reconciles_its_date()
    {
        var row = new ProjectMilestone { Name = "Handover", DueDate = Today, Status = MilestoneStatus.Achieved };
        Assert.True(MilestoneRules.Reconcile(row, Today).Ok);
        Assert.Equal(Today, row.AchievedDate);
        row.Status = MilestoneStatus.Pending;
        MilestoneRules.Reconcile(row, Today);
        Assert.Null(row.AchievedDate);
    }
}
