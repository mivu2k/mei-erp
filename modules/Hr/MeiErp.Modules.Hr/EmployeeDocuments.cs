using MeiErp.Platform.Kernel;
using MeiErp.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Hr;

public enum EmployeeDocumentKind { Other=0, Contract=1, NationalId=2, Cv=3, Certificate=4, Licence=5, Photo=6, Appraisal=7 }

public sealed class EmployeeDocument : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public string Title { get; set; } = "";
    public EmployeeDocumentKind Kind { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long SizeBytes { get; set; }
    public byte[]? Content { get; set; }
    public bool HasFile { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public string? Notes { get; set; }
}

public sealed record EmployeeDocumentInput(int? Id,int EmployeeId,string Title,EmployeeDocumentKind Kind,
    DateOnly? ExpiresOn,string? Notes,string? FileName=null,string? ContentType=null,byte[]? Content=null);

public interface IEmployeeDocumentService
{
    Task<IReadOnlyList<EmployeeDocument>> ForEmployeeAsync(int employeeId,CancellationToken ct=default);
    Task<IReadOnlyList<EmployeeDocument>> ExpiringAsync(int withinDays,CancellationToken ct=default);
    Task<Result<EmployeeDocument>> SaveAsync(EmployeeDocumentInput input,CancellationToken ct=default);
    Task<Result> DeleteAsync(int id,CancellationToken ct=default);
    Task<EmployeeDocument?> FileAsync(int id,CancellationToken ct=default);
}

public sealed class EmployeeDocumentService(HrDbContext db,IClock clock):IEmployeeDocumentService
{
    public async Task<IReadOnlyList<EmployeeDocument>> ForEmployeeAsync(int employeeId,CancellationToken ct=default)=>
        await db.EmployeeDocuments.AsNoTracking().Where(x=>x.EmployeeId==employeeId).OrderByDescending(x=>x.Id).Select(x=>new EmployeeDocument{Id=x.Id,EmployeeId=x.EmployeeId,Title=x.Title,Kind=x.Kind,FileName=x.FileName,ContentType=x.ContentType,SizeBytes=x.SizeBytes,HasFile=x.HasFile,ExpiresOn=x.ExpiresOn,Notes=x.Notes}).ToListAsync(ct);
    public async Task<IReadOnlyList<EmployeeDocument>> ExpiringAsync(int withinDays,CancellationToken ct=default)
    {
        if(withinDays is <1 or >3650)throw new ArgumentOutOfRangeException(nameof(withinDays));var cutoff=clock.Today.AddDays(withinDays);
        return await db.EmployeeDocuments.AsNoTracking().Where(x=>x.ExpiresOn!=null&&x.ExpiresOn<=cutoff).OrderBy(x=>x.ExpiresOn).Select(x=>new EmployeeDocument{Id=x.Id,EmployeeId=x.EmployeeId,Title=x.Title,Kind=x.Kind,FileName=x.FileName,ContentType=x.ContentType,SizeBytes=x.SizeBytes,HasFile=x.HasFile,ExpiresOn=x.ExpiresOn,Notes=x.Notes,Employee=new Employee{Id=x.Employee!.Id,Code=x.Employee.Code,FullName=x.Employee.FullName,JoinedOn=x.Employee.JoinedOn}}).ToListAsync(ct);
    }
    public async Task<Result<EmployeeDocument>> SaveAsync(EmployeeDocumentInput input,CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(input.Title))return Result.Fail<EmployeeDocument>("Document title is required.","document.no-title");
        if(!await db.Employees.AnyAsync(x=>x.Id==input.EmployeeId,ct))return Result.Fail<EmployeeDocument>("Employee not found.","document.no-employee");
        if(input.Content?.LongLength>10*1024*1024)return Result.Fail<EmployeeDocument>("Files are limited to 10 MB.","document.too-large");
        EmployeeDocument row;
        if(input.Id is null or 0){row=new(){EmployeeId=input.EmployeeId};db.EmployeeDocuments.Add(row);}
        else{row=await db.EmployeeDocuments.FirstOrDefaultAsync(x=>x.Id==input.Id&&x.EmployeeId==input.EmployeeId,ct)??throw new InvalidOperationException("Document not found.");}
        row.Title=input.Title.Trim();row.Kind=input.Kind;row.ExpiresOn=input.ExpiresOn;row.Notes=input.Notes;
        if(input.Content is not null){row.Content=input.Content;row.HasFile=true;row.SizeBytes=input.Content.LongLength;row.FileName=input.FileName;row.ContentType=input.ContentType;}
        await db.SaveChangesAsync(ct);return Result.Success(row);
    }
    public async Task<Result> DeleteAsync(int id,CancellationToken ct=default)
    {
        var row=await db.EmployeeDocuments.FirstOrDefaultAsync(x=>x.Id==id,ct);if(row is null)return Result.Fail("Document not found.","document.not-found");
        db.EmployeeDocuments.Remove(row);await db.SaveChangesAsync(ct);return Result.Success();
    }
    public Task<EmployeeDocument?> FileAsync(int id,CancellationToken ct=default)=>db.EmployeeDocuments.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==id,ct);
}
