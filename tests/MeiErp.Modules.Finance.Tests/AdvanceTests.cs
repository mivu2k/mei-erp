using MeiErp.Modules.Finance;
using MeiErp.Platform.Kernel;
using MeiErp.Platform.Workflow;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MeiErp.Modules.Finance.Tests;

/// <summary>
/// The advance lifecycle, and the part that goes wrong quietly: the gap between
/// what somebody took and what they actually spent.
///
/// Twenty thousand taken, seventeen spent. The three thousand has to end up
/// somewhere, and every route it can take is pinned here — because an advance
/// that silently becomes salary is the failure nobody notices for a year.
/// </summary>
[Collection("postgres")]
public sealed class AdvanceTests : IAsyncLifetime
{
    private static readonly string BaseConnection =
        Environment.GetEnvironmentVariable("MEIERP_TEST_DB")
        ?? "Host=127.0.0.1;Username=meierp;Password=DevPassword1!;";

    private readonly string _database = $"mei_adv_{Guid.NewGuid():N}";
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero));
    private readonly TestUser _user = new("user-1", "Rafiq");

    private bool _available;
    private int _cash, _advanceHead, _directorCapital, _travel, _payables;

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

            await using var db = NewDb();
            await db.Database.EnsureCreatedAsync();
            await db.EnsureAuditTableForTestsAsync();

            var cash = new Account { Code = "1100", Name = "Cash", Type = AccountType.Asset, IsPostable = true };

            // Where money held by staff sits until it is accounted for.
            var advances = new Account { Code = "1700", Name = "Employee advances", Type = AccountType.Asset, IsPostable = true };
            var directorCapital = new Account { Code = "3210", Name = "Director capital", Type = AccountType.Equity, IsPostable = true };
            var travel = new Account { Code = "5220", Name = "Staff travel", Type = AccountType.Expense, IsPostable = true };

            // Where an overspend left outstanding is parked: the company owes
            // it back, so it belongs on a liability.
            var payables = new Account { Code = "2100", Name = "Payables", Type = AccountType.Liability, IsPostable = false };

            db.Accounts.AddRange(cash, advances, directorCapital, travel, payables);
            await db.SaveChangesAsync();

            _cash = cash.Id; _advanceHead = advances.Id; _directorCapital = directorCapital.Id;
            _travel = travel.Id; _payables = payables.Id;
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

    private FinanceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FinanceDbContext>().UseNpgsql(Connection).Options, _user, _clock);

    private AdvanceService NewService(FinanceDbContext db) =>
        new(db, new VoucherService(db, _clock, _user), new AutoApprove(), _user, _clock);

    /// <summary>Takes an advance all the way to disbursed, which most tests start from.</summary>
    private async Task<PaymentRequest> DisbursedAsync(
        AdvanceService service, FinanceDbContext db, decimal asked, decimal taken)
    {
        var draft = await service.SaveDraftAsync(new AdvanceInput(
            null, "Site visit", asked, _clock.Today, null, null, null));
        Assert.True(draft.Ok, draft.Error);

        await service.SubmitAsync(draft.Value.Id);

        // The fake engine approves immediately; the sink is what a real one
        // would call, so the status moves the same way.
        var live = await db.PaymentRequests.FirstAsync(a => a.Id == draft.Value.Id);
        live.Status = PaymentRequestStatus.Approved;
        await db.SaveChangesAsync();

        var paid = await service.DisburseAsync(draft.Value.Id, taken, _cash, _clock.Today);
        Assert.True(paid.Ok, paid.Error);

        return paid.Value;
    }

    // ---------- one record, two kinds ----------

    [SkippableFact]
    public async Task An_advance_is_a_payment_request_of_its_own_kind()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var draft = await service.SaveDraftAsync(new AdvanceInput(
            null, "Site visit", 5_000, _clock.Today, null, null, null));

        db.ChangeTracker.Clear();

        var stored = await db.PaymentRequests.FirstAsync(r => r.Id == draft.Value.Id);
        Assert.Equal(PaymentRequestKind.Advance, stored.Kind);
        Assert.Equal("Site visit", stored.Title);
    }

    [SkippableFact]
    public async Task The_advance_list_does_not_pick_up_itemized_claims()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        await service.SaveDraftAsync(new AdvanceInput(
            null, "Site visit", 5_000, _clock.Today, null, null, null));

        // Same table now, so the filter is the only thing keeping the two
        // screens from showing each other's documents.
        db.PaymentRequests.Add(new PaymentRequest
        {
            Reference = "PR-26-9001",
            Title = "Stationery claim",
            Kind = PaymentRequestKind.Itemized,
            Amount = 900,
            RequestedByUserId = "user-1",
            RequestedByName = "Rafiq",
            NeededBy = _clock.Today,
            Status = PaymentRequestStatus.Draft
        });
        await db.SaveChangesAsync();

        var advances = await service.ListAsync(null, mineOnly: false);

        Assert.Single(advances);
        Assert.All(advances, a => Assert.Equal(PaymentRequestKind.Advance, a.Kind));

        // ...and the same holds the other way round.
        var requests = await new PaymentRequestService(
            db, new VoucherService(db, _clock, _user), new AutoApprove(), _user, _clock)
            .ListAsync(null, mineOnly: false);

        Assert.Single(requests);
        Assert.All(requests, r => Assert.Equal(PaymentRequestKind.Itemized, r.Kind));
    }

    // ---------- per-person accounts ----------

    [SkippableFact]
    public async Task Each_person_gets_their_own_advance_account()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var advance = await DisbursedAsync(service, db, 20_000, 20_000);

        db.ChangeTracker.Clear();

        var reloaded = await db.PaymentRequests.FirstAsync(a => a.Id == advance.Id);
        Assert.NotNull(reloaded.AdvanceAccountId);
        Assert.NotEqual(_advanceHead, reloaded.AdvanceAccountId);

        var own = await db.Accounts.FirstAsync(a => a.Id == reloaded.AdvanceAccountId);
        Assert.Equal(_advanceHead, own.ParentId);
        Assert.Equal(_user.UserId, own.PersonId);

        // One shared head says the company is owed something and nothing about
        // by whom. This is what makes the trial balance answer it.
        var accounts = new AccountService(db);
        Assert.Equal(20_000, await accounts.BalanceAsync(own.Id));
        Assert.Equal(0, await accounts.BalanceAsync(_advanceHead));
        Assert.Equal(20_000, await accounts.BalanceWithChildrenAsync(_advanceHead));

        // The parent stops being postable once it has a child, or it would
        // double-count itself against them in every report.
        var parent = await db.Accounts.FirstAsync(a => a.Id == _advanceHead);
        Assert.False(parent.IsPostable);
    }

    [SkippableFact]
    public async Task A_second_advance_reuses_the_same_persons_account()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var first = await DisbursedAsync(service, db, 5_000, 5_000);
        var second = await DisbursedAsync(service, db, 3_000, 3_000);

        db.ChangeTracker.Clear();

        var a = await db.PaymentRequests.FirstAsync(x => x.Id == first.Id);
        var b = await db.PaymentRequests.FirstAsync(x => x.Id == second.Id);

        Assert.Equal(a.AdvanceAccountId, b.AdvanceAccountId);
        Assert.Equal(8_000, await new AccountService(db).BalanceAsync(a.AdvanceAccountId!.Value));
    }

    [SkippableFact]
    public async Task An_overspend_left_outstanding_becomes_a_payable_not_a_negative_asset()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var advance = await DisbursedAsync(service, db, 20_000, 20_000);
        await service.JustifyAsync(advance.Id,
            [new AdvanceExpenseInput(_clock.Today, "Fuel", 23_000, _travel, null)]);

        var settled = await service.SettleAsync(
            advance.Id, DifferenceHandling.Outstanding, _cash, _clock.Today);
        Assert.True(settled.Ok, settled.Error);

        db.ChangeTracker.Clear();
        var accounts = new AccountService(db);

        // They spent 3,000 of their own money. The company owes it back, so it
        // sits on a liability - as a negative on the advance asset head it would
        // read as though they were still holding company cash.
        Assert.Equal(0, await accounts.BalanceWithChildrenAsync(_advanceHead));
        Assert.Equal(-3_000, await accounts.BalanceWithChildrenAsync(_payables));

        var owed = await db.Accounts.FirstAsync(a => a.ParentId == _payables);
        Assert.Equal(_user.UserId, owed.PersonId);
        Assert.Contains(_user.Name, owed.Name);
    }

    [SkippableFact]
    public async Task Paying_off_an_outstanding_overspend_clears_the_payable()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var advance = await DisbursedAsync(service, db, 20_000, 20_000);
        await service.JustifyAsync(advance.Id,
            [new AdvanceExpenseInput(_clock.Today, "Fuel", 23_000, _travel, null)]);
        await service.SettleAsync(advance.Id, DifferenceHandling.Outstanding, _cash, _clock.Today);

        var cleared = await service.ClearDifferenceAsync(advance.Id, 3_000, _cash, _clock.Today);
        Assert.True(cleared.Ok, cleared.Error);

        db.ChangeTracker.Clear();
        var accounts = new AccountService(db);

        // Settled: nothing owed either way, and the cash actually went out.
        Assert.Equal(0, await accounts.BalanceWithChildrenAsync(_payables));
        Assert.Equal(-23_000, await accounts.BalanceAsync(_cash));
    }

    // ---------- disbursement ----------

    [SkippableFact]
    public async Task Director_funds_clear_against_director_capital_and_use_dfr_numbering()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);
        var draft = await service.SaveDraftAsync(new AdvanceInput(
            null, "Director travel", 5_000, _clock.Today, null, null, null,
            IsDirectorRequest: true));
        Assert.True(draft.Ok, draft.Error);
        Assert.StartsWith("DFR-", draft.Value.Reference);

        await service.SubmitAsync(draft.Value.Id);
        var live = await db.PaymentRequests.FirstAsync(a => a.Id == draft.Value.Id);
        live.Status = PaymentRequestStatus.Approved;
        await db.SaveChangesAsync();

        var paid = await service.DisburseAsync(draft.Value.Id, 5_000, _cash, _clock.Today);
        Assert.True(paid.Ok, paid.Error);
        var accounts = new AccountService(db);
        Assert.Equal(5_000, await accounts.BalanceWithChildrenAsync(_directorCapital));
        Assert.Equal(-5_000, await accounts.BalanceAsync(_cash));
        Assert.Equal(0, await accounts.BalanceWithChildrenAsync(_advanceHead));
    }

    [SkippableFact]
    public async Task Paying_out_moves_money_without_making_it_an_expense_yet()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        await DisbursedAsync(service, db, asked: 20_000, taken: 20_000);

        db.ChangeTracker.Clear();
        var accounts = new AccountService(db);

        // The money has left the business but is still theirs to account for,
        // so it sits on the advance account rather than on an expense head.
        Assert.Equal(20_000, await accounts.BalanceWithChildrenAsync(_advanceHead));
        Assert.Equal(-20_000, await accounts.BalanceAsync(_cash));
        Assert.Equal(0, await accounts.BalanceAsync(_travel));
    }

    [SkippableFact]
    public async Task More_cannot_be_paid_out_than_was_approved()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var draft = await service.SaveDraftAsync(new AdvanceInput(
            null, "Site visit", 20_000, _clock.Today, null, null, null));
        await service.SubmitAsync(draft.Value.Id);

        var live = await db.PaymentRequests.FirstAsync(a => a.Id == draft.Value.Id);
        live.Status = PaymentRequestStatus.Approved;
        await db.SaveChangesAsync();

        var result = await service.DisburseAsync(draft.Value.Id, 30_000, _cash, _clock.Today);

        // Handing over more than was signed off defeats the approval entirely.
        Assert.True(result.Failed);
        Assert.Equal("advance.over-disbursement", result.Code);
    }

    [SkippableFact]
    public async Task An_unapproved_advance_cannot_be_paid_out()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var draft = await service.SaveDraftAsync(new AdvanceInput(
            null, "Site visit", 20_000, _clock.Today, null, null, null));

        var result = await service.DisburseAsync(draft.Value.Id, 20_000, _cash, _clock.Today);

        Assert.True(result.Failed);
        Assert.Equal("advance.not-approved", result.Code);
    }

    // ---------- the difference ----------

    [SkippableFact]
    public async Task Settling_charges_the_receipts_to_their_own_heads()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var advance = await DisbursedAsync(service, db, 20_000, 20_000);

        await service.JustifyAsync(advance.Id,
            [new AdvanceExpenseInput(_clock.Today, "Fuel and tolls", 17_000, _travel, "R-1")]);

        var settled = await service.SettleAsync(
            advance.Id, DifferenceHandling.SettleNow, _cash, _clock.Today);

        Assert.True(settled.Ok, settled.Error);

        db.ChangeTracker.Clear();
        var accounts = new AccountService(db);

        // This is the moment the money actually becomes an expense.
        Assert.Equal(17_000, await accounts.BalanceAsync(_travel));
    }

    [SkippableFact]
    public async Task Settling_now_puts_the_unspent_money_back_through_cash()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var advance = await DisbursedAsync(service, db, 20_000, 20_000);
        await service.JustifyAsync(advance.Id,
            [new AdvanceExpenseInput(_clock.Today, "Fuel", 17_000, _travel, null)]);

        await service.SettleAsync(advance.Id, DifferenceHandling.SettleNow, _cash, _clock.Today);

        db.ChangeTracker.Clear();
        var accounts = new AccountService(db);

        // 20,000 out, 3,000 handed back: cash is down by the 17,000 actually
        // spent, and the person owes nothing.
        Assert.Equal(-17_000, await accounts.BalanceAsync(_cash));
        Assert.Equal(0, await accounts.BalanceWithChildrenAsync(_advanceHead));

        var closed = await db.PaymentRequests.FirstAsync(a => a.Id == advance.Id);
        Assert.Equal(0, closed.OutstandingDifference);
    }

    [SkippableFact]
    public async Task Leaving_it_outstanding_keeps_the_money_on_their_account()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var advance = await DisbursedAsync(service, db, 20_000, 20_000);
        await service.JustifyAsync(advance.Id,
            [new AdvanceExpenseInput(_clock.Today, "Fuel", 17_000, _travel, null)]);

        await service.SettleAsync(advance.Id, DifferenceHandling.Outstanding, _cash, _clock.Today);

        db.ChangeTracker.Clear();
        var accounts = new AccountService(db);

        // The books keep showing they are holding 3,000. Cash has not moved
        // again, because the money never came back.
        Assert.Equal(3_000, await accounts.BalanceWithChildrenAsync(_advanceHead));
        Assert.Equal(-20_000, await accounts.BalanceAsync(_cash));

        var closed = await db.PaymentRequests.FirstAsync(a => a.Id == advance.Id);
        Assert.Equal(3_000, closed.OutstandingDifference);
    }

    [SkippableFact]
    public async Task Recovering_from_payroll_also_leaves_it_on_their_account()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var advance = await DisbursedAsync(service, db, 20_000, 20_000);
        await service.JustifyAsync(advance.Id,
            [new AdvanceExpenseInput(_clock.Today, "Fuel", 17_000, _travel, null)]);

        await service.SettleAsync(advance.Id, DifferenceHandling.RecoverFromPayroll, _cash, _clock.Today);

        db.ChangeTracker.Clear();

        // Payroll will credit this account when it deducts. Posting anything
        // here as well would take the money twice - the classic double-posting
        // bug in advance handling.
        Assert.Equal(3_000, await new AccountService(db).BalanceWithChildrenAsync(_advanceHead));

        var closed = await db.PaymentRequests.FirstAsync(a => a.Id == advance.Id);
        Assert.Equal(DifferenceHandling.RecoverFromPayroll, closed.DifferenceHandling);
        Assert.Equal(3_000, closed.OutstandingDifference);
    }

    [SkippableFact]
    public async Task Spending_more_than_was_taken_is_paid_back_to_them()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var advance = await DisbursedAsync(service, db, 20_000, 20_000);

        // They spent 23,000 of their own money against a 20,000 advance.
        await service.JustifyAsync(advance.Id,
            [new AdvanceExpenseInput(_clock.Today, "Fuel and repairs", 23_000, _travel, null)]);

        var settled = await service.SettleAsync(
            advance.Id, DifferenceHandling.SettleNow, _cash, _clock.Today);

        Assert.True(settled.Ok, settled.Error);

        db.ChangeTracker.Clear();
        var accounts = new AccountService(db);

        // Cash is down by the full 23,000, and their account is square.
        Assert.Equal(-23_000, await accounts.BalanceAsync(_cash));
        Assert.Equal(0, await accounts.BalanceWithChildrenAsync(_advanceHead));
        Assert.Equal(23_000, await accounts.BalanceAsync(_travel));
    }

    [SkippableFact]
    public async Task An_exact_accounting_leaves_nothing_outstanding()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var advance = await DisbursedAsync(service, db, 20_000, 20_000);
        await service.JustifyAsync(advance.Id,
            [new AdvanceExpenseInput(_clock.Today, "Fuel", 20_000, _travel, null)]);

        await service.SettleAsync(advance.Id, DifferenceHandling.SettleNow, _cash, _clock.Today);

        db.ChangeTracker.Clear();
        var closed = await db.PaymentRequests.FirstAsync(a => a.Id == advance.Id);

        Assert.Equal(0, closed.Difference);
        Assert.Equal(0, await new AccountService(db).BalanceWithChildrenAsync(_advanceHead));
    }

    // ---------- clearing later ----------

    [SkippableFact]
    public async Task An_outstanding_amount_can_be_handed_back_later()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var advance = await DisbursedAsync(service, db, 20_000, 20_000);
        await service.JustifyAsync(advance.Id,
            [new AdvanceExpenseInput(_clock.Today, "Fuel", 17_000, _travel, null)]);
        await service.SettleAsync(advance.Id, DifferenceHandling.Outstanding, _cash, _clock.Today);

        var cleared = await service.ClearDifferenceAsync(
            advance.Id, 3_000, _cash, _clock.Today.AddDays(7));

        Assert.True(cleared.Ok, cleared.Error);

        db.ChangeTracker.Clear();
        var accounts = new AccountService(db);

        Assert.Equal(0, await accounts.BalanceWithChildrenAsync(_advanceHead));
        Assert.Equal(-17_000, await accounts.BalanceAsync(_cash));

        var closed = await db.PaymentRequests.FirstAsync(a => a.Id == advance.Id);
        Assert.Equal(0, closed.OutstandingDifference);
    }

    [SkippableFact]
    public async Task Part_of_an_outstanding_amount_can_be_handed_back()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var advance = await DisbursedAsync(service, db, 20_000, 20_000);
        await service.JustifyAsync(advance.Id,
            [new AdvanceExpenseInput(_clock.Today, "Fuel", 17_000, _travel, null)]);
        await service.SettleAsync(advance.Id, DifferenceHandling.Outstanding, _cash, _clock.Today);

        await service.ClearDifferenceAsync(advance.Id, 1_000, _cash, _clock.Today.AddDays(7));

        db.ChangeTracker.Clear();
        var closed = await db.PaymentRequests.FirstAsync(a => a.Id == advance.Id);

        Assert.Equal(2_000, closed.OutstandingDifference);
        Assert.Equal(2_000, await new AccountService(db).BalanceWithChildrenAsync(_advanceHead));
    }

    [SkippableFact]
    public async Task More_cannot_be_handed_back_than_is_outstanding()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var advance = await DisbursedAsync(service, db, 20_000, 20_000);
        await service.JustifyAsync(advance.Id,
            [new AdvanceExpenseInput(_clock.Today, "Fuel", 17_000, _travel, null)]);
        await service.SettleAsync(advance.Id, DifferenceHandling.Outstanding, _cash, _clock.Today);

        var result = await service.ClearDifferenceAsync(advance.Id, 5_000, _cash, _clock.Today);

        // Otherwise the advance account goes the wrong way and the person ends
        // up appearing to be owed money they never lent.
        Assert.True(result.Failed);
        Assert.Equal("advance.over-clearing", result.Code);
    }

    // ---------- guards ----------

    [SkippableFact]
    public async Task Receipts_cannot_be_entered_before_the_money_is_handed_over()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var draft = await service.SaveDraftAsync(new AdvanceInput(
            null, "Site visit", 20_000, _clock.Today, null, null, null));

        var result = await service.JustifyAsync(draft.Value.Id,
            [new AdvanceExpenseInput(_clock.Today, "Fuel", 5_000, _travel, null)]);

        Assert.True(result.Failed);
        Assert.Equal("advance.not-disbursed", result.Code);
    }

    [SkippableFact]
    public async Task An_advance_cannot_be_settled_before_it_is_accounted_for()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var advance = await DisbursedAsync(service, db, 20_000, 20_000);

        var result = await service.SettleAsync(
            advance.Id, DifferenceHandling.SettleNow, _cash, _clock.Today);

        Assert.True(result.Failed);
        Assert.Equal("advance.not-justified", result.Code);
    }

    [SkippableFact]
    public async Task A_receipt_without_a_head_is_refused()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var advance = await DisbursedAsync(service, db, 20_000, 20_000);

        // Without a head there is nowhere to charge it at settlement, and the
        // voucher would not balance.
        var result = await service.JustifyAsync(advance.Id,
            [new AdvanceExpenseInput(_clock.Today, "Fuel", 5_000, null, null)]);

        Assert.True(result.Failed);
        Assert.Equal("advance.no-head", result.Code);
    }

    [SkippableFact]
    public async Task A_submitted_advance_cannot_be_edited()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");

        await using var db = NewDb();
        var service = NewService(db);

        var draft = await service.SaveDraftAsync(new AdvanceInput(
            null, "Site visit", 20_000, _clock.Today, null, null, null));
        await service.SubmitAsync(draft.Value.Id);

        db.ChangeTracker.Clear();

        // Changing the amount under an approver is how someone signs off one
        // figure and authorises another.
        var edit = await service.SaveDraftAsync(new AdvanceInput(
            draft.Value.Id, "Site visit", 90_000, _clock.Today, null, null, null));

        Assert.True(edit.Failed);
        Assert.Equal("advance.not-editable", edit.Code);
    }

    /// <summary>Approval routing is tested against the engine itself; this only needs to say yes.</summary>
    private sealed class AutoApprove : IApprovalEngine
    {
        private int _next = 1;

        public Task<Result<ApprovalRequest>> SubmitAsync(
            SubmitApproval request, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(new ApprovalRequest
            {
                Id = _next++,
                DocumentType = request.DocumentType,
                DocumentId = request.DocumentId,
                Status = ApprovalStatus.Pending
            }));

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

        public Task<ApprovalHistory?> HistoryAsync(
            string documentType, int documentId, CancellationToken ct = default) =>
            Task.FromResult<ApprovalHistory?>(null);
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
