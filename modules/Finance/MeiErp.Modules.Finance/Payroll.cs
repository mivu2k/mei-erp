using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Finance;

/// <summary>
/// The accounts-side mirror of a person on the payroll.
///
/// Separate from HR's employee record on purpose: payroll needs a ledger head
/// and a salary, and it must keep working whether or not the HR module is
/// installed. Linked by staff number and by login where those exist.
/// </summary>
public class PayrollEmployee : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    public string Code { get; set; } = "";
    public string FullName { get; set; } = "";

    public string? UserId { get; set; }
    public string? DepartmentId { get; set; }
    public string? Designation { get; set; }

    public DateOnly JoinedOn { get; set; }
    public DateOnly? LeftOn { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Where this person's cost is charged. Null uses the default salary head.</summary>
    public int? SalaryAccountId { get; set; }

    public List<SalaryStructure> Structures { get; set; } = [];

    public bool IsEmployedOn(DateOnly date) =>
        JoinedOn <= date && (LeftOn is null || LeftOn >= date);
}

/// <summary>
/// A named part of pay — basic, an allowance, or a deduction.
///
/// Kept as data rather than as columns so adding "fuel allowance" is a row and
/// not a migration.
/// </summary>
public class PayComponent : AuditableEntity
{
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";

    public PayComponentKind Kind { get; set; }

    /// <summary>Where this component's cost or liability is posted.</summary>
    public int? AccountId { get; set; }
    public Account? Account { get; set; }

    /// <summary>
    /// Whether an allowance is reduced when somebody is absent. Basic always
    /// is; a fixed phone allowance usually is not.
    /// </summary>
    public bool ProRateOnAttendance { get; set; } = true;

    public bool IsActive { get; set; } = true;
}

public enum PayComponentKind
{
    /// <summary>Adds to pay and to cost.</summary>
    Earning = 0,

    /// <summary>Taken off pay and owed to somebody else.</summary>
    Deduction = 1
}

/// <summary>
/// What somebody is paid, effective from a date.
///
/// Saving a new structure supersedes the old one rather than editing it, so a
/// payslip issued last month still explains itself.
/// </summary>
public class SalaryStructure : AuditableEntity
{
    public int EmployeeId { get; set; }
    public PayrollEmployee? Employee { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Set when a later structure superseded this one.</summary>
    public DateOnly? EffectiveTo { get; set; }

    public decimal BasicSalary { get; set; }

    public List<SalaryLine> Lines { get; set; } = [];

    public bool IsCurrentOn(DateOnly date) =>
        EffectiveFrom <= date && (EffectiveTo is null || EffectiveTo >= date);
}

public class SalaryLine : Entity
{
    public int SalaryStructureId { get; set; }
    public SalaryStructure? Structure { get; set; }

    public int ComponentId { get; set; }
    public PayComponent? Component { get; set; }

    public decimal Amount { get; set; }
}

/// <summary>One month's payroll for everybody in it.</summary>
public class PayrollRun : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    public string Reference { get; set; } = "";

    /// <summary>The month being paid, always the first of it.</summary>
    public DateOnly Month { get; set; }

    public PayrollRunStatus Status { get; set; } = PayrollRunStatus.Draft;

    public List<Payslip> Payslips { get; set; } = [];

    /// <summary>The single aggregated voucher this run posted.</summary>
    public int? VoucherId { get; set; }

    public DateTime? ApprovedUtc { get; set; }
    public DateTime? PaidUtc { get; set; }

    public decimal TotalGross => Payslips.Sum(p => p.Gross);
    public decimal TotalDeductions => Payslips.Sum(p => p.TotalDeductions);
    public decimal TotalNet => Payslips.Sum(p => p.Net);

    public bool IsEditable => Status is PayrollRunStatus.Draft;
}

public enum PayrollRunStatus
{
    Draft = 0,
    Approved = 1,
    Paid = 2,
    Cancelled = 3
}

/// <summary>
/// One person's pay for one month.
///
/// Everything is snapshotted onto the payslip — names, amounts, the lot — so an
/// approved run cannot shift under a later edit to the pay component catalog.
/// </summary>
public class Payslip : AuditableEntity
{
    public int RunId { get; set; }
    public PayrollRun? Run { get; set; }

    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public string? UserId { get; set; }
    public string? DepartmentId { get; set; }

    public decimal BasicSalary { get; set; }

    /// <summary>Days actually worked, and days in the month, for pro-rating.</summary>
    public decimal DaysWorked { get; set; }
    public decimal DaysInMonth { get; set; }

    /// <summary>Which head this person's cost was charged to.</summary>
    public int? SalaryAccountId { get; set; }

    public List<PayslipLine> Lines { get; set; } = [];

    public decimal Gross => Lines.Where(l => l.Kind == PayComponentKind.Earning).Sum(l => l.Amount);
    public decimal TotalDeductions => Lines.Where(l => l.Kind == PayComponentKind.Deduction).Sum(l => l.Amount);
    public decimal Net => Gross - TotalDeductions;
}

public class PayslipLine : Entity
{
    public int PayslipId { get; set; }
    public Payslip? Payslip { get; set; }

    public int? ComponentId { get; set; }

    /// <summary>Snapshotted, so renaming a component later cannot rewrite an old payslip.</summary>
    public string Name { get; set; } = "";

    public PayComponentKind Kind { get; set; }
    public decimal Amount { get; set; }

    public int? AccountId { get; set; }

    /// <summary>Set when this line is recovering an advance, so payroll can mark it repaid.</summary>
    public int? AdvanceId { get; set; }
}
