using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Inventory;

/// <summary>
/// The item catalogue.
///
/// The party master used to live here too, back when Inventory owned buying and
/// selling. Both moved to Sales &amp; Purchase; what is left is the goods.
/// </summary>
public interface ICatalogService
{
    /// <param name="domainId">
    /// Which stock book to read. Null spans every book, which is what a
    /// group-wide valuation wants; a working screen always passes one, because
    /// the point of the partition is that the workshop's spares and the main
    /// store's goods are never listed together by accident.
    /// </param>
    Task<IReadOnlyList<Item>> ItemsAsync(string? search = null, bool includeInactive = false, int? domainId = null, CancellationToken ct = default);
    Task<Item?> GetItemAsync(int id, CancellationToken ct = default);
    Task<Result<Item>> SaveItemAsync(Item item, CancellationToken ct = default);
    Task<Result> DeleteItemAsync(int id, CancellationToken ct = default);

    /// <summary>Items at or below their reorder level, in one book or across all of them.</summary>
    Task<IReadOnlyList<Item>> ReorderReportAsync(int? domainId = null, CancellationToken ct = default);
}

public sealed class CatalogService(InventoryDbContext db) : ICatalogService
{
    public async Task<IReadOnlyList<Item>> ItemsAsync(
        string? search = null, bool includeInactive = false, int? domainId = null, CancellationToken ct = default)
    {
        var query = db.Items.AsNoTracking().Include(i => i.Category).Include(i=>i.ProductFamily).Include(i=>i.ParentItem).AsQueryable();
        if (domainId is { } book) query = query.Where(i => i.DomainId == book);
        if (!includeInactive) query = query.Where(i => i.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(i =>
                EF.Functions.ILike(i.Name, pattern) || EF.Functions.ILike(i.Code, pattern));
        }

        return await query.OrderBy(i => i.Name).ToListAsync(ct);
    }

    public Task<Item?> GetItemAsync(int id, CancellationToken ct = default) =>
        db.Items.FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<Result<Item>> SaveItemAsync(Item item, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(item.Code))
            return Result.Fail<Item>("An item needs a code.", "item.no-code");

        if (string.IsNullOrWhiteSpace(item.Name))
            return Result.Fail<Item>("An item needs a name.", "item.no-name");

        // An item that named no book predates the partition, or came from a
        // screen that has not been taught about it. Default it to the main
        // store rather than writing a dangling row.
        if (item.DomainId == 0)
        {
            item.DomainId = await db.StockDomains
                .OrderByDescending(x => x.IsDefault).ThenBy(x => x.Id)
                .Select(x => x.Id).FirstOrDefaultAsync(ct);

            if (item.DomainId == 0)
                return Result.Fail<Item>("No stock book exists to file this item under.", "item.no-domain");
        }

        // Uniqueness is per book: the workshop and the main store number their
        // goods independently, so the same code in the other book is not a clash.
        var taken = await db.Items.AnyAsync(
            i => i.Code == item.Code && i.DomainId == item.DomainId && i.Id != item.Id, ct);
        if (taken) return Result.Fail<Item>($"Code {item.Code} is already in use in this stock book.", "item.duplicate-code");
        if(item.Kind==InventoryItemKind.Accessory)
        {
            if(item.ParentItemId is null)return Result.Fail<Item>("An accessory must belong to a model.","item.no-parent");
            var parent=await db.Items.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==item.ParentItemId,ct);
            if(parent is null||parent.Kind!=InventoryItemKind.Model)return Result.Fail<Item>("Choose a valid parent model.","item.bad-parent");
            if(item.ProductFamilyId is not null&&parent.ProductFamilyId!=item.ProductFamilyId)return Result.Fail<Item>("Accessory and model must belong to the same product family.","item.family-mismatch");
            item.ProductFamilyId=parent.ProductFamilyId;
        }
        else item.ParentItemId=null;

        if (item.Id == 0)
        {
            db.Items.Add(item);
        }
        else
        {
            var existing = await db.Items.FirstOrDefaultAsync(i => i.Id == item.Id, ct);
            if (existing is null) return Result.Fail<Item>("That item no longer exists.", "item.not-found");

            var entry = db.Entry(existing);

            // Quantity and cost are owned by StockService and derived from
            // movements. Letting an edit screen set them would make the stock
            // ledger and the running figure disagree with no way to tell which
            // is right.
            //
            // Read from OriginalValues rather than from `existing`: an edit
            // screen usually hands back the very instance it loaded, so
            // `existing` and `item` are the same tracked object and copying one
            // onto the other would preserve nothing at all.
            var quantity = entry.OriginalValues.GetValue<decimal>(nameof(Item.QuantityOnHand));
            var average = entry.OriginalValues.GetValue<decimal>(nameof(Item.AverageCost));
            var last = entry.OriginalValues.GetValue<decimal?>(nameof(Item.LastCost));

            // The book an item belongs to is fixed at creation, and preserved
            // here for the same reason as the quantity: its stock history, its
            // warehouse balances and its movements all sit in that book. Moving
            // the item alone would leave every one of them behind, and the two
            // valuations would silently stop adding up.
            var domain = entry.OriginalValues.GetValue<int>(nameof(Item.DomainId));

            entry.CurrentValues.SetValues(item);

            existing.QuantityOnHand = quantity;
            existing.AverageCost = average;
            existing.LastCost = last;
            existing.DomainId = domain;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(item);
    }

    public async Task<Result> DeleteItemAsync(int id, CancellationToken ct = default)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return Result.Fail("That item no longer exists.", "item.not-found");

        if (item.QuantityOnHand != 0)
        {
            return Result.Fail(
                $"{item.Name} still has {item.QuantityOnHand:0.##} {item.Unit} in stock. " +
                "Deactivate it instead, or bring the stock to nil first.",
                "item.has-stock");
        }

        var moved = await db.StockMovements.AnyAsync(m => m.ItemId == id, ct);
        if (moved)
        {
            // History has to keep resolving.
            return Result.Fail(
                "This item has stock history. Deactivate it instead - deleting it would " +
                "leave those movements pointing at nothing.",
                "item.has-history");
        }

        db.Items.Remove(item);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }



    public async Task<IReadOnlyList<Item>> ReorderReportAsync(int? domainId = null, CancellationToken ct = default) =>
        await db.Items.AsNoTracking()
            .Where(i => i.IsActive && i.ReorderLevel > 0 && i.QuantityOnHand <= i.ReorderLevel)
            .Where(i => domainId == null || i.DomainId == domainId)
            .OrderBy(i => i.Name)
            .ToListAsync(ct);
}
