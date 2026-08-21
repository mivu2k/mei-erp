using MeiErp.Modules.GatePass;
using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MeiErp.Modules.GatePass.Tests;

/// <summary>
/// The separation the module exists for: whoever raises a pass must not be the
/// one who clears it through the gate. Everything else here protects the
/// record of what actually left the premises.
/// </summary>
[Collection("postgres")]
public sealed class GatePassTests : IAsyncLifetime
{
    private static readonly string BaseConnection =
        Environment.GetEnvironmentVariable("MEIERP_TEST_DB")
        ?? "Host=127.0.0.1;Username=meierp;Password=DevPassword1!;";

    private readonly string _database = $"mei_gp_{Guid.NewGuid():N}";
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero));

    private bool _available;

    private string Connection => BaseConnection + $"Database={_database};";

    public async Task InitializeAsync()
    {
        try
        {
            await using (var admin = new DbContext(new DbContextOptionsBuilder()
                .UseNpgsql(BaseConnection + "Database=postgres;").Options))
            {
                await admin.Database.ExecuteSqlRawAsync($"CREATE DATABASE \"{_database}\";");
            }

            await using var db = NewDb(Raiser);
            await db.Database.EnsureCreatedAsync();
            _available = true;
        }
        catch (NpgsqlException) { _available = false; }
    }

    public async Task DisposeAsync()
    {
        if (!_available) return;
        try
        {
            await using var admin = new DbContext(new DbContextOptionsBuilder()
                .UseNpgsql(BaseConnection + "Database=postgres;").Options);
            await admin.Database.ExecuteSqlRawAsync($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE);");
        }
        catch { /* a stray throwaway database is harmless */ }
    }

    private static readonly TestUser Raiser = new("user-raiser", "Storekeeper");
    private static readonly TestUser Security = new("user-security", "Gate Security");

    private GatePassDbContext NewDb(ICurrentUser user) =>
        new(new DbContextOptionsBuilder<GatePassDbContext>().UseNpgsql(Connection).Options, user, _clock);

    private GatePassService NewService(GatePassDbContext db, ICurrentUser user) =>
        new(db, user, _clock);

    private static PassInput SamplePass(bool returnable = false, DateOnly? back = null) => new(
        null, PassDirection.Outward, new DateOnly(2026, 8, 21), "A Customer",
        returnable, back, "ABC-123", "A Driver", "Demo",
        [new PassItemInput("Laptop", 2, "each", null)]);

    // ---------- the separation ----------

    [SkippableFact]
    public async Task The_person_who_raised_a_pass_cannot_clear_it_through_the_gate()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb(Raiser);
        var raised = await NewService(db, Raiser).SaveAsync(SamplePass());
        Assert.True(raised.Ok, raised.Error);

        var cleared = await NewService(db, Raiser).ClearAsync(raised.Value.Id);

        // One person doing both is exactly how goods leave unnoticed.
        Assert.True(cleared.Failed);
        Assert.Equal("pass.self-clearance", cleared.Code);
    }

    [SkippableFact]
    public async Task Security_can_clear_a_pass_somebody_else_raised()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb(Raiser);
        var raised = await NewService(db, Raiser).SaveAsync(SamplePass());

        var cleared = await NewService(db, Security).ClearAsync(raised.Value.Id);

        Assert.True(cleared.Ok, cleared.Error);
        Assert.Equal(PassStatus.Cleared, cleared.Value.Status);
        Assert.Equal("Gate Security", cleared.Value.ClearedByName);
    }

    [SkippableFact]
    public async Task A_pass_cannot_be_cleared_twice()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb(Raiser);
        var raised = await NewService(db, Raiser).SaveAsync(SamplePass());
        await NewService(db, Security).ClearAsync(raised.Value.Id);

        var again = await NewService(db, Security).ClearAsync(raised.Value.Id);

        Assert.True(again.Failed);
        Assert.Equal("pass.already-cleared", again.Code);
    }

    // ---------- the record must match the paper ----------

    [SkippableFact]
    public async Task A_cleared_pass_cannot_be_edited()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb(Raiser);
        var raised = await NewService(db, Raiser).SaveAsync(SamplePass());
        await NewService(db, Security).ClearAsync(raised.Value.Id);

        var edit = await NewService(db, Raiser).SaveAsync(SamplePass() with
        {
            Id = raised.Value.Id,
            Items = [new PassItemInput("Laptop", 20, "each", null)]
        });

        // Security is holding a printed copy; if the record can still change,
        // the pass proves nothing about what actually went out.
        Assert.True(edit.Failed);
        Assert.Equal("pass.not-editable", edit.Code);
    }

    [SkippableFact]
    public async Task A_cleared_pass_cannot_be_cancelled()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb(Raiser);
        var raised = await NewService(db, Raiser).SaveAsync(SamplePass());
        await NewService(db, Security).ClearAsync(raised.Value.Id);

        var cancelled = await NewService(db, Raiser).CancelAsync(raised.Value.Id, "Changed my mind");

        // The goods have gone. Cancelling would erase the only record they left.
        Assert.True(cancelled.Failed);
        Assert.Equal("pass.already-cleared", cancelled.Code);
    }

    [SkippableFact]
    public async Task A_returnable_pass_without_a_return_date_is_refused()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb(Raiser);

        var result = await NewService(db, Raiser).SaveAsync(SamplePass(returnable: true, back: null));

        // Otherwise it is a pass nobody will ever chase.
        Assert.True(result.Failed);
        Assert.Equal("pass.no-return-date", result.Code);
    }

    // ---------- returns ----------

    [SkippableFact]
    public async Task A_partial_return_leaves_the_pass_open()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb(Raiser);
        var raised = await NewService(db, Raiser).SaveAsync(
            SamplePass(returnable: true, back: new DateOnly(2026, 8, 28)));
        await NewService(db, Security).ClearAsync(raised.Value.Id);

        var pass = await NewService(db, Security).GetAsync(raised.Value.Id);
        var itemId = pass!.Items.Single().Id;

        var returned = await NewService(db, Security)
            .ReceiveBackAsync(raised.Value.Id, [new ReturnLine(itemId, 1)]);

        Assert.True(returned.Ok, returned.Error);
        Assert.Equal(PassStatus.PartiallyReturned, returned.Value.Status);
        Assert.False(returned.Value.IsFullyReturned);
    }

    [SkippableFact]
    public async Task The_pass_closes_only_when_the_last_item_is_back()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb(Raiser);
        var raised = await NewService(db, Raiser).SaveAsync(
            SamplePass(returnable: true, back: new DateOnly(2026, 8, 28)));
        await NewService(db, Security).ClearAsync(raised.Value.Id);

        var pass = await NewService(db, Security).GetAsync(raised.Value.Id);
        var itemId = pass!.Items.Single().Id;

        await NewService(db, Security).ReceiveBackAsync(raised.Value.Id, [new ReturnLine(itemId, 1)]);
        var final = await NewService(db, Security).ReceiveBackAsync(raised.Value.Id, [new ReturnLine(itemId, 1)]);

        Assert.Equal(PassStatus.Returned, final.Value.Status);
        Assert.True(final.Value.IsFullyReturned);
    }

    [SkippableFact]
    public async Task More_cannot_come_back_than_went_out()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb(Raiser);
        var raised = await NewService(db, Raiser).SaveAsync(
            SamplePass(returnable: true, back: new DateOnly(2026, 8, 28)));
        await NewService(db, Security).ClearAsync(raised.Value.Id);

        var pass = await NewService(db, Security).GetAsync(raised.Value.Id);
        var itemId = pass!.Items.Single().Id;

        var result = await NewService(db, Security)
            .ReceiveBackAsync(raised.Value.Id, [new ReturnLine(itemId, 5)]);

        Assert.True(result.Failed);
        Assert.Equal("pass.over-return", result.Code);
    }

    [SkippableFact]
    public async Task Goods_cannot_be_returned_before_the_pass_has_left()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb(Raiser);
        var raised = await NewService(db, Raiser).SaveAsync(
            SamplePass(returnable: true, back: new DateOnly(2026, 8, 28)));

        var pass = await NewService(db, Raiser).GetAsync(raised.Value.Id);
        var itemId = pass!.Items.Single().Id;

        var result = await NewService(db, Security)
            .ReceiveBackAsync(raised.Value.Id, [new ReturnLine(itemId, 1)]);

        Assert.True(result.Failed);
        Assert.Equal("pass.not-cleared", result.Code);
    }

    // ---------- overdue ----------

    [SkippableFact]
    public async Task A_pass_past_its_date_with_goods_still_out_reads_as_overdue()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        var pass = new Pass
        {
            IsReturnable = true,
            ExpectedBack = new DateOnly(2026, 8, 20),
            Status = PassStatus.Cleared,
            Items = [new PassItem { Description = "Laptop", Quantity = 2, ReturnedQuantity = 1 }]
        };

        // Today is the 21st, the goods were due back on the 20th, one is still out.
        Assert.True(pass.IsOverdue(_clock.Today));
    }

    [SkippableFact]
    public async Task A_fully_returned_pass_is_never_overdue()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        var pass = new Pass
        {
            IsReturnable = true,
            ExpectedBack = new DateOnly(2026, 8, 1),
            Status = PassStatus.Returned,
            Items = [new PassItem { Description = "Laptop", Quantity = 2, ReturnedQuantity = 2 }]
        };

        Assert.False(pass.IsOverdue(_clock.Today));
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
