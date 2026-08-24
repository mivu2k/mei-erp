using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Finance;

public sealed record PersonalLedgerAccount(
    int AccountId, string Code, string Name, decimal Balance,
    IReadOnlyList<LedgerRow> Rows);

public interface IPersonalLedgerService
{
    Task<IReadOnlyList<PersonalLedgerAccount>> MineAsync(CancellationToken ct = default);
}

/// <summary>Posted voucher lines explicitly tagged to the signed-in person.</summary>
public sealed class PersonalLedgerService(FinanceDbContext db, ICurrentUser currentUser)
    : IPersonalLedgerService
{
    public async Task<IReadOnlyList<PersonalLedgerAccount>> MineAsync(CancellationToken ct = default)
    {
        if (currentUser.UserId is not { Length: > 0 } userId) return [];

        var lines = await db.VoucherLines.AsNoTracking()
            .Include(x => x.Voucher).Include(x => x.Account)
            .Where(x => x.PersonId == userId && x.Voucher!.Status == VoucherStatus.Posted)
            .OrderBy(x => x.Voucher!.Date).ThenBy(x => x.VoucherId).ThenBy(x => x.Id)
            .ToListAsync(ct);

        var result = new List<PersonalLedgerAccount>();
        foreach (var group in lines.GroupBy(x => new { x.AccountId, x.AccountCode, x.AccountName }))
        {
            decimal running = 0;
            var rows = group.Select(x =>
            {
                running += x.Debit - x.Credit;
                return new LedgerRow(x.Voucher!.Date, x.Voucher.Number,
                    x.Narration ?? x.Voucher.Narration, x.Debit, x.Credit, running,
                    x.PersonName, "");
            }).ToList();
            result.Add(new(group.Key.AccountId, group.Key.AccountCode, group.Key.AccountName, running, rows));
        }
        return [.. result.OrderBy(x => x.Code)];
    }
}
