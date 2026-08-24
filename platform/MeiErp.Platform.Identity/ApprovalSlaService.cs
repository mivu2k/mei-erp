using MeiErp.Platform.Kernel;
using MeiErp.Platform.Notifications;
using MeiErp.Platform.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MeiErp.Platform.Identity;

/// <summary>Processes reminder and escalation deadlines for open approval steps.</summary>
public sealed class ApprovalSlaService(
    PlatformDbContext db,
    IApproverResolver resolver,
    IUserDirectory users,
    INotifier notifier,
    IClock clock)
{
    public async Task<int> SweepAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var requests = await db.ApprovalRequests
            .Include(r => r.StepStates)
            .Include(r => r.Actions)
            .AsSplitQuery()
            .Where(r => r.Status == ApprovalStatus.Pending && r.DueUtc != null && r.DueUtc <= now)
            .ToListAsync(ct);

        var changed = 0;
        foreach (var request in requests)
        {
            var step = WorkflowRouter.CurrentStep(request);
            if (step?.StartedUtc is null) continue;

            var escalationHours = step.EscalateAfterHours;
            var escalationDue = escalationHours is not null
                && step.EscalatedUtc is null
                && step.StartedUtc.Value.AddHours(escalationHours.Value) <= now;

            // Escalation wins when both deadlines have passed. Sending a reminder
            // immediately before an escalation is duplicate noise.
            if (escalationDue)
            {
                if (!string.IsNullOrWhiteSpace(step.EscalateToRole))
                {
                    var escalatedTo = await users.InRoleAsync(step.EscalateToRole, ct);
                    if (escalatedTo.Count > 0)
                    {
                        await notifier.NotifyAsync(new NotificationRequest(
                            [.. escalatedTo.Select(u => new NotificationRecipient(u.Id, u.FullName, u.Email))],
                            NotificationCategories.ApprovalEscalated,
                            $"{request.DocumentReference} has been escalated",
                            $"{request.Summary}\nThe {step.Name} approval is overdue and has been escalated to {step.EscalateToRole}.",
                            request.DocumentUrl,
                            request.ModuleKey,
                            NotificationPriority.High,
                            EventKey(request, step, "escalated")), ct);

                        step.EscalatedUtc = now;
                        request.Actions.Add(new ApprovalAction
                        {
                            StepOrder = step.Order,
                            StepName = step.Name,
                            Decision = ApprovalDecision.Escalated,
                            ActedByUserId = "system",
                            ActedByName = "System",
                            ActedUtc = now,
                            Comment = $"Escalated to role {step.EscalateToRole} after {escalationHours.GetValueOrDefault()} hours."
                        });
                        changed++;
                        continue;
                    }
                }
            }

            var reminderHours = step.ReminderAfterHours;
            var reminderDue = reminderHours is not null
                && step.RemindedUtc is null
                && step.StartedUtc.Value.AddHours(reminderHours.Value) <= now;

            if (!reminderDue) continue;

            var approvers = await resolver.ResolveAsync(step, request, ct);
            if (approvers.Count == 0) continue;

            await notifier.NotifyAsync(new NotificationRequest(
                [.. approvers.Select(a => new NotificationRecipient(a.UserId, a.Name, a.Email))],
                NotificationCategories.ApprovalReminder,
                $"Reminder: {request.DocumentReference} needs your approval",
                $"{request.Summary}\nThis has been waiting at {step.Name} for {reminderHours.GetValueOrDefault()} hours.",
                request.DocumentUrl,
                request.ModuleKey,
                NotificationPriority.High,
                EventKey(request, step, "reminder")), ct);

            step.RemindedUtc = now;
            changed++;
        }

        if (changed > 0) await db.SaveChangesAsync(ct);
        return changed;
    }

    private static string EventKey(ApprovalRequest request, ApprovalStepState step, string kind) =>
        $"approval:{request.DocumentType}:{request.DocumentId}:step:{step.Order}:{kind}";
}

/// <summary>Runs the SLA sweep periodically without coupling time-based work to web requests.</summary>
public sealed class ApprovalSlaWorker(
    IServiceScopeFactory scopes,
    ILogger<ApprovalSlaWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SweepAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await SweepAsync(stoppingToken);
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ApprovalSlaService>().SweepAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (DbUpdateConcurrencyException ex)
        {
            // Another app instance won this sweep; xmin prevents duplicate state.
            logger.LogInformation(ex, "Approval SLA sweep was completed by another instance.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Approval SLA sweep failed; it will retry on the next interval.");
        }
    }
}
