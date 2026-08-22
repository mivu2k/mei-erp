using Microsoft.AspNetCore.Identity;

namespace MeiErp.Platform.Identity;

/// <summary>
/// A person who can sign in.
///
/// The extra fields beyond ASP.NET Identity's own exist mostly to feed the
/// approval engine: it cannot route "to the requester's line manager" or "to
/// the department head" unless somebody records who those are. The previous
/// platform had no reporting line at all, which is why every approval chain in
/// it was hardcoded.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = "";

    /// <summary>Payroll or staff number, for matching against an HR record.</summary>
    public string? EmployeeCode { get; set; }

    public string? Designation { get; set; }

    public string? DepartmentId { get; set; }
    public Department? Department { get; set; }

    /// <summary>
    /// Who this person reports to. Read directly by
    /// <c>ApproverRule.LineManager</c>, which is the single rule that removes
    /// most hardcoded approval chains.
    /// </summary>
    public string? LineManagerId { get; set; }
    public ApplicationUser? LineManager { get; set; }

    /// <summary>
    /// Deactivated rather than deleted. A leaver's approvals, audit rows and
    /// document history all have to keep resolving to a name.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedUtc { get; set; }
    public DateTime? LastLoginUtc { get; set; }

    /// <summary>Forces a password change at next sign-in - set on invitation and on admin reset.</summary>
    public bool MustChangePassword { get; set; }

    public byte[]? Photo { get; set; }

    /// <summary>Per-user overrides on top of role-granted module access.</summary>
    public List<UserModuleAccess> ModuleAccess { get; set; } = [];
}

/// <summary>
/// A role, scoped to one app.
///
/// Holding it both admits the user to that module and decides what they can do
/// inside it. A null <see cref="ModuleKey"/> means platform-wide (Super Admin).
/// </summary>
public class ApplicationRole : IdentityRole
{
    /// <summary>Which module this role belongs to. Null for a platform-wide role.</summary>
    public string? ModuleKey { get; set; }

    public string? Description { get; set; }

    /// <summary>
    /// Shipped with the module rather than created by an admin. Protected from
    /// deletion, because removing one would silently strip access from everyone
    /// holding it.
    /// </summary>
    public bool IsSystemRole { get; set; }
}

/// <summary>
/// A per-user grant or deny sitting on top of what their roles give them.
///
/// <b>Deny wins.</b> An explicit revocation must never be overridable by adding
/// another role - that is the whole point of having an override at all.
/// </summary>
public class UserModuleAccess
{
    public int Id { get; set; }

    public string UserId { get; set; } = "";
    public ApplicationUser? User { get; set; }

    public string ModuleKey { get; set; } = "";

    /// <summary>False denies, and beats any role that would have granted it.</summary>
    public bool Granted { get; set; }

    public string? Reason { get; set; }
    public DateTime SetUtc { get; set; }
    public string? SetBy { get; set; }
}

/// <summary>
/// An organisational unit. Owned here rather than in HR because the approval
/// engine, the permission model and the report filters all need it, and HR is
/// a module that may not be installed.
/// </summary>
public class Department
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string? Code { get; set; }

    /// <summary>
    /// The person <c>ApproverRule.DepartmentHead</c> routes to. Nullable, and
    /// the engine treats a missing head as a routing failure to escalate - never
    /// as an automatic approval.
    /// </summary>
    public string? HeadUserId { get; set; }
    public ApplicationUser? Head { get; set; }

    /// <summary>Departments nest; a section rolls up to a division.</summary>
    public string? ParentId { get; set; }
    public Department? Parent { get; set; }
    public List<Department> Children { get; set; } = [];

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// The company on every document in every module. One row.
/// </summary>
public class CompanyProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? LegalName { get; set; }
    public byte[]? Logo { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }

    /// <summary>National tax number, printed on invoices.</summary>
    public string? TaxNumber { get; set; }
    public string? SalesTaxNumber { get; set; }

    /// <summary>Printed at the foot of every document.</summary>
    public string? FooterNote { get; set; }

    public string Currency { get; set; } = "PKR";
    public string CurrencySymbol { get; set; } = "Rs";

    /// <summary>
    /// A detached copy, for an edit screen to work on.
    ///
    /// <see cref="ICompanyProfileService.GetAsync"/> hands back the process-wide
    /// cached instance, so a form bound straight to it would rewrite the company
    /// on every keystroke for every user in the building - and leave the edit in
    /// place even if the save is abandoned.
    /// </summary>
    public CompanyProfile Clone() => (CompanyProfile)MemberwiseClone();
}
