using MeiErp.Platform.Kernel;

namespace MeiErp.Platform.Workflow;

/// <summary>
/// The routing rules, as pure functions over the request's own state.
///
/// Deliberately free of the database and the clock so the decisions that matter
/// - which steps apply, when a step is satisfied, what happens next - can be
/// tested directly. Every date it needs is passed in. The previous platform's
/// approval logic was untestable because it was welded to EF and to
/// <c>DateTime.Now</c>; this is the correction.
/// </summary>
public static class WorkflowRouter
{
    /// <summary>
    /// Freeze the steps that apply to this document into the request.
    ///
    /// The plan is snapshotted rather than re-read, so editing the workflow
    /// tomorrow cannot re-route something submitted today. Steps outside the
    /// amount band are dropped from the plan entirely - never carried as
    /// auto-approved, which would put a signature in the history that nobody gave.
    /// </summary>
    public static Result<List<ApprovalStepState>> BuildPlan(
        WorkflowDefinition definition, decimal? amount)
    {
        var applicable = definition.StepsFor(amount).ToList();

        if (applicable.Count == 0)
        {
            // A document that matches no step must not sail through. Silent
            // auto-approval is the worst possible failure mode for an approval
            // engine: it looks like success.
            return Result.Fail<List<ApprovalStepState>>(
                $"No approval step in '{definition.Name}' covers an amount of {amount:N2}. " +
                "Check the amount bands on the workflow.",
                "workflow.no-applicable-step");
        }

        var plan = applicable.Select(s => new ApprovalStepState
        {
            Order = s.Order,
            Name = s.Name,
            Rule = s.Rule,
            RuleValue = s.RuleValue,
            Quorum = s.Quorum,
            AllowReturn = s.AllowReturn,
            ReminderAfterHours = s.ReminderAfterHours,
            EscalateAfterHours = s.EscalateAfterHours,
            EscalateToRole = s.EscalateToRole,
            Outcome = StepOutcome.Waiting
        }).ToList();

        return Result.Success(plan);
    }

    /// <summary>
    /// Whether recording one more approval satisfies the step.
    /// An <see cref="StepQuorum.All"/> step needs every resolved approver.
    /// </summary>
    public static bool IsStepSatisfied(ApprovalStepState step) =>
        step.ReceivedApprovals >= step.RequiredApprovals;

    /// <summary>The next step awaiting a decision, or null when the request is finished.</summary>
    public static ApprovalStepState? NextStep(ApprovalRequest request) =>
        request.StepStates
            .Where(s => s.Outcome is StepOutcome.Waiting)
            .OrderBy(s => s.Order)
            .FirstOrDefault();

    /// <summary>The step currently open for decisions.</summary>
    public static ApprovalStepState? CurrentStep(ApprovalRequest request) =>
        request.StepStates.FirstOrDefault(s => s.Outcome is StepOutcome.Open);

    /// <summary>
    /// Whether this person may decide this step. Enforces both segregation
    /// rules, which were convention before and are now checked in one place.
    /// </summary>
    public static Result CanDecide(
        ApprovalRequest request,
        WorkflowDefinition definition,
        ApprovalStepState step,
        string userId,
        IReadOnlyList<EligibleApprover> eligible)
    {
        if (request.Status is not ApprovalStatus.Pending)
            return Result.Fail("This request is no longer open.", "workflow.not-open");

        if (step.Outcome is not StepOutcome.Open)
            return Result.Fail("This step is not awaiting a decision.", "workflow.step-not-open");

        if (definition.BlockSelfApproval &&
            string.Equals(userId, request.RequestedByUserId, StringComparison.Ordinal))
        {
            return Result.Fail(
                "You raised this request, so you cannot approve it.",
                "workflow.self-approval");
        }

        if (definition.RequireDistinctApprovers &&
            request.Actions.Any(a =>
                a.StepOrder != step.Order &&
                a.Decision is ApprovalDecision.Approved &&
                string.Equals(a.ActedByUserId, userId, StringComparison.Ordinal)))
        {
            return Result.Fail(
                "You have already approved an earlier step of this request; " +
                "this workflow requires a different approver at each level.",
                "workflow.distinct-approver");
        }

        // On an all-of step the same person must not satisfy the quorum twice.
        if (request.Actions.Any(a =>
                a.StepOrder == step.Order &&
                a.Decision is ApprovalDecision.Approved &&
                string.Equals(a.ActedByUserId, userId, StringComparison.Ordinal)))
        {
            return Result.Fail("You have already approved this step.", "workflow.already-approved");
        }

        if (!eligible.Any(e => string.Equals(e.UserId, userId, StringComparison.Ordinal)))
            return Result.Fail("You are not an approver for this step.", "workflow.not-eligible");

        return Result.Success();
    }

    /// <summary>
    /// Apply a decision and work out where the request lands.
    ///
    /// Returns what changed rather than mutating and hoping the caller notices;
    /// the engine persists it inside one transaction together with the module's
    /// own status update.
    /// </summary>
    public static RoutingOutcome Advance(
        ApprovalRequest request,
        ApprovalStepState step,
        ApprovalDecision decision,
        DateTime nowUtc)
    {
        switch (decision)
        {
            case ApprovalDecision.Rejected:
                step.Outcome = StepOutcome.Rejected;
                step.SettledUtc = nowUtc;
                SkipRemaining(request, nowUtc);
                return Settle(request, ApprovalStatus.Rejected, nowUtc);

            case ApprovalDecision.Returned:
                step.Outcome = StepOutcome.Returned;
                step.SettledUtc = nowUtc;
                SkipRemaining(request, nowUtc);
                // Returned is not terminal: the document stays alive and keeps
                // its history so the raiser can correct and resubmit.
                return Settle(request, ApprovalStatus.Returned, nowUtc);

            case ApprovalDecision.Cancelled:
                step.Outcome = StepOutcome.Skipped;
                step.SettledUtc = nowUtc;
                SkipRemaining(request, nowUtc);
                return Settle(request, ApprovalStatus.Cancelled, nowUtc);

            case ApprovalDecision.Approved:
                step.ReceivedApprovals++;

                if (!IsStepSatisfied(step))
                {
                    // An all-of step still waiting on other signatures stays open.
                    return new RoutingOutcome(
                        ApprovalStatus.Pending, step.Order, StillAwaitingSameStep: true);
                }

                step.Outcome = StepOutcome.Approved;
                step.SettledUtc = nowUtc;

                var next = NextStep(request);
                if (next is null)
                    return Settle(request, ApprovalStatus.Approved, nowUtc);

                next.Outcome = StepOutcome.Open;
                next.StartedUtc = nowUtc;
                request.CurrentStepOrder = next.Order;
                return new RoutingOutcome(ApprovalStatus.Pending, next.Order, StillAwaitingSameStep: false);

            default:
                // Escalated and Reassigned are recorded as actions but do not
                // move the request, so they never reach here.
                return new RoutingOutcome(request.Status, request.CurrentStepOrder, false);
        }
    }

    private static RoutingOutcome Settle(
        ApprovalRequest request, ApprovalStatus status, DateTime nowUtc)
    {
        request.Status = status;
        request.CurrentStepOrder = null;
        request.DueUtc = null;
        request.CompletedUtc = nowUtc;
        return new RoutingOutcome(status, null, false);
    }

    private static void SkipRemaining(ApprovalRequest request, DateTime nowUtc)
    {
        foreach (var s in request.StepStates.Where(s => s.Outcome is StepOutcome.Waiting))
        {
            s.Outcome = StepOutcome.Skipped;
            s.SettledUtc = nowUtc;
        }
    }

    /// <summary>
    /// When the open step falls due, given when it opened. Null when the step
    /// sets no SLA - most do not, and a due date invented for them would make
    /// the whole inbox look overdue.
    /// </summary>
    public static DateTime? DueAt(ApprovalStepState step, DateTime startedUtc) =>
        step.EscalateAfterHours is { } hours
            ? startedUtc.AddHours(hours)
            : step.ReminderAfterHours is { } remind
                ? startedUtc.AddHours(remind)
                : null;
}

/// <param name="Status">Where the request now stands.</param>
/// <param name="CurrentStepOrder">The step now open, or null once settled.</param>
/// <param name="StillAwaitingSameStep">True when an all-of step needs more signatures.</param>
public sealed record RoutingOutcome(
    ApprovalStatus Status,
    int? CurrentStepOrder,
    bool StillAwaitingSameStep);
