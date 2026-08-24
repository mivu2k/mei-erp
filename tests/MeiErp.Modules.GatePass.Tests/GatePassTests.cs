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
            await db.EnsureAuditTableForTestsAsync();
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

    [SkippableFact]
    public async Task Legacy_carrier_and_reference_metadata_is_preserved()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb(Raiser);
        var input = SamplePass() with
        {
            PersonPhone = "0300-1234567", PersonCnic = "35202-1234567-1",
            CompanyName = "MEI Customer", Department = "Stores", Notes = "Handle carefully",
            ReferenceType = "RepairJob", ReferenceNumber = "JOB-104"
        };

        var saved = await NewService(db, Raiser).SaveAsync(input);
        db.ChangeTracker.Clear();
        var loaded = await NewService(db, Raiser).GetAsync(saved.Value.Id);

        Assert.Equal("35202-1234567-1", loaded!.PersonCnic);
        Assert.Equal("MEI Customer", loaded.CompanyName);
        Assert.Equal("JOB-104", loaded.ReferenceNumber);
        Assert.Equal("Handle carefully", loaded.Notes);
    }

    [SkippableFact]
    public async Task Cancellation_keeps_a_separate_reason_and_timestamp()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb(Raiser);
        var raised = await NewService(db, Raiser).SaveAsync(SamplePass());

        var result = await NewService(db, Raiser).CancelAsync(raised.Value.Id, "Wrong vehicle");
        var loaded = await NewService(db, Raiser).GetAsync(raised.Value.Id);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("Wrong vehicle", loaded!.CancellationReason);
        Assert.Equal(_clock.UtcNow, loaded.CancelledUtc);
        Assert.Equal("Demo", loaded.Purpose);
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
        Assert.Equal(_clock.UtcNow, final.Value.ReturnedUtc);
        Assert.Equal("Gate Security", final.Value.ReturnReceivedByName);
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

    private static DemoInput SampleDemo() => new(null, "Demo Customer", "0300", "C-1", "Sales", "REF-9",
        new DateOnly(2026, 8, 25), "Handle carefully",
        [new("Projector", "P-1", 1, "Cable", null), new("Screen", null, 1, null, null)]);

    [SkippableFact]
    public async Task Demo_issuance_gets_a_stable_number_and_issuer()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db=NewDb(Raiser);var result=await new DemoIssuanceService(db,Raiser,_clock).SaveAsync(SampleDemo());
        Assert.True(result.Ok,result.Error);Assert.Equal("DEMO-26-0001",result.Value.Number);Assert.Equal("Storekeeper",result.Value.IssuedByName);
    }

    [SkippableFact]
    public async Task Partial_demo_return_marks_only_selected_items_and_stays_open()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db=NewDb(Raiser);var service=new DemoIssuanceService(db,Raiser,_clock);var issued=await service.SaveAsync(SampleDemo());
        var selected=issued.Value.Items.First().Id;var result=await new DemoIssuanceService(db,Security,_clock).ReturnAsync(issued.Value.Id,[selected],"Good");
        Assert.True(result.Ok,result.Error);Assert.Equal(DemoStatus.PartiallyReturned,result.Value.Status);Assert.Single(result.Value.Items,x=>x.ReturnedUtc!=null);
    }

    [SkippableFact]
    public async Task Final_demo_item_closes_the_issuance_and_records_receiver()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db=NewDb(Raiser);var service=new DemoIssuanceService(db,Raiser,_clock);var issued=await service.SaveAsync(SampleDemo());
        await new DemoIssuanceService(db,Security,_clock).ReturnAsync(issued.Value.Id,[issued.Value.Items[0].Id],null);
        var final=await new DemoIssuanceService(db,Security,_clock).ReturnAsync(issued.Value.Id,[issued.Value.Items[1].Id],"Complete");
        Assert.Equal(DemoStatus.Returned,final.Value.Status);Assert.Equal("Gate Security",final.Value.ReceivedByName);Assert.Equal("Complete",final.Value.ReturnCondition);
    }

    [SkippableFact]
    public async Task Demo_with_return_activity_cannot_be_edited_or_cancelled()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db=NewDb(Raiser);var service=new DemoIssuanceService(db,Raiser,_clock);var issued=await service.SaveAsync(SampleDemo());
        await new DemoIssuanceService(db,Security,_clock).ReturnAsync(issued.Value.Id,[issued.Value.Items[0].Id],null);
        var edit=await service.SaveAsync(SampleDemo() with{Id=issued.Value.Id});var cancel=await service.CancelAsync(issued.Value.Id);
        Assert.Equal("demo.not-editable",edit.Code);Assert.Equal("demo.not-cancellable",cancel.Code);
    }

    [SkippableFact]
    public async Task Overdue_demo_filter_includes_partial_issuances_with_items_out()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db=NewDb(Raiser);var service=new DemoIssuanceService(db,Raiser,_clock);
        var issued=await service.SaveAsync(SampleDemo() with{ExpectedReturnOn=new DateOnly(2026,8,20)});
        await new DemoIssuanceService(db,Security,_clock).ReturnAsync(issued.Value.Id,[issued.Value.Items[0].Id],null);
        var rows=await service.ListAsync(new(OverdueOnly:true));Assert.Contains(rows,x=>x.Id==issued.Value.Id);
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
