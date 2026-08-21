using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Finance;

/// <summary>
/// A float held by one person for small day-to-day spending.
///
/// The float is a fixed amount. Spending draws it down; a top-up restores it to
/// the float. That is what makes "what should be in the tin" answerable without
/// counting it.
/// </summary>
public class PettyCashBox : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Who holds it. A name, since a custodian often has no login.</summary>
    public string CustodianName { get; set; } = "";
    public string? CustodianUserId { get; set; }

    /// <summary>The standing float this box is topped up to.</summary>
    public decimal Float { get; set; }

    /// <summary>The cash account the box's balance lives on.</summary>
    public int AccountId { get; set; }
    public Account? Account { get; set; }

    public bool IsActive { get; set; } = true;

    public List<PettyCashEntry> Entries { get; set; } = [];
}

/// <summary>One spend from, or top-up to, a petty cash box.</summary>
public class PettyCashEntry : AuditableEntity
{
    public int BoxId { get; set; }
    public PettyCashBox? Box { get; set; }

    public DateOnly Date { get; set; }

    public PettyCashKind Kind { get; set; }

    public string Description { get; set; } = "";
    public decimal Amount { get; set; }

    /// <summary>Which expense head a spend is charged to. Null on a top-up.</summary>
    public int? ExpenseAccountId { get; set; }
    public Account? ExpenseAccount { get; set; }

    public string? PaidTo { get; set; }
    public string? ReceiptNumber { get; set; }

    /// <summary>The voucher this produced. Every entry has one.</summary>
    public int? VoucherId { get; set; }
}

public enum PettyCashKind
{
    /// <summary>Money spent out of the box.</summary>
    Spend = 0,

    /// <summary>Money put into the box, restoring it toward the float.</summary>
    TopUp = 1
}

public interface IPettyCashService
{
    Task<IReadOnlyList<PettyCashBox>> BoxesAsync(CancellationToken ct = default);
    Task<PettyCashBox?> GetBoxAsync(int id, CancellationToken ct = default);
    Task<Result<PettyCashBox>> SaveBoxAsync(PettyCashBox box, CancellationToken ct = default);

    /// <summary>What is actually in the box, from the ledger.</summary>
    Task<decimal> BalanceAsync(int boxId, CancellationToken ct = default);

    Task<IReadOnlyList<PettyCashEntry>> EntriesAsync(
        int boxId, DateOnly? from, DateOnly? to, CancellationToken ct = default);

    /// <summary>Records a spend and posts it: Dr expense, Cr the box.</summary>
    Task<Result<PettyCashEntry>> SpendAsync(PettyCashEntry entry, CancellationToken ct = default);

    /// <summary>Tops the box up out of a bank or main cash head.</summary>
    Task<Result<PettyCashEntry>> TopUpAsync(
        int boxId, decimal amount, int fromAccountId, DateOnly date, string? note,
        CancellationToken ct = default);
}

public sealed class PettyCashService(
    FinanceDbContext db, IVoucherService vouchers, IAccountService accounts) : IPettyCashService
{
    public async Task<IReadOnlyList<PettyCashBox>> BoxesAsync(CancellationToken ct = default) =>
        await db.PettyCashBoxes.AsNoTracking()
            .Include(b => b.Account)
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .ToListAsync(ct);

    public Task<PettyCashBox?> GetBoxAsync(int id, CancellationToken ct = default) =>
        db.PettyCashBoxes.Include(b => b.Account).FirstOrDefaultAsync(b => b.Id == id, ct);

    public async Task<Result<PettyCashBox>> SaveBoxAsync(
        PettyCashBox box, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(box.Name))
            return Result.Fail<PettyCashBox>("A box needs a name.", "petty.no-name");

        if (string.IsNullOrWhiteSpace(box.CustodianName))
        {
            // An unattributed float is one nobody is answerable for.
            return Result.Fail<PettyCashBox>("Say who holds this float.", "petty.no-custodian");
        }

        if (box.Float <= 0)
            return Result.Fail<PettyCashBox>("A float has to be more than nothing.", "petty.bad-float");

        if (box.Id == 0)
        {
            if (box.AccountId == 0)
            {
                var created = await CreateAccountAsync(box, ct);
                if (created.Failed) return Result.Fail<PettyCashBox>(created.Error!, created.Code);
                box.AccountId = created.Value.Id;
            }

            db.PettyCashBoxes.Add(box);
        }
        else
        {
            var existing = await db.PettyCashBoxes.FirstOrDefaultAsync(b => b.Id == box.Id, ct);
            if (existing is null) return Result.Fail<PettyCashBox>("That box no longer exists.", "petty.not-found");

            // The account is fixed once entries exist against it.
            var accountId = db.Entry(existing).OriginalValues.GetValue<int>(nameof(PettyCashBox.AccountId));
            db.Entry(existing).CurrentValues.SetValues(box);
            existing.AccountId = accountId;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(box);
    }

    private async Task<Result<Account>> CreateAccountAsync(PettyCashBox box, CancellationToken ct)
    {
        var parent = await db.Accounts.FirstOrDefaultAsync(a => a.Code == "1100", ct);
        if (parent is null)
            return Result.Fail<Account>("The 1100 Cash in hand head is missing.", "petty.no-parent");

        var siblings = await db.Accounts.CountAsync(a => a.ParentId == parent.Id, ct);

        var account = new Account
        {
            Code = $"1100-{siblings + 1:D2}",
            Name = $"Petty cash — {box.Name}",
            Type = AccountType.Asset,
            ParentId = parent.Id,
            IsPostable = true,
            IsSystem = true,
            IsActive = true
        };

        db.Accounts.Add(account);

        if (parent.IsPostable)
        {
            var parentUsed = await db.VoucherLines.AnyAsync(l => l.AccountId == parent.Id, ct);
            if (!parentUsed) parent.IsPostable = false;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(account);
    }

    public async Task<decimal> BalanceAsync(int boxId, CancellationToken ct = default)
    {
        var accountId = await db.PettyCashBoxes
            .Where(b => b.Id == boxId).Select(b => b.AccountId).FirstOrDefaultAsync(ct);

        return accountId == 0 ? 0 : await accounts.BalanceAsync(accountId, null, ct);
    }

    public async Task<IReadOnlyList<PettyCashEntry>> EntriesAsync(
        int boxId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var query = db.PettyCashEntries.AsNoTracking()
            .Include(e => e.ExpenseAccount)
            .Where(e => e.BoxId == boxId);

        if (from is not null) query = query.Where(e => e.Date >= from);
        if (to is not null) query = query.Where(e => e.Date <= to);

        return await query.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id)
                          .Take(500).ToListAsync(ct);
    }

    public async Task<Result<PettyCashEntry>> SpendAsync(
        PettyCashEntry entry, CancellationToken ct = default)
    {
        var box = await db.PettyCashBoxes.FirstOrDefaultAsync(b => b.Id == entry.BoxId, ct);
        if (box is null) return Result.Fail<PettyCashEntry>("That box no longer exists.", "petty.not-found");

        if (entry.Amount <= 0)
            return Result.Fail<PettyCashEntry>("The amount must be more than nothing.", "petty.bad-amount");

        if (string.IsNullOrWhiteSpace(entry.Description))
            return Result.Fail<PettyCashEntry>("Say what the money was spent on.", "petty.no-description");

        if (entry.ExpenseAccountId is null)
            return Result.Fail<PettyCashEntry>("Choose which head this is charged to.", "petty.no-head");

        var balance = await BalanceAsync(box.Id, ct);
        if (entry.Amount > balance)
        {
            // A petty cash box cannot go overdrawn - there is either cash in the
            // tin or there is not, and a negative balance means the record has
            // stopped describing the tin.
            return Result.Fail<PettyCashEntry>(
                $"Only {balance:N2} is left in {box.Name}, and this spends {entry.Amount:N2}. " +
                "Top the box up first.",
                "petty.insufficient");
        }

        var posted = await vouchers.PostSystemVoucherAsync(new SystemVoucher(
            Type: VoucherType.Payment,
            Date: entry.Date,
            Narration: $"Petty cash — {entry.Description}",
            Lines:
            [
                new VoucherLineInput(entry.ExpenseAccountId.Value, entry.Amount, 0,
                    entry.Description, null, entry.PaidTo),
                new VoucherLineInput(box.AccountId, 0, entry.Amount, entry.Description)
            ],
            Module: FinanceModule.Key,
            DocumentType: "finance.petty-cash",
            DocumentId: box.Id,
            DocumentReference: entry.ReceiptNumber ?? box.Name), ct);

        if (posted.Failed) return Result.Fail<PettyCashEntry>(posted.Error!, posted.Code);

        entry.Kind = PettyCashKind.Spend;
        entry.VoucherId = posted.Value.Id;

        db.PettyCashEntries.Add(entry);
        await db.SaveChangesAsync(ct);

        return Result.Success(entry);
    }

    public async Task<Result<PettyCashEntry>> TopUpAsync(
        int boxId, decimal amount, int fromAccountId, DateOnly date, string? note,
        CancellationToken ct = default)
    {
        var box = await db.PettyCashBoxes.FirstOrDefaultAsync(b => b.Id == boxId, ct);
        if (box is null) return Result.Fail<PettyCashEntry>("That box no longer exists.", "petty.not-found");

        if (amount <= 0)
            return Result.Fail<PettyCashEntry>("The amount must be more than nothing.", "petty.bad-amount");

        var balance = await BalanceAsync(boxId, ct);
        if (balance + amount > box.Float)
        {
            // The float is the whole point: topping past it means the box is no
            // longer a fixed float, and "what should be in the tin" stops having
            // an answer.
            return Result.Fail<PettyCashEntry>(
                $"{box.Name} holds {balance:N2} against a float of {box.Float:N2}, " +
                $"so it can take at most {box.Float - balance:N2}.",
                "petty.over-float");
        }

        var posted = await vouchers.PostSystemVoucherAsync(new SystemVoucher(
            Type: VoucherType.Contra,
            Date: date,
            Narration: note ?? $"Petty cash top-up — {box.Name}",
            Lines:
            [
                new VoucherLineInput(box.AccountId, amount, 0, note),
                new VoucherLineInput(fromAccountId, 0, amount, note)
            ],
            Module: FinanceModule.Key,
            DocumentType: "finance.petty-cash",
            DocumentId: box.Id,
            DocumentReference: box.Name), ct);

        if (posted.Failed) return Result.Fail<PettyCashEntry>(posted.Error!, posted.Code);

        var entry = new PettyCashEntry
        {
            BoxId = box.Id,
            Date = date,
            Kind = PettyCashKind.TopUp,
            Description = note ?? "Top-up",
            Amount = amount,
            VoucherId = posted.Value.Id
        };

        db.PettyCashEntries.Add(entry);
        await db.SaveChangesAsync(ct);

        return Result.Success(entry);
    }
}
