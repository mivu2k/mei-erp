using MeiErp.Platform.Kernel;

namespace MeiErp.Platform.Workflow;

/// <summary>
/// A live approval, running against one document.
///
/// The document itself is referenced by module key, type and id rather than a
/// foreign key: the engine routes a purchase order without Inventory and the
/// engine knowing anything about each other. Modules keep their own status
/// enum; the engine drives it through an adapter when the request settles.
/// </summary>
public class ApprovalRequest : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    /// <summary>e.g. "inventory".</summary>
    public string ModuleKey { get; set; } = "";

    /// <summary>e.g. "inventory.purchase-order".</summary>
    public string DocumentType { get; set; } = "";

    /// <summary>The document's own primary key, inside its module.</summary>
    public int DocumentId { get; set; }

    /// <summary>Human reference, e.g. "PO-26-0142". Shown in the inbox so the row means something.</summary>
    public string DocumentReference { get; set; } = "";

    /// <summary>One line describing what is being approved, written by the raising module.</summary>
    public string Summary { get; set; } = "";

    /// <summary>Deep link back into the document, e.g. "/inventory/purchase-orders/142".</summary>
    public string DocumentUrl { get; set; } = "";

    /// <summary>What the approval is worth, if the flow routes on amount.</summary>
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }

    public string RequestedByUserId { get; set; } = "";
    public string RequestedByName { get; set; } = "";
    public DateTime RequestedUtc { get; set; }

    /// <summary>The requester's department, captured at submission for department-head routing.</summary>
    public string? DepartmentId { get; set; }
    public int? ProjectId { get; set; }

    /// <summary>Which definition, and which revision of it, this request is running.</summary>
    public int WorkflowDefinitionId { get; set; }
    public int DefinitionRevision { get; set; }

    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

    /// <summary>Order of the step currently awaiting a decision. Null once settled.</summary>
    public int? CurrentStepOrder { get; set; }

    public DateTime? CompletedUtc { get; set; }

    /// <summary>When the current step becomes late. Recomputed on every step change.</summary>
    public DateTime? DueUtc { get; set; }

    public List<ApprovalStepState> StepStates { get; set; } = [];

    /// <summary>Append-only. A decision is never edited or removed, only followed by another.</summary>
    public List<ApprovalAction> Actions { get; set; } = [];

    public bool IsOpen => Status is ApprovalStatus.Pending or ApprovalStatus.Returned;
}

/// <summary>
/// The frozen shape of one step for this particular request. Copied from the
/// definition at submission so that editing the workflow later cannot change
/// how a request already in flight behaves.
/// </summary>
public class ApprovalStepState : Entity
{
    public int ApprovalRequestId { get; set; }
    public ApprovalRequest? Request { get; set; }

    public int Order { get; set; }
    public string Name { get; set; } = "";
    public ApproverRule Rule { get; set; }
    public string? RuleValue { get; set; }
    public StepQuorum Quorum { get; set; }
    public bool AllowReturn { get; set; }
    public int? ReminderAfterHours { get; set; }
    public int? EscalateAfterHours { get; set; }
    public string? EscalateToRole { get; set; }

    public StepOutcome Outcome { get; set; } = StepOutcome.Waiting;

    public DateTime? StartedUtc { get; set; }
    public DateTime? SettledUtc { get; set; }

    /// <summary>Set when a reminder has gone out, so it goes out once rather than every sweep.</summary>
    public DateTime? RemindedUtc { get; set; }
    public DateTime? EscalatedUtc { get; set; }

    /// <summary>
    /// For an <see cref="StepQuorum.All"/> step, how many distinct approvals are
    /// still outstanding. Resolved when the step opens, because the set of
    /// eligible approvers can change while a request is in flight.
    /// </summary>
    public int RequiredApprovals { get; set; } = 1;
    public int ReceivedApprovals { get; set; }
}

/// <summary>
/// One decision, permanently. Append-only: correcting a mistake means recording
/// another action, never editing this row. The audit trail is the only defence
/// when an approval is disputed a year later.
/// </summary>
public class ApprovalAction : Entity
{
    public int ApprovalRequestId { get; set; }
    public ApprovalRequest? Request { get; set; }

    public int StepOrder { get; set; }
    public string StepName { get; set; } = "";

    public ApprovalDecision Decision { get; set; }

    public string ActedByUserId { get; set; } = "";
    public string ActedByName { get; set; } = "";

    /// <summary>
    /// Set when this person acted as someone else's delegate, naming who they
    /// stood in for. A delegated approval that looks identical to a direct one
    /// is how accountability gets lost.
    /// </summary>
    public string? OnBehalfOfUserId { get; set; }
    public string? OnBehalfOfName { get; set; }

    public DateTime ActedUtc { get; set; }

    /// <summary>Required on rejection and return; optional on approval.</summary>
    public string? Comment { get; set; }

    /// <summary>Captured for the audit trail on financially consequential approvals.</summary>
    public string? IpAddress { get; set; }
}

public enum ApprovalStatus
{
    /// <summary>Awaiting a decision at <c>CurrentStepOrder</c>.</summary>
    Pending = 0,

    Approved = 1,

    /// <summary>Rejected outright. Terminal - the document is dead.</summary>
    Rejected = 2,

    /// <summary>
    /// Sent back to the raiser to correct and resubmit. Not terminal, and not
    /// the same as rejection: the document lives and keeps its history.
    /// </summary>
    Returned = 3,

    /// <summary>Withdrawn by the raiser before a decision.</summary>
    Cancelled = 4
}

public enum StepOutcome
{
    /// <summary>Not reached yet.</summary>
    Waiting = 0,
    Open = 1,
    Approved = 2,
    Rejected = 3,
    Returned = 4,

    /// <summary>Bypassed because its amount band did not apply, or the request settled first.</summary>
    Skipped = 5
}

public enum ApprovalDecision
{
    Approved = 0,
    Rejected = 1,
    Returned = 2,
    Cancelled = 3,

    /// <summary>Not a decision - a reassignment or escalation, recorded so the trail stays complete.</summary>
    Escalated = 4,
    Reassigned = 5
}

/// <summary>
/// Standing authority for one person to approve in another's place.
///
/// Fed automatically from approved HR leave, which is only possible because
/// both live on one platform. A manager on leave whose approvals silently stop
/// is the single most common way a workflow engine gets abandoned.
/// </summary>
public class ApprovalDelegation : AuditableEntity
{
    public string FromUserId { get; set; } = "";
    public string FromName { get; set; } = "";
    public string ToUserId { get; set; } = "";
    public string ToName { get; set; } = "";

    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }

    /// <summary>Null delegates everything; set to scope it to one document type.</summary>
    public string? DocumentType { get; set; }

    /// <summary>Cap on what the delegate may approve. Null means the delegator's full authority.</summary>
    public decimal? MaxAmount { get; set; }

    public string? Reason { get; set; }

    /// <summary>True when HR leave created this, so the sweep can retire it without touching manual ones.</summary>
    public bool FromLeave { get; set; }

    public bool IsActiveOn(DateOnly date) => date >= FromDate && date <= ToDate;
}
