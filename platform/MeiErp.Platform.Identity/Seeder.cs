using System.Security.Claims;
using MeiErp.Platform.Kernel;
using MeiErp.Platform.Workflow;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MeiErp.Platform.Identity;

/// <summary>
/// Brings a fresh database up to a usable state, and keeps an existing one in
/// step as modules add permissions.
///
/// Everything here is additive and idempotent: it creates what is missing and
/// never overwrites what an administrator has changed. A seeder that resets
/// roles on every startup silently undoes people's work.
/// </summary>
public sealed class PlatformSeeder(
    PlatformDbContext db,
    UserManager<ApplicationUser> users,
    RoleManager<ApplicationRole> roles,
    IModuleCatalog catalog,
    IConfiguration config,
    IClock clock,
    ILogger<PlatformSeeder> log)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        await SeedRolesAsync(ct);
        await SeedAdminAsync(ct);
        await SeedCompanyAsync(ct);
        await SeedWorkflowsAsync(ct);
    }

    /// <summary>
    /// Super Admin plus every role template the loaded modules ship. New
    /// permissions on an existing role are added; removed ones are left alone,
    /// because an admin may have granted them deliberately.
    /// </summary>
    private async Task SeedRolesAsync(CancellationToken ct)
    {
        var superAdmin = await EnsureRoleAsync(
            PlatformPermissions.SuperAdminRole, null,
            "Holds every permission on the platform, including ones added later.", ct);

        // Super Admin is not given claims: the permission handler short-circuits
        // on the role itself, so a module installed next month is covered
        // without anyone re-running a seeder.
        _ = superAdmin;

        foreach (var module in catalog.All)
        {
            foreach (var template in module.RoleTemplates)
            {
                var role = await EnsureRoleAsync(
                    template.Name, module.Key, template.Description, ct);

                var existing = (await roles.GetClaimsAsync(role))
                    .Where(c => c.Type == PermissionClaim.Type)
                    .Select(c => c.Value)
                    .ToHashSet(StringComparer.Ordinal);

                foreach (var permission in template.Permissions.Where(p => !existing.Contains(p)))
                {
                    await roles.AddClaimAsync(role, new Claim(PermissionClaim.Type, permission));
                    log.LogInformation(
                        "Granted {Permission} to role {Role}", permission, template.Name);
                }
            }
        }
    }

    private async Task<ApplicationRole> EnsureRoleAsync(
        string name, string? moduleKey, string? description, CancellationToken ct)
    {
        var role = await roles.FindByNameAsync(name);
        if (role is not null) return role;

        role = new ApplicationRole
        {
            Name = name,
            ModuleKey = moduleKey,
            Description = description,
            IsSystemRole = true
        };

        var result = await roles.CreateAsync(role);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not create role '{name}': " +
                string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        log.LogInformation("Created role {Role}", name);
        return role;
    }

    /// <summary>
    /// The first administrator, so a fresh install can be signed into.
    /// Credentials come from configuration, never from a literal in the source.
    /// </summary>
    private async Task SeedAdminAsync(CancellationToken ct)
    {
        var email = config["Seed:AdminEmail"] ?? "admin@mei.local";
        if (await users.FindByEmailAsync(email) is not null) return;

        var password = config["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            log.LogWarning(
                "No Seed:AdminPassword configured - skipping the first administrator. " +
                "Set it in appsettings.Development.json or as an environment variable.");
            return;
        }

        var admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = config["Seed:AdminName"] ?? "Administrator",
            IsActive = true,
            CreatedUtc = clock.UtcNow,

            // Even the seeded account changes its password on first sign-in;
            // a known default left in place is how an install stays wide open.
            MustChangePassword = true
        };

        var result = await users.CreateAsync(admin, password);
        if (!result.Succeeded)
        {
            log.LogError("Could not create the first administrator: {Errors}",
                string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }

        await users.AddToRoleAsync(admin, PlatformPermissions.SuperAdminRole);
        log.LogInformation("Created the first administrator: {Email}", email);
    }

    private async Task SeedCompanyAsync(CancellationToken ct)
    {
        if (await db.CompanyProfiles.AnyAsync(ct)) return;

        db.CompanyProfiles.Add(new CompanyProfile
        {
            Name = config["Seed:CompanyName"] ?? "MEI",
            Currency = "PKR",
            CurrencySymbol = "Rs"
        });

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// A single-step default workflow for every approvable document a module
    /// declares, so approvals work the moment a module is installed.
    ///
    /// Only created when the document type has none - an administrator's own
    /// design is never replaced.
    /// </summary>
    private async Task SeedWorkflowsAsync(CancellationToken ct)
    {
        var existing = await db.Workflows
            .Select(w => w.DocumentType)
            .ToListAsync(ct);

        var missing = catalog.AllApprovables
            .Where(a => !existing.Contains(a.Key, StringComparer.Ordinal))
            .ToList();

        if (missing.Count == 0) return;

        foreach (var doc in missing)
        {
            db.Workflows.Add(new WorkflowDefinition
            {
                DocumentType = doc.Key,
                Name = $"{doc.Name} approval",
                Description =
                    "Created automatically so approvals work out of the box. " +
                    "Edit it to add levels and amount bands.",
                IsActive = true,
                Revision = 1,
                BlockSelfApproval = true,
                Steps =
                [
                    new WorkflowStep
                    {
                        Order = 1,
                        Name = "Line manager",
                        Rule = ApproverRule.LineManager,
                        Quorum = StepQuorum.Any,
                        AllowReturn = true,
                        ReminderAfterHours = 24,
                        EscalateAfterHours = 72
                    }
                ]
            });

            log.LogInformation("Seeded a default workflow for {DocumentType}", doc.Key);
        }

        await db.SaveChangesAsync(ct);
    }
}

public static class SeederExtensions
{
    /// <summary>Runs the seeder once at startup, in its own scope.</summary>
    public static async Task SeedPlatformAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<PlatformSeeder>();
        await seeder.SeedAsync();
    }
}
