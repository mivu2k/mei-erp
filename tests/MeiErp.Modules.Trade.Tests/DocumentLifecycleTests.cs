using MeiErp.Platform.Kernel;
using MeiErp.Platform.Workflow;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MeiErp.Modules.Trade.Tests;

/// <summary>
/// The draft/submit line, in both directions.
///
/// The rule worth defending: a draft is a working note that binds nobody, and
/// anything past it is a document somebody has been shown. Letting an issued
/// quotation be edited is how a business ends up honouring a figure nobody
/// agreed to.
/// </summary>
[Collection("postgres")]
public sealed class DocumentLifecycleTests : IAsyncLifetime
{
    private static readonly string BaseConnection =
        Environment.GetEnvironmentVariable("MEIERP_TEST_DB")
        ?? "Host=127.0.0.1;Username=meierp;Password=DevPassword1!;";

    private readonly string _database = $"mei_trade_{Guid.NewGuid():N}";
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 8, 23, 9, 0, 0, TimeSpan.Zero));
    private readonly SystemUser _user = new("Trade Tester");

    private bool _available;
    private int _customerId, _supplierId;

    private string Connection => BaseConnection + $"Database={_database};";

    private TradeDbContext NewDb() =>
        new(new DbContextOptionsBuilder<TradeDbContext>().UseNpgsql(Connection).Options, _user, _clock);

    private TradeDocumentService NewService(TradeDbContext db) =>
        new(db, new StubApprovals(), _clock);

    public async Task InitializeAsync()
    {
        try
        {
            await using (var admin = new DbContext(new DbContextOptionsBuilder()
                .UseNpgsql(BaseConnection + "Database=postgres;").Options))
            {
                await admin.Database.ExecuteSqlRawAsync($"CREATE DATABASE \"{_database}\";");
            }

            await using var db = NewDb();
            await db.Database.EnsureCreatedAsync();
            await db.EnsureAuditTableForTestsAsync();

            var customer = new Party { Code = "CUST", Name = "A Customer", IsCustomer = true, PaymentTermDays = 30 };
            var supplier = new Party { Code = "SUPP", Name = "A Supplier", IsSupplier = true };
            db.AddRange(customer, supplier);
            await db.SaveChangesAsync();

            _customerId = customer.Id;
            _supplierId = supplier.Id;
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

    private QuotationInput Quote(TradeDirection direction, int? id = null, decimal price = 100) =>
        new(id, direction, direction == TradeDirection.Sales ? _customerId : _supplierId, 1,
            _clock.Today, _clock.Today.AddDays(30), 0, 0, null, null, null, null,
            [new DocumentLineInput(null, null, "Labour", 2, price)]);

    [SkippableTheory]
    [InlineData(TradeDirection.Sales, "SQ-26-0001")]
    [InlineData(TradeDirection.Purchase, "PQ-26-0001")]
    public async Task A_quotation_starts_as_an_editable_draft_numbered_for_its_side(
        TradeDirection direction, string expectedNumber)
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();

        var saved = await NewService(db).SaveQuotationAsync(Quote(direction));

        Assert.True(saved.Ok, saved.Error);
        Assert.Equal(expectedNumber, saved.Value.Number);
        Assert.Equal(DocumentStatus.Draft, saved.Value.Status);
        Assert.True(saved.Value.Status.IsEditable());
        Assert.Equal(200, saved.Value.Total);       // 2 x 100, no tax or discount
    }

    [SkippableFact]
    public async Task Totals_apply_the_discount_before_the_tax()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();

        var saved = await NewService(db).SaveQuotationAsync(
            new QuotationInput(null, TradeDirection.Sales, _customerId, 1,
                _clock.Today, null, TaxPercent: 10, Discount: 100, null, null, null, null,
                [new DocumentLineInput(null, null, "Work", 1, 1000)]));

        Assert.True(saved.Ok, saved.Error);

        // Tax on 900, not on 1000: discounting after tax would overcharge.
        Assert.Equal(900, saved.Value.Taxable);
        Assert.Equal(90, saved.Value.Tax);
        Assert.Equal(990, saved.Value.Total);
    }

    [SkippableFact]
    public async Task A_submitted_quotation_can_no_longer_be_edited()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        var service = NewService(db);

        var draft = await service.SaveQuotationAsync(Quote(TradeDirection.Sales));
        var submitted = await service.SubmitQuotationAsync(draft.Value.Id);

        Assert.True(submitted.Ok, submitted.Error);
        Assert.Equal(DocumentStatus.PendingApproval, submitted.Value.Status);

        // Editing under an approver would mean they signed off one figure and
        // the customer received another.
        var edit = await service.SaveQuotationAsync(Quote(TradeDirection.Sales, draft.Value.Id, price: 5));
        Assert.True(edit.Failed);
        Assert.Equal("quotation.not-editable", edit.Code);

        var delete = await service.DeleteQuotationDraftAsync(draft.Value.Id);
        Assert.True(delete.Failed);
        Assert.Equal("quotation.not-draft", delete.Code);
    }

    [SkippableFact]
    public async Task A_returned_quotation_becomes_editable_again()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        var service = NewService(db);

        var draft = await service.SaveQuotationAsync(Quote(TradeDirection.Sales));
        await service.SubmitQuotationAsync(draft.Value.Id);

        // Handed back for correction, rather than rejected outright.
        await new QuotationApprovalSink(db).OnSettledAsync(
            draft.Value.Id, ApprovalStatus.Returned, new ApprovalRequest());
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var edit = await service.SaveQuotationAsync(Quote(TradeDirection.Sales, draft.Value.Id, price: 250));
        Assert.True(edit.Ok, edit.Error);
        Assert.Equal(500, edit.Value.Total);
    }

    [SkippableFact]
    public async Task An_outcome_can_only_be_recorded_once_the_quotation_is_approved()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        var service = NewService(db);

        var draft = await service.SaveQuotationAsync(Quote(TradeDirection.Sales));

        // Nobody outside has seen a draft, so it cannot have been accepted.
        var tooEarly = await service.SetQuotationOutcomeAsync(draft.Value.Id, true, null);
        Assert.True(tooEarly.Failed);
        Assert.Equal("quotation.not-approved", tooEarly.Code);

        await service.SubmitQuotationAsync(draft.Value.Id);
        await new QuotationApprovalSink(db).OnSettledAsync(
            draft.Value.Id, ApprovalStatus.Approved, new ApprovalRequest());
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var accepted = await service.SetQuotationOutcomeAsync(draft.Value.Id, true, "Signed off");
        Assert.True(accepted.Ok, accepted.Error);
        Assert.Equal(DocumentStatus.Accepted, accepted.Value.Status);
    }

    [SkippableFact]
    public async Task A_document_is_refused_against_a_party_on_the_wrong_side()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        var service = NewService(db);

        // The one master holds both sides, so the side has to be checked.
        var wrong = await service.SaveQuotationAsync(
            Quote(TradeDirection.Sales) with { PartyId = _supplierId });

        Assert.True(wrong.Failed);
        Assert.Equal("document.not-customer", wrong.Code);
    }

    [SkippableFact]
    public async Task An_invoice_takes_its_due_date_from_the_partys_terms()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();

        var saved = await NewService(db).SaveInvoiceAsync(new InvoiceInput(
            null, TradeDirection.Sales, _customerId, 1, _clock.Today,
            DueDate: null, 0, 0, null, null, null, null,
            [new DocumentLineInput(null, null, "Work", 1, 500)]));

        Assert.True(saved.Ok, saved.Error);

        // Net 30 on the customer, so nobody has to remember to type it.
        Assert.Equal(_clock.Today.AddDays(30), saved.Value.DueDate);
        Assert.Equal(500, saved.Value.Balance);
        Assert.False(saved.Value.IsSettled);
    }

    [SkippableFact]
    public async Task Approving_an_invoice_is_what_posts_it_and_freezes_it()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        var service = NewService(db);

        var draft = await service.SaveInvoiceAsync(new InvoiceInput(
            null, TradeDirection.Purchase, _supplierId, 1, _clock.Today, null, 0, 0,
            "THEIR-1", null, null, null,
            [new DocumentLineInput(null, null, "Parts", 1, 250)]));

        Assert.Equal(DocumentStatus.Draft, draft.Value.Status);

        var posted = await service.PostInvoiceAsync(draft.Value.Id);
        Assert.True(posted.Ok, posted.Error);
        Assert.Equal(DocumentStatus.PendingApproval, posted.Value.Status);

        await new InvoiceApprovalSink(db).OnSettledAsync(
            draft.Value.Id, ApprovalStatus.Approved, new ApprovalRequest());
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await service.InvoiceAsync(draft.Value.Id);

        // Approval is the moment it enters the books, so it is terminal.
        Assert.Equal(DocumentStatus.Posted, reloaded!.Status);
        Assert.False(reloaded.Status.IsEditable());
        Assert.True(reloaded.Status.IsClosed());
    }

    [SkippableFact]
    public async Task An_overdue_invoice_is_only_overdue_once_posted()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();

        var draft = await NewService(db).SaveInvoiceAsync(new InvoiceInput(
            null, TradeDirection.Sales, _customerId, 1, _clock.Today.AddDays(-90),
            DueDate: _clock.Today.AddDays(-60), 0, 0, null, null, null, null,
            [new DocumentLineInput(null, null, "Work", 1, 100)]));

        // Long past its due date, but still a draft - it owes nobody anything.
        Assert.False(draft.Value.IsOverdueOn(_clock.Today));

        draft.Value.Status = DocumentStatus.Posted;
        Assert.True(draft.Value.IsOverdueOn(_clock.Today));
    }

    /// <summary>Approval routing is tested against the engine itself; this only needs to hand back an id.</summary>
    private sealed class StubApprovals : IApprovalEngine
    {
        public Task<Result<ApprovalRequest>> SubmitAsync(
            SubmitApproval request, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(new ApprovalRequest { Id = 1 }));

        public Task<Result<ApprovalRequest>> DecideAsync(
            int requestId, ApprovalDecision decision, string? comment, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Result> CancelAsync(int requestId, string? reason, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());

        public Task<Result<ApprovalRequest>> ResubmitAsync(int requestId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ApprovalInboxItem>> InboxAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ApprovalInboxItem>>([]);

        public Task<Result> CanDecideAsync(int requestId, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());


        public Task<IReadOnlyDictionary<int, ApprovalPosition>> PositionsAsync(
            string documentType, IReadOnlyList<int> documentIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<int, ApprovalPosition>>(
                new Dictionary<int, ApprovalPosition>());

        public Task<ApprovalHistory?> HistoryAsync(
            string documentType, int documentId, CancellationToken ct = default) =>
            Task.FromResult<ApprovalHistory?>(null);
    }
}
