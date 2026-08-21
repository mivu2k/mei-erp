using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Finance;

/// <summary>
/// A recurring utility connection — an electricity meter, a gas connection, a
/// phone line. What is tracked is the bill against the connection, so a jump in
/// consumption is visible against its own history rather than lost in a total.
/// </summary>
public class UtilityConnection : AuditableEntity
{
    public string Name { get; set; } = "";

    public UtilityKind Kind { get; set; }

    /// <summary>Meter or account number with the provider.</summary>
    public string? ConnectionNumber { get; set; }
    public string? Provider { get; set; }

    /// <summary>Which head bills against this connection are charged to.</summary>
    public int ExpenseAccountId { get; set; }
    public Account? ExpenseAccount { get; set; }

    public string? Location { get; set; }
    public bool IsActive { get; set; } = true;

    public List<UtilityBill> Bills { get; set; } = [];
}

public enum UtilityKind
{
    Electricity = 0,
    Gas = 1,
    Water = 2,
    Telephone = 3,
    Internet = 4,
    Other = 5
}

/// <summary>One bill against a connection.</summary>
public class UtilityBill : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    public int ConnectionId { get; set; }
    public UtilityConnection? Connection { get; set; }

    /// <summary>The month the bill covers, e.g. 2026-08.</summary>
    public DateOnly BillingMonth { get; set; }

    public string? BillNumber { get; set; }

    public DateOnly IssuedOn { get; set; }
    public DateOnly DueOn { get; set; }

    public decimal Amount { get; set; }

    /// <summary>Units consumed, where the provider states them. Null for a flat charge.</summary>
    public decimal? Units { get; set; }

    public DateOnly? PaidOn { get; set; }
    public int? VoucherId { get; set; }

    public bool IsPaid => PaidOn is not null;

    public bool IsOverdue(DateOnly today) => !IsPaid && DueOn < today;
}

public interface IUtilityService
{
    Task<IReadOnlyList<UtilityConnection>> ConnectionsAsync(CancellationToken ct = default);
    Task<Result<UtilityConnection>> SaveConnectionAsync(UtilityConnection connection, CancellationToken ct = default);

    Task<IReadOnlyList<UtilityBill>> BillsAsync(int? connectionId, bool unpaidOnly, CancellationToken ct = default);
    Task<Result<UtilityBill>> SaveBillAsync(UtilityBill bill, CancellationToken ct = default);

    /// <summary>Pays a bill and posts it: Dr the connection's head, Cr cash.</summary>
    Task<Result<UtilityBill>> PayAsync(int billId, int fromAccountId, DateOnly date, CancellationToken ct = default);

    /// <summary>Unpaid bills already past their due date.</summary>
    Task<IReadOnlyList<UtilityBill>> OverdueAsync(CancellationToken ct = default);
}

public sealed class UtilityService(
    FinanceDbContext db, IVoucherService vouchers, IClock clock) : IUtilityService
{
    public async Task<IReadOnlyList<UtilityConnection>> ConnectionsAsync(CancellationToken ct = default) =>
        await db.UtilityConnections.AsNoTracking()
            .Include(c => c.ExpenseAccount)
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

    public async Task<Result<UtilityConnection>> SaveConnectionAsync(
        UtilityConnection connection, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connection.Name))
            return Result.Fail<UtilityConnection>("A connection needs a name.", "utility.no-name");

        if (connection.ExpenseAccountId == 0)
            return Result.Fail<UtilityConnection>("Choose which head bills are charged to.", "utility.no-head");

        if (connection.Id == 0)
        {
            db.UtilityConnections.Add(connection);
        }
        else
        {
            var existing = await db.UtilityConnections.FirstOrDefaultAsync(c => c.Id == connection.Id, ct);
            if (existing is null) return Result.Fail<UtilityConnection>("That connection no longer exists.", "utility.not-found");
            db.Entry(existing).CurrentValues.SetValues(connection);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(connection);
    }

    public async Task<IReadOnlyList<UtilityBill>> BillsAsync(
        int? connectionId, bool unpaidOnly, CancellationToken ct = default)
    {
        var query = db.UtilityBills.AsNoTracking().Include(b => b.Connection).AsQueryable();

        if (connectionId is not null) query = query.Where(b => b.ConnectionId == connectionId);
        if (unpaidOnly) query = query.Where(b => b.PaidOn == null);

        return await query.OrderByDescending(b => b.BillingMonth).Take(500).ToListAsync(ct);
    }

    public async Task<Result<UtilityBill>> SaveBillAsync(
        UtilityBill bill, CancellationToken ct = default)
    {
        var connection = await db.UtilityConnections
            .FirstOrDefaultAsync(c => c.Id == bill.ConnectionId, ct);

        if (connection is null)
            return Result.Fail<UtilityBill>("That connection no longer exists.", "utility.not-found");

        if (bill.Amount <= 0)
            return Result.Fail<UtilityBill>("A bill has to be for more than nothing.", "utility.bad-amount");

        if (bill.DueOn < bill.IssuedOn)
            return Result.Fail<UtilityBill>("The due date is before the bill was issued.", "utility.bad-dates");

        // The month is what makes one bill per connection identifiable, so
        // entering August twice is caught rather than quietly doubling the cost.
        var month = new DateOnly(bill.BillingMonth.Year, bill.BillingMonth.Month, 1);
        bill.BillingMonth = month;

        var duplicate = await db.UtilityBills.AnyAsync(
            b => b.ConnectionId == bill.ConnectionId
              && b.BillingMonth == month
              && b.Id != bill.Id, ct);

        if (duplicate)
        {
            return Result.Fail<UtilityBill>(
                $"{connection.Name} already has a bill for {month:MMMM yyyy}.",
                "utility.duplicate-month");
        }

        if (bill.Id == 0)
        {
            db.UtilityBills.Add(bill);
        }
        else
        {
            var existing = await db.UtilityBills.FirstOrDefaultAsync(b => b.Id == bill.Id, ct);
            if (existing is null) return Result.Fail<UtilityBill>("That bill no longer exists.", "utility.no-bill");

            if (existing.IsPaid)
            {
                // Changing the amount after payment would leave the voucher and
                // the bill disagreeing about what was paid.
                return Result.Fail<UtilityBill>(
                    "This bill has been paid and cannot be edited. Reverse its voucher first.",
                    "utility.already-paid");
            }

            db.Entry(existing).CurrentValues.SetValues(bill);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(bill);
    }

    public async Task<Result<UtilityBill>> PayAsync(
        int billId, int fromAccountId, DateOnly date, CancellationToken ct = default)
    {
        var bill = await db.UtilityBills
            .Include(b => b.Connection)
            .FirstOrDefaultAsync(b => b.Id == billId, ct);

        if (bill is null) return Result.Fail<UtilityBill>("That bill no longer exists.", "utility.no-bill");
        if (bill.IsPaid) return Result.Fail<UtilityBill>("This bill has already been paid.", "utility.already-paid");

        var posted = await vouchers.PostSystemVoucherAsync(new SystemVoucher(
            Type: VoucherType.Payment,
            Date: date,
            Narration: $"{bill.Connection!.Name} — {bill.BillingMonth:MMMM yyyy}" +
                       (bill.BillNumber is null ? "" : $" ({bill.BillNumber})"),
            Lines:
            [
                new VoucherLineInput(bill.Connection.ExpenseAccountId, bill.Amount, 0,
                    $"{bill.Connection.Name} {bill.BillingMonth:MMM yyyy}"),
                new VoucherLineInput(fromAccountId, 0, bill.Amount, bill.Connection.Provider)
            ],
            Module: FinanceModule.Key,
            DocumentType: "finance.utility-bill",
            DocumentId: bill.Id,
            DocumentReference: bill.BillNumber ?? bill.Connection.Name), ct);

        if (posted.Failed) return Result.Fail<UtilityBill>(posted.Error!, posted.Code);

        bill.PaidOn = date;
        bill.VoucherId = posted.Value.Id;

        await db.SaveChangesAsync(ct);
        return Result.Success(bill);
    }

    public async Task<IReadOnlyList<UtilityBill>> OverdueAsync(CancellationToken ct = default)
    {
        var today = clock.Today;

        return await db.UtilityBills.AsNoTracking()
            .Include(b => b.Connection)
            .Where(b => b.PaidOn == null && b.DueOn < today)
            .OrderBy(b => b.DueOn)
            .ToListAsync(ct);
    }
}
