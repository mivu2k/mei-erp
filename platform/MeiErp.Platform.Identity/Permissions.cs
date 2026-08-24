using System.Security.Claims;
using MeiErp.Platform.Kernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace MeiErp.Platform.Identity;

/// <summary>Permissions the platform itself defines, before any module loads.</summary>
public static class PlatformPermissions
{
    public const string UsersManage = "platform.users.manage";
    public const string RolesManage = "platform.roles.manage";
    public const string CompanyManage = "platform.company.manage";
    public const string DepartmentsManage = "platform.departments.manage";
    public const string WorkflowsManage = "platform.workflows.manage";
    public const string AuditView = "platform.audit.view";
    public const string OutboxManage = "platform.outbox.manage";
    public const string LabelsManage = "platform.labels.manage";

    /// <summary>Every user has this implicitly; it is what puts the approvals inbox in the nav.</summary>
    public const string ApprovalsAct = "platform.approvals.act";

    public static readonly IReadOnlyList<PermissionDescriptor> All =
    [
        new(UsersManage,       "Users",     "Create, edit, deactivate users and reset their passwords"),
        new(RolesManage,       "Roles",     "Create roles and change what each one can do"),
        new(DepartmentsManage, "Structure", "Manage departments and who heads them"),
        new(CompanyManage,     "Company",   "Edit the company profile printed on every document"),
        new(WorkflowsManage,   "Approvals", "Design approval workflows and their amount bands"),
        new(ApprovalsAct,      "Approvals", "See and act on the approvals inbox"),
        new(AuditView,         "Audit",     "Read the platform audit trail"),
        new(OutboxManage,      "System",    "Review and retry failed integration events"),
        new(LabelsManage,      "Printing",  "Configure label sizes and printed fields")
    ];

    /// <summary>Holds every permission, including ones modules add later.</summary>
    public const string SuperAdminRole = "Super Admin";
}

/// <summary>
/// The claim type a permission is stored under, on the role.
/// </summary>
public static class PermissionClaim
{
    public const string Type = "permission";

    /// <summary>Module access is stamped onto the principal at sign-in so the nav never queries the database.</summary>
    public const string ModuleType = "module";
}

/// <summary>
/// Builds an authorization policy for any permission string on demand.
///
/// Permissions are data, not code: they live as role claims and come from the
/// module catalog, so adding one never means adding a policy registration.
/// Also understands "module:{key}" for gating a whole app.
/// </summary>
public sealed class PermissionPolicyProvider(
    Microsoft.Extensions.Options.IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public const string ModulePolicyPrefix = "module:";

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var existing = await base.GetPolicyAsync(policyName);
        if (existing is not null) return existing;

        if (policyName.StartsWith(ModulePolicyPrefix, StringComparison.Ordinal))
        {
            var moduleKey = policyName[ModulePolicyPrefix.Length..];
            return new AuthorizationPolicyBuilder()
                .AddRequirements(new ModuleAccessRequirement(moduleKey))
                .Build();
        }

        // Anything namespaced like a permission becomes a permission policy.
        if (policyName.Contains('.', StringComparison.Ordinal))
        {
            return new AuthorizationPolicyBuilder()
                .AddRequirements(new PermissionRequirement(policyName))
                .Build();
        }

        return null;
    }
}

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed record ModuleAccessRequirement(string ModuleKey) : IAuthorizationRequirement;

public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        // Super Admin holds everything, including permissions from modules that
        // did not exist when the role was created.
        if (context.User.IsInRole(PlatformPermissions.SuperAdminRole) ||
            context.User.HasClaim(PermissionClaim.Type, requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public sealed class ModuleAccessHandler : AuthorizationHandler<ModuleAccessRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ModuleAccessRequirement requirement)
    {
        if (context.User.IsInRole(PlatformPermissions.SuperAdminRole) ||
            context.User.HasClaim(PermissionClaim.ModuleType, requirement.ModuleKey))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Stamps permissions and module access onto the principal at sign-in.
///
/// The nav, the portal and every page check claims rather than hitting the
/// database on each render. The cost is that a change takes effect at next
/// sign-in - which is why <c>SecurityStampValidationInterval</c> is set short
/// in the host, so a revoked permission does not stay live for a whole day.
/// </summary>
public sealed class PlatformClaimsPrincipalFactory(
    UserManager<ApplicationUser> users,
    RoleManager<ApplicationRole> roles,
    Microsoft.Extensions.Options.IOptions<IdentityOptions> options,
    IModuleAccessService access)
    : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>(users, roles, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        identity.AddClaim(new Claim("full_name", user.FullName));

        if (!string.IsNullOrEmpty(user.DepartmentId))
            identity.AddClaim(new Claim("department", user.DepartmentId));

        foreach (var moduleKey in await access.ModulesForAsync(user.Id))
            identity.AddClaim(new Claim(PermissionClaim.ModuleType, moduleKey));

        return identity;
    }
}

/// <summary>
/// Works out which modules a user may enter, combining their roles' module
/// scopes with their per-user overrides.
/// </summary>
public interface IModuleAccessService
{
    /// <summary>Module keys this user may enter. Deny overrides always win.</summary>
    Task<IReadOnlyList<string>> ModulesForAsync(string userId, CancellationToken ct = default);

    Task SetAsync(string userId, string moduleKey, bool granted, string? reason, CancellationToken ct = default);

    Task ClearAsync(string userId, string moduleKey, CancellationToken ct = default);
}
