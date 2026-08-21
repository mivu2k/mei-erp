using MeiErp.Platform.Kernel;
using MeiErp.Platform.Workflow;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Platform.Identity;

/// <summary>
/// Designing approval workflows.
///
/// The validation here is the point of the screen: a workflow that looks
/// plausible but leaves a gap in its amount bands will silently refuse to route
/// a document, and the person who saved it will not find out until someone
/// tries to submit one.
/// </summary>
public interface IWorkflowAdminService
{
    Task<IReadOnlyList<WorkflowSummary>> ListAsync(CancellationToken ct = default);
    Task<WorkflowDefinition?> GetAsync(int id, CancellationToken ct = default);
    Task<Result<int>> SaveAsync(WorkflowDefinition definition, CancellationToken ct = default);

    /// <summary>Problems worth telling the designer about before they save.</summary>
    IReadOnlyList<string> Validate(WorkflowDefinition definition);
}

public sealed record WorkflowSummary(
    int Id, string DocumentType, string DocumentName, string Name,
    int Revision, bool IsActive, int StepCount, int OpenRequests);

/// <inheritdoc />
public sealed class WorkflowAdminService(
    PlatformDbContext db, IModuleCatalog catalog) : IWorkflowAdminService
{
    public async Task<IReadOnlyList<WorkflowSummary>> ListAsync(CancellationToken ct = default)
    {
        var rows = await db.Workflows
            .AsNoTracking()
            .Select(w => new
            {
                w.Id, w.DocumentType, w.Name, w.Revision, w.IsActive,
                StepCount = w.Steps.Count,
                Open = db.ApprovalRequests.Count(
                    r => r.DocumentType == w.DocumentType && r.Status == ApprovalStatus.Pending)
            })
            .ToListAsync(ct);

        return rows.Select(r => new WorkflowSummary(
            r.Id, r.DocumentType,
            catalog.AllApprovables.FirstOrDefault(a => a.Key == r.DocumentType)?.Name
                ?? r.DocumentType,
            r.Name, r.Revision, r.IsActive, r.StepCount, r.Open))
            .OrderBy(r => r.DocumentName)
            .ToList();
    }

    public Task<WorkflowDefinition?> GetAsync(int id, CancellationToken ct = default) =>
        db.Workflows
          .Include(w => w.Steps.OrderBy(s => s.Order))
          .FirstOrDefaultAsync(w => w.Id == id, ct);

    public async Task<Result<int>> SaveAsync(
        WorkflowDefinition definition, CancellationToken ct = default)
    {
        var problems = Validate(definition);
        if (problems.Count > 0)
            return Result.Fail<int>(string.Join(" ", problems), "workflow.invalid");

        var existing = await db.Workflows
            .Include(w => w.Steps)
            .FirstOrDefaultAsync(w => w.Id == definition.Id, ct);

        if (existing is null)
        {
            db.Workflows.Add(definition);
            await db.SaveChangesAsync(ct);
            return Result.Success(definition.Id);
        }

        var hasOpenRequests = await db.ApprovalRequests.AnyAsync(
            r => r.DocumentType == existing.DocumentType && r.Status == ApprovalStatus.Pending, ct);

        if (hasOpenRequests)
        {
            // Requests already in flight snapshotted their steps at submission,
            // so they are unaffected either way. Bumping the revision keeps the
            // history honest about which rules produced which decision.
            existing.Revision++;
        }

        existing.Name = definition.Name;
        existing.Description = definition.Description;
        existing.BlockSelfApproval = definition.BlockSelfApproval;
        existing.RequireDistinctApprovers = definition.RequireDistinctApprovers;
        existing.IsActive = definition.IsActive;

        db.WorkflowSteps.RemoveRange(existing.Steps);
        existing.Steps = definition.Steps;

        await db.SaveChangesAsync(ct);
        return Result.Success(existing.Id);
    }

    public IReadOnlyList<string> Validate(WorkflowDefinition definition)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(definition.Name))
            problems.Add("Give the workflow a name.");

        if (definition.Steps.Count == 0)
        {
            problems.Add(
                "A workflow needs at least one step. One with none would approve " +
                "everything the moment it was submitted.");
            return problems;
        }

        foreach (var step in definition.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Name))
                problems.Add($"Step {step.Order} needs a name.");

            // A rule that points at a role or a person needs to say which.
            var needsValue = step.Rule is ApproverRule.Role
                or ApproverRule.Permission or ApproverRule.User;

            if (needsValue && string.IsNullOrWhiteSpace(step.RuleValue))
                problems.Add($"'{step.Name}' does not say who approves it.");

            if (step.MinAmount is not null && step.MaxAmount is not null
                && step.MinAmount >= step.MaxAmount)
            {
                problems.Add(
                    $"'{step.Name}' has an amount band that can never match " +
                    $"({step.MinAmount:N0} to {step.MaxAmount:N0}).");
            }
        }

        // The gap check. A band set of "up to 50,000" and "500,000 and above"
        // looks complete at a glance and silently refuses everything between.
        var banded = definition.Steps
            .Where(s => s.MinAmount is not null || s.MaxAmount is not null)
            .ToList();

        if (banded.Count > 0 && banded.Count == definition.Steps.Count)
        {
            var lowest = banded.Min(s => s.MinAmount ?? 0);
            if (lowest > 0)
            {
                problems.Add(
                    $"Nothing approves an amount below {lowest:N0}. Add a step with no lower " +
                    "bound, or documents under that value cannot be submitted at all.");
            }
        }

        if (definition.Steps.Select(s => s.Order).Distinct().Count() != definition.Steps.Count)
            problems.Add("Two steps share the same position.");

        return problems;
    }
}
