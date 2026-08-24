using MeiErp.Modules.Finance;
using MeiErp.Modules.GatePass;
using MeiErp.Modules.Inventory;
using MeiErp.Modules.Repair;
using MeiErp.Modules.Tender;
using MeiErp.Platform.Kernel;
using MeiErp.Platform.Workflow;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Host.Services;

public sealed record DashboardSnapshot(int AwaitingMe,int OverdueApprovals,int PendingPayments,int OpenRepairs,int LowStock,int OverdueReturns,int OverdueFiles,int ActiveTenders);
public interface IDashboardService{Task<DashboardSnapshot> GetAsync(CancellationToken ct=default);}
public sealed class DashboardService(IServiceScopeFactory scopes,ICurrentUser user,IClock clock):IDashboardService
{
    public async Task<DashboardSnapshot> GetAsync(CancellationToken ct=default)
    {
        using var scope=scopes.CreateScope();var sp=scope.ServiceProvider;var inbox=await sp.GetRequiredService<IApprovalEngine>().InboxAsync(ct);var payments=0;var repairs=0;var low=0;var returns=0;var files=0;var tenders=0;
        if(user.InModule(FinanceModule.Key))payments=await sp.GetRequiredService<FinanceDbContext>().PaymentRequests.CountAsync(x=>x.Status==PaymentRequestStatus.Pending||x.Status==PaymentRequestStatus.Approved,ct);
        if(user.InModule(RepairModule.Key))repairs=await sp.GetRequiredService<RepairDbContext>().Jobs.CountAsync(x=>x.Status!=JobStatus.Delivered&&x.Status!=JobStatus.Cancelled,ct);
        if(user.InModule(InventoryModule.Key))low=await sp.GetRequiredService<InventoryDbContext>().Items.CountAsync(x=>x.IsActive&&x.QuantityOnHand<=x.ReorderLevel,ct);
        if(user.InModule(GatePassModule.Key)){var db=sp.GetRequiredService<GatePassDbContext>();var rows=await db.Passes.AsNoTracking().Include(x=>x.Items).Where(x=>x.IsReturnable&&x.ExpectedBack<clock.Today&&x.Status!=PassStatus.Returned&&x.Status!=PassStatus.Cancelled).ToListAsync(ct);returns=rows.Count(x=>!x.IsFullyReturned)+await db.DemoIssuances.CountAsync(x=>(x.Status==DemoStatus.Issued||x.Status==DemoStatus.PartiallyReturned)&&x.ExpectedReturnOn<clock.Today,ct);}
        if(user.InModule(TenderModule.Key)){var db=sp.GetRequiredService<TenderDbContext>();tenders=await db.Tenders.CountAsync(x=>x.Status!=TenderStatus.Won&&x.Status!=TenderStatus.Lost&&x.Status!=TenderStatus.Cancelled,ct);var outFiles=await db.PhysicalFiles.AsNoTracking().Include(x=>x.Movements).Where(x=>x.Status==PhysicalFileStatus.Issued).ToListAsync(ct);files=outFiles.Count(x=>x.Movements.Where(m=>m.Action is FileMovementAction.Issued or FileMovementAction.Transferred).OrderByDescending(m=>m.MovedOn).ThenByDescending(m=>m.Id).FirstOrDefault()?.DueBack<clock.Today);}
        return new(inbox.Count,inbox.Count(x=>x.IsOverdue),payments,repairs,low,returns,files,tenders);
    }
}
