using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Inventory;

public enum StockUnitStatus{InStock,Issued,Sold,Returned,Damaged,Scrapped}
public class StockUnit:AuditableEntity
{
    public int ItemId{get;set;}public Item? Item{get;set;}public string SerialNumber{get;set;}="";public StockUnitStatus Status{get;set;}public int? StockBatchId{get;set;}public StockBatch? Batch{get;set;}public int? WarehouseId{get;set;}public Warehouse? Warehouse{get;set;}public decimal? UnitCost{get;set;}public DateOnly ReceivedOn{get;set;}public DateOnly? IssuedOn{get;set;}public string? IssuedTo{get;set;}public string? Reference{get;set;}public string? Notes{get;set;}public bool CountsAsStock=>Status is StockUnitStatus.InStock or StockUnitStatus.Returned;
}
public class StockBatch:AuditableEntity
{
    public int ItemId{get;set;}public Item? Item{get;set;}public string BatchNumber{get;set;}="";public int WarehouseId{get;set;}public Warehouse? Warehouse{get;set;}public DateOnly ReceivedOn{get;set;}public DateOnly? ExpiresOn{get;set;}public decimal Quantity{get;set;}public decimal RemainingQuantity{get;set;}public decimal? UnitCost{get;set;}public string? Reference{get;set;}public string? Notes{get;set;}public bool IsExpired(DateOnly today)=>ExpiresOn is { } date&&date<today;
}
public sealed record TrackingReceipt(int ItemId,decimal Quantity,decimal UnitCost,DateOnly Date,string? BatchNumber,DateOnly? ExpiresOn,IReadOnlyList<string> Serials,string? Reference);
public interface IStockTrackingService
{
    Task<Result> StageReceiptAsync(TrackingReceipt input,CancellationToken ct=default);
    Task<Result> StageIssueAsync(int itemId,decimal quantity,IReadOnlyList<string> serials,DateOnly date,string? issuedTo,string? reference,CancellationToken ct=default);
    Task<List<StockUnit>> UnitsAsync(int? itemId=null,StockUnitStatus? status=null,CancellationToken ct=default);
    Task<StockUnit?> FindSerialAsync(string serial,CancellationToken ct=default);
    Task<List<StockBatch>> BatchesAsync(int? itemId=null,bool openOnly=false,CancellationToken ct=default);
    Task<List<StockBatch>> ExpiringAsync(int days=30,CancellationToken ct=default);
}
public sealed class StockTrackingService(InventoryDbContext db,IClock clock):IStockTrackingService
{
    public async Task<Result> StageReceiptAsync(TrackingReceipt input,CancellationToken ct=default)
    {
        var item=await db.Items.FirstOrDefaultAsync(x=>x.Id==input.ItemId,ct);if(item is null)return Result.Fail("Item not found.");
        var serials=input.Serials.Select(x=>x.Trim()).Where(x=>x.Length>0).ToList();
        if(item.IsSerialized&&(input.Quantity!=decimal.Truncate(input.Quantity)||serials.Count!=(int)input.Quantity))return Result.Fail($"{item.Name} is serialized: enter exactly {input.Quantity:0} serial numbers.","stock.serial-count");
        if(!item.IsSerialized&&serials.Count>0)return Result.Fail($"{item.Name} is not serialized.","stock.unexpected-serials");
        if(serials.Count!=serials.Distinct(StringComparer.OrdinalIgnoreCase).Count())return Result.Fail("The same serial number appears more than once.","stock.duplicate-serial");
        if(serials.Count>0&&await db.StockUnits.AnyAsync(x=>x.ItemId==item.Id&&serials.Contains(x.SerialNumber),ct))return Result.Fail("One of those serial numbers is already recorded.","stock.serial-exists");
        if(item.IsBatchTracked&&string.IsNullOrWhiteSpace(input.BatchNumber))return Result.Fail($"{item.Name} requires a batch number.","stock.batch-required");
        if(!item.IsBatchTracked&&!string.IsNullOrWhiteSpace(input.BatchNumber))return Result.Fail($"{item.Name} is not batch tracked.","stock.unexpected-batch");
        // Scoped to the item's own stock book, exactly as StockService does:
        // serials and batches have to land on the same shelf the quantity does,
        // or the tracked units and the balance describe different warehouses.
        var warehouse=db.Warehouses.Local.FirstOrDefault(x=>x.IsDefault&&x.DomainId==item.DomainId)??await db.Warehouses.Where(x=>x.DomainId==item.DomainId).OrderByDescending(x=>x.IsDefault).ThenBy(x=>x.Id).FirstOrDefaultAsync(ct);if(warehouse is null){warehouse=new(){Name="Main warehouse",Code="WH-"+item.DomainId,IsDefault=true,DomainId=item.DomainId};db.Add(warehouse);}
        StockBatch? batch=null;if(item.IsBatchTracked){batch=new(){ItemId=item.Id,BatchNumber=input.BatchNumber!.Trim(),Warehouse=warehouse,ReceivedOn=input.Date,ExpiresOn=input.ExpiresOn,Quantity=input.Quantity,RemainingQuantity=input.Quantity,UnitCost=input.UnitCost,Reference=input.Reference};db.Add(batch);}
        foreach(var serial in serials)db.StockUnits.Add(new(){ItemId=item.Id,SerialNumber=serial,Status=StockUnitStatus.InStock,Warehouse=warehouse,Batch=batch,UnitCost=input.UnitCost,ReceivedOn=input.Date,Reference=input.Reference});return Result.Success();
    }
    public async Task<Result> StageIssueAsync(int itemId,decimal quantity,IReadOnlyList<string> serials,DateOnly date,string? issuedTo,string? reference,CancellationToken ct=default)
    {
        var item=await db.Items.FirstOrDefaultAsync(x=>x.Id==itemId,ct);if(item is null)return Result.Fail("Item not found.");var clean=serials.Select(x=>x.Trim()).Where(x=>x.Length>0).ToList();
        if(item.IsSerialized&&(quantity!=decimal.Truncate(quantity)||clean.Count!=(int)quantity))return Result.Fail($"Choose exactly {quantity:0} serial numbers for {item.Name}.","stock.serial-count");if(!item.IsSerialized&&clean.Count>0)return Result.Fail("This item is not serialized.","stock.unexpected-serials");
        if(item.IsSerialized){var units=await db.StockUnits.Where(x=>x.ItemId==itemId&&clean.Contains(x.SerialNumber)).ToListAsync(ct);if(units.Count!=clean.Count||units.Any(x=>!x.CountsAsStock))return Result.Fail("A selected serial is not available in stock.","stock.serial-unavailable");foreach(var unit in units){unit.Status=StockUnitStatus.Sold;unit.IssuedOn=date;unit.IssuedTo=issuedTo;unit.Reference=reference;unit.WarehouseId=null;}}
        if(item.IsBatchTracked){var left=quantity;var batches=await db.StockBatches.Where(x=>x.ItemId==itemId&&x.RemainingQuantity>0).OrderBy(x=>x.ExpiresOn??DateOnly.MaxValue).ThenBy(x=>x.ReceivedOn).ToListAsync(ct);if(batches.Sum(x=>x.RemainingQuantity)<quantity)return Result.Fail("Tracked batches do not contain enough quantity.","stock.batch-insufficient");foreach(var batch in batches){var used=Math.Min(left,batch.RemainingQuantity);batch.RemainingQuantity-=used;left-=used;if(left==0)break;}}
        return Result.Success();
    }
    public async Task<List<StockUnit>> UnitsAsync(int? itemId=null,StockUnitStatus? status=null,CancellationToken ct=default){var q=db.StockUnits.AsNoTracking().Include(x=>x.Item).Include(x=>x.Warehouse).AsQueryable();if(itemId is { } id)q=q.Where(x=>x.ItemId==id);if(status is { } s)q=q.Where(x=>x.Status==s);return await q.OrderBy(x=>x.Item!.Name).ThenBy(x=>x.SerialNumber).ToListAsync(ct);}
    public Task<StockUnit?> FindSerialAsync(string serial,CancellationToken ct=default)=>db.StockUnits.AsNoTracking().Include(x=>x.Item).Include(x=>x.Warehouse).FirstOrDefaultAsync(x=>x.SerialNumber==serial,ct);
    public async Task<List<StockBatch>> BatchesAsync(int? itemId=null,bool openOnly=false,CancellationToken ct=default){var q=db.StockBatches.AsNoTracking().Include(x=>x.Item).Include(x=>x.Warehouse).AsQueryable();if(itemId is { } id)q=q.Where(x=>x.ItemId==id);if(openOnly)q=q.Where(x=>x.RemainingQuantity>0);return await q.OrderBy(x=>x.ExpiresOn??DateOnly.MaxValue).ToListAsync(ct);}
    public Task<List<StockBatch>> ExpiringAsync(int days=30,CancellationToken ct=default){var cutoff=clock.Today.AddDays(days);return db.StockBatches.AsNoTracking().Include(x=>x.Item).Include(x=>x.Warehouse).Where(x=>x.RemainingQuantity>0&&x.ExpiresOn!=null&&x.ExpiresOn<=cutoff).OrderBy(x=>x.ExpiresOn).ToListAsync(ct);}
}
