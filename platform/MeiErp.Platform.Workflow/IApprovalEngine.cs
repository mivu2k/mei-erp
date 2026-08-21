using MeiErp.Platform.Kernel;

namespace MeiErp.Platform.Workflow;

/// <summary>
/// The one way a document gets approved, anywhere in the suite.
///
/// A module submits its document and stops thinking about approval. It learns
/// the outcome through <see cref="IApprovalSink"/>, so the module never queries
/// the engine's tables and the engine never references the module.
/// </summary>
public interface IApprovalEngine
{
    /// <summary>
    /// Put a document into approval. Returns the request, or a failure when no
    /// active workflow covers this document type.
    /// </summary>
    Task<Result<ApprovalRequest>> SubmitAsync(SubmitApproval request, CancellationToken ct = default);

    /// <summary>Record a decision at the request's current step.</summary>
    Task<Result<ApprovalRequest>> DecideAsync(
        int requestId, ApprovalDecision decision, string? comment, CancellationToken ct = default);

    /// <summary>
    /// Withdraw a request. Only the raiser, and only while it is still open -
    /// cancelling something already approved would strand whatever the approval
    /// authorised.
    /// </summary>
    Task<Result> CancelAsync(int requestId, string? reason, CancellationToken ct = default);

    /// <summary>
    /// Resubmit a returned request after the raiser has corrected it. Routing
    /// restarts from the first step, because a corrected document is a
    /// different document and earlier approvers signed off on the old one.
    /// </summary>
    Task<Result<ApprovalRequest>> ResubmitAsync(int requestId, CancellationToken ct = default);

    /// <summary>
    /// Everything awaiting the current user, across every module. This is the
    /// single inbox - a manager checks one queue, not eight.
    /// </summary>
    Task<IReadOnlyList<ApprovalInboxItem>> InboxAsync(CancellationToken ct = default);

    /// <summary>Whether the current user may decide this request right now, and why not if they may not.</summary>
    Task<Result> CanDecideAsync(int requestId, CancellationToken ct = default);

    /// <summary>The full history of one document's approval, for its detail page.</summary>
    Task<ApprovalHistory?> HistoryAsync(
        string documentType, int documentId, CancellationToken ct = default);
}

/// <param name="ModuleKey">Owning module, e.g. "inventory".</param>
/// <param name="DocumentType">e.g. "inventory.purchase-order".</param>
/// <param name="Amount">Drives amount-band routing. Null for flows with no value attached.</param>
public sealed record SubmitApproval(
    string ModuleKey,
    string DocumentType,
    int DocumentId,
    string DocumentReference,
    string Summary,
    string DocumentUrl,
    decimal? Amount = null,
    string? Currency = null,
    string? DepartmentId = null,
    int? ProjectId = null);

/// <summary>One row in the unified approvals inbox.</summary>
public sealed record ApprovalInboxItem(
    int RequestId,
    string ModuleKey,
    string ModuleName,
    string DocumentType,
    string DocumentReference,
    string Summary,
    string DocumentUrl,
    decimal? Amount,
    string? Currency,
    string RequestedByName,
    DateTime RequestedUtc,
    string StepName,
    int StepOrder,
    DateTime? DueUtc,
    bool IsOverdue,
    /// <summary>Set when the user sees this because they are standing in for someone.</summary>
    string? OnBehalfOfName);

/// <summary>A document's approval story, for display on its own page.</summary>
public sealed record ApprovalHistory(
    int RequestId,
    ApprovalStatus Status,
    string? CurrentStepName,
    IReadOnlyList<ApprovalHistoryStep> Steps,
    IReadOnlyList<ApprovalHistoryAction> Actions);

public sealed record ApprovalHistoryStep(
    int Order, string Name, StepOutcome Outcome,
    int RequiredApprovals, int ReceivedApprovals,
    DateTime? StartedUtc, DateTime? SettledUtc);

public sealed record ApprovalHistoryAction(
    int StepOrder, string StepName, ApprovalDecision Decision,
    string ActedByName, string? OnBehalfOfName,
    DateTime ActedUtc, string? Comment);

/// <summary>
/// How a module hears that its document was approved, rejected or returned.
///
/// Each module implements one of these per document type and updates its own
/// status enum. That is what lets the existing flows migrate one at a time and
/// roll back independently, rather than in a single cutover of nine live
/// workflows - which is the fastest way to break production.
/// </summary>
public interface IApprovalSink
{
    /// <summary>Which document type this sink handles, e.g. "inventory.purchase-order".</summary>
    string DocumentType { get; }

    /// <summary>
    /// Apply a settled approval to the document. Runs in the same transaction
    /// as the decision, so the document's status and the approval history can
    /// never disagree.
    /// </summary>
    Task<Result> OnSettledAsync(
        int documentId, ApprovalStatus status, ApprovalRequest request, CancellationToken ct = default);
}

/// <summary>
/// Resolves who is allowed to approve a given step. Separated from the engine
/// because it is the piece that has to reach into the user directory, and the
/// piece most worth testing on its own.
/// </summary>
public interface IApproverResolver
{
    /// <summary>
    /// Every user id eligible to decide this step, delegations already applied.
    /// Empty means nobody can - the caller must treat that as a routing failure
    /// and escalate, never as an automatic approval.
    /// </summary>
    Task<IReadOnlyList<EligibleApprover>> ResolveAsync(
        ApprovalStepState step, ApprovalRequest request, CancellationToken ct = default);
}

/// <param name="OnBehalfOfUserId">Set when this person is eligible only as a delegate.</param>
public sealed record EligibleApprover(
    string UserId,
    string Name,
    string? Email,
    string? OnBehalfOfUserId = null,
    string? OnBehalfOfName = null);
