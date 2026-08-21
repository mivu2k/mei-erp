using MeiErp.Platform.Kernel;

namespace MeiErp.Platform.Workflow;

/// <summary>
/// An approval workflow for one document type, e.g. "Purchase Order approval".
///
/// The previous platform hand-coded nine of these - leave, payment requests,
/// advances, payroll runs, quotations, purchase orders, sales orders, stock
/// transfers and gate passes - each with its own status enum and its own
/// transition code. Adding a second approval level anywhere meant editing that
/// module. This is that engine, once, driven from an admin screen.
/// </summary>
public class WorkflowDefinition : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    /// <summary>Which document type this routes, e.g. "inventory.purchase-order".</summary>
    public string DocumentType { get; set; } = "";

    public string Name { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>
    /// Definitions are versioned and never edited in place. A request already in
    /// flight keeps running the revision it started on, so changing the rules
    /// today cannot silently re-route something submitted last week - or worse,
    /// leave it pointing at a step that no longer exists.
    /// </summary>
    public int Revision { get; set; } = 1;

    /// <summary>Only one revision per document type is live at a time.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When true, the person who raised the document can never approve it, at
    /// any step. Segregation of duties was convention before; here it is data,
    /// and it defaults to on.
    /// </summary>
    public bool BlockSelfApproval { get; set; } = true;

    /// <summary>
    /// When true, one person cannot satisfy two different steps of the same
    /// request - a second signature has to be a second pair of eyes.
    /// </summary>
    public bool RequireDistinctApprovers { get; set; }

    public List<WorkflowStep> Steps { get; set; } = [];

    /// <summary>
    /// The steps that apply to a document, given its amount. Steps whose amount
    /// band excludes this value drop out entirely rather than auto-approving,
    /// which is what makes "under 50,000 needs one signature, above it needs
    /// three" a configuration rather than a branch in module code.
    /// </summary>
    public IEnumerable<WorkflowStep> StepsFor(decimal? amount) =>
        Steps.Where(s => s.AppliesTo(amount)).OrderBy(s => s.Order);
}

/// <summary>One level of approval within a definition.</summary>
public class WorkflowStep : Entity
{
    public int WorkflowDefinitionId { get; set; }
    public WorkflowDefinition? Definition { get; set; }

    public int Order { get; set; }

    /// <summary>Shown in the inbox and on the document's history, e.g. "Department head".</summary>
    public string Name { get; set; } = "";

    public ApproverRule Rule { get; set; } = ApproverRule.Role;

    /// <summary>
    /// What <see cref="Rule"/> points at: a role name, a permission key, or a
    /// user id. Null for the rules that derive their approver from the
    /// requester, such as <see cref="ApproverRule.LineManager"/>.
    /// </summary>
    public string? RuleValue { get; set; }

    /// <summary>
    /// With several eligible approvers, whether one is enough or all must sign.
    /// A quotation needing both the customer's and the manager's yes is an
    /// <see cref="StepQuorum.All"/> step, not two bespoke boolean fields.
    /// </summary>
    public StepQuorum Quorum { get; set; } = StepQuorum.Any;

    /// <summary>Inclusive lower bound of the amount band. Null means no lower bound.</summary>
    public decimal? MinAmount { get; set; }

    /// <summary>Exclusive upper bound of the amount band. Null means no upper bound.</summary>
    public decimal? MaxAmount { get; set; }

    /// <summary>Hours before this step is considered late and a reminder goes out.</summary>
    public int? ReminderAfterHours { get; set; }

    /// <summary>Hours before the step escalates. Nothing sits silently for a week.</summary>
    public int? EscalateAfterHours { get; set; }

    /// <summary>Who it escalates to - a role name. Null escalates to the next step's approvers.</summary>
    public string? EscalateToRole { get; set; }

    /// <summary>
    /// True when this step's approver may send the document back to the raiser
    /// to fix rather than killing it. Distinct from rejection, and missing from
    /// every one of the nine hand-rolled flows it replaces.
    /// </summary>
    public bool AllowReturn { get; set; } = true;

    /// <summary>Does this step apply to a document of this amount?</summary>
    public bool AppliesTo(decimal? amount)
    {
        if (MinAmount is null && MaxAmount is null) return true;

        // A step with an amount band cannot judge a document that has no amount,
        // so it stays out of the route rather than guessing.
        if (amount is null) return false;

        if (MinAmount is not null && amount < MinAmount) return false;
        if (MaxAmount is not null && amount >= MaxAmount) return false;
        return true;
    }
}

/// <summary>How a step decides who is allowed to approve it.</summary>
public enum ApproverRule
{
    /// <summary>Anyone holding the named role. <c>RuleValue</c> is the role name.</summary>
    Role = 0,

    /// <summary>Anyone holding the named permission. <c>RuleValue</c> is the permission key.</summary>
    Permission = 1,

    /// <summary>One named person. <c>RuleValue</c> is their user id.</summary>
    User = 2,

    /// <summary>
    /// The requester's own line manager, read from the user directory. This one
    /// rule removes most of the hardcoding in the flows being replaced.
    /// </summary>
    LineManager = 3,

    /// <summary>The head of the requester's department.</summary>
    DepartmentHead = 4,

    /// <summary>The manager of the project the document is charged to.</summary>
    ProjectManager = 5,

    /// <summary>Whoever holds the budget the document spends against.</summary>
    BudgetHolder = 6
}

/// <summary>Whether one eligible approver is enough, or every one must sign.</summary>
public enum StepQuorum
{
    Any = 0,
    All = 1
}
