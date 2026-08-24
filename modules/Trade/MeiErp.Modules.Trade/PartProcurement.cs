using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Trade;

/// <summary>
/// A part the workshop buys against a job.
///
/// Deliberately carries no stock quantity. The workshop buys for the device on
/// the bench rather than for a shelf, so what matters is what it cost and what
/// it sells for - not how many are on hand. That is why this is not an
/// Inventory item: an item with a quantity nobody counts is a quantity nobody
/// can trust.
///
/// Moved here from the Repair module: the workshop was buying through its own
/// parallel purchasing screens, against its own supplier list. Buying is buying.
/// </summary>
public class Part : AuditableEntity
{
    public string? Sku { get; set; }
    public string Name { get; set; } = "";
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public decimal SellingPrice { get; set; }

    /// <summary>What the most recent purchase cost. Only ever moves forward in time.</summary>
    public decimal? LastPurchaseCost { get; set; }

    /// <summary>Weighted average across every purchase ever recorded.</summary>
    public decimal? AverageCost { get; set; }

    public decimal PurchasedQuantity { get; set; }
    public DateOnly? LastPurchasedOn { get; set; }

    /// <summary>The party last bought from, as an id plus a name snapshot.</summary>
    public int? LastSupplierId { get; set; }
    public string? LastSupplierName { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Null rather than zero when nothing has been bought yet: an unpriced part
    /// has no margin, not a nil one.
    /// </summary>
    public decimal? MarginPercent => LastPurchaseCost is > 0
        ? Math.Round((SellingPrice - LastPurchaseCost.Value) / LastPurchaseCost.Value * 100m, 2)
        : null;
}

/// <summary>A supplier invoice for parts. Records cost; moves no stock.</summary>
public class PartPurchase : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }

    public string Number { get; set; } = "";

    public int PartyId { get; set; }
    public Party? Party { get; set; }
    public string PartyName { get; set; } = "";

    public string? SupplierInvoiceNumber { get; set; }
    public DateOnly PurchasedOn { get; set; }

    public string ReceivedById { get; set; } = "";
    public string ReceivedByName { get; set; } = "";

    public decimal TaxAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal OtherCharges { get; set; }

    public string? Notes { get; set; }

    public List<PartPurchaseLine> Lines { get; set; } = [];

    public decimal Subtotal => Lines.Sum(x => x.LineTotal);
    public decimal Total => Math.Round(Subtotal - DiscountAmount + TaxAmount + OtherCharges, 2);
}

public class PartPurchaseLine : Entity
{
    public int PartPurchaseId { get; set; }
    public PartPurchase? Purchase { get; set; }

    public int PartId { get; set; }
    public Part? Part { get; set; }

    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }

    /// <summary>Optionally re-prices the part on the way in.</summary>
    public decimal? NewSellingPrice { get; set; }

    public string? Remarks { get; set; }

    public decimal LineTotal => Math.Round(Quantity * UnitCost, 2);
}

public sealed record PartPurchaseLineInput(
    int PartId, decimal Quantity, decimal UnitCost, decimal? NewSellingPrice, string? Remarks);

public sealed record PartPurchaseInput(
    int PartyId, string? SupplierInvoiceNumber, DateOnly PurchasedOn,
    decimal TaxAmount, decimal DiscountAmount, decimal OtherCharges,
    string? Notes, IReadOnlyList<PartPurchaseLineInput> Lines);

public sealed record PartPricePoint(
    DateOnly Date, string PurchaseNumber, string Supplier, decimal Quantity, decimal UnitCost);

public interface IPartProcurementService
{
    Task<IReadOnlyList<Part>> PartsAsync(string? search = null, CancellationToken ct = default);
    Task<Part?> PartAsync(int id, CancellationToken ct = default);
    Task<Result<Part>> SavePartAsync(Part part, CancellationToken ct = default);

    Task<IReadOnlyList<PartPurchase>> PurchasesAsync(string? search = null, CancellationToken ct = default);
    Task<PartPurchase?> PurchaseAsync(int id, CancellationToken ct = default);
    Task<Result<PartPurchase>> ReceiveAsync(PartPurchaseInput input, CancellationToken ct = default);

    Task<IReadOnlyList<PartPricePoint>> PriceHistoryAsync(int partId, CancellationToken ct = default);
}

public sealed class PartProcurementService(
    TradeDbContext db, IClock clock, ICurrentUser user) : IPartProcurementService
{
    public async Task<IReadOnlyList<Part>> PartsAsync(string? search = null, CancellationToken ct = default)
    {
        var q = db.Parts.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var p = $"%{search.Trim()}%";
            q = q.Where(x => EF.Functions.ILike(x.Name, p) || (x.Sku != null && EF.Functions.ILike(x.Sku, p)));
        }

        return await q.OrderBy(x => x.Name).Take(500).ToListAsync(ct);
    }

    public Task<Part?> PartAsync(int id, CancellationToken ct = default) =>
        db.Parts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<Result<Part>> SavePartAsync(Part part, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(part.Name))
            return Result.Fail<Part>("Part name is required.", "part.no-name");

        if (part.SellingPrice < 0)
            return Result.Fail<Part>("Selling price cannot be negative.", "part.bad-price");

        part.Sku = string.IsNullOrWhiteSpace(part.Sku) ? null : part.Sku.Trim();

        if (part.Sku is not null && await db.Parts.AnyAsync(x => x.Sku == part.Sku && x.Id != part.Id, ct))
            return Result.Fail<Part>("That SKU is already in use.", "part.duplicate-sku");

        Part row;

        if (part.Id == 0)
        {
            row = part;
            db.Parts.Add(row);
        }
        else
        {
            var existing = await db.Parts.FirstOrDefaultAsync(x => x.Id == part.Id, ct);
            if (existing is null) return Result.Fail<Part>("Part not found.", "part.not-found");

            // Cost figures are owned by ReceiveAsync and derived from purchases.
            // Letting an edit screen set them would make the price history and
            // the averages disagree with no way to tell which is right.
            row = existing;
            row.Sku = part.Sku;
            row.Name = part.Name.Trim();
            row.Brand = part.Brand;
            row.Model = part.Model;
            row.SellingPrice = part.SellingPrice;
            row.IsActive = part.IsActive;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(row);
    }

    public async Task<IReadOnlyList<PartPurchase>> PurchasesAsync(
        string? search = null, CancellationToken ct = default)
    {
        var q = db.PartPurchases.AsNoTracking().Include(x => x.Lines).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var p = $"%{search.Trim()}%";
            q = q.Where(x => EF.Functions.ILike(x.Number, p)
                          || EF.Functions.ILike(x.PartyName, p)
                          || (x.SupplierInvoiceNumber != null && EF.Functions.ILike(x.SupplierInvoiceNumber, p)));
        }

        return await q.OrderByDescending(x => x.Id).Take(500).ToListAsync(ct);
    }

    public Task<PartPurchase?> PurchaseAsync(int id, CancellationToken ct = default) =>
        db.PartPurchases.AsNoTracking()
            .Include(x => x.Lines).ThenInclude(x => x.Part)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<Result<PartPurchase>> ReceiveAsync(
        PartPurchaseInput input, CancellationToken ct = default)
    {
        var party = await db.Parties.FirstOrDefaultAsync(x => x.Id == input.PartyId, ct);
        if (party is null) return Result.Fail<PartPurchase>("Select a valid supplier.", "purchase.no-supplier");
        if (!party.IsSupplier)
            return Result.Fail<PartPurchase>($"{party.Name} is not marked as a supplier.", "purchase.not-supplier");

        if (input.Lines.Count == 0)
            return Result.Fail<PartPurchase>("Add at least one part.", "purchase.no-lines");

        if (input.Lines.Any(x => x.PartId == 0 || x.Quantity <= 0 || x.UnitCost < 0))
            return Result.Fail<PartPurchase>(
                "Every line needs a part, positive quantity and non-negative cost.", "purchase.bad-line");

        var ids = input.Lines.Select(x => x.PartId).Distinct().ToList();
        var parts = await db.Parts.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        if (parts.Count != ids.Count)
            return Result.Fail<PartPurchase>("A selected part no longer exists.", "purchase.part-missing");

        var stem = $"PUR-{clock.Today.Year % 100:D2}-";
        var count = await db.PartPurchases.IgnoreQueryFilters().CountAsync(x => x.Number.StartsWith(stem), ct);

        var row = new PartPurchase
        {
            Number = stem + $"{count + 1:D4}",
            PartyId = party.Id,
            PartyName = party.Name,
            SupplierInvoiceNumber = input.SupplierInvoiceNumber,
            PurchasedOn = input.PurchasedOn,
            TaxAmount = input.TaxAmount,
            DiscountAmount = input.DiscountAmount,
            OtherCharges = input.OtherCharges,
            Notes = input.Notes,
            ReceivedById = user.UserId ?? "system",
            ReceivedByName = user.Name ?? "System"
        };

        foreach (var x in input.Lines)
        {
            row.Lines.Add(new PartPurchaseLine
            {
                PartId = x.PartId,
                Quantity = x.Quantity,
                UnitCost = x.UnitCost,
                NewSellingPrice = x.NewSellingPrice,
                Remarks = x.Remarks
            });
        }

        db.PartPurchases.Add(row);

        foreach (var group in input.Lines.GroupBy(x => x.PartId))
        {
            var part = parts[group.Key];
            var qty = group.Sum(x => x.Quantity);
            var value = group.Sum(x => Math.Round(x.Quantity * x.UnitCost, 2));
            var oldValue = (part.AverageCost ?? 0) * part.PurchasedQuantity;

            part.PurchasedQuantity += qty;
            part.AverageCost = Math.Round((oldValue + value) / part.PurchasedQuantity, 4);

            // An older invoice entered late still moves the average, but must
            // never overwrite a newer last cost - that figure is for spotting
            // price drift, and rewriting it backwards hides exactly that.
            if (part.LastPurchasedOn is null || input.PurchasedOn >= part.LastPurchasedOn)
            {
                part.LastPurchaseCost = Math.Round(value / qty, 4);
                part.LastPurchasedOn = input.PurchasedOn;
                part.LastSupplierId = party.Id;
                part.LastSupplierName = party.Name;
            }

            var price = group.LastOrDefault(x => x.NewSellingPrice is >= 0)?.NewSellingPrice;
            if (price is not null) part.SellingPrice = price.Value;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(row);
    }

    public async Task<IReadOnlyList<PartPricePoint>> PriceHistoryAsync(
        int partId, CancellationToken ct = default) =>
        await db.PartPurchaseLines.AsNoTracking()
            .Where(x => x.PartId == partId)
            .OrderBy(x => x.Purchase!.PurchasedOn)
            .Select(x => new PartPricePoint(
                x.Purchase!.PurchasedOn, x.Purchase.Number, x.Purchase.PartyName, x.Quantity, x.UnitCost))
            .ToListAsync(ct);
}
