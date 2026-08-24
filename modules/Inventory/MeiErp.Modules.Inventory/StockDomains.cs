using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Inventory;

/// <summary>
/// One set of books for stock. The business keeps two: the main trading store,
/// and the workshop's spare parts.
///
/// This is a data partition, not a second copy of the module. Every item,
/// warehouse and movement belongs to exactly one domain, and every query in the
/// module is scoped to one, so the two stores have separate stock figures,
/// separate valuation, separate reorder levels and separate reports while
/// sharing one implementation. Two Inventory modules would have meant every
/// future fix applied twice, which is the duplication this whole exercise
/// exists to remove.
///
/// Stock does not move between domains. An item belongs to a domain, so a
/// transfer - which moves a named item between warehouses - is necessarily
/// within one. Getting goods from one book to the other is a sale out of one
/// and a purchase into the other, which is also how the money should read.
/// </summary>
public class StockDomain : AuditableEntity
{
    /// <summary>Short handle used in numbering and on screen, e.g. "MAIN", "SPARE".</summary>
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>Where a screen lands when the person has not chosen a book.</summary>
    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>The two books every install starts with.</summary>
public static class StockDomainCodes
{
    /// <summary>Goods the business buys and sells.</summary>
    public const string Main = "MAIN";

    /// <summary>Parts the workshop consumes on repair jobs.</summary>
    public const string Spare = "SPARE";
}

public interface IStockDomainService
{
    Task<IReadOnlyList<StockDomain>> ListAsync(CancellationToken ct = default);
    Task<StockDomain?> GetAsync(int id, CancellationToken ct = default);
    Task<StockDomain?> ByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// The book a screen should show when none was chosen. Never null on a
    /// seeded install; the seeder guarantees at least the two.
    /// </summary>
    Task<StockDomain> DefaultAsync(CancellationToken ct = default);

    Task<Result<StockDomain>> SaveAsync(StockDomain value, CancellationToken ct = default);
}

public sealed class StockDomainService(InventoryDbContext db) : IStockDomainService
{
    public async Task<IReadOnlyList<StockDomain>> ListAsync(CancellationToken ct = default) =>
        await db.StockDomains.AsNoTracking()
            .OrderByDescending(x => x.IsDefault).ThenBy(x => x.Name)
            .ToListAsync(ct);

    public Task<StockDomain?> GetAsync(int id, CancellationToken ct = default) =>
        db.StockDomains.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<StockDomain?> ByCodeAsync(string code, CancellationToken ct = default) =>
        db.StockDomains.AsNoTracking().FirstOrDefaultAsync(x => x.Code == code, ct);

    public async Task<StockDomain> DefaultAsync(CancellationToken ct = default) =>
        await db.StockDomains.AsNoTracking()
            .OrderByDescending(x => x.IsDefault).ThenBy(x => x.Id)
            .FirstOrDefaultAsync(ct)
        ?? throw new InvalidOperationException(
            "No stock domain exists. The Inventory seeder creates the main and spare books.");

    public async Task<Result<StockDomain>> SaveAsync(StockDomain value, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(value.Name))
            return Result.Fail<StockDomain>("A stock book needs a name.");
        if (string.IsNullOrWhiteSpace(value.Code))
            return Result.Fail<StockDomain>("A stock book needs a code.");

        value.Code = value.Code.Trim().ToUpperInvariant();

        if (await db.StockDomains.AnyAsync(x => x.Code == value.Code && x.Id != value.Id, ct))
            return Result.Fail<StockDomain>($"Another stock book already uses the code {value.Code}.");

        if (value.Id == 0)
        {
            if (!await db.StockDomains.AnyAsync(ct)) value.IsDefault = true;
            db.Add(value);
        }
        else
        {
            db.Update(value);
        }

        await db.SaveChangesAsync(ct);

        // Only one book can be the landing place. Done after the save so a new
        // row already has its id.
        if (value.IsDefault)
        {
            await db.StockDomains
                .Where(x => x.Id != value.Id && x.IsDefault)
                .ExecuteUpdateAsync(x => x.SetProperty(y => y.IsDefault, false), ct);
        }

        return Result.Success(value);
    }
}
