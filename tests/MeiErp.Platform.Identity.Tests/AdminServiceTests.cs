using MeiErp.Platform.Identity;
using MeiErp.Platform.Kernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace MeiErp.Platform.Identity.Tests;

/// <summary>
/// The administration rules that stop someone breaking their own system:
/// locking out the last administrator, and building a reporting line that
/// loops. Both are unrecoverable through the UI once done, which is exactly
/// why they are refused rather than warned about.
/// </summary>
[Collection("postgres")]
public sealed class AdminServiceTests : IAsyncLifetime
{
    private static readonly string BaseConnection =
        Environment.GetEnvironmentVariable("MEIERP_TEST_DB")
        ?? "Host=127.0.0.1;Username=meierp;Password=DevPassword1!;";

    private readonly string _database = $"mei_admin_{Guid.NewGuid():N}";
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero));

    private ServiceProvider _services = default!;
    private bool _available;

    private string Connection => BaseConnection + $"Database={_database};";

    public async Task InitializeAsync()
    {
        try
        {
            await using (var admin = new DbContext(
                new DbContextOptionsBuilder().UseNpgsql(BaseConnection + "Database=postgres;").Options))
            {
                await admin.Database.ExecuteSqlRawAsync($"CREATE DATABASE \"{_database}\";");
            }

            var services = new ServiceCollection();
            services.AddLogging();

            // Identity token providers are built on data protection, so it has to
            // be registered here exactly as the host registers it.
            services.AddDataProtection();
            services.AddDbContext<PlatformDbContext>(o => o.UseNpgsql(Connection));
            services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.Password.RequiredLength = 10;
                o.Password.RequireNonAlphanumeric = false;
                o.User.RequireUniqueEmail = true;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<PlatformDbContext>()
            // ResetPasswordAsync generates a real reset token, so the providers
            // have to be registered here exactly as the host registers them.
            // Faking that would test a code path production never runs.
            .AddDefaultTokenProviders();

            services.AddSingleton<IClock>(_clock);
            services.AddScoped<IAdminService, AdminService>();

            _services = services.BuildServiceProvider();

            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            await db.Database.MigrateAsync();

            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            await roles.CreateAsync(new ApplicationRole
            {
                Name = PlatformPermissions.SuperAdminRole,
                IsSystemRole = true
            });

            _available = true;
        }
        catch (NpgsqlException)
        {
            // Only an unreachable server means "skip". Catching everything here
            // would turn a genuine setup bug into a silent skip and a green run
            // that asserted nothing - the exact failure this suite exists to
            // avoid, and one the previous platform shipped with.
            _available = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_services is not null) await _services.DisposeAsync();
        if (!_available) return;

        try
        {
            await using var admin = new DbContext(
                new DbContextOptionsBuilder().UseNpgsql(BaseConnection + "Database=postgres;").Options);
            await admin.Database.ExecuteSqlRawAsync(
                $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE);");
        }
        catch { /* a stray throwaway database is harmless */ }
    }

    private async Task<string> AddUserAsync(
        IServiceScope scope, string name, string email, bool superAdmin = false)
    {
        var admin = scope.ServiceProvider.GetRequiredService<IAdminService>();

        var created = await admin.CreateUserAsync(
            new UserInput(name, email, null, null, null, null), "TempPass123!");

        Assert.True(created.Ok, created.Error);

        if (superAdmin)
        {
            var result = await admin.SetRolesAsync(
                created.Value, [PlatformPermissions.SuperAdminRole]);
            Assert.True(result.Ok, result.Error);
        }

        return created.Value;
    }

    // ---------- the last administrator ----------

    [SkippableFact]
    public async Task The_last_administrator_cannot_be_deactivated()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        using var scope = _services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IAdminService>();
        var onlyAdmin = await AddUserAsync(scope, "Only Admin", "only@mei.local", superAdmin: true);

        var result = await admin.SetActiveAsync(onlyAdmin, active: false);

        // Allowing this leaves nobody able to administer the system, and no way
        // back except editing the database by hand.
        Assert.True(result.Failed);
        Assert.Equal("user.last-admin", result.Code);
    }

    [SkippableFact]
    public async Task The_last_administrator_cannot_have_the_role_taken_away()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        using var scope = _services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IAdminService>();
        var onlyAdmin = await AddUserAsync(scope, "Only Admin", "only@mei.local", superAdmin: true);

        var result = await admin.SetRolesAsync(onlyAdmin, []);

        Assert.True(result.Failed);
        Assert.Equal("user.last-admin", result.Code);
    }

    [SkippableFact]
    public async Task An_administrator_can_be_deactivated_once_a_second_one_exists()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        using var scope = _services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IAdminService>();
        var first = await AddUserAsync(scope, "First Admin", "first@mei.local", superAdmin: true);
        await AddUserAsync(scope, "Second Admin", "second@mei.local", superAdmin: true);

        var result = await admin.SetActiveAsync(first, active: false);

        Assert.True(result.Ok, result.Error);
    }

    [SkippableFact]
    public async Task A_deactivated_administrator_does_not_count_as_cover()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        using var scope = _services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IAdminService>();
        var active = await AddUserAsync(scope, "Active Admin", "active@mei.local", superAdmin: true);
        var dormant = await AddUserAsync(scope, "Dormant Admin", "dormant@mei.local", superAdmin: true);

        await admin.SetActiveAsync(dormant, active: false);

        // Only one administrator can actually sign in now, so the remaining one
        // must still be protected.
        var result = await admin.SetActiveAsync(active, active: false);

        Assert.True(result.Failed);
        Assert.Equal("user.last-admin", result.Code);
    }

    // ---------- the reporting line ----------

    [SkippableFact]
    public async Task Nobody_can_be_their_own_line_manager()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        using var scope = _services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IAdminService>();
        var user = await AddUserAsync(scope, "Rafiq", "rafiq@mei.local");

        var result = await admin.UpdateUserAsync(
            user, new UserInput("Rafiq", "rafiq@mei.local", null, null, null, user));

        Assert.True(result.Failed);
        Assert.Equal("user.self-manager", result.Code);
    }

    [SkippableFact]
    public async Task A_reporting_line_that_loops_is_refused()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        using var scope = _services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IAdminService>();

        var a = await AddUserAsync(scope, "A", "a@mei.local");
        var b = await AddUserAsync(scope, "B", "b@mei.local");
        var c = await AddUserAsync(scope, "C", "c@mei.local");

        // A -> B -> C, then try to close the loop with C -> A.
        Assert.True((await admin.UpdateUserAsync(a,
            new UserInput("A", "a@mei.local", null, null, null, b))).Ok);
        Assert.True((await admin.UpdateUserAsync(b,
            new UserInput("B", "b@mei.local", null, null, null, c))).Ok);

        var result = await admin.UpdateUserAsync(c,
            new UserInput("C", "c@mei.local", null, null, null, a));

        // Left in place, line-manager approval routing would walk the chain for ever.
        Assert.True(result.Failed);
        Assert.Equal("user.manager-cycle", result.Code);
    }

    [SkippableFact]
    public async Task An_ordinary_reporting_line_is_accepted()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        using var scope = _services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IAdminService>();

        var manager = await AddUserAsync(scope, "Manager", "manager@mei.local");
        var staff = await AddUserAsync(scope, "Staff", "staff@mei.local");

        var result = await admin.UpdateUserAsync(staff,
            new UserInput("Staff", "staff@mei.local", null, null, null, manager));

        Assert.True(result.Ok, result.Error);

        var detail = await admin.UserAsync(staff);
        Assert.Equal(manager, detail!.LineManagerId);
    }

    // ---------- new accounts ----------

    [SkippableFact]
    public async Task A_new_user_must_change_the_password_they_were_given()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        using var scope = _services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var id = await AddUserAsync(scope, "Newcomer", "new@mei.local");

        var user = await users.FindByIdAsync(id);

        // Whoever created the account knows this password.
        Assert.True(user!.MustChangePassword);
    }

    [SkippableFact]
    public async Task Two_accounts_cannot_share_an_email()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        using var scope = _services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IAdminService>();
        await AddUserAsync(scope, "First", "same@mei.local");

        var second = await admin.CreateUserAsync(
            new UserInput("Second", "same@mei.local", null, null, null, null), "TempPass123!");

        Assert.True(second.Failed);
        Assert.Equal("user.duplicate-email", second.Code);
    }

    [SkippableFact]
    public async Task An_admin_reset_forces_the_owner_to_choose_their_own_password()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        using var scope = _services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IAdminService>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var id = await AddUserAsync(scope, "Rafiq", "rafiq@mei.local");

        // Clear the flag as if they had already set their own password.
        var user = await users.FindByIdAsync(id);
        user!.MustChangePassword = false;
        await users.UpdateAsync(user);

        var result = await admin.ResetPasswordAsync(id, "AdminChose99!");
        Assert.True(result.Ok, result.Error);

        var after = await users.FindByIdAsync(id);
        Assert.True(after!.MustChangePassword);
    }
}
