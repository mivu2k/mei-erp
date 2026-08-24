using MeiErp.Platform.Kernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Platform.Identity;

/// <inheritdoc />
public sealed class ModuleAccessService(
    PlatformDbContext db, IClock clock, ICurrentUser currentUser) : IModuleAccessService
{
    public async Task<IReadOnlyList<string>> ModulesForAsync(
        string userId, CancellationToken ct = default)
    {
        // Roles admit a user to their module...
        var fromRoles = await (
            from userRole in db.UserRoles
            join role in db.Roles on userRole.RoleId equals role.Id
            where userRole.UserId == userId && role.ModuleKey != null
            select role.ModuleKey!).Distinct().ToListAsync(ct);

        // ...and per-user overrides sit on top.
        var overrides = await db.ModuleAccess
            .Where(a => a.UserId == userId)
            .Select(a => new { a.ModuleKey, a.Granted })
            .ToListAsync(ct);

        var allowed = new HashSet<string>(fromRoles, StringComparer.OrdinalIgnoreCase);

        foreach (var o in overrides.Where(o => o.Granted))
            allowed.Add(o.ModuleKey);

        // Deny last, and unconditionally. An explicit revocation must not be
        // defeatable by adding another role - that is the point of an override.
        foreach (var o in overrides.Where(o => !o.Granted))
            allowed.Remove(o.ModuleKey);

        return [.. allowed];
    }

    public async Task SetAsync(
        string userId, string moduleKey, bool granted, string? reason, CancellationToken ct = default)
    {
        var existing = await db.ModuleAccess
            .FirstOrDefaultAsync(a => a.UserId == userId && a.ModuleKey == moduleKey, ct);

        if (existing is null)
        {
            existing = new UserModuleAccess { UserId = userId, ModuleKey = moduleKey };
            db.ModuleAccess.Add(existing);
        }

        existing.Granted = granted;
        existing.Reason = reason;
        existing.SetUtc = clock.UtcNow;
        existing.SetBy = currentUser.UserId;

        await db.SaveChangesAsync(ct);
    }

    public async Task ClearAsync(string userId, string moduleKey, CancellationToken ct = default)
    {
        await db.ModuleAccess
            .Where(a => a.UserId == userId && a.ModuleKey == moduleKey)
            .ExecuteDeleteAsync(ct);
    }
}

/// <summary>
/// Looks people up. The approval engine's routing rules run entirely through
/// this, so they never touch Identity's tables directly.
/// </summary>
public interface IUserDirectory
{
    Task<UserSummary?> FindAsync(string userId, CancellationToken ct = default);

    /// <summary>The person a requester reports to. Null when nobody is recorded.</summary>
    Task<UserSummary?> LineManagerOfAsync(string userId, CancellationToken ct = default);

    /// <summary>Who heads a department. Null when nobody is set.</summary>
    Task<UserSummary?> DepartmentHeadAsync(string departmentId, CancellationToken ct = default);

    Task<IReadOnlyList<UserSummary>> InRoleAsync(string roleName, CancellationToken ct = default);

    Task<IReadOnlyList<UserSummary>> WithPermissionAsync(string permission, CancellationToken ct = default);

    Task<IReadOnlyList<UserSummary>> SearchAsync(string? term, int take = 25, CancellationToken ct = default);
}

public sealed record UserSummary(
    string Id, string FullName, string? Email, string? Designation,
    string? DepartmentId, string? DepartmentName, bool IsActive);

/// <inheritdoc />
public sealed class UserDirectory(PlatformDbContext db) : IUserDirectory
{
    /// <summary>
    /// Narrow the users first, then project. Filtering the other way round -
    /// projecting into <see cref="UserSummary"/> and then testing a property of
    /// the record - leaves EF trying to translate a predicate over a constructed
    /// object across the department outer join, which it cannot do: the query
    /// throws at run time while still compiling and passing every test.
    /// </summary>
    private IQueryable<UserSummary> Project(IQueryable<ApplicationUser> users) =>
        from u in users
        join d in db.Departments on u.DepartmentId equals d.Id into dd
        from d in dd.DefaultIfEmpty()
        select new UserSummary(
            u.Id, u.FullName, u.Email, u.Designation, u.DepartmentId, d.Name, u.IsActive);

    public Task<UserSummary?> FindAsync(string userId, CancellationToken ct = default) =>
        Project(db.Users.Where(u => u.Id == userId)).FirstOrDefaultAsync(ct);

    public async Task<UserSummary?> LineManagerOfAsync(string userId, CancellationToken ct = default)
    {
        var managerId = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.LineManagerId)
            .FirstOrDefaultAsync(ct);

        return managerId is null ? null : await FindAsync(managerId, ct);
    }

    public async Task<UserSummary?> DepartmentHeadAsync(string departmentId, CancellationToken ct = default)
    {
        var headId = await db.Departments
            .Where(d => d.Id == departmentId)
            .Select(d => d.HeadUserId)
            .FirstOrDefaultAsync(ct);

        return headId is null ? null : await FindAsync(headId, ct);
    }

    public async Task<IReadOnlyList<UserSummary>> InRoleAsync(
        string roleName, CancellationToken ct = default)
    {
        var ids = await (
            from userRole in db.UserRoles
            join role in db.Roles on userRole.RoleId equals role.Id
            where role.Name == roleName
            select userRole.UserId).ToListAsync(ct);

        return await Project(db.Users.Where(u => ids.Contains(u.Id) && u.IsActive)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<UserSummary>> WithPermissionAsync(
        string permission, CancellationToken ct = default)
    {
        // Anyone in a role carrying the claim, plus every Super Admin - who
        // holds all permissions implicitly and would otherwise be missed.
        var ids = await (
            from userRole in db.UserRoles
            join claim in db.RoleClaims on userRole.RoleId equals claim.RoleId
            where claim.ClaimType == PermissionClaim.Type && claim.ClaimValue == permission
            select userRole.UserId).Distinct().ToListAsync(ct);

        var admins = await (
            from userRole in db.UserRoles
            join role in db.Roles on userRole.RoleId equals role.Id
            where role.Name == PlatformPermissions.SuperAdminRole
            select userRole.UserId).ToListAsync(ct);

        var all = ids.Union(admins).ToList();

        return await Project(db.Users.Where(u => all.Contains(u.Id) && u.IsActive)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<UserSummary>> SearchAsync(
        string? term, int take = 25, CancellationToken ct = default)
    {
        var users = db.Users.Where(u => u.IsActive);

        if (!string.IsNullOrWhiteSpace(term))
        {
            var pattern = $"%{term.Trim()}%";
            users = users.Where(u =>
                EF.Functions.ILike(u.FullName, pattern) ||
                (u.Email != null && EF.Functions.ILike(u.Email, pattern)));
        }

        // Ordered before Take so it is the first N by name, and again after the
        // join so the outer join cannot hand them back in another order.
        var page = await Project(users.OrderBy(u => u.FullName).Take(take)).ToListAsync(ct);
        return [.. page.OrderBy(u => u.FullName)];
    }
}

/// <summary>Reads the one company profile, cached process-wide.</summary>
public interface ICompanyProfileService
{
    Task<CompanyProfile> GetAsync(CancellationToken ct = default);
    Task SaveAsync(CompanyProfile profile, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class CompanyProfileService(PlatformDbContext db) : ICompanyProfileService
{
    // The profile heads every printed document, so it is read constantly and
    // written almost never. Cached until a save drops it.
    private static CompanyProfile? _cached;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<CompanyProfile> GetAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;

        await Gate.WaitAsync(ct);
        try
        {
            _cached ??= await db.CompanyProfiles.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync(ct)
                        ?? new CompanyProfile { Name = "MEI" };
            return _cached;
        }
        finally { Gate.Release(); }
    }

    public async Task SaveAsync(CompanyProfile profile, CancellationToken ct = default)
    {
        var existing = await db.CompanyProfiles.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            // GetAsync hands back an unsaved stand-in when the table is empty, so
            // the first save arrives carrying whatever Id that stand-in had.
            profile.Id = 0;
            db.CompanyProfiles.Add(profile);
        }
        else
        {
            // SetValues copies by property name, key included, and EF refuses to
            // modify a key on a tracked entity. Match the row being updated.
            profile.Id = existing.Id;
            db.Entry(existing).CurrentValues.SetValues(profile);
        }

        await db.SaveChangesAsync(ct);

        // Drop the cache so a logo change is live immediately, not after a restart.
        _cached = null;
    }
}
