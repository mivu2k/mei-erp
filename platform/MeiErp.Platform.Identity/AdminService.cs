using System.Security.Claims;
using MeiErp.Platform.Kernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Platform.Identity;

/// <summary>
/// Everything the administration screens do to users, roles and departments.
///
/// Kept out of the pages so the rules - you cannot strip the last administrator,
/// you cannot make someone their own manager - live in one place and are
/// testable without a browser.
/// </summary>
public interface IAdminService
{
    Task<IReadOnlyList<UserRow>> UsersAsync(string? search, bool includeInactive, CancellationToken ct = default);
    Task<UserDetail?> UserAsync(string userId, CancellationToken ct = default);
    Task<Result<string>> CreateUserAsync(UserInput input, string password, CancellationToken ct = default);
    Task<Result> UpdateUserAsync(string userId, UserInput input, CancellationToken ct = default);
    Task<Result> SetActiveAsync(string userId, bool active, CancellationToken ct = default);
    Task<Result> ResetPasswordAsync(string userId, string newPassword, CancellationToken ct = default);
    Task<Result> SetRolesAsync(string userId, IReadOnlyList<string> roleNames, CancellationToken ct = default);

    Task<IReadOnlyList<RoleRow>> RolesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> RolePermissionsAsync(string roleId, CancellationToken ct = default);
    Task<Result<string>> CreateRoleAsync(string name, string? moduleKey, string? description, CancellationToken ct = default);
    Task<Result> SetRolePermissionsAsync(string roleId, IReadOnlyList<string> permissions, CancellationToken ct = default);
    Task<Result> DeleteRoleAsync(string roleId, CancellationToken ct = default);

    Task<IReadOnlyList<Department>> DepartmentsAsync(CancellationToken ct = default);
    Task<Result> SaveDepartmentAsync(Department department, CancellationToken ct = default);
    Task<Result> DeleteDepartmentAsync(string id, CancellationToken ct = default);
}

public sealed record UserRow(
    string Id, string FullName, string? Email, string? Designation,
    string? DepartmentName, string? LineManagerName,
    bool IsActive, bool LockedOut, IReadOnlyList<string> Roles, DateTime? LastLoginUtc);

public sealed record UserDetail(
    string Id, string FullName, string? Email, string? EmployeeCode, string? Designation,
    string? DepartmentId, string? LineManagerId, bool IsActive,
    IReadOnlyList<string> Roles);

public sealed record UserInput(
    string FullName, string Email, string? EmployeeCode, string? Designation,
    string? DepartmentId, string? LineManagerId);

public sealed record RoleRow(
    string Id, string Name, string? ModuleKey, string? Description,
    bool IsSystemRole, int MemberCount, int PermissionCount);

/// <inheritdoc />
public sealed class AdminService(
    PlatformDbContext db,
    UserManager<ApplicationUser> users,
    RoleManager<ApplicationRole> roles,
    IClock clock) : IAdminService
{
    // ------------------------------------------------------------- users

    public async Task<IReadOnlyList<UserRow>> UsersAsync(
        string? search, bool includeInactive, CancellationToken ct = default)
    {
        var query = db.Users.AsNoTracking().AsQueryable();

        if (!includeInactive)
            query = query.Where(u => u.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(u =>
                EF.Functions.ILike(u.FullName, pattern) ||
                (u.Email != null && EF.Functions.ILike(u.Email, pattern)) ||
                (u.EmployeeCode != null && EF.Functions.ILike(u.EmployeeCode, pattern)));
        }

        var rows = await (
            from u in query
            join d in db.Departments on u.DepartmentId equals d.Id into dd
            from d in dd.DefaultIfEmpty()
            join m in db.Users on u.LineManagerId equals m.Id into mm
            from m in mm.DefaultIfEmpty()
            orderby u.FullName
            select new
            {
                u.Id, u.FullName, u.Email, u.Designation, u.IsActive, u.LastLoginUtc,
                u.LockoutEnd,
                DepartmentName = d.Name,
                ManagerName = m.FullName
            }).ToListAsync(ct);

        // Roles in one query rather than per row - the classic N+1 that makes a
        // user list feel slow at fifty people and unusable at five hundred.
        var roleMap = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            select new { ur.UserId, r.Name })
            .ToListAsync(ct);

        var byUser = roleMap
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Name!).ToList());

        var now = clock.UtcNow;

        return rows.Select(r => new UserRow(
            r.Id, r.FullName, r.Email, r.Designation,
            r.DepartmentName, r.ManagerName,
            r.IsActive,
            r.LockoutEnd is not null && r.LockoutEnd > now,
            byUser.TryGetValue(r.Id, out var rs) ? rs : [],
            r.LastLoginUtc)).ToList();
    }

    public async Task<UserDetail?> UserAsync(string userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return null;

        var roleNames = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            where ur.UserId == userId
            select r.Name!).ToListAsync(ct);

        return new UserDetail(
            user.Id, user.FullName, user.Email, user.EmployeeCode, user.Designation,
            user.DepartmentId, user.LineManagerId, user.IsActive, roleNames);
    }

    public async Task<Result<string>> CreateUserAsync(
        UserInput input, string password, CancellationToken ct = default)
    {
        if (await users.FindByEmailAsync(input.Email) is not null)
            return Result.Fail<string>($"{input.Email} already has an account.", "user.duplicate-email");

        var user = new ApplicationUser
        {
            UserName = input.Email,
            Email = input.Email,
            EmailConfirmed = true,
            FullName = input.FullName,
            EmployeeCode = string.IsNullOrWhiteSpace(input.EmployeeCode) ? null : input.EmployeeCode,
            Designation = input.Designation,
            DepartmentId = string.IsNullOrWhiteSpace(input.DepartmentId) ? null : input.DepartmentId,
            LineManagerId = string.IsNullOrWhiteSpace(input.LineManagerId) ? null : input.LineManagerId,
            IsActive = true,
            CreatedUtc = clock.UtcNow,

            // Whoever created the account knows this password. The owner picks
            // their own before they can do anything else.
            MustChangePassword = true
        };

        var result = await users.CreateAsync(user, password);

        return result.Succeeded
            ? Result.Success(user.Id)
            : Result.Fail<string>(Describe(result), "user.create-failed");
    }

    public async Task<Result> UpdateUserAsync(
        string userId, UserInput input, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null) return Result.Fail("That user no longer exists.", "user.not-found");

        // A person cannot report to themselves, and a two-person loop would
        // make line-manager approval routing spin forever.
        if (input.LineManagerId == userId)
            return Result.Fail("Someone cannot be their own line manager.", "user.self-manager");

        if (await WouldLoopAsync(userId, input.LineManagerId, ct))
        {
            return Result.Fail(
                "That reporting line loops back on itself, which would leave approvals with nowhere to go.",
                "user.manager-cycle");
        }

        user.FullName = input.FullName;
        user.Email = input.Email;
        user.UserName = input.Email;
        user.EmployeeCode = string.IsNullOrWhiteSpace(input.EmployeeCode) ? null : input.EmployeeCode;
        user.Designation = input.Designation;
        user.DepartmentId = string.IsNullOrWhiteSpace(input.DepartmentId) ? null : input.DepartmentId;
        user.LineManagerId = string.IsNullOrWhiteSpace(input.LineManagerId) ? null : input.LineManagerId;

        var result = await users.UpdateAsync(user);
        return result.Succeeded ? Result.Success() : Result.Fail(Describe(result));
    }

    /// <summary>Walks up the proposed reporting line looking for the person we started from.</summary>
    private async Task<bool> WouldLoopAsync(string userId, string? managerId, CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = managerId;

        while (current is not null)
        {
            if (current == userId) return true;
            if (!seen.Add(current)) return true;   // a pre-existing loop further up

            current = await db.Users
                .Where(u => u.Id == current)
                .Select(u => u.LineManagerId)
                .FirstOrDefaultAsync(ct);
        }

        return false;
    }

    public async Task<Result> SetActiveAsync(string userId, bool active, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null) return Result.Fail("That user no longer exists.", "user.not-found");

        if (!active && await IsLastAdministratorAsync(userId, ct))
        {
            return Result.Fail(
                "This is the last active administrator. Give someone else the Super Admin role first, " +
                "or nobody will be able to administer the system.",
                "user.last-admin");
        }

        user.IsActive = active;
        await users.UpdateAsync(user);

        // Drops their live sessions immediately rather than waiting for the
        // cookie to expire, which for a leaver could be the rest of the day.
        await users.UpdateSecurityStampAsync(user);

        return Result.Success();
    }

    public async Task<Result> ResetPasswordAsync(
        string userId, string newPassword, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null) return Result.Fail("That user no longer exists.", "user.not-found");

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, newPassword);

        if (!result.Succeeded) return Result.Fail(Describe(result));

        // An administrator now knows this password, so the owner must replace it.
        user.MustChangePassword = true;
        await users.UpdateAsync(user);

        return Result.Success();
    }

    public async Task<Result> SetRolesAsync(
        string userId, IReadOnlyList<string> roleNames, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null) return Result.Fail("That user no longer exists.", "user.not-found");

        var current = await users.GetRolesAsync(user);

        var losingAdmin = current.Contains(PlatformPermissions.SuperAdminRole)
                          && !roleNames.Contains(PlatformPermissions.SuperAdminRole);

        if (losingAdmin && await IsLastAdministratorAsync(userId, ct))
        {
            return Result.Fail(
                "This is the last administrator. Give someone else the Super Admin role first.",
                "user.last-admin");
        }

        var toRemove = current.Except(roleNames, StringComparer.Ordinal).ToList();
        var toAdd = roleNames.Except(current, StringComparer.Ordinal).ToList();

        if (toRemove.Count > 0) await users.RemoveFromRolesAsync(user, toRemove);
        if (toAdd.Count > 0) await users.AddToRolesAsync(user, toAdd);

        // Roles decide module access, which is stamped on the principal. Rotate
        // the stamp so the change lands at the next revalidation rather than at
        // their next sign-in.
        await users.UpdateSecurityStampAsync(user);

        return Result.Success();
    }

    /// <summary>
    /// Locking out the only administrator is unrecoverable without database
    /// access - which is precisely the situation this platform exists to avoid.
    /// </summary>
    private async Task<bool> IsLastAdministratorAsync(string excludingUserId, CancellationToken ct)
    {
        var others = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            join u in db.Users on ur.UserId equals u.Id
            where r.Name == PlatformPermissions.SuperAdminRole
                  && u.IsActive
                  && u.Id != excludingUserId
            select u.Id).AnyAsync(ct);

        return !others;
    }

    // ------------------------------------------------------------- roles

    public async Task<IReadOnlyList<RoleRow>> RolesAsync(CancellationToken ct = default)
    {
        var rows = await db.Roles.AsNoTracking()
            .OrderBy(r => r.ModuleKey ?? "")
            .ThenBy(r => r.Name)
            .Select(r => new
            {
                r.Id, r.Name, r.ModuleKey, r.Description, r.IsSystemRole,
                Members = db.UserRoles.Count(ur => ur.RoleId == r.Id),
                Permissions = db.RoleClaims.Count(
                    c => c.RoleId == r.Id && c.ClaimType == PermissionClaim.Type)
            })
            .ToListAsync(ct);

        return rows.Select(r => new RoleRow(
            r.Id, r.Name!, r.ModuleKey, r.Description,
            r.IsSystemRole, r.Members, r.Permissions)).ToList();
    }

    public async Task<IReadOnlyList<string>> RolePermissionsAsync(
        string roleId, CancellationToken ct = default) =>
        await db.RoleClaims
            .Where(c => c.RoleId == roleId && c.ClaimType == PermissionClaim.Type)
            .Select(c => c.ClaimValue!)
            .ToListAsync(ct);

    public async Task<Result<string>> CreateRoleAsync(
        string name, string? moduleKey, string? description, CancellationToken ct = default)
    {
        if (await roles.FindByNameAsync(name) is not null)
            return Result.Fail<string>($"A role called '{name}' already exists.", "role.duplicate");

        var role = new ApplicationRole
        {
            Name = name,
            ModuleKey = moduleKey,
            Description = description,
            IsSystemRole = false
        };

        var result = await roles.CreateAsync(role);
        return result.Succeeded
            ? Result.Success(role.Id)
            : Result.Fail<string>(Describe(result));
    }

    public async Task<Result> SetRolePermissionsAsync(
        string roleId, IReadOnlyList<string> permissions, CancellationToken ct = default)
    {
        var role = await roles.FindByIdAsync(roleId);
        if (role is null) return Result.Fail("That role no longer exists.", "role.not-found");

        var current = await RolePermissionsAsync(roleId, ct);

        foreach (var gone in current.Except(permissions, StringComparer.Ordinal))
            await roles.RemoveClaimAsync(role, new Claim(PermissionClaim.Type, gone));

        foreach (var added in permissions.Except(current, StringComparer.Ordinal))
            await roles.AddClaimAsync(role, new Claim(PermissionClaim.Type, added));

        return Result.Success();
    }

    public async Task<Result> DeleteRoleAsync(string roleId, CancellationToken ct = default)
    {
        var role = await roles.FindByIdAsync(roleId);
        if (role is null) return Result.Fail("That role no longer exists.", "role.not-found");

        if (role.IsSystemRole)
        {
            return Result.Fail(
                "This role ships with its module and cannot be deleted. " +
                "Remove its permissions instead if you do not want it used.",
                "role.system");
        }

        var members = await db.UserRoles.CountAsync(ur => ur.RoleId == roleId, ct);
        if (members > 0)
        {
            return Result.Fail(
                $"{members} {(members == 1 ? "person holds" : "people hold")} this role. " +
                "Move them off it first - deleting it would silently strip their access.",
                "role.in-use");
        }

        var result = await roles.DeleteAsync(role);
        return result.Succeeded ? Result.Success() : Result.Fail(Describe(result));
    }

    // ------------------------------------------------------------- departments

    public async Task<IReadOnlyList<Department>> DepartmentsAsync(CancellationToken ct = default) =>
        await db.Departments.AsNoTracking()
            .Include(d => d.Head)
            .OrderBy(d => d.Name)
            .ToListAsync(ct);

    public async Task<Result> SaveDepartmentAsync(Department department, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(department.Name))
            return Result.Fail("A department needs a name.", "department.no-name");

        if (department.ParentId == department.Id)
            return Result.Fail("A department cannot sit inside itself.", "department.self-parent");

        var existing = await db.Departments.FirstOrDefaultAsync(d => d.Id == department.Id, ct);

        if (existing is null)
            db.Departments.Add(department);
        else
            db.Entry(existing).CurrentValues.SetValues(department);

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeleteDepartmentAsync(string id, CancellationToken ct = default)
    {
        var staff = await db.Users.CountAsync(u => u.DepartmentId == id, ct);
        if (staff > 0)
        {
            return Result.Fail(
                $"{staff} {(staff == 1 ? "person is" : "people are")} in this department. " +
                "Move them first.",
                "department.in-use");
        }

        var children = await db.Departments.CountAsync(d => d.ParentId == id, ct);
        if (children > 0)
            return Result.Fail("This department has sub-departments. Remove them first.", "department.has-children");

        await db.Departments.Where(d => d.Id == id).ExecuteDeleteAsync(ct);
        return Result.Success();
    }

    private static string Describe(IdentityResult result) =>
        string.Join(" ", result.Errors.Select(e => e.Description));
}
