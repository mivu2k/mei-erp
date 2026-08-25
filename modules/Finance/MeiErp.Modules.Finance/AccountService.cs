using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Finance;

public interface IAccountService
{
    Task<IReadOnlyList<Account>> ListAsync(bool includeInactive = false, CancellationToken ct = default);

    /// <summary>Only the accounts an entry may actually be posted to.</summary>
    Task<IReadOnlyList<Account>> PostableAsync(AccountType? type = null, CancellationToken ct = default);

    Task<Account?> GetAsync(int id, CancellationToken ct = default);
    Task<Result<Account>> SaveAsync(Account account, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>Signed balance as at a date. Positive means debit.</summary>
    Task<decimal> BalanceAsync(int accountId, DateOnly? asAt = null, CancellationToken ct = default);

    /// <summary>This account's balance plus every account beneath it.</summary>
    Task<decimal> BalanceWithChildrenAsync(int accountId, DateOnly? asAt = null, CancellationToken ct = default);

    /// <summary>
    /// The spending categories offered to this audience, by name.
    ///
    /// What a requester picks from. Codes are deliberately not part of it -
    /// somebody claiming a taxi fare knows "Travel", not 5220.
    /// </summary>
    Task<IReadOnlyList<Account>> CategoriesAsync(
        ExpenseAudience audience, CancellationToken ct = default);
}

public sealed class AccountService(FinanceDbContext db) : IAccountService
{
    public async Task<IReadOnlyList<Account>> ListAsync(
        bool includeInactive = false, CancellationToken ct = default)
    {
        var query = db.Accounts.AsNoTracking().AsQueryable();
        if (!includeInactive) query = query.Where(a => a.IsActive);
        return await query.OrderBy(a => a.Code).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Account>> PostableAsync(
        AccountType? type = null, CancellationToken ct = default)
    {
        var query = db.Accounts.AsNoTracking().Where(a => a.IsActive && a.IsPostable);
        if (type is not null) query = query.Where(a => a.Type == type);
        return await query.OrderBy(a => a.Code).ToListAsync(ct);
    }

    public Task<Account?> GetAsync(int id, CancellationToken ct = default) =>
        db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<Result<Account>> SaveAsync(Account account, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(account.Code))
            return Result.Fail<Account>("An account needs a code.", "account.no-code");

        if (string.IsNullOrWhiteSpace(account.Name))
            return Result.Fail<Account>("An account needs a name.", "account.no-name");

        var codeTaken = await db.Accounts
            .AnyAsync(a => a.Code == account.Code && a.Id != account.Id, ct);

        if (codeTaken)
            return Result.Fail<Account>($"Code {account.Code} is already in use.", "account.duplicate-code");

        if (account.ParentId == account.Id && account.Id != 0)
            return Result.Fail<Account>("An account cannot sit under itself.", "account.self-parent");

        if (account.Id != 0)
        {
            var existing = await db.Accounts.FirstOrDefaultAsync(a => a.Id == account.Id, ct);
            if (existing is null)
                return Result.Fail<Account>("That account no longer exists.", "account.not-found");

            // Turning a posted-to account into a heading would strand its
            // existing entries on something that can no longer hold a balance.
            if (existing.IsPostable && !account.IsPostable)
            {
                var used = await db.VoucherLines.AnyAsync(l => l.AccountId == account.Id, ct);
                if (used)
                {
                    return Result.Fail<Account>(
                        "This account already has entries against it, so it cannot become a heading. " +
                        "Make a new heading and move the children instead.",
                        "account.has-entries");
                }
            }

            db.Entry(existing).CurrentValues.SetValues(account);
        }
        else
        {
            db.Accounts.Add(account);
        }

        // A parent stops being postable the moment something sits beneath it.
        if (account.ParentId is not null)
        {
            var parent = await db.Accounts.FirstOrDefaultAsync(a => a.Id == account.ParentId, ct);
            if (parent is not null && parent.IsPostable)
            {
                var parentUsed = await db.VoucherLines.AnyAsync(l => l.AccountId == parent.Id, ct);

                // Only demote a clean parent. One with entries stays postable and
                // the report shows both - wrong, but visible, rather than silently
                // dropping history.
                if (!parentUsed) parent.IsPostable = false;
            }
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(account);
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken ct = default)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (account is null) return Result.Fail("That account no longer exists.", "account.not-found");

        if (account.IsSystem)
        {
            return Result.Fail(
                "This account is used by the system itself and cannot be removed. " +
                "Deactivate it if it should not be picked.",
                "account.system");
        }

        var used = await db.VoucherLines.AnyAsync(l => l.AccountId == id, ct);
        if (used)
        {
            // History must keep resolving. Deactivating hides it from pickers
            // without breaking every voucher that points at it.
            return Result.Fail(
                "This account has entries against it. Deactivate it instead - deleting it " +
                "would leave those entries pointing at nothing.",
                "account.has-entries");
        }

        var hasChildren = await db.Accounts.AnyAsync(a => a.ParentId == id, ct);
        if (hasChildren)
            return Result.Fail("This heading has accounts underneath it. Move or remove them first.", "account.has-children");

        db.Accounts.Remove(account);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<decimal> BalanceAsync(
        int accountId, DateOnly? asAt = null, CancellationToken ct = default)
    {
        var query = db.VoucherLines
            .Where(l => l.AccountId == accountId && l.Voucher!.Status == VoucherStatus.Posted);

        if (asAt is not null) query = query.Where(l => l.Voucher!.Date <= asAt);

        // Summed in the database rather than pulled into memory: this is read on
        // every account row of the chart, and at year five that is a lot of rows.
        return await query.SumAsync(l => l.Debit - l.Credit, ct);
    }

    public async Task<IReadOnlyList<Account>> CategoriesAsync(
        ExpenseAudience audience, CancellationToken ct = default) =>
        await db.Accounts.AsNoTracking()
            .Where(a => a.IsActive
                     && a.IsPostable
                     && a.Type == AccountType.Expense
                     && (a.Audience & audience) != 0)
            .OrderBy(a => a.Name)
            .ToListAsync(ct);

    public async Task<decimal> BalanceWithChildrenAsync(
        int accountId, DateOnly? asAt = null, CancellationToken ct = default)
    {
        // A heading's own balance is not its total once it has children hanging
        // beneath it - advances per person, one payable per supplier. Asking for
        // "employee advances" means the sum of everyone's, not the empty parent.
        var ids = new List<int> { accountId };

        for (var frontier = ids.ToList(); frontier.Count > 0;)
        {
            frontier = await db.Accounts
                .Where(a => a.ParentId != null && frontier.Contains(a.ParentId.Value))
                .Select(a => a.Id)
                .ToListAsync(ct);

            ids.AddRange(frontier);
        }

        var query = db.VoucherLines
            .Where(l => ids.Contains(l.AccountId) && l.Voucher!.Status == VoucherStatus.Posted);

        if (asAt is not null) query = query.Where(l => l.Voucher!.Date <= asAt);

        return await query.SumAsync(l => l.Debit - l.Credit, ct);
    }
}
