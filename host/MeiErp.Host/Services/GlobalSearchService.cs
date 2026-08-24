using MeiErp.Modules.Auto;
using MeiErp.Modules.Finance;
using MeiErp.Modules.GatePass;
using MeiErp.Modules.Hr;
using MeiErp.Modules.Inventory;
using MeiErp.Modules.Trade;
using MeiErp.Modules.Ledger;
using MeiErp.Modules.Repair;
using MeiErp.Modules.Tender;
using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Host.Services;

public sealed record GlobalSearchHit(string Label,string Kind,string Module,string Icon,string Url);
public interface IGlobalSearchService{Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(string term,CancellationToken ct=default);}

public sealed class GlobalSearchService(IServiceScopeFactory scopes,ICurrentUser user):IGlobalSearchService
{
    public async Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(string term,CancellationToken ct=default)
    {
        var text=(term??"").Trim();if(text.Length<2)return[];var pattern=$"%{text}%";using var scope=scopes.CreateScope();var sp=scope.ServiceProvider;var hits=new List<GlobalSearchHit>();
        if(user.InModule(FinanceModule.Key)){var db=sp.GetRequiredService<FinanceDbContext>();hits.AddRange(await db.Vouchers.AsNoTracking().Where(x=>EF.Functions.ILike(x.Number,pattern)||(x.Narration!=null&&EF.Functions.ILike(x.Narration,pattern))).OrderByDescending(x=>x.Id).Take(5).Select(x=>new GlobalSearchHit(x.Number+" — "+x.Narration,"Voucher","Finance","ReceiptLong","/finance/vouchers")).ToListAsync(ct));hits.AddRange(await db.Accounts.AsNoTracking().Where(x=>EF.Functions.ILike(x.Code,pattern)||EF.Functions.ILike(x.Name,pattern)).Take(4).Select(x=>new GlobalSearchHit(x.Code+" — "+x.Name,"Account","Finance","AccountTree","/finance/accounts")).ToListAsync(ct));}
        if(user.InModule(InventoryModule.Key)){var db=sp.GetRequiredService<InventoryDbContext>();hits.AddRange(await db.Items.AsNoTracking().Where(x=>EF.Functions.ILike(x.Code,pattern)||EF.Functions.ILike(x.Name,pattern)||(x.Barcode!=null&&EF.Functions.ILike(x.Barcode,pattern))).Take(5).Select(x=>new GlobalSearchHit(x.Code+" — "+x.Name,"Stock item","Inventory","Inventory2","/inventory/items")).ToListAsync(ct));}
        if(user.InModule(SalesModule.Key)||user.InModule(PurchaseModule.Key)){var db=sp.GetRequiredService<TradeDbContext>();var supplierSide=user.InModule(PurchaseModule.Key);hits.AddRange(await db.Parties.AsNoTracking().Where(x=>EF.Functions.ILike(x.Name,pattern)||(x.Phone!=null&&EF.Functions.ILike(x.Phone,pattern))).Where(x=>supplierSide?x.IsSupplier:x.IsCustomer).Take(4).Select(x=>new GlobalSearchHit(x.Name,supplierSide?"Supplier":"Customer",supplierSide?"Purchase":"Sales","Groups",supplierSide?"/purchase/suppliers":"/sales/customers")).ToListAsync(ct));}
        if(user.InModule(RepairModule.Key)){var db=sp.GetRequiredService<RepairDbContext>();hits.AddRange(await db.Jobs.AsNoTracking().Where(x=>EF.Functions.ILike(x.Number,pattern)||EF.Functions.ILike(x.CustomerName,pattern)||(x.SerialNumber!=null&&EF.Functions.ILike(x.SerialNumber,pattern))).OrderByDescending(x=>x.Id).Take(6).Select(x=>new GlobalSearchHit(x.Number+" — "+x.CustomerName,"Repair job","Repair","Build","/repair/jobs/"+x.Id)).ToListAsync(ct));}
        if(user.InModule(HrModule.Key)){var db=sp.GetRequiredService<HrDbContext>();hits.AddRange(await db.Employees.AsNoTracking().Where(x=>EF.Functions.ILike(x.Code,pattern)||EF.Functions.ILike(x.FullName,pattern)||(x.Cnic!=null&&EF.Functions.ILike(x.Cnic,pattern))).Take(5).Select(x=>new GlobalSearchHit(x.Code+" — "+x.FullName,"Employee","HR","Badge","/hr/employees")).ToListAsync(ct));}
        if(user.InModule(TenderModule.Key)){var db=sp.GetRequiredService<TenderDbContext>();hits.AddRange(await db.Tenders.AsNoTracking().Where(x=>EF.Functions.ILike(x.Reference,pattern)||EF.Functions.ILike(x.Title,pattern)||EF.Functions.ILike(x.ClientName,pattern)).Take(5).Select(x=>new GlobalSearchHit(x.Reference+" — "+x.Title,"Tender","Tender","Gavel","/tender/tenders")).ToListAsync(ct));hits.AddRange(await db.PhysicalFiles.AsNoTracking().Where(x=>EF.Functions.ILike(x.FileNumber,pattern)||EF.Functions.ILike(x.OwnerTitle,pattern)).Take(4).Select(x=>new GlobalSearchHit(x.FileNumber+" — "+x.OwnerTitle,"Physical file","Tender","Folder","/tender/files/"+x.Id)).ToListAsync(ct));}
        if(user.InModule(AutoModule.Key)){var db=sp.GetRequiredService<AutoDbContext>();hits.AddRange(await db.Vehicles.AsNoTracking().Where(x=>EF.Functions.ILike(x.Registration,pattern)||EF.Functions.ILike(x.Make,pattern)||(x.Model!=null&&EF.Functions.ILike(x.Model,pattern))).Take(5).Select(x=>new GlobalSearchHit(x.Registration+" — "+x.Make+" "+x.Model,"Vehicle","Fleet","DirectionsCar","/auto/vehicles")).ToListAsync(ct));}
        if(user.InModule(GatePassModule.Key)){var db=sp.GetRequiredService<GatePassDbContext>();hits.AddRange(await db.Passes.AsNoTracking().Where(x=>EF.Functions.ILike(x.Number,pattern)||EF.Functions.ILike(x.PartyName,pattern)||(x.VehicleNumber!=null&&EF.Functions.ILike(x.VehicleNumber,pattern))).Take(5).Select(x=>new GlobalSearchHit(x.Number+" — "+x.PartyName,"Gate pass","Gate Pass","LocalShipping","/gatepass/passes")).ToListAsync(ct));hits.AddRange(await db.DemoIssuances.AsNoTracking().Where(x=>EF.Functions.ILike(x.Number,pattern)||EF.Functions.ILike(x.CustomerName,pattern)||(x.CustomerPhone!=null&&EF.Functions.ILike(x.CustomerPhone,pattern))).Take(5).Select(x=>new GlobalSearchHit(x.Number+" — "+x.CustomerName,"Demo goods","Gate Pass","Inventory2","/gatepass/demos/"+x.Id)).ToListAsync(ct));}
        if(user.InModule(LedgerModule.Key)){var db=sp.GetRequiredService<LedgerDbContext>();hits.AddRange(await db.Ledgers.AsNoTracking().Where(x=>EF.Functions.ILike(x.Name,pattern)||EF.Functions.ILike(x.CounterpartyName,pattern)||(x.Reference!=null&&EF.Functions.ILike(x.Reference,pattern))).Take(5).Select(x=>new GlobalSearchHit(x.Name+" — "+x.CounterpartyName,"Plain ledger","Ledger","MenuBook","/ledger/ledgers/"+x.Id)).ToListAsync(ct));}
        return hits.Take(30).ToList();
    }
}
