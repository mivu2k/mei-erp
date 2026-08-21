using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Finance;

/// <summary>
/// Somebody money is owed to or owed by, outside the formal customer and
/// supplier ledgers.
///
/// A party is just a name and a side. The side is the only thing it decides:
/// whether the party's own account hangs under Receivables or Payables. Money
/// is never written against a party directly - it is posted as a real voucher,
/// which is what keeps the books balanced.
/// </summary>
public class ThirdParty : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    public string Name { get; set; } = "";

    public ThirdPartySide Side { get; set; }

    public string? Phone { get; set; }
    public string? Cnic { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// The account created for this party under Receivables or Payables.
    /// Every posting for them lands here, which is what makes a statement
    /// possible at all.
    /// </summary>
    public int AccountId { get; set; }
    public Account? Account { get; set; }

    public bool IsActive { get; set; } = true;
}

public enum ThirdPartySide
{
    /// <summary>They owe us. Sits under Receivables.</summary>
    Receivable = 0,

    /// <summary>We owe them. Sits under Payables.</summary>
    Payable = 1
}

/// <param name="ContraCode">
/// Which cash or bank head the money moved through. The party's own account
/// name is identical on every row of their statement and tells the reader
/// nothing; where it came from or went is the useful column.
/// </param>
public sealed record PartyStatementRow(
    DateOnly Date, string VoucherNumber, string Narration,
    string? ContraCode, string? ContraName,
    decimal Received, decimal Paid, decimal Balance);

public sealed record PartyStatement(
    int PartyId, string Name, ThirdPartySide Side,
    DateOnly From, DateOnly To,
    decimal Opening, IReadOnlyList<PartyStatementRow> Rows, decimal Closing);

public interface IThirdPartyService
{
    Task<IReadOnlyList<ThirdParty>> ListAsync(bool includeInactive, CancellationToken ct = default);
    Task<ThirdParty?> GetAsync(int id, CancellationToken ct = default);
    Task<Result<ThirdParty>> SaveAsync(ThirdParty party, CancellationToken ct = default);

    /// <summary>What the party currently owes, or is owed, signed for their side.</summary>
    Task<decimal> BalanceAsync(int partyId, CancellationToken ct = default);

    /// <summary>
    /// Records money moving to or from a party by posting a real voucher.
    /// Nothing here writes a balance directly.
    /// </summary>
    Task<Result<Voucher>> RecordAsync(
        int partyId, PartyMovement direction, decimal amount,
        int cashAccountId, DateOnly date, string? narration, CancellationToken ct = default);

    Task<PartyStatement?> StatementAsync(
        int partyId, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// Which cash head this party was last settled through, so the payment
    /// dialog can default to it. Null for a split entry, since there is no
    /// single head to reuse.
    /// </summary>
    Task<int?> LastCashHeadAsync(int partyId, CancellationToken ct = default);
}

public enum PartyMovement
{
    /// <summary>Money came in from them.</summary>
    Received = 0,

    /// <summary>Money went out to them.</summary>
    Paid = 1
}

public sealed class ThirdPartyService(
    FinanceDbContext db, IVoucherService vouchers, IAccountService accounts) : IThirdPartyService
{
    /// <summary>Receivables and Payables — where a party's own account is hung.</summary>
    private const string ReceivablesCode = "1600";
    private const string PayablesCode = "2100";

    public async Task<IReadOnlyList<ThirdParty>> ListAsync(
        bool includeInactive, CancellationToken ct = default)
    {
        var query = db.ThirdParties.AsNoTracking().Include(p => p.Account).AsQueryable();
        if (!includeInactive) query = query.Where(p => p.IsActive);
        return await query.OrderBy(p => p.Name).ToListAsync(ct);
    }

    public Task<ThirdParty?> GetAsync(int id, CancellationToken ct = default) =>
        db.ThirdParties.Include(p => p.Account).FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Result<ThirdParty>> SaveAsync(
        ThirdParty party, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(party.Name))
            return Result.Fail<ThirdParty>("A party needs a name.", "party.no-name");

        if (party.Id == 0)
        {
            var account = await CreateAccountForAsync(party, ct);
            if (account.Failed) return Result.Fail<ThirdParty>(account.Error!, account.Code);

            party.AccountId = account.Value.Id;
            db.ThirdParties.Add(party);
        }
        else
        {
            var existing = await db.ThirdParties.FirstOrDefaultAsync(p => p.Id == party.Id, ct);
            if (existing is null) return Result.Fail<ThirdParty>("That party no longer exists.", "party.not-found");

            // Read the previous side from the change tracker, not from
            // `existing`: an edit screen hands back the very instance it loaded,
            // so `existing` and `party` are usually the same tracked object and
            // comparing them to each other is always false.
            var previousSide = db.Entry(existing).OriginalValues
                .GetValue<ThirdPartySide>(nameof(ThirdParty.Side));

            if (previousSide != party.Side)
            {
                var used = await db.VoucherLines.AnyAsync(l => l.AccountId == existing.AccountId, ct);

                if (used)
                {
                    // Moving the account between Receivables and Payables after
                    // it has entries would silently restate every past balance
                    // sheet this party appeared on.
                    return Result.Fail<ThirdParty>(
                        "This party already has entries, so their side cannot be changed. " +
                        "Settle the balance and create a new party if the relationship has reversed.",
                        "party.side-locked");
                }

                var moved = await MoveAccountAsync(existing, party.Side, ct);
                if (moved.Failed) return Result.Fail<ThirdParty>(moved.Error!, moved.Code);
            }

            var accountId = db.Entry(existing).OriginalValues.GetValue<int>(nameof(ThirdParty.AccountId));
            db.Entry(existing).CurrentValues.SetValues(party);
            existing.AccountId = accountId;

            // Keep the account's name in step with a rename, so the ledger and
            // the party list do not drift apart.
            var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
            if (account is not null) account.Name = party.Name;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(party);
    }

    private async Task<Result<Account>> CreateAccountForAsync(ThirdParty party, CancellationToken ct)
    {
        var parentCode = party.Side is ThirdPartySide.Receivable ? ReceivablesCode : PayablesCode;

        var parent = await db.Accounts.FirstOrDefaultAsync(a => a.Code == parentCode, ct);
        if (parent is null)
        {
            return Result.Fail<Account>(
                $"The {parentCode} heading is missing from the chart of accounts, " +
                "so there is nowhere to hang this party.",
                "party.no-parent");
        }

        // Codes run beneath the parent: 1600-001, 2100-014.
        var siblings = await db.Accounts.CountAsync(a => a.ParentId == parent.Id, ct);
        var code = $"{parentCode}-{siblings + 1:D3}";

        var account = new Account
        {
            Code = code,
            Name = party.Name,
            Type = party.Side is ThirdPartySide.Receivable ? AccountType.Asset : AccountType.Liability,
            ParentId = parent.Id,
            IsPostable = true,

            // Created by the system and pointed at by a party record, so it
            // cannot be deleted out from under one.
            IsSystem = true,
            IsActive = true
        };

        db.Accounts.Add(account);

        // A parent holding children must not also hold a balance of its own.
        if (parent.IsPostable)
        {
            var parentUsed = await db.VoucherLines.AnyAsync(l => l.AccountId == parent.Id, ct);
            if (!parentUsed) parent.IsPostable = false;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(account);
    }

    private async Task<Result> MoveAccountAsync(
        ThirdParty party, ThirdPartySide side, CancellationToken ct)
    {
        var parentCode = side is ThirdPartySide.Receivable ? ReceivablesCode : PayablesCode;
        var parent = await db.Accounts.FirstOrDefaultAsync(a => a.Code == parentCode, ct);
        if (parent is null) return Result.Fail($"The {parentCode} heading is missing.", "party.no-parent");

        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == party.AccountId, ct);
        if (account is null) return Result.Fail("This party's account has gone.", "party.no-account");

        account.ParentId = parent.Id;
        account.Type = side is ThirdPartySide.Receivable ? AccountType.Asset : AccountType.Liability;

        return Result.Success();
    }

    public async Task<decimal> BalanceAsync(int partyId, CancellationToken ct = default)
    {
        var party = await db.ThirdParties.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == partyId, ct);

        if (party is null) return 0;

        var signed = await accounts.BalanceAsync(party.AccountId, null, ct);

        // Signed per side, so it reads as what is still outstanding either way:
        // a receivable is positive when they owe us, a payable positive when we
        // owe them.
        return party.Side is ThirdPartySide.Receivable ? signed : -signed;
    }

    public async Task<Result<Voucher>> RecordAsync(
        int partyId, PartyMovement direction, decimal amount,
        int cashAccountId, DateOnly date, string? narration, CancellationToken ct = default)
    {
        if (amount <= 0)
            return Result.Fail<Voucher>("The amount must be more than nothing.", "party.bad-amount");

        var party = await db.ThirdParties.FirstOrDefaultAsync(p => p.Id == partyId, ct);
        if (party is null) return Result.Fail<Voucher>("That party no longer exists.", "party.not-found");

        var cash = await db.Accounts.FirstOrDefaultAsync(a => a.Id == cashAccountId, ct);
        if (cash is null) return Result.Fail<Voucher>("That cash or bank head no longer exists.", "party.no-cash-head");

        // Money in: cash goes up, the party's account goes down.
        // Money out: the reverse. Posted as a balanced voucher like everything
        // else - nothing writes a party balance directly.
        var lines = direction is PartyMovement.Received
            ? new[]
              {
                  new VoucherLineInput(cash.Id, amount, 0, narration),
                  new VoucherLineInput(party.AccountId, 0, amount, narration, null, party.Name)
              }
            : new[]
              {
                  new VoucherLineInput(party.AccountId, amount, 0, narration, null, party.Name),
                  new VoucherLineInput(cash.Id, 0, amount, narration)
              };

        return await vouchers.PostSystemVoucherAsync(new SystemVoucher(
            Type: direction is PartyMovement.Received ? VoucherType.Receipt : VoucherType.Payment,
            Date: date,
            Narration: narration ?? $"{(direction is PartyMovement.Received ? "Received from" : "Paid to")} {party.Name}",
            Lines: lines,
            Module: FinanceModule.Key,
            DocumentType: "finance.third-party",
            DocumentId: party.Id,
            DocumentReference: party.Name), ct);
    }

    public async Task<PartyStatement?> StatementAsync(
        int partyId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var party = await db.ThirdParties.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == partyId, ct);

        if (party is null) return null;

        var receivable = party.Side is ThirdPartySide.Receivable;

        var openingSigned = await db.VoucherLines
            .Where(l => l.AccountId == party.AccountId
                     && l.Voucher!.Status == VoucherStatus.Posted
                     && l.Voucher.Date < from)
            .SumAsync(l => l.Debit - l.Credit, ct);

        var opening = receivable ? openingSigned : -openingSigned;

        var entries = await db.VoucherLines
            .Where(l => l.AccountId == party.AccountId
                     && l.Voucher!.Status == VoucherStatus.Posted
                     && l.Voucher.Date >= from && l.Voucher.Date <= to)
            .Select(l => new
            {
                l.Voucher!.Date,
                l.Voucher.Number,
                l.Voucher.Narration,
                l.Debit,
                l.Credit,
                l.VoucherId,
                Contra = l.Voucher.Lines
                    .Where(o => o.AccountId != party.AccountId)
                    .Select(o => new { o.AccountCode, o.AccountName })
                    .ToList()
            })
            .OrderBy(l => l.Date).ThenBy(l => l.VoucherId)
            .ToListAsync(ct);

        var rows = new List<PartyStatementRow>();
        var running = opening;

        foreach (var entry in entries)
        {
            var signed = entry.Debit - entry.Credit;
            running += receivable ? signed : -signed;

            // A multi-line voucher has no single contra head, so it says so
            // rather than inventing one.
            var (code, name) = entry.Contra.Count switch
            {
                0 => (null, null),
                1 => (entry.Contra[0].AccountCode, entry.Contra[0].AccountName),
                _ => (null, $"Split — {entry.Contra.Count} heads")
            };

            rows.Add(new PartyStatementRow(
                entry.Date, entry.Number, entry.Narration,
                code, name,

                // Received and Paid are the party's credit and debit, so the
                // columns read the same way for both sides.
                Received: entry.Credit,
                Paid: entry.Debit,
                Balance: running));
        }

        return new PartyStatement(
            party.Id, party.Name, party.Side, from, to, opening, rows, running);
    }

    public async Task<int?> LastCashHeadAsync(int partyId, CancellationToken ct = default)
    {
        var party = await db.ThirdParties.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == partyId, ct);

        if (party is null) return null;

        var lastVoucherId = await db.VoucherLines
            .Where(l => l.AccountId == party.AccountId && l.Voucher!.Status == VoucherStatus.Posted)
            .OrderByDescending(l => l.Voucher!.Date).ThenByDescending(l => l.VoucherId)
            .Select(l => (int?)l.VoucherId)
            .FirstOrDefaultAsync(ct);

        if (lastVoucherId is null) return null;

        var others = await db.VoucherLines
            .Where(l => l.VoucherId == lastVoucherId && l.AccountId != party.AccountId)
            .Select(l => l.AccountId)
            .ToListAsync(ct);

        // Null for a split entry: there is no single head to reuse, and
        // guessing one would put the next payment somewhere arbitrary.
        return others.Count == 1 ? others[0] : null;
    }
}
