using MeiErp.Platform.Workflow;
using Xunit;

namespace MeiErp.Platform.Workflow.Tests;

/// <summary>
/// The routing rules, tested directly. These decide who is allowed to spend
/// money, so they are proven rather than assumed - the gap that left the
/// previous platform's approval flows entirely uncovered.
/// </summary>
public class WorkflowRouterTests
{
    private static readonly DateTime Now = new(2026, 8, 21, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>A three-tier purchase workflow of the kind the office actually runs.</summary>
    private static WorkflowDefinition PurchaseWorkflow() => new()
    {
        Id = 1,
        Name = "Purchase order approval",
        DocumentType = "inventory.purchase-order",
        Steps =
        [
            new WorkflowStep { Order = 1, Name = "Storekeeper",     Rule = ApproverRule.Role, RuleValue = "Storekeeper" },
            new WorkflowStep { Order = 2, Name = "Department head", Rule = ApproverRule.DepartmentHead, MinAmount = 50_000 },
            new WorkflowStep { Order = 3, Name = "Director",        Rule = ApproverRule.Role, RuleValue = "Director", MinAmount = 500_000 }
        ]
    };

    private static ApprovalRequest RequestFor(
        WorkflowDefinition definition, decimal? amount, string raisedBy = "user-raiser")
    {
        var plan = WorkflowRouter.BuildPlan(definition, amount);
        Assert.True(plan.Ok, plan.Error);

        var request = new ApprovalRequest
        {
            Id = 100,
            DocumentType = definition.DocumentType,
            WorkflowDefinitionId = definition.Id,
            Amount = amount,
            RequestedByUserId = raisedBy,
            RequestedByName = "Raiser",
            Status = ApprovalStatus.Pending,
            StepStates = plan.Value
        };

        var first = request.StepStates.OrderBy(s => s.Order).First();
        first.Outcome = StepOutcome.Open;
        first.StartedUtc = Now;
        request.CurrentStepOrder = first.Order;
        return request;
    }

    private static void Approve(ApprovalRequest request, string userId)
    {
        var step = WorkflowRouter.CurrentStep(request)!;
        request.Actions.Add(new ApprovalAction
        {
            StepOrder = step.Order,
            StepName = step.Name,
            Decision = ApprovalDecision.Approved,
            ActedByUserId = userId,
            ActedUtc = Now
        });
        WorkflowRouter.Advance(request, step, ApprovalDecision.Approved, Now);
    }

    // ---------- amount-band routing ----------

    [Fact]
    public void Small_order_needs_only_the_first_signature()
    {
        var request = RequestFor(PurchaseWorkflow(), 10_000);

        Assert.Single(request.StepStates);
        Assert.Equal("Storekeeper", request.StepStates[0].Name);
    }

    [Fact]
    public void Mid_sized_order_adds_the_department_head()
    {
        var request = RequestFor(PurchaseWorkflow(), 120_000);

        Assert.Equal(2, request.StepStates.Count);
        Assert.Equal(["Storekeeper", "Department head"],
            request.StepStates.OrderBy(s => s.Order).Select(s => s.Name));
    }

    [Fact]
    public void Large_order_routes_all_the_way_to_the_director()
    {
        var request = RequestFor(PurchaseWorkflow(), 750_000);

        Assert.Equal(3, request.StepStates.Count);
    }

    [Fact]
    public void Band_boundary_is_inclusive_below_and_exclusive_above()
    {
        // Exactly 50,000 must reach the department head: a band written as
        // "50,000 and above" has to include 50,000, or the one order sitting on
        // the threshold is the one that escapes review.
        var atBoundary = RequestFor(PurchaseWorkflow(), 50_000);
        Assert.Contains(atBoundary.StepStates, s => s.Name == "Department head");

        var justBelow = RequestFor(PurchaseWorkflow(), 49_999.99m);
        Assert.DoesNotContain(justBelow.StepStates, s => s.Name == "Department head");
    }

    [Fact]
    public void A_document_matching_no_step_is_refused_rather_than_auto_approved()
    {
        // Silent auto-approval is the worst failure mode an approval engine has,
        // because it looks exactly like success.
        var gapped = new WorkflowDefinition
        {
            Name = "Gapped",
            Steps = [new WorkflowStep { Order = 1, Name = "Manager", MinAmount = 100_000 }]
        };

        var plan = WorkflowRouter.BuildPlan(gapped, 5_000);

        Assert.True(plan.Failed);
        Assert.Equal("workflow.no-applicable-step", plan.Code);
    }

    [Fact]
    public void An_amount_banded_step_stays_out_when_the_document_has_no_amount()
    {
        var definition = new WorkflowDefinition
        {
            Name = "Leave",
            Steps =
            [
                new WorkflowStep { Order = 1, Name = "Manager" },
                new WorkflowStep { Order = 2, Name = "Director", MinAmount = 1 }
            ]
        };

        var plan = WorkflowRouter.BuildPlan(definition, amount: null);

        Assert.True(plan.Ok);
        Assert.Single(plan.Value);
        Assert.Equal("Manager", plan.Value[0].Name);
    }

    // ---------- progression ----------

    [Fact]
    public void Approving_the_last_step_settles_the_request()
    {
        var request = RequestFor(PurchaseWorkflow(), 10_000);

        Approve(request, "user-store");

        Assert.Equal(ApprovalStatus.Approved, request.Status);
        Assert.Null(request.CurrentStepOrder);
        Assert.Equal(Now, request.CompletedUtc);
    }

    [Fact]
    public void Approving_an_intermediate_step_opens_the_next_one()
    {
        var request = RequestFor(PurchaseWorkflow(), 750_000);

        Approve(request, "user-store");

        Assert.Equal(ApprovalStatus.Pending, request.Status);
        Assert.Equal(2, request.CurrentStepOrder);
        Assert.Equal(StepOutcome.Open, WorkflowRouter.CurrentStep(request)!.Outcome);
    }

    [Fact]
    public void A_full_three_tier_run_ends_approved_with_every_step_recorded()
    {
        var request = RequestFor(PurchaseWorkflow(), 750_000);

        Approve(request, "user-store");
        Approve(request, "user-head");
        Approve(request, "user-director");

        Assert.Equal(ApprovalStatus.Approved, request.Status);
        Assert.All(request.StepStates, s => Assert.Equal(StepOutcome.Approved, s.Outcome));
        Assert.Equal(3, request.Actions.Count);
    }

    // ---------- rejection, return, cancellation ----------

    [Fact]
    public void Rejection_is_terminal_and_skips_every_remaining_step()
    {
        var request = RequestFor(PurchaseWorkflow(), 750_000);
        var step = WorkflowRouter.CurrentStep(request)!;

        WorkflowRouter.Advance(request, step, ApprovalDecision.Rejected, Now);

        Assert.Equal(ApprovalStatus.Rejected, request.Status);
        Assert.Equal(StepOutcome.Rejected, request.StepStates[0].Outcome);
        Assert.All(request.StepStates.Skip(1), s => Assert.Equal(StepOutcome.Skipped, s.Outcome));
    }

    [Fact]
    public void Return_is_not_the_same_as_rejection()
    {
        // Return sends the document back to be fixed; the request stays alive.
        // None of the nine hand-rolled flows this replaces could express it.
        var request = RequestFor(PurchaseWorkflow(), 750_000);
        var step = WorkflowRouter.CurrentStep(request)!;

        WorkflowRouter.Advance(request, step, ApprovalDecision.Returned, Now);

        Assert.Equal(ApprovalStatus.Returned, request.Status);
        Assert.True(request.IsOpen);
    }

    [Fact]
    public void A_rejected_request_is_not_open()
    {
        var request = RequestFor(PurchaseWorkflow(), 10_000);
        WorkflowRouter.Advance(request, WorkflowRouter.CurrentStep(request)!, ApprovalDecision.Rejected, Now);

        Assert.False(request.IsOpen);
    }

    // ---------- quorum ----------

    [Fact]
    public void An_all_of_step_waits_for_every_signature()
    {
        // The quotation case: the customer's yes and the manager's yes are
        // independent, and both are required.
        var definition = new WorkflowDefinition
        {
            Name = "Quotation",
            Steps = [new WorkflowStep { Order = 1, Name = "Customer and manager", Quorum = StepQuorum.All }]
        };
        var request = RequestFor(definition, null);
        var step = WorkflowRouter.CurrentStep(request)!;
        step.RequiredApprovals = 2;

        var afterFirst = WorkflowRouter.Advance(request, step, ApprovalDecision.Approved, Now);

        Assert.True(afterFirst.StillAwaitingSameStep);
        Assert.Equal(ApprovalStatus.Pending, request.Status);

        var afterSecond = WorkflowRouter.Advance(request, step, ApprovalDecision.Approved, Now);

        Assert.False(afterSecond.StillAwaitingSameStep);
        Assert.Equal(ApprovalStatus.Approved, request.Status);
    }

    [Fact]
    public void One_rejection_kills_an_all_of_step_even_after_a_yes()
    {
        var definition = new WorkflowDefinition
        {
            Name = "Quotation",
            Steps = [new WorkflowStep { Order = 1, Name = "Both", Quorum = StepQuorum.All }]
        };
        var request = RequestFor(definition, null);
        var step = WorkflowRouter.CurrentStep(request)!;
        step.RequiredApprovals = 2;

        WorkflowRouter.Advance(request, step, ApprovalDecision.Approved, Now);
        WorkflowRouter.Advance(request, step, ApprovalDecision.Rejected, Now);

        Assert.Equal(ApprovalStatus.Rejected, request.Status);
    }

    // ---------- segregation of duties ----------

    [Fact]
    public void The_raiser_cannot_approve_their_own_request()
    {
        var definition = PurchaseWorkflow();
        var request = RequestFor(definition, 10_000, raisedBy: "user-raiser");
        var step = WorkflowRouter.CurrentStep(request)!;
        var eligible = new[] { new EligibleApprover("user-raiser", "Raiser", null) };

        var can = WorkflowRouter.CanDecide(request, definition, step, "user-raiser", eligible);

        Assert.True(can.Failed);
        Assert.Equal("workflow.self-approval", can.Code);
    }

    [Fact]
    public void Distinct_approvers_stops_one_person_signing_twice_up_the_chain()
    {
        var definition = PurchaseWorkflow();
        definition.RequireDistinctApprovers = true;
        var request = RequestFor(definition, 750_000);

        Approve(request, "user-manager");

        var step = WorkflowRouter.CurrentStep(request)!;
        var eligible = new[] { new EligibleApprover("user-manager", "Manager", null) };

        var can = WorkflowRouter.CanDecide(request, definition, step, "user-manager", eligible);

        Assert.True(can.Failed);
        Assert.Equal("workflow.distinct-approver", can.Code);
    }

    [Fact]
    public void Someone_outside_the_eligible_set_is_refused()
    {
        var definition = PurchaseWorkflow();
        var request = RequestFor(definition, 10_000);
        var step = WorkflowRouter.CurrentStep(request)!;
        var eligible = new[] { new EligibleApprover("user-store", "Storekeeper", null) };

        var can = WorkflowRouter.CanDecide(request, definition, step, "user-passerby", eligible);

        Assert.True(can.Failed);
        Assert.Equal("workflow.not-eligible", can.Code);
    }

    [Fact]
    public void A_settled_request_accepts_no_further_decisions()
    {
        var definition = PurchaseWorkflow();
        var request = RequestFor(definition, 10_000);
        var step = WorkflowRouter.CurrentStep(request)!;
        Approve(request, "user-store");

        var can = WorkflowRouter.CanDecide(request, definition, step, "user-store",
            [new EligibleApprover("user-store", "Storekeeper", null)]);

        Assert.True(can.Failed);
        Assert.Equal("workflow.not-open", can.Code);
    }

    // ---------- SLA ----------

    [Fact]
    public void A_step_with_no_sla_has_no_due_date()
    {
        // Inventing one would make the whole inbox read as overdue.
        var step = new ApprovalStepState { Order = 1, Name = "Manager" };

        Assert.Null(WorkflowRouter.DueAt(step, Now));
    }

    [Fact]
    public void Escalation_hours_win_over_reminder_hours_for_the_due_date()
    {
        var step = new ApprovalStepState
        {
            Order = 1, Name = "Manager", ReminderAfterHours = 24, EscalateAfterHours = 72
        };

        Assert.Equal(Now.AddHours(72), WorkflowRouter.DueAt(step, Now));
    }
}
