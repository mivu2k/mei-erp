using MeiErp.Platform.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MeiErp.Platform.Identity.Tests;

/// <summary>
/// The one company row behind every printed document.
///
/// The interesting behaviour is all in saving it: the profile is read constantly
/// and written almost never, so it is cached process-wide, and both the cache and
/// the single-row upsert have a way of going wrong that a screen only reveals
/// once somebody actually presses Save twice.
/// </summary>
[Collection("postgres")]
public sealed class CompanyProfileServiceTests : IAsyncLifetime
{
    private static readonly string BaseConnection =
        Environment.GetEnvironmentVariable("MEIERP_TEST_DB")
        ?? "Host=127.0.0.1;Username=meierp;Password=DevPassword1!;";

    private readonly string _database = $"mei_company_{Guid.NewGuid():N}";
    private bool _available;

    private string Connection => BaseConnection + $"Database={_database};";

    private PlatformDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(Connection).Options);

    public async Task InitializeAsync()
    {
        try
        {
            await using (var admin = new DbContext(
                new DbContextOptionsBuilder().UseNpgsql(BaseConnection + "Database=postgres;").Options))
            {
                await admin.Database.ExecuteSqlRawAsync($"CREATE DATABASE \"{_database}\";");
            }

            await using var db = NewContext();
            await db.Database.MigrateAsync();

            _available = true;
        }
        catch (NpgsqlException)
        {
            // Only an unreachable server means "skip"; anything else is a real
            // setup bug and must surface as a failure, not a silent green run.
            _available = false;
        }

        // The service caches the profile in a static field, so one test's save
        // would otherwise be visible to the next.
        ResetCache();
    }

    public async Task DisposeAsync()
    {
        ResetCache();
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

    private static void ResetCache() =>
        typeof(CompanyProfileService)
            .GetField("_cached", System.Reflection.BindingFlags.NonPublic
                               | System.Reflection.BindingFlags.Static)!
            .SetValue(null, null);

    [SkippableFact]
    public async Task An_empty_table_reads_as_a_stand_in_rather_than_nothing()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewContext();
        var profile = await new CompanyProfileService(db).GetAsync();

        // Documents render on a fresh install, before anyone has visited the
        // admin screen. A null here would be a crash on the first invoice.
        Assert.NotNull(profile);
        Assert.NotEmpty(profile.Name);
    }

    [SkippableFact]
    public async Task The_first_save_inserts_even_though_the_stand_in_carries_no_row()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewContext();
        var service = new CompanyProfileService(db);

        // Exactly what the admin screen does: read, edit, save. On a fresh
        // install the thing it read was never a row.
        var edited = (await service.GetAsync()).Clone();
        edited.Name = "MEI Engineering";
        edited.City = "Karachi";

        await service.SaveAsync(edited);

        await using var fresh = NewContext();
        var rows = await fresh.CompanyProfiles.AsNoTracking().ToListAsync();

        Assert.Single(rows);
        Assert.Equal("MEI Engineering", rows[0].Name);
        Assert.Equal("Karachi", rows[0].City);
    }

    [SkippableFact]
    public async Task Saving_twice_updates_the_same_row_instead_of_adding_another()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using (var db = NewContext())
        {
            var service = new CompanyProfileService(db);
            var first = (await service.GetAsync()).Clone();
            first.Name = "MEI";
            await service.SaveAsync(first);
        }

        await using (var db = NewContext())
        {
            var service = new CompanyProfileService(db);
            var second = (await service.GetAsync()).Clone();
            second.Name = "MEI Engineering";
            second.FooterNote = "Thank you for your business.";
            await service.SaveAsync(second);
        }

        await using var fresh = NewContext();
        var rows = await fresh.CompanyProfiles.AsNoTracking().ToListAsync();

        // "One row" is the whole contract of this table. A second save that
        // inserted would leave every document picking whichever row came back
        // first, and the wrong company name on half of them.
        Assert.Single(rows);
        Assert.Equal("MEI Engineering", rows[0].Name);
        Assert.Equal("Thank you for your business.", rows[0].FooterNote);
    }

    [SkippableFact]
    public async Task A_save_from_a_page_opened_before_the_row_existed_still_updates_it()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        // Two people open the admin screen on a fresh install. Both are handed
        // the stand-in, which carries no row and so no Id.
        await using var db = NewContext();
        var service = new CompanyProfileService(db);

        var opened = (await service.GetAsync()).Clone();
        Assert.Equal(0, opened.Id);

        // The first of them saves, creating the row.
        var first = opened.Clone();
        first.Name = "MEI";
        await service.SaveAsync(first);

        // The second saves the copy they have been holding all along. It still
        // says Id 0, and the update must find the row anyway - copying that 0
        // onto the tracked entity would try to move a primary key, which EF
        // refuses outright and the screen reports as a crash.
        opened.Name = "MEI Engineering";
        await service.SaveAsync(opened);

        await using var fresh = NewContext();
        var rows = await fresh.CompanyProfiles.AsNoTracking().ToListAsync();

        Assert.Single(rows);
        Assert.Equal("MEI Engineering", rows[0].Name);
    }

    [SkippableFact]
    public async Task A_save_is_visible_to_the_next_read_rather_than_after_a_restart()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewContext();
        var service = new CompanyProfileService(db);

        var before = await service.GetAsync();
        Assert.NotEqual("MEI Engineering", before.Name);

        var edited = before.Clone();
        edited.Name = "MEI Engineering";
        await service.SaveAsync(edited);

        // The cache is dropped on save, so a logo or name change reaches the
        // next printed document immediately.
        await using var fresh = NewContext();
        var after = await new CompanyProfileService(fresh).GetAsync();
        Assert.Equal("MEI Engineering", after.Name);
    }

    [SkippableFact]
    public async Task Editing_a_clone_leaves_the_cached_profile_alone()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewContext();
        var service = new CompanyProfileService(db);

        var cached = await service.GetAsync();
        var original = cached.Name;

        // What an edit screen holds. If this were the cached instance itself,
        // typing in the form would rename the company for everyone signed in,
        // and abandoning the edit would not put it back.
        var editing = cached.Clone();
        editing.Name = "Typed but never saved";

        Assert.Equal(original, cached.Name);
        Assert.Equal(original, (await service.GetAsync()).Name);
    }
}
