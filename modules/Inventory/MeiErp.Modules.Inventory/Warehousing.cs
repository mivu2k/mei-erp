using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Inventory;

public class Warehouse:AuditableEntity
{
    /// <summary>
    /// Which set of stock books this location belongs to. A warehouse holds one
    /// book's goods only, so the workshop's bins and the main store's racks are
    /// never counted together. <see cref="IsDefault"/> is per domain.
    /// </summary>
    public int DomainId{get;set;}
    public StockDomain? Domain{get;set;}

    public string Name{get;set;}="";public string? Code{get;set;}public string? Address{get;set;}public string? Notes{get;set;}public bool IsActive{get;set;}=true;public bool IsDefault{get;set;}
}
public class WarehouseBalance:Entity
{
    public int WarehouseId{get;set;}public Warehouse? Warehouse{get;set;}public int ItemId{get;set;}public Item? Item{get;set;}public decimal Quantity{get;set;}
}
public enum TransferStatus{Draft,InTransit,Received,Cancelled}
public class StockTransfer:AuditableEntity,IConcurrencyChecked
{
    public uint Version{get;set;}public string Number{get;set;}="";public DateOnly Date{get;set;}public TransferStatus Status{get;set;}public int FromWarehouseId{get;set;}public Warehouse? FromWarehouse{get;set;}public int ToWarehouseId{get;set;}public Warehouse? ToWarehouse{get;set;}public string? Reference{get;set;}public string? Notes{get;set;}public string RaisedByName{get;set;}="";public DateTime? DispatchedUtc{get;set;}public string? DispatchedBy{get;set;}public DateTime? ReceivedUtc{get;set;}public string? ReceivedBy{get;set;}public List<StockTransferLine> Lines{get;set;}=[];
}
public class StockTransferLine:Entity
{
    public int StockTransferId{get;set;}public StockTransfer? Transfer{get;set;}public int ItemId{get;set;}public Item? Item{get;set;}public string ItemCode{get;set;}="";public string ItemName{get;set;}="";public decimal Quantity{get;set;}public decimal? ReceivedQuantity{get;set;}public string? Note{get;set;}public decimal Shortfall=>ReceivedQuantity is { } r?r-Quantity:0;
}
public enum StockCountStatus{Draft,Counted,Posted,Cancelled}
public class InventoryCount:AuditableEntity,IConcurrencyChecked
{
    public uint Version{get;set;}public string Number{get;set;}="";public DateOnly Date{get;set;}public int WarehouseId{get;set;}public Warehouse? Warehouse{get;set;}public StockCountStatus Status{get;set;}public string CountedByName{get;set;}="";public string? Notes{get;set;}public DateTime? PostedUtc{get;set;}public List<InventoryCountLine> Lines{get;set;}=[];public int VarianceCount=>Lines.Count(x=>x.Variance!=0);
}
public class InventoryCountLine:Entity
{
    public int InventoryCountId{get;set;}public InventoryCount? Count{get;set;}public int ItemId{get;set;}public Item? Item{get;set;}public string ItemCode{get;set;}="";public string ItemName{get;set;}="";public decimal SystemQuantity{get;set;}public decimal? CountedQuantity{get;set;}public string? Note{get;set;}public decimal Variance=>CountedQuantity is { } q?q-SystemQuantity:0;
}

public interface IWarehouseService
{
    /// <param name="domainId">One stock book's locations. Null lists every book's.</param>
    Task<List<Warehouse>> ListAsync(int? domainId=null,CancellationToken ct=default);Task<Warehouse> SaveAsync(Warehouse value,CancellationToken ct=default);Task DeleteAsync(int id,CancellationToken ct=default);Task<List<WarehouseBalance>> BalancesAsync(int warehouseId,CancellationToken ct=default);
}
public sealed class WarehouseService(InventoryDbContext db):IWarehouseService
{
    public Task<List<Warehouse>> ListAsync(int? domainId=null,CancellationToken ct=default)=>db.Warehouses.AsNoTracking().Where(x=>domainId==null||x.DomainId==domainId).OrderByDescending(x=>x.IsDefault).ThenBy(x=>x.Name).ToListAsync(ct);
    public async Task<Warehouse> SaveAsync(Warehouse value,CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(value.Name))throw new InvalidOperationException("Warehouse name is required.");
        if(value.DomainId==0)value.DomainId=await db.StockDomains.OrderByDescending(x=>x.IsDefault).ThenBy(x=>x.Id).Select(x=>x.Id).FirstOrDefaultAsync(ct);
        if(value.DomainId==0)throw new InvalidOperationException("No stock book exists to file this warehouse under.");
        // First location in a book is that book's default, so stock arriving
        // there has somewhere to land without anyone configuring it.
        if(value.Id==0){if(!await db.Warehouses.AnyAsync(x=>x.DomainId==value.DomainId,ct))value.IsDefault=true;db.Add(value);}else db.Update(value);
        await db.SaveChangesAsync(ct);
        // Default is per book, not global: each book needs its own landing shelf.
        if(value.IsDefault)await db.Warehouses.Where(x=>x.Id!=value.Id&&x.IsDefault&&x.DomainId==value.DomainId).ExecuteUpdateAsync(x=>x.SetProperty(y=>y.IsDefault,false),ct);
        return value;
    }
    public async Task DeleteAsync(int id,CancellationToken ct=default){var row=await db.Warehouses.FindAsync([id],ct);if(row is null)return;if(await db.WarehouseBalances.AnyAsync(x=>x.WarehouseId==id&&x.Quantity!=0,ct))throw new InvalidOperationException("Move all stock out before deleting this warehouse.");if(row.IsDefault&&await db.Warehouses.CountAsync(x=>x.DomainId==row.DomainId,ct)>1)throw new InvalidOperationException("Choose another default warehouse in this stock book first.");db.Remove(row);await db.SaveChangesAsync(ct);}
    public Task<List<WarehouseBalance>> BalancesAsync(int warehouseId,CancellationToken ct=default)=>db.WarehouseBalances.AsNoTracking().Include(x=>x.Item).Where(x=>x.WarehouseId==warehouseId&&x.Quantity!=0).OrderBy(x=>x.Item!.Name).ToListAsync(ct);
}

public sealed record TransferLineInput(int ItemId,decimal Quantity,string? Note=null);
public sealed record TransferInput(int? Id,DateOnly Date,int FromWarehouseId,int ToWarehouseId,string? Reference,string? Notes,IReadOnlyList<TransferLineInput> Lines);
public interface ITransferService{Task<List<StockTransfer>> ListAsync(CancellationToken ct=default);Task<StockTransfer?> GetAsync(int id,CancellationToken ct=default);Task<Result<StockTransfer>> SaveAsync(TransferInput input,CancellationToken ct=default);Task<Result> DispatchAsync(int id,string actor,CancellationToken ct=default);Task<Result> ReceiveAsync(int id,IReadOnlyDictionary<int,decimal> quantities,string actor,CancellationToken ct=default);Task<Result> CancelAsync(int id,CancellationToken ct=default);}
public sealed class TransferService(InventoryDbContext db,IClock clock,ICurrentUser user):ITransferService
{
    public Task<List<StockTransfer>> ListAsync(CancellationToken ct=default)=>db.StockTransfers.AsNoTracking().Include(x=>x.FromWarehouse).Include(x=>x.ToWarehouse).Include(x=>x.Lines).OrderByDescending(x=>x.Id).Take(300).ToListAsync(ct);
    public Task<StockTransfer?> GetAsync(int id,CancellationToken ct=default)=>db.StockTransfers.Include(x=>x.FromWarehouse).Include(x=>x.ToWarehouse).Include(x=>x.Lines).FirstOrDefaultAsync(x=>x.Id==id,ct);
    public async Task<Result<StockTransfer>> SaveAsync(TransferInput input,CancellationToken ct=default){if(input.FromWarehouseId==input.ToWarehouseId)return Result.Fail<StockTransfer>("Source and destination must differ.");if(input.Lines.Count==0||input.Lines.Any(x=>x.Quantity<=0))return Result.Fail<StockTransfer>("Add at least one positive quantity.");
        // A transfer moves goods between shelves, not between sets of books. An
        // item belongs to one book, so a cross-book transfer could never be
        // received into a warehouse that is allowed to hold it - refused here
        // with an explanation rather than failing obscurely at dispatch.
        var ends=await db.Warehouses.AsNoTracking().Where(x=>x.Id==input.FromWarehouseId||x.Id==input.ToWarehouseId).Select(x=>new{x.Id,x.DomainId}).ToListAsync(ct);
        if(ends.Count!=2)return Result.Fail<StockTransfer>("Source or destination warehouse no longer exists.");
        if(ends[0].DomainId!=ends[1].DomainId)return Result.Fail<StockTransfer>("A transfer cannot cross stock books. Sell out of one and buy into the other.");
        var domain=ends[0].DomainId;
        var items=await db.Items.Where(x=>input.Lines.Select(l=>l.ItemId).Contains(x.Id)).ToDictionaryAsync(x=>x.Id,ct);if(items.Count!=input.Lines.Select(x=>x.ItemId).Distinct().Count())return Result.Fail<StockTransfer>("An item no longer exists.");
        if(items.Values.Any(x=>x.DomainId!=domain))return Result.Fail<StockTransfer>("Every item on a transfer must belong to the same stock book as the warehouses.");StockTransfer row;if(input.Id is { } id){row=await GetAsync(id,ct)??throw new InvalidOperationException("Transfer not found.");if(row.Status!=TransferStatus.Draft)return Result.Fail<StockTransfer>("Only draft transfers can be edited.");db.StockTransferLines.RemoveRange(row.Lines);}else{row=new(){Number=await Next("TRF",ct),RaisedByName=user.Name??"Unknown"};db.Add(row);}row.Date=input.Date;row.FromWarehouseId=input.FromWarehouseId;row.ToWarehouseId=input.ToWarehouseId;row.Reference=input.Reference;row.Notes=input.Notes;row.Lines=input.Lines.Select(x=>new StockTransferLine{ItemId=x.ItemId,ItemCode=items[x.ItemId].Code,ItemName=items[x.ItemId].Name,Quantity=x.Quantity,Note=x.Note}).ToList();await db.SaveChangesAsync(ct);return Result.Success(row);}
    public async Task<Result> DispatchAsync(int id,string actor,CancellationToken ct=default){var row=await GetAsync(id,ct);if(row is null)return Result.Fail("Transfer not found.");if(row.Status!=TransferStatus.Draft)return Result.Fail("Only a draft transfer can be dispatched.");foreach(var line in row.Lines){var balance=await Balance(row.FromWarehouseId,line.ItemId,ct);if(balance.Quantity<line.Quantity)return Result.Fail($"Only {balance.Quantity:0.##} of {line.ItemName} is held at the source warehouse.");}foreach(var line in row.Lines)(await Balance(row.FromWarehouseId,line.ItemId,ct)).Quantity-=line.Quantity;row.Status=TransferStatus.InTransit;row.DispatchedUtc=clock.UtcNow;row.DispatchedBy=actor;await db.SaveChangesAsync(ct);return Result.Success();}
    public async Task<Result> ReceiveAsync(int id,IReadOnlyDictionary<int,decimal> quantities,string actor,CancellationToken ct=default){var row=await GetAsync(id,ct);if(row is null)return Result.Fail("Transfer not found.");if(row.Status!=TransferStatus.InTransit)return Result.Fail("Only an in-transit transfer can be received.");foreach(var line in row.Lines){var q=quantities.GetValueOrDefault(line.Id,line.Quantity);if(q<0||q>line.Quantity)return Result.Fail("Received quantity must be between zero and dispatched quantity.");line.ReceivedQuantity=q;(await Balance(row.ToWarehouseId,line.ItemId,ct)).Quantity+=q;}row.Status=TransferStatus.Received;row.ReceivedUtc=clock.UtcNow;row.ReceivedBy=actor;await db.SaveChangesAsync(ct);return Result.Success();}
    public async Task<Result> CancelAsync(int id,CancellationToken ct=default){var row=await GetAsync(id,ct);if(row is null)return Result.Fail("Transfer not found.");if(row.Status!=TransferStatus.Draft)return Result.Fail("Only a draft transfer can be cancelled.");row.Status=TransferStatus.Cancelled;await db.SaveChangesAsync(ct);return Result.Success();}
    private async Task<WarehouseBalance> Balance(int warehouse,int item,CancellationToken ct){var row=await db.WarehouseBalances.FirstOrDefaultAsync(x=>x.WarehouseId==warehouse&&x.ItemId==item,ct);if(row is not null)return row;row=new(){WarehouseId=warehouse,ItemId=item};db.Add(row);return row;}
    private async Task<string> Next(string prefix,CancellationToken ct)=>prefix+"-"+clock.Today.Year+"-"+(await db.StockTransfers.CountAsync(ct)+1).ToString("D5");
}

public sealed record CountLineInput(int ItemId,decimal? Quantity,string? Note=null);
public interface IInventoryCountService{Task<List<InventoryCount>> ListAsync(CancellationToken ct=default);Task<InventoryCount?> GetAsync(int id,CancellationToken ct=default);Task<Result<InventoryCount>> CreateAsync(int warehouseId,string? notes,CancellationToken ct=default);Task<Result> RecordAsync(int id,IReadOnlyList<CountLineInput> lines,CancellationToken ct=default);Task<Result> PostAsync(int id,CancellationToken ct=default);Task<Result> CancelAsync(int id,CancellationToken ct=default);}
public sealed class InventoryCountService(InventoryDbContext db,IClock clock,ICurrentUser user):IInventoryCountService
{
    public Task<List<InventoryCount>> ListAsync(CancellationToken ct=default)=>db.InventoryCounts.AsNoTracking().Include(x=>x.Warehouse).Include(x=>x.Lines).OrderByDescending(x=>x.Id).ToListAsync(ct);
    public Task<InventoryCount?> GetAsync(int id,CancellationToken ct=default)=>db.InventoryCounts.Include(x=>x.Warehouse).Include(x=>x.Lines).FirstOrDefaultAsync(x=>x.Id==id,ct);
    public async Task<Result<InventoryCount>> CreateAsync(int warehouseId,string? notes,CancellationToken ct=default){if(!await db.Warehouses.AnyAsync(x=>x.Id==warehouseId,ct))return Result.Fail<InventoryCount>("Warehouse not found.");var balances=await db.WarehouseBalances.Include(x=>x.Item).Where(x=>x.WarehouseId==warehouseId).ToListAsync(ct);var row=new InventoryCount{Number=$"CNT-{clock.Today.Year}-{await db.InventoryCounts.CountAsync(ct)+1:D5}",Date=clock.Today,WarehouseId=warehouseId,CountedByName=user.Name??"Unknown",Notes=notes,Lines=balances.Select(x=>new InventoryCountLine{ItemId=x.ItemId,ItemCode=x.Item!.Code,ItemName=x.Item.Name,SystemQuantity=x.Quantity}).ToList()};db.Add(row);await db.SaveChangesAsync(ct);return Result.Success(row);}
    public async Task<Result> RecordAsync(int id,IReadOnlyList<CountLineInput> lines,CancellationToken ct=default){var row=await GetAsync(id,ct);if(row is null)return Result.Fail("Count not found.");if(row.Status is not StockCountStatus.Draft and not StockCountStatus.Counted)return Result.Fail("This count can no longer be edited.");foreach(var line in row.Lines){var input=lines.FirstOrDefault(x=>x.ItemId==line.ItemId);line.CountedQuantity=input?.Quantity;line.Note=input?.Note;}if(row.Lines.Any(x=>x.CountedQuantity is null or <0))return Result.Fail("Every line needs a non-negative counted quantity.");row.Status=StockCountStatus.Counted;await db.SaveChangesAsync(ct);return Result.Success();}
    public async Task<Result> PostAsync(int id,CancellationToken ct=default){var row=await GetAsync(id,ct);if(row is null)return Result.Fail("Count not found.");if(row.Status!=StockCountStatus.Counted)return Result.Fail("Finish counting before posting.");foreach(var line in row.Lines.Where(x=>x.Variance!=0)){var balance=await db.WarehouseBalances.SingleAsync(x=>x.WarehouseId==row.WarehouseId&&x.ItemId==line.ItemId,ct);balance.Quantity=line.CountedQuantity!.Value;var item=await db.Items.SingleAsync(x=>x.Id==line.ItemId,ct);item.QuantityOnHand+=line.Variance;db.StockMovements.Add(new(){ItemId=item.Id,ItemCode=item.Code,ItemName=item.Name,DomainId=item.DomainId,Date=row.Date,Type=StockMovementType.Adjustment,Quantity=line.Variance,UnitCost=item.AverageCost,BalanceAfter=item.QuantityOnHand,Reference=row.Number,Narration=line.Note??$"Stock count at {row.Warehouse!.Name}",WarehouseId=row.WarehouseId});}row.Status=StockCountStatus.Posted;row.PostedUtc=clock.UtcNow;await db.SaveChangesAsync(ct);return Result.Success();}
    public async Task<Result> CancelAsync(int id,CancellationToken ct=default){var row=await GetAsync(id,ct);if(row is null)return Result.Fail("Count not found.");if(row.Status==StockCountStatus.Posted)return Result.Fail("A posted count cannot be cancelled.");row.Status=StockCountStatus.Cancelled;await db.SaveChangesAsync(ct);return Result.Success();}
}
