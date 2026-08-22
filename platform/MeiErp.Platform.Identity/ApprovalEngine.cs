using MeiErp.Platform.Kernel;
using MeiErp.Platform.Notifications;
using MeiErp.Platform.Workflow;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Platform.Identity;

/// <summary>
/// The approval engine, wired to the database.
///
/// All routing decisions live in <see cref="WorkflowRouter"/>, which is pure
/// and separately tested. This class only loads, persists and notifies - so the
/// rules that decide who may spend money are never tangled up with EF.
/// </summary>
public sealed class ApprovalEngine(
    PlatformDbContext db,
    IApproverResolver resolver,
    ICurrentUser currentUser,
    IClock clock,
    IModuleCatalog catalog,
    INotifier notifier,
    IEnumerable<IApprovalSink> sinks) : IApprovalEngine
{
    /// <summary>
    /// Groups the notifications one step's assignment raised, so deciding it
    /// stands the rest down.
    ///
    /// Built from the document and step rather than the request id, because it
    /// has to be known <i>before</i> the request is saved - that is what lets the
    /// notification rows commit in the same transaction as the approval.
    /// </summary>
    private static string StepEvent(string documentType, int documentId, int stepOrder) =>
        $"approval:{documentType}:{documentId}:step:{stepOrder}";

    public async Task<Result<ApprovalRequest>> SubmitAsync(
        SubmitApproval request, CancellationToken ct = default)
    {
        var definition = await db.Workflows
            .Include(w => w.Steps)
            .FirstOrDefaultAsync(w => w.DocumentType == request.DocumentType && w.IsActive, ct);

        if (definition is null)
        {
            return Result.Fail<ApprovalRequest>(
                $"No active approval workflow covers {request.DocumentType}. " +
                "An administrator needs to set one up before this can be submitted.",
                "approval.no-workflow");
        }

        // Submitting twice would produce two competing routes and two different
        // answers for the same record. There is a filtered unique index behind
        // this too, so a race loses at the database rather than here.
        var alreadyOpen = await db.ApprovalRequests.AnyAsync(
            r => r.DocumentType == request.DocumentType
              && r.DocumentId == request.DocumentId
              && r.Status == ApprovalStatus.Pending, ct);

        if (alreadyOpen)
            return Result.Fail<ApprovalRequest>("This is already awaiting approval.", "approval.already-open");

        var plan = WorkflowRouter.BuildPlan(definition, request.Amount);
        if (plan.Failed)
            return Result.Fail<ApprovalRequest>(plan.Error!, plan.Code);

        var now = clock.UtcNow;

        var approval = new ApprovalRequest
        {
            ModuleKey = request.ModuleKey,
            DocumentType = request.DocumentType,
            DocumentId = request.DocumentId,
            DocumentReference = request.DocumentReference,
            Summary = request.Summary,
            DocumentUrl = request.DocumentUrl,
            Amount = request.Amount,
            Currency = request.Currency,
            DepartmentId = request.DepartmentId,
            ProjectId = request.ProjectId,
            RequestedByUserId = currentUser.UserId ?? "system",
            RequestedByName = currentUser.Name ?? "System",
            RequestedUtc = now,
            WorkflowDefinitionId = definition.Id,
            DefinitionRevision = definition.Revision,
            Status = ApprovalStatus.Pending,
            StepStates = plan.Value
        };

        var opened = await OpenFirstStepAsync(approval, now, ct);
        if (opened.Failed)
            return Result.Fail<ApprovalRequest>(opened.Error!, opened.Code);

        db.ApprovalRequests.Add(approval);

        // Staged, not sent: the notification rows commit with the approval on the
        // next line. Without that, an approval could land with nobody told, which
        // is the failure that made the engine's best feature invisible.
        await NotifyStepAssignedAsync(approval, ct);

        await db.SaveChangesAsync(ct);

        return Result.Success(approval);
    }

    /// <summary>
    /// Opens the first step and resolves how many signatures it needs.
    ///
    /// A step nobody can approve is refused at submission rather than accepted
    /// and left to rot in a queue - the raiser finds out immediately, while
    /// they still have the context to do something about it.
    /// </summary>
    private async Task<Result> OpenFirstStepAsync(
        ApprovalRequest approval, DateTime now, CancellationToken ct)
    {
        var first = approval.StepStates.OrderBy(s => s.Order).First();

        var eligible = await resolver.ResolveAsync(first, approval, ct);
        if (eligible.Count == 0)
        {
            return Result.Fail(
                $"Nobody can approve '{first.Name}'. " +
                Explain(first) +
                " Fix that before submitting, or the request would have nowhere to go.",
                "approval.no-approver");
        }

        first.Outcome = StepOutcome.Open;
        first.StartedUtc = now;
        first.RequiredApprovals = first.Quorum == StepQuorum.All ? eligible.Count : 1;

        approval.CurrentStepOrder = first.Order;
        approval.DueUtc = WorkflowRouter.DueAt(first, now);

        return Result.Success();
    }

    /// <summary>
    /// Tells whoever can act on the currently open step that it is waiting.
    ///
    /// Everybody eligible is told, not just the first: a step needing one of
    /// four signatures is answered by whichever of the four gets there, and the
    /// other three are stood down when it settles.
    /// </summary>
    private async Task NotifyStepAssignedAsync(ApprovalRequest approval, CancellationToken ct)
    {
        var step = WorkflowRouter.CurrentStep(approval);
        if (step is null) return;

        var eligible = await resolver.ResolveAsync(step, approval, ct);
        if (eligible.Count == 0) return;

        var amount = approval.Amount is { } value
            ? $"{approval.Currency ?? ""} {value:N0}".Trim() + " — "
            : "";

        await notifier.NotifyAsync(new NotificationRequest(
            [.. eligible.Select(e => new NotificationRecipient(e.UserId, e.Name, e.Email))],
            NotificationCategories.ApprovalAssigned,
            $"{approval.DocumentReference} needs your approval",
            $"{amount}{approval.Summary}\nRaised by {approval.RequestedByName}. Step: {step.Name}.",
            approval.DocumentUrl,
            approval.ModuleKey,
            NotificationPriority.High,
            StepEvent(approval.DocumentType, approval.DocumentId, step.Order)), ct);
    }

    /// <summary>
    /// Tells the person who raised it what happened to it.
    ///
    /// Cancellation is the one outcome nobody is told about: the raiser is the
    /// only one who can cancel, so the message would go straight back to whoever
    /// just clicked the button.
    /// </summary>
    private async Task NotifySettledAsync(
        ApprovalRequest approval, ApprovalStatus status, string? comment, CancellationToken ct)
    {
        if (status is ApprovalStatus.Cancelled) return;

        var what = status switch
        {
            ApprovalStatus.Approved => "was approved",
            ApprovalStatus.Rejected => "was rejected",
            ApprovalStatus.Returned => "was returned for correction",
            _ => "was decided"
        };

        var body = $"{approval.Summary}\nYour {approval.DocumentReference} {what}.";
        if (!string.IsNullOrWhiteSpace(comment)) body += $"\n\n\"{comment}\"";

        // The raiser's address is read now rather than snapshotted at submission:
        // somebody who changed their email since raising it should be told at the
        // address they have today.
        var email = await db.Users
            .Where(u => u.Id == approval.RequestedByUserId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct);

        await notifier.NotifyAsync(new NotificationRequest(
            [new NotificationRecipient(
                approval.RequestedByUserId, approval.RequestedByName, email)],
            NotificationCategories.ApprovalSettled,
            $"{approval.DocumentReference} {what}",
            body,
            approval.DocumentUrl,
            approval.ModuleKey,
            // A rejection or a return is blocking somebody, so it earns the
            // channels that reach outside the app. An approval is good news
            // that can wait for the bell.
            status is ApprovalStatus.Approved
                ? NotificationPriority.Normal
                : NotificationPriority.High), ct);
    }

    /// <summary>Says what is actually missing, rather than "no approver found".</summary>
    private static string Explain(ApprovalStepState step) => step.Rule switch
    {
        ApproverRule.LineManager =>
            "The person raising it has no line manager recorded.",
        ApproverRule.DepartmentHead =>
            "Their department has no head set, or they are not in a department.",
        ApproverRule.Role =>
            $"Nobody active holds the '{step.RuleValue}' role.",
        ApproverRule.Permission =>
            $"Nobody active holds the '{step.RuleValue}' permission.",
        ApproverRule.User =>
            "The named approver is deactivated or no longer exists.",
        _ => "The step's approver rule matches nobody."
    };

    public async Task<Result<ApprovalRequest>> DecideAsync(
        int requestId, ApprovalDecision decision, string? comment, CancellationToken ct = default)
    {
        var approval = await LoadAsync(requestId, ct);
        if (approval is null)
            return Result.Fail<ApprovalRequest>("That request no longer exists.", "approval.not-found");

        var definition = await db.Workflows
            .Include(w => w.Steps)
            .FirstOrDefaultAsync(w => w.Id == approval.WorkflowDefinitionId, ct);

        if (definition is null)
            return Result.Fail<ApprovalRequest>("Its workflow no longer exists.", "approval.no-workflow");

        var step = WorkflowRouter.CurrentStep(approval);
        if (step is null)
            return Result.Fail<ApprovalRequest>("This request is not awaiting a decision.", "approval.not-open");

        if (decision is ApprovalDecision.Returned && !step.AllowReturn)
            return Result.Fail<ApprovalRequest>("This step does not allow returning for correction.", "approval.no-return");

        if (decision is ApprovalDecision.Rejected or ApprovalDecision.Returned
            && string.IsNullOrWhiteSpace(comment))
        {
            // Somebody has to act on this. "Rejected" with no reason is a
            // dead end for whoever raised it.
            return Result.Fail<ApprovalRequest>(
                "Say why - the person who raised this needs to know what to do next.",
                "approval.reason-required");
        }

        var userId = currentUser.UserId ?? "";
        var eligible = await resolver.ResolveAsync(step, approval, ct);

        var allowed = WorkflowRouter.CanDecide(approval, definition, step, userId, eligible);
        if (allowed.Failed)
            return Result.Fail<ApprovalRequest>(allowed.Error!, allowed.Code);

        var acting = eligible.First(e => e.UserId == userId);
        var now = clock.UtcNow;

        approval.Actions.Add(new ApprovalAction
        {
            StepOrder = step.Order,
            StepName = step.Name,
            Decision = decision,
            ActedByUserId = userId,
            ActedByName = currentUser.Name ?? acting.Name,
            OnBehalfOfUserId = acting.OnBehalfOfUserId,
            OnBehalfOfName = acting.OnBehalfOfName,
            ActedUtc = now,
            Comment = comment
        });

        var outcome = WorkflowRouter.Advance(approval, step, decision, now);

        // Whoever else was holding this step no longer needs to look at it. Done
        // before anything new is raised, so a step that routes back to the same
        // person leaves them the new message rather than clearing it.
        if (!outcome.StillAwaitingSameStep)
        {
            await notifier.DismissEventAsync(
                StepEvent(approval.DocumentType, approval.DocumentId, step.Order), ct);
        }

        // A newly opened step needs its own quorum resolved: the set of eligible
        // approvers can have changed while the request was in flight.
        if (outcome.Status is ApprovalStatus.Pending && !outcome.StillAwaitingSameStep)
        {
            var next = WorkflowRouter.CurrentStep(approval);
            if (next is not null)
            {
                var nextEligible = await resolver.ResolveAsync(next, approval, ct);

                if (nextEligible.Count == 0)
                {
                    return Result.Fail<ApprovalRequest>(
                        $"The next step, '{next.Name}', has no approver. " + Explain(next) +
                        " The request stays where it is until that is fixed.",
                        "approval.no-approver");
                }

                next.RequiredApprovals = next.Quorum == StepQuorum.All ? nextEligible.Count : 1;
                approval.DueUtc = WorkflowRouter.DueAt(next, now);

                await NotifyStepAssignedAsync(approval, ct);
            }
        }

        // The module updates its own record in this same transaction, so its
        // status and the approval history can never disagree.
        if (outcome.Status is not ApprovalStatus.Pending)
        {
            var sink = sinks.FirstOrDefault(s => s.DocumentType == approval.DocumentType);
            if (sink is not null)
            {
                var applied = await sink.OnSettledAsync(
                    approval.DocumentId, outcome.Status, approval, ct);

                if (applied.Failed)
                    return Result.Fail<ApprovalRequest>(applied.Error!, applied.Code);
            }

            await NotifySettledAsync(approval, outcome.Status, comment, ct);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(approval);
    }

    public async Task<Result> CancelAsync(
        int requestId, string? reason, CancellationToken ct = default)
    {
        var approval = await LoadAsync(requestId, ct);
        if (approval is null) return Result.Fail("That request no longer exists.", "approval.not-found");

        if (approval.RequestedByUserId != currentUser.UserId)
            return Result.Fail("Only the person who raised this can withdraw it.", "approval.not-raiser");

        if (approval.Status is not ApprovalStatus.Pending)
        {
            // Cancelling something already approved would strand whatever the
            // approval authorised.
            return Result.Fail("This has already been decided.", "approval.not-open");
        }

        var step = WorkflowRouter.CurrentStep(approval)!;
        var now = clock.UtcNow;

        approval.Actions.Add(new ApprovalAction
        {
            StepOrder = step.Order,
            StepName = step.Name,
            Decision = ApprovalDecision.Cancelled,
            ActedByUserId = currentUser.UserId ?? "",
            ActedByName = currentUser.Name ?? "",
            ActedUtc = now,
            Comment = reason
        });

        WorkflowRouter.Advance(approval, step, ApprovalDecision.Cancelled, now);

        var sink = sinks.FirstOrDefault(s => s.DocumentType == approval.DocumentType);
        if (sink is not null)
            await sink.OnSettledAsync(approval.DocumentId, ApprovalStatus.Cancelled, approval, ct);

        // The approvers holding it are told nothing new, but what they were told
        // is stood down - a bell full of withdrawn requests teaches people to
        // ignore the bell.
        await notifier.DismissEventAsync(
            StepEvent(approval.DocumentType, approval.DocumentId, step.Order), ct);

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<ApprovalRequest>> ResubmitAsync(
        int requestId, CancellationToken ct = default)
    {
        var previous = await LoadAsync(requestId, ct);
        if (previous is null)
            return Result.Fail<ApprovalRequest>("That request no longer exists.", "approval.not-found");

        if (previous.Status is not ApprovalStatus.Returned)
            return Result.Fail<ApprovalRequest>("Only a returned request can be resubmitted.", "approval.not-returned");

        if (previous.RequestedByUserId != currentUser.UserId)
            return Result.Fail<ApprovalRequest>("Only the person who raised this can resubmit it.", "approval.not-raiser");

        // Routing restarts from step one: a corrected document is a different
        // document, and the earlier approvers signed off on the old one.
        previous.Status = ApprovalStatus.Cancelled;
        previous.CompletedUtc = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        return await SubmitAsync(new SubmitApproval(
            previous.ModuleKey, previous.DocumentType, previous.DocumentId,
            previous.DocumentReference, previous.Summary, previous.DocumentUrl,
            previous.Amount, previous.Currency, previous.DepartmentId, previous.ProjectId), ct);
    }

    public async Task<IReadOnlyList<ApprovalInboxItem>> InboxAsync(CancellationToken ct = default)
    {
        var userId = currentUser.UserId;
        if (userId is null) return [];

        var open = await db.ApprovalRequests
            .Include(r => r.StepStates)
            .Where(r => r.Status == ApprovalStatus.Pending)
            .ToListAsync(ct);

        var now = clock.UtcNow;
        var items = new List<ApprovalInboxItem>();

        foreach (var request in open)
        {
            var step = WorkflowRouter.CurrentStep(request);
            if (step is null) continue;

            // Resolved per request because delegation and department heads can
            // change while things are in flight. At registry scale this is a
            // handful of small queries; if the inbox ever gets slow, this is
            // the loop to attack.
            var eligible = await resolver.ResolveAsync(step, request, ct);
            var mine = eligible.FirstOrDefault(e => e.UserId == userId);
            if (mine is null) continue;

            // Never show someone their own request to approve.
            if (request.RequestedByUserId == userId) continue;

            items.Add(new ApprovalInboxItem(
                request.Id, request.ModuleKey,
                catalog.Find(request.ModuleKey)?.Name ?? request.ModuleKey,
                request.DocumentType, request.DocumentReference, request.Summary,
                request.DocumentUrl, request.Amount, request.Currency,
                request.RequestedByName, request.RequestedUtc,
                step.Name, step.Order, request.DueUtc,
                request.DueUtc is not null && request.DueUtc < now,
                mine.OnBehalfOfName));
        }

        // Overdue first, then oldest - the order someone should work them in.
        return items
            .OrderByDescending(i => i.IsOverdue)
            .ThenBy(i => i.RequestedUtc)
            .ToList();
    }

    public async Task<Result> CanDecideAsync(int requestId, CancellationToken ct = default)
    {
        var approval = await LoadAsync(requestId, ct);
        if (approval is null) return Result.Fail("That request no longer exists.", "approval.not-found");

        var definition = await db.Workflows
            .Include(w => w.Steps)
            .FirstOrDefaultAsync(w => w.Id == approval.WorkflowDefinitionId, ct);

        if (definition is null) return Result.Fail("Its workflow no longer exists.", "approval.no-workflow");

        var step = WorkflowRouter.CurrentStep(approval);
        if (step is null) return Result.Fail("This request is not awaiting a decision.", "approval.not-open");

        var eligible = await resolver.ResolveAsync(step, approval, ct);
        return WorkflowRouter.CanDecide(
            approval, definition, step, currentUser.UserId ?? "", eligible);
    }

    public async Task<ApprovalHistory?> HistoryAsync(
        string documentType, int documentId, CancellationToken ct = default)
    {
        var approval = await db.ApprovalRequests
            .Include(r => r.StepStates)
            .Include(r => r.Actions)
            .Where(r => r.DocumentType == documentType && r.DocumentId == documentId)
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync(ct);

        if (approval is null) return null;

        return new ApprovalHistory(
            approval.Id,
            approval.Status,
            WorkflowRouter.CurrentStep(approval)?.Name,
            approval.StepStates.OrderBy(s => s.Order)
                .Select(s => new ApprovalHistoryStep(
                    s.Order, s.Name, s.Outcome, s.RequiredApprovals, s.ReceivedApprovals,
                    s.StartedUtc, s.SettledUtc)).ToList(),
            approval.Actions.OrderBy(a => a.ActedUtc)
                .Select(a => new ApprovalHistoryAction(
                    a.StepOrder, a.StepName, a.Decision, a.ActedByName,
                    a.OnBehalfOfName, a.ActedUtc, a.Comment)).ToList());
    }

    private Task<ApprovalRequest?> LoadAsync(int id, CancellationToken ct) =>
        db.ApprovalRequests
          .Include(r => r.StepStates)
          .Include(r => r.Actions)
          .FirstOrDefaultAsync(r => r.Id == id, ct);
}

/// <summary>
/// Turns a step's rule into the actual people who may sign it, applying any
/// delegation that is live today.
/// </summary>
public sealed class ApproverResolver(
    PlatformDbContext db,
    IUserDirectory directory,
    IClock clock) : IApproverResolver
{
    public async Task<IReadOnlyList<EligibleApprover>> ResolveAsync(
        ApprovalStepState step, ApprovalRequest request, CancellationToken ct = default)
    {
        var direct = await ResolveDirectAsync(step, request, ct);
        if (direct.Count == 0) return direct;

        return await ApplyDelegationsAsync(direct, request, ct);
    }

    private async Task<List<EligibleApprover>> ResolveDirectAsync(
        ApprovalStepState step, ApprovalRequest request, CancellationToken ct)
    {
        switch (step.Rule)
        {
            case ApproverRule.LineManager:
            {
                var manager = await directory.LineManagerOfAsync(request.RequestedByUserId, ct);
                return Single(manager);
            }

            case ApproverRule.DepartmentHead:
            {
                if (request.DepartmentId is null) return [];
                var head = await directory.DepartmentHeadAsync(request.DepartmentId, ct);
                return Single(head);
            }

            case ApproverRule.Role when step.RuleValue is not null:
            {
                var people = await directory.InRoleAsync(step.RuleValue, ct);
                return [.. people.Select(Map)];
            }

            case ApproverRule.Permission when step.RuleValue is not null:
            {
                var people = await directory.WithPermissionAsync(step.RuleValue, ct);
                return [.. people.Select(Map)];
            }

            case ApproverRule.User when step.RuleValue is not null:
            {
                var person = await directory.FindAsync(step.RuleValue, ct);
                return Single(person);
            }

            default:
                // ProjectManager and BudgetHolder arrive with the modules that
                // own those concepts. Returning nobody makes the request refuse
                // at submission with a clear reason, rather than approving.
                return [];
        }
    }

    /// <summary>
    /// Adds anyone standing in for an eligible approver today.
    ///
    /// The delegate is added rather than substituted: if the original is at
    /// their desk after all, they can still sign it themselves.
    /// </summary>
    private async Task<IReadOnlyList<EligibleApprover>> ApplyDelegationsAsync(
        List<EligibleApprover> direct, ApprovalRequest request, CancellationToken ct)
    {
        var today = clock.Today;
        var ids = direct.Select(a => a.UserId).ToList();

        var delegations = await db.ApprovalDelegations
            .Where(d => ids.Contains(d.FromUserId)
                     && d.FromDate <= today && d.ToDate >= today
                     && (d.DocumentType == null || d.DocumentType == request.DocumentType))
            .ToListAsync(ct);

        if (delegations.Count == 0) return direct;

        var all = new List<EligibleApprover>(direct);

        foreach (var d in delegations)
        {
            // A delegation can be capped below the delegator's own authority.
            if (d.MaxAmount is not null && request.Amount > d.MaxAmount) continue;

            if (all.Any(a => a.UserId == d.ToUserId && a.OnBehalfOfUserId is null)) continue;

            all.Add(new EligibleApprover(
                d.ToUserId, d.ToName, null, d.FromUserId, d.FromName));
        }

        return all;
    }

    private static List<EligibleApprover> Single(UserSummary? user) =>
        user is null || !user.IsActive ? [] : [Map(user)];

    private static EligibleApprover Map(UserSummary u) =>
        new(u.Id, u.FullName, u.Email);
}
