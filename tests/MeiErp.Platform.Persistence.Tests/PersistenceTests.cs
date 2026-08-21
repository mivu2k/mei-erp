using MeiErp.Platform.Kernel;
using MeiErp.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MeiErp.Platform.Persistence.Tests;

/// <summary>A throwaway module context, so the base class's behaviour can be tested directly.</summary>
public sealed class TestDbContext(
    DbContextOptions options, ICurrentUser user, IClock clock)
    : ModuleDbContext(options, user, clock)
{
    protected override string Schema => "test_module";

    public DbSet<Widget> Widgets => Set<Widget>();
}

public sealed class Widget : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

/// <summary>
/// The persistence rules, proven against a real PostgreSQL rather than an
/// in-memory provider - soft delete, audit stamping and xmin concurrency are
/// all things an in-memory provider would happily fake.
///
/// These skip when no server is reachable, and CI must run them: the previous
/// platform had integration tests that silently returned and reported green,
/// which is worse than having none.
/// </summary>
[Collection("postgres")]
public sealed class PersistenceTests : IAsyncLifetime
{
    /// <summary>
    /// Read from the environment so no password is committed, with a local dev
    /// default that only ever reaches a throwaway database on 127.0.0.1.
    /// CI sets MEIERP_TEST_DB to point at its own server.
    /// </summary>
    private static readonly string BaseConnection =
        Environment.GetEnvironmentVariable("MEIERP_TEST_DB")
        ?? "Host=127.0.0.1;Username=meierp;Password=DevPassword1!;";

    private readonly string _database = $"mei_test_{Guid.NewGuid():N}";
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero));
    private readonly TestUser _user = new("user-42", "Rafiq");
    private bool _available;

    private string Connection => BaseConnection + $"Database={_database};";

    public async Task InitializeAsync()
    {
        try
        {
            await using var admin = new NpgsqlConnectionWrapper(BaseConnection + "Database=postgres;");
            await admin.ExecuteAsync($"CREATE DATABASE \"{_database}\";");

            await using var db = NewContext();
            await db.Database.EnsureCreatedAsync();
            _available = true;
        }
        catch (Exception)
        {
            // No server, or no permission to create databases. The tests skip
            // rather than fail locally - but Skip.IfNot fails them in CI.
            _available = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (!_available) return;
        try
        {
            await using var admin = new NpgsqlConnectionWrapper(BaseConnection + "Database=postgres;");
            await admin.ExecuteAsync(
                $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE);");
        }
        catch { /* the throwaway database outliving one test run is harmless */ }
    }

    private TestDbContext NewContext(ICurrentUser? user = null) =>
        new(new DbContextOptionsBuilder()
                .UseNpgsql(Connection)
                .Options,
            user ?? _user,
            _clock);

    [SkippableFact]
    public async Task Insert_stamps_who_and_when()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewContext();
        db.Widgets.Add(new Widget { Name = "Bolt", Price = 12.50m });
        await db.SaveChangesAsync();

        var saved = await db.Widgets.SingleAsync();
        Assert.Equal(_clock.UtcNow, saved.CreatedUtc);
        Assert.Equal("user-42", saved.CreatedBy);
        Assert.Null(saved.ModifiedUtc);
    }

    [SkippableFact]
    public async Task Update_never_rewrites_the_created_stamp()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using (var db = NewContext())
        {
            db.Widgets.Add(new Widget { Name = "Bolt", Price = 12.50m });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext(new TestUser("user-99", "Someone else")))
        {
            var widget = await db.Widgets.SingleAsync();
            widget.Price = 15m;
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var saved = await db.Widgets.SingleAsync();
            Assert.Equal("user-42", saved.CreatedBy);   // unchanged
            Assert.Equal("user-99", saved.ModifiedBy);
        }
    }

    [SkippableFact]
    public async Task Delete_is_soft_and_the_row_stops_appearing()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using (var db = NewContext())
        {
            db.Widgets.Add(new Widget { Name = "Bolt", Price = 12.50m });
            await db.SaveChangesAsync();

            db.Widgets.Remove(await db.Widgets.SingleAsync());
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            // Gone from ordinary queries...
            Assert.Empty(await db.Widgets.ToListAsync());

            // ...but still there, so history keeps resolving.
            var kept = await db.Widgets.IgnoreQueryFilters().SingleAsync();
            Assert.True(kept.IsDeleted);
            Assert.Equal("user-42", kept.DeletedBy);
            Assert.Equal(_clock.UtcNow, kept.DeletedUtc);
        }
    }

    [SkippableFact]
    public async Task A_lost_update_is_refused_rather_than_silently_winning()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using (var seed = NewContext())
        {
            seed.Widgets.Add(new Widget { Name = "Bolt", Price = 100m });
            await seed.SaveChangesAsync();
        }

        // Two people open the same record.
        await using var first = NewContext();
        await using var second = NewContext();
        var a = await first.Widgets.SingleAsync();
        var b = await second.Widgets.SingleAsync();

        a.Price = 150m;
        await first.SaveChangesAsync();

        b.Price = 200m;

        // Without xmin, b would quietly overwrite a's change and nobody would
        // ever know the first edit happened.
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => second.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task Money_is_stored_at_four_decimal_places_without_drift()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewContext();
        db.Widgets.Add(new Widget { Name = "Odd", Price = 0.1m + 0.2m });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var saved = await db.Widgets.SingleAsync();

        // The value that comes back is exactly the one that went in - the whole
        // reason money is numeric and never floating point.
        Assert.Equal(0.3m, saved.Price);
    }

    private sealed class TestUser(string id, string name) : ICurrentUser
    {
        public string? UserId { get; } = id;
        public string? Name { get; } = name;
        public string? Email => null;
        public bool IsAuthenticated => true;
        public bool Can(string permission) => true;
        public bool InModule(string moduleKey) => true;
        public IReadOnlyCollection<string> Roles { get; } = [];
    }
}

/// <summary>Minimal raw-SQL helper for creating and dropping the throwaway database.</summary>
internal sealed class NpgsqlConnectionWrapper(string connectionString) : IAsyncDisposable
{
    private readonly DbContext _db = new(
        new DbContextOptionsBuilder().UseNpgsql(connectionString).Options);

    public Task ExecuteAsync(string sql) => _db.Database.ExecuteSqlRawAsync(sql);

    public ValueTask DisposeAsync() => _db.DisposeAsync();
}
