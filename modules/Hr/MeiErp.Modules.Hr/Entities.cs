using MeiErp.Platform.Kernel;

namespace MeiErp.Modules.Hr;

/// <summary>
/// A member of staff.
///
/// Deliberately separate from the platform's <c>ApplicationUser</c>: plenty of
/// staff never sign in, and a login is not proof of employment. The two are
/// linked by <see cref="UserId"/> when the person does have an account.
/// </summary>
public class Employee : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    public string Code { get; set; } = "";
    public string FullName { get; set; } = "";

    /// <summary>Identity user id when this person can sign in. Null for staff who cannot.</summary>
    public string? UserId { get; set; }

    public string? Designation { get; set; }

    /// <summary>Platform department id - a plain string, not a foreign key across modules.</summary>
    public string? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }

    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Cnic { get; set; }

    public DateOnly JoinedOn { get; set; }
    public DateOnly? LeftOn { get; set; }

    public EmploymentStatus Status { get; set; } = EmploymentStatus.Active;

    public decimal? BasicSalary { get; set; }

    public List<LeaveBalance> LeaveBalances { get; set; } = [];

    /// <summary>Employed as at a date. Used by leave and attendance rather than reading Status.</summary>
    public bool IsEmployedOn(DateOnly date) =>
        JoinedOn <= date && (LeftOn is null || LeftOn >= date);
}

public enum EmploymentStatus
{
    Active = 0,
    OnLeave = 1,
    Suspended = 2,
    Resigned = 3,
    Terminated = 4,
    Retired = 5
}

/// <summary>
/// A kind of leave, with its yearly entitlement.
/// </summary>
public class LeaveType : AuditableEntity
{
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";

    /// <summary>Days granted per year. Zero means unlimited, e.g. unpaid leave.</summary>
    public decimal AnnualEntitlement { get; set; }

    /// <summary>Whether the employee is paid for these days.</summary>
    public bool IsPaid { get; set; } = true;

    /// <summary>Unused days that survive into next year, capped. Zero forfeits the lot.</summary>
    public decimal MaxCarryForward { get; set; }

    /// <summary>
    /// Whether taking this needs approval at all. Bereavement is usually taken
    /// first and recorded afterwards.
    /// </summary>
    public bool RequiresApproval { get; set; } = true;

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// What one employee has left of one leave type, this year.
/// </summary>
public class LeaveBalance : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public int LeaveTypeId { get; set; }
    public LeaveType? LeaveType { get; set; }

    public int Year { get; set; }

    public decimal Entitled { get; set; }
    public decimal CarriedForward { get; set; }

    /// <summary>Days actually taken - only counted once leave is approved.</summary>
    public decimal Taken { get; set; }

    /// <summary>
    /// Days on requests that are submitted but not yet decided.
    ///
    /// Held rather than ignored, so two pending requests cannot both spend the
    /// same entitlement before either is approved. This is the whole reason
    /// Pending is tracked separately from Taken.
    /// </summary>
    public decimal Pending { get; set; }

    public decimal Available => Entitled + CarriedForward - Taken - Pending;
}

/// <summary>
/// A request to be away.
///
/// Its status is driven by the platform approval engine through
/// <c>LeaveApprovalSink</c>; nothing here decides who approves it.
/// </summary>
public class LeaveRequest : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    public string Reference { get; set; } = "";

    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    /// <summary>Snapshotted so the list reads correctly without a join.</summary>
    public string EmployeeName { get; set; } = "";

    public int LeaveTypeId { get; set; }
    public LeaveType? LeaveType { get; set; }
    public string LeaveTypeName { get; set; } = "";

    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }

    /// <summary>
    /// Working days, worked out when the request is raised and then fixed.
    /// Recomputing it later would let a change to the holiday calendar silently
    /// alter a leave request somebody already approved.
    /// </summary>
    public decimal Days { get; set; }

    public string? Reason { get; set; }

    /// <summary>Who covers the work. Not enforced - it is information for the approver.</summary>
    public string? CoveredByName { get; set; }

    public LeaveStatus Status { get; set; } = LeaveStatus.Draft;

    /// <summary>The engine's request id, once submitted.</summary>
    public int? ApprovalRequestId { get; set; }

    public string RequestedByUserId { get; set; } = "";
    public DateTime? SubmittedUtc { get; set; }
    public DateTime? DecidedUtc { get; set; }

    /// <summary>Why it was rejected or returned, copied off the approval for display.</summary>
    public string? DecisionComment { get; set; }

    public bool IsOpen => Status is LeaveStatus.Draft or LeaveStatus.Pending or LeaveStatus.Returned;

    /// <summary>Overlaps another request's dates - two people cannot be away as one.</summary>
    public bool OverlapsWith(DateOnly from, DateOnly to) =>
        FromDate <= to && ToDate >= from;
}

/// <summary>
/// Kept as this module's own status even though the engine drives it.
///
/// That separation is what lets each flow migrate to the engine independently
/// and roll back on its own, instead of nine of them moving in one cutover.
/// </summary>
public enum LeaveStatus
{
    Draft = 0,
    Pending = 1,
    Approved = 2,
    Rejected = 3,

    /// <summary>Sent back to be corrected. Still alive, unlike Rejected.</summary>
    Returned = 4,

    Cancelled = 5
}

/// <summary>A day nobody works, so it is not counted against leave.</summary>
public class Holiday : AuditableEntity
{
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Recurs on the same calendar date each year, e.g. Independence Day.</summary>
    public bool IsAnnual { get; set; }
}
