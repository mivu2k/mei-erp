using MeiErp.Modules.Repair;
using Xunit;

namespace MeiErp.Modules.Repair.Tests;

/// <summary>
/// The repair pipeline as a state machine.
///
/// Pure, so the question "can a job go from here to there" is answered without
/// a database - which is what makes it worth pinning at all.
/// </summary>
public class JobWorkflowTests
{
    [Fact]
    public void A_job_moves_forward_one_step_at_a_time()
    {
        Assert.True(JobWorkflow.CanMove(JobStatus.Received, JobStatus.Diagnosing));
        Assert.True(JobWorkflow.CanMove(JobStatus.Diagnosing, JobStatus.InProgress));
        Assert.True(JobWorkflow.CanMove(JobStatus.InProgress, JobStatus.Completed));
        Assert.True(JobWorkflow.CanMove(JobStatus.Completed, JobStatus.Delivered));
    }

    [Fact]
    public void A_job_cannot_skip_straight_to_delivered()
    {
        // Otherwise a device is marked handed over before anyone worked on it.
        Assert.False(JobWorkflow.CanMove(JobStatus.Received, JobStatus.Delivered));
        Assert.False(JobWorkflow.CanMove(JobStatus.Diagnosing, JobStatus.Completed));
    }

    [Fact]
    public void A_job_cannot_go_backwards()
    {
        Assert.False(JobWorkflow.CanMove(JobStatus.Completed, JobStatus.InProgress));
        Assert.False(JobWorkflow.CanMove(JobStatus.InProgress, JobStatus.Received));
    }

    [Fact]
    public void Delivered_and_cancelled_are_terminal()
    {
        // A delivered device is with its owner and a cancelled job was never
        // done. Reopening either would rewrite history; raise a new job instead.
        Assert.Empty(JobWorkflow.Next(JobStatus.Delivered));
        Assert.Empty(JobWorkflow.Next(JobStatus.Cancelled));
    }

    [Fact]
    public void A_job_can_be_cancelled_from_any_live_state()
    {
        foreach (var status in JobWorkflow.Open)
            Assert.True(JobWorkflow.CanMove(status, JobStatus.Cancelled),
                $"{status} should be cancellable");
    }

    [Fact]
    public void Quoting_is_optional()
    {
        // Small jobs go straight to work without troubling the customer.
        Assert.True(JobWorkflow.CanMove(JobStatus.Diagnosing, JobStatus.InProgress));
        Assert.True(JobWorkflow.CanMove(JobStatus.Diagnosing, JobStatus.AwaitingApproval));
    }

    [Fact]
    public void Open_is_a_List_so_it_can_be_used_inside_an_EF_predicate()
    {
        // array.Contains(x) inside an EF query binds to the ReadOnlySpan
        // overload and throws at query time. See CLAUDE.md.
        Assert.IsType<List<JobStatus>>(JobWorkflow.Open);
        Assert.DoesNotContain(JobStatus.Delivered, JobWorkflow.Open);
        Assert.DoesNotContain(JobStatus.Cancelled, JobWorkflow.Open);
    }

    [Fact]
    public void Non_billable_work_never_reaches_the_total()
    {
        var job = new Job
        {
            WorkItems =
            [
                new WorkItem { Description = "Screen", Quantity = 1, UnitPrice = 5000, IsBillable = true },
                new WorkItem { Description = "Goodwill clean", Quantity = 1, UnitPrice = 500, IsBillable = false }
            ]
        };

        Assert.Equal(5000, job.Total);
    }

    [Fact]
    public void An_unpriced_line_has_no_margin_rather_than_a_nil_one()
    {
        var known = new WorkItem { Quantity = 2, UnitPrice = 100, UnitCost = 60 };
        var unknown = new WorkItem { Quantity = 2, UnitPrice = 100, UnitCost = null };

        Assert.Equal(80, known.Margin);

        // Zero would read as "we made nothing on it", which is a different and
        // wrong statement from "we do not know what it cost".
        Assert.Null(unknown.Margin);
    }
}
