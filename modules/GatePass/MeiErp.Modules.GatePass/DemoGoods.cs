using MeiErp.Platform.Kernel;
using MeiErp.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.GatePass;

public enum DemoStatus { Issued = 0, PartiallyReturned = 1, Returned = 2, Cancelled = 3 }

public sealed class DemoIssuance : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }
    public string Number { get; set; } = "";
    public DemoStatus Status { get; set; }
    public string CustomerName { get; set; } = "";
    public string? CustomerPhone { get; set; }
    public string? CustomerReference { get; set; }
    public string? Department { get; set; }
    public string? ReferenceLetter { get; set; }
    public DateTime IssuedUtc { get; set; }
    public string IssuedByUserId { get; set; } = "";
    public string IssuedByName { get; set; } = "";
    public DateOnly? ExpectedReturnOn { get; set; }
    public DateTime? ReturnedUtc { get; set; }
    public string? ReceivedByName { get; set; }
    public string? ReturnCondition { get; set; }
    public string? Notes { get; set; }
    public List<DemoIssuanceItem> Items { get; set; } = [];
    public bool IsOverdue(DateOnly today) => Status is DemoStatus.Issued or DemoStatus.PartiallyReturned && ExpectedReturnOn < today;
}

public sealed class DemoIssuanceItem : Entity
{
    public int DemoIssuanceId { get; set; }
    public DemoIssuance? DemoIssuance { get; set; }
    public string Description { get; set; } = "";
    public string? SerialNumber { get; set; }
    public decimal Quantity { get; set; } = 1;
    public string? Accessories { get; set; }
    public string? Remarks { get; set; }
    public DateTime? ReturnedUtc { get; set; }
}

public sealed record DemoFilter(string? Search = null, DemoStatus? Status = null, bool OutstandingOnly = false, bool OverdueOnly = false);
public sealed record DemoItemInput(string Description, string? SerialNumber, decimal Quantity, string? Accessories, string? Remarks);
public sealed record DemoInput(int? Id, string CustomerName, string? CustomerPhone, string? CustomerReference,
    string? Department, string? ReferenceLetter, DateOnly? ExpectedReturnOn, string? Notes, IReadOnlyList<DemoItemInput> Items);

public interface IDemoIssuanceService
{
    Task<IReadOnlyList<DemoIssuance>> ListAsync(DemoFilter filter, CancellationToken ct = default);
    Task<DemoIssuance?> GetAsync(int id, CancellationToken ct = default);
    Task<Result<DemoIssuance>> SaveAsync(DemoInput input, CancellationToken ct = default);
    Task<Result<DemoIssuance>> ReturnAsync(int id, IReadOnlyCollection<int> itemIds, string? condition, CancellationToken ct = default);
    Task<Result> CancelAsync(int id, CancellationToken ct = default);
}

public sealed class DemoIssuanceService(GatePassDbContext db, ICurrentUser user, IClock clock) : IDemoIssuanceService
{
    public async Task<IReadOnlyList<DemoIssuance>> ListAsync(DemoFilter filter, CancellationToken ct = default)
    {
        var q = db.DemoIssuances.AsNoTracking().Include(x => x.Items).AsQueryable();
        if (filter.Status is { } status) q = q.Where(x => x.Status == status);
        if (filter.OutstandingOnly || filter.OverdueOnly) q = q.Where(x => x.Status == DemoStatus.Issued || x.Status == DemoStatus.PartiallyReturned);
        if (filter.OverdueOnly) q = q.Where(x => x.ExpectedReturnOn != null && x.ExpectedReturnOn < clock.Today);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim();
            q = q.Where(x => x.Number.Contains(s) || x.CustomerName.Contains(s) ||
                (x.CustomerPhone != null && x.CustomerPhone.Contains(s)) || (x.ReferenceLetter != null && x.ReferenceLetter.Contains(s)));
        }
        return await q.OrderByDescending(x => x.Id).Take(500).ToListAsync(ct);
    }

    public Task<DemoIssuance?> GetAsync(int id, CancellationToken ct = default) =>
        db.DemoIssuances.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<Result<DemoIssuance>> SaveAsync(DemoInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.CustomerName)) return Result.Fail<DemoIssuance>("Customer name is required.", "demo.no-customer");
        if (input.Items.Count == 0) return Result.Fail<DemoIssuance>("Add at least one item.", "demo.no-items");
        if (input.Items.Any(x => string.IsNullOrWhiteSpace(x.Description) || x.Quantity <= 0))
            return Result.Fail<DemoIssuance>("Every item needs a description and positive quantity.", "demo.bad-item");
        DemoIssuance row;
        if (input.Id is null or 0)
        {
            var stem = $"DEMO-{clock.Today.Year % 100:D2}-";
            var count = await db.DemoIssuances.IgnoreQueryFilters().CountAsync(x => x.Number.StartsWith(stem), ct);
            row = new() { Number = stem + (count + 1).ToString("D4"), Status = DemoStatus.Issued,
                IssuedUtc = clock.UtcNow, IssuedByUserId = user.UserId ?? "", IssuedByName = user.Name ?? "" };
            db.DemoIssuances.Add(row);
        }
        else
        {
            row = await db.DemoIssuances.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == input.Id, ct)
                ?? throw new InvalidOperationException("Demo issuance not found.");
            if (row.Status != DemoStatus.Issued) return Result.Fail<DemoIssuance>("Only a wholly outstanding issuance can be edited.", "demo.not-editable");
            db.DemoIssuanceItems.RemoveRange(row.Items); row.Items.Clear();
        }
        row.CustomerName = input.CustomerName.Trim(); row.CustomerPhone = input.CustomerPhone; row.CustomerReference = input.CustomerReference;
        row.Department = input.Department; row.ReferenceLetter = input.ReferenceLetter; row.ExpectedReturnOn = input.ExpectedReturnOn; row.Notes = input.Notes;
        row.Items.AddRange(input.Items.Select(x => new DemoIssuanceItem { Description=x.Description.Trim(), SerialNumber=x.SerialNumber,
            Quantity=x.Quantity, Accessories=x.Accessories, Remarks=x.Remarks }));
        await db.SaveChangesAsync(ct); return Result.Success(row);
    }

    public async Task<Result<DemoIssuance>> ReturnAsync(int id, IReadOnlyCollection<int> itemIds, string? condition, CancellationToken ct = default)
    {
        var row = await db.DemoIssuances.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return Result.Fail<DemoIssuance>("Demo issuance not found.", "demo.not-found");
        if (row.Status is DemoStatus.Returned or DemoStatus.Cancelled) return Result.Fail<DemoIssuance>("This issuance is already closed.", "demo.closed");
        var outstanding = row.Items.Where(x => x.ReturnedUtc == null).ToList();
        var selected = outstanding.Where(x => itemIds.Contains(x.Id)).ToList();
        if (selected.Count == 0) return Result.Fail<DemoIssuance>("Select at least one outstanding item.", "demo.no-return-items");
        var now = clock.UtcNow; foreach (var item in selected) item.ReturnedUtc = now;
        if (row.Items.All(x => x.ReturnedUtc != null)) { row.Status=DemoStatus.Returned; row.ReturnedUtc=now; row.ReturnCondition=condition; }
        else row.Status=DemoStatus.PartiallyReturned;
        row.ReceivedByName=user.Name; await db.SaveChangesAsync(ct); return Result.Success(row);
    }

    public async Task<Result> CancelAsync(int id, CancellationToken ct = default)
    {
        var row = await db.DemoIssuances.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return Result.Fail("Demo issuance not found.", "demo.not-found");
        if (row.Status != DemoStatus.Issued) return Result.Fail("An issuance with return activity cannot be cancelled.", "demo.not-cancellable");
        row.Status=DemoStatus.Cancelled; await db.SaveChangesAsync(ct); return Result.Success();
    }
}
