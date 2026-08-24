using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Repair;

public enum RepairCatalogKind { Symptom, Accessory, Brand, DeviceType }

public class RepairCatalogItem : AuditableEntity
{
    public RepairCatalogKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string? Category { get; set; }
}

public class RepairDiagnosis : AuditableEntity
{
    public int JobId { get; set; }
    public Job? Job { get; set; }
    public string TechnicianId { get; set; } = "";
    public string TechnicianName { get; set; } = "";
    public string Findings { get; set; } = "";
    public string? RequiredParts { get; set; }
    public string? RequiredLabour { get; set; }
    public int? EstimatedDays { get; set; }
    public decimal? EstimatedHours { get; set; }
    public string? WorkPerformed { get; set; }
    public string? InternalNotes { get; set; }
}

public class RepairStatusHistory : Entity
{
    public int JobId { get; set; }
    public Job? Job { get; set; }
    public JobStatus FromStatus { get; set; }
    public JobStatus ToStatus { get; set; }
    public string ChangedById { get; set; } = "";
    public string ChangedByName { get; set; } = "";
    public DateTime ChangedUtc { get; set; }
    public string? Note { get; set; }
}

public sealed record DiagnosisInput(string Findings,string? RequiredParts,string? RequiredLabour,
    int? EstimatedDays,decimal? EstimatedHours,string? WorkPerformed,string? InternalNotes);

public interface IRepairWorkshopDepthService
{
    Task<IReadOnlyList<RepairCatalogItem>> CatalogAsync(RepairCatalogKind kind,CancellationToken ct=default);
    Task<Result<RepairCatalogItem>> AddCatalogAsync(RepairCatalogKind kind,string name,string? category,CancellationToken ct=default);
    Task<Result> RemoveCatalogAsync(int id,CancellationToken ct=default);
    Task<IReadOnlyList<RepairDiagnosis>> DiagnosesAsync(int jobId,CancellationToken ct=default);
    Task<IReadOnlyList<RepairStatusHistory>> HistoryAsync(int jobId,CancellationToken ct=default);
    Task<Result<RepairDiagnosis>> AddDiagnosisAsync(int jobId,DiagnosisInput input,CancellationToken ct=default);
}

public sealed class RepairWorkshopDepthService(RepairDbContext db,ICurrentUser user,IClock clock):IRepairWorkshopDepthService
{
    public async Task<IReadOnlyList<RepairCatalogItem>> CatalogAsync(RepairCatalogKind kind,CancellationToken ct=default)=>await db.RepairCatalogItems.AsNoTracking().Where(x=>x.Kind==kind).OrderBy(x=>x.Category).ThenBy(x=>x.Name).ToListAsync(ct);
    public async Task<Result<RepairCatalogItem>> AddCatalogAsync(RepairCatalogKind kind,string name,string? category,CancellationToken ct=default)
    {
        name=name.Trim();if(name.Length==0)return Result.Fail<RepairCatalogItem>("A name is required.","catalog.no-name");
        if(await db.RepairCatalogItems.AnyAsync(x=>x.Kind==kind&&EF.Functions.ILike(x.Name,name),ct))return Result.Fail<RepairCatalogItem>("That entry already exists.","catalog.duplicate");
        var row=new RepairCatalogItem{Kind=kind,Name=name,Category=string.IsNullOrWhiteSpace(category)?null:category.Trim()};db.Add(row);await db.SaveChangesAsync(ct);return Result.Success(row);
    }
    public async Task<Result> RemoveCatalogAsync(int id,CancellationToken ct=default)
    {
        var row=await db.RepairCatalogItems.FirstOrDefaultAsync(x=>x.Id==id,ct);if(row is null)return Result.Fail("Catalog entry not found.","catalog.not-found");row.IsDeleted=true;await db.SaveChangesAsync(ct);return Result.Success();
    }
    public async Task<IReadOnlyList<RepairDiagnosis>> DiagnosesAsync(int jobId,CancellationToken ct=default)=>await db.RepairDiagnoses.AsNoTracking().Where(x=>x.JobId==jobId).OrderByDescending(x=>x.Id).ToListAsync(ct);
    public async Task<IReadOnlyList<RepairStatusHistory>> HistoryAsync(int jobId,CancellationToken ct=default)=>await db.RepairStatusHistory.AsNoTracking().Where(x=>x.JobId==jobId).OrderByDescending(x=>x.Id).ToListAsync(ct);
    public async Task<Result<RepairDiagnosis>> AddDiagnosisAsync(int jobId,DiagnosisInput input,CancellationToken ct=default)
    {
        var open=JobWorkflow.Open;
        if(!await db.Jobs.AnyAsync(x=>x.Id==jobId&&open.Contains(x.Status),ct))return Result.Fail<RepairDiagnosis>("An open repair job is required.","diagnosis.job-closed");
        if(string.IsNullOrWhiteSpace(input.Findings))return Result.Fail<RepairDiagnosis>("Record the technician's findings.","diagnosis.no-findings");
        if(input.EstimatedDays is <0||input.EstimatedHours is <0)return Result.Fail<RepairDiagnosis>("Estimates cannot be negative.","diagnosis.bad-estimate");
        var row=new RepairDiagnosis{JobId=jobId,TechnicianId=user.UserId??"system",TechnicianName=user.Name??"System",Findings=input.Findings.Trim(),RequiredParts=input.RequiredParts,RequiredLabour=input.RequiredLabour,EstimatedDays=input.EstimatedDays,EstimatedHours=input.EstimatedHours,WorkPerformed=input.WorkPerformed,InternalNotes=input.InternalNotes};db.Add(row);
        var job=await db.Jobs.FirstAsync(x=>x.Id==jobId,ct);job.Diagnosis=row.Findings;if(job.Status==JobStatus.Received){db.RepairStatusHistory.Add(new(){JobId=job.Id,FromStatus=job.Status,ToStatus=JobStatus.Diagnosing,ChangedById=user.UserId??"system",ChangedByName=user.Name??"System",ChangedUtc=clock.UtcNow,Note="Diagnosis recorded"});job.Status=JobStatus.Diagnosing;job.AssignedToUserId=user.UserId;job.AssignedToName=user.Name;}
        await db.SaveChangesAsync(ct);return Result.Success(row);
    }
}
