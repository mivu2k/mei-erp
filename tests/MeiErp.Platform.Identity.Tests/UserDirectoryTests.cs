using MeiErp.Platform.Identity;
using MeiErp.Platform.Kernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace MeiErp.Platform.Identity.Tests;

/// <summary>
/// Every one of these executes its query against a real PostgreSQL server,
/// because that is the only place the failure they pin can appear.
///
/// <see cref="UserDirectory"/> outer-joins users to departments and projects
/// the pair into <see cref="UserSummary"/>. Filtering *after* that projection -
/// testing a property of the constructed record - compiles, satisfies every
/// in-memory test, and then throws <see cref="InvalidOperationException"/> the
/// first time a real screen calls it, because EF cannot translate a predicate
/// over a constructed object across the join. Three screens shipped broken
/// that way: the department editor, the user editor and the workshop job
/// editor. A test that does not reach a database cannot tell the difference.
/// </summary>
[Collection("postgres")]
public sealed class UserDirectoryTests : IAsyncLifetime
{
    private static readonly string BaseConnection =
        Environment.GetEnvironmentVariable("MEIERP_TEST_DB")
        ?? "Host=127.0.0.1;Username=meierp;Password=DevPassword1!;";

    private readonly string _database = $"mei_dir_{Guid.NewGuid():N}";
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
            .AddDefaultTokenProviders();

            services.AddSingleton<IClock>(_clock);
            services.AddScoped<IAdminService, AdminService>();
            services.AddScoped<IUserDirectory, UserDirectory>();

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
            // Only an unreachable server means "skip"; see AdminServiceTests.
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

    private static async Task<string> AddUserAsync(
        IServiceScope scope, string name, string email, string? departmentId = null)
    {
        var admin = scope.ServiceProvider.GetRequiredService<IAdminService>();

        var created = await admin.CreateUserAsync(
            new UserInput(name, email, null, null, departmentId, null), "TempPass123!");

        Assert.True(created.Ok, created.Error);
        return created.Value;
    }

    /// <summary>
    /// The call the department editor makes. It asks for everyone so the head
    /// picker can be filled, which is the widest form of the query.
    /// </summary>
    [SkippableFact]
    public async Task Searching_everyone_runs_against_the_database()
    {
        Skip.IfNot(_available, "PostgreSQL is not reachable.");

        using var scope = _services.CreateScope();
        await AddUserAsync(scope, "Ayesha Khan", "ayesha@example.com");
        await AddUserAsync(scope, "Bilal Ahmed", "bilal@example.com");

        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();

        var people = await directory.SearchAsync(null, take: 500);

        Assert.Equal(2, people.Count);
        Assert.Equal("Ayesha Khan", people[0].FullName);   // ordered by name
        Assert.Equal("Bilal Ahmed", people[1].FullName);
    }

    /// <summary>A user with no department still comes back - the join is outer.</summary>
    [SkippableFact]
    public async Task A_user_without_a_department_is_still_listed()
    {
        Skip.IfNot(_available, "PostgreSQL is not reachable.");

        using var scope = _services.CreateScope();
        await AddUserAsync(scope, "Unassigned Person", "nobody@example.com");

        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();
        var people = await directory.SearchAsync(null, take: 500);

        var person = Assert.Single(people);
        Assert.Null(person.DepartmentId);
        Assert.Null(person.DepartmentName);
    }

    /// <summary>The department name is carried across the join, not left null.</summary>
    [SkippableFact]
    public async Task A_users_department_name_comes_back_with_them()
    {
        Skip.IfNot(_available, "PostgreSQL is not reachable.");

        using var scope = _services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IAdminService>();

        var department = new Department { Name = "Finance", Code = "FIN" };
        Assert.True((await admin.SaveDepartmentAsync(department)).Ok);

        await AddUserAsync(scope, "Sana Malik", "sana@example.com", department.Id);

        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();
        var person = Assert.Single(await directory.SearchAsync(null, take: 500));

        Assert.Equal("Finance", person.DepartmentName);
    }

    /// <summary>Searching by name filters in the database, before the join.</summary>
    [SkippableFact]
    public async Task Searching_by_name_narrows_the_list()
    {
        Skip.IfNot(_available, "PostgreSQL is not reachable.");

        using var scope = _services.CreateScope();
        await AddUserAsync(scope, "Ayesha Khan", "ayesha@example.com");
        await AddUserAsync(scope, "Bilal Ahmed", "bilal@example.com");

        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();

        var found = Assert.Single(await directory.SearchAsync("ayesha"));
        Assert.Equal("Ayesha Khan", found.FullName);
    }

    /// <summary>Finding one person by id goes through the same projection.</summary>
    [SkippableFact]
    public async Task Finding_one_person_runs_against_the_database()
    {
        Skip.IfNot(_available, "PostgreSQL is not reachable.");

        using var scope = _services.CreateScope();
        var id = await AddUserAsync(scope, "Ayesha Khan", "ayesha@example.com");

        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();

        var found = await directory.FindAsync(id);
        Assert.NotNull(found);
        Assert.Equal("Ayesha Khan", found!.FullName);
    }

    /// <summary>
    /// The lookup the approval engine depends on: the same projection, reached
    /// through the role join rather than a search box.
    /// </summary>
    [SkippableFact]
    public async Task Listing_a_role_runs_against_the_database()
    {
        Skip.IfNot(_available, "PostgreSQL is not reachable.");

        using var scope = _services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IAdminService>();

        var id = await AddUserAsync(scope, "Ayesha Khan", "ayesha@example.com");
        Assert.True((await admin.SetRolesAsync(id, [PlatformPermissions.SuperAdminRole])).Ok);

        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();

        var people = await directory.InRoleAsync(PlatformPermissions.SuperAdminRole);
        Assert.Equal("Ayesha Khan", Assert.Single(people).FullName);
    }
}
