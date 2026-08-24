using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace MeiErp.Modules.Repair;

public enum RepairPhotoKind { Before, After, Damage, Diagnostic, Other }

public class RepairPhoto : AuditableEntity
{
    public int JobId { get; set; }
    public Job? Job { get; set; }
    public RepairPhotoKind Kind { get; set; }
    public string StoredName { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Length { get; set; }
    public string? Caption { get; set; }
    public string UploadedByName { get; set; } = "";
}

public sealed record RepairPhotoFile(RepairPhoto Photo,byte[] Content);

public interface IRepairPhotoService
{
    Task<IReadOnlyList<RepairPhoto>> ListAsync(int jobId,CancellationToken ct=default);
    Task<Result<RepairPhoto>> UploadAsync(int jobId,RepairPhotoKind kind,string originalName,
        string contentType,long length,string? caption,Stream content,CancellationToken ct=default);
    Task<RepairPhotoFile?> GetAsync(int id,CancellationToken ct=default);
    Task<Result> RemoveAsync(int id,CancellationToken ct=default);
}

public sealed class RepairPhotoService(RepairDbContext db,IHostEnvironment environment,ICurrentUser user):IRepairPhotoService
{
    private const long MaxBytes=10*1024*1024;
    private static readonly Dictionary<string,string> Allowed=new(StringComparer.OrdinalIgnoreCase){{"image/jpeg",".jpg"},{"image/png",".png"},{"image/webp",".webp"},{"application/pdf",".pdf"}};
    private string Root=>Path.Combine(environment.ContentRootPath,"App_Data","repair-photos");
    public async Task<IReadOnlyList<RepairPhoto>> ListAsync(int jobId,CancellationToken ct=default)=>await db.RepairPhotos.AsNoTracking().Where(x=>x.JobId==jobId).OrderByDescending(x=>x.Id).ToListAsync(ct);
    public async Task<Result<RepairPhoto>> UploadAsync(int jobId,RepairPhotoKind kind,string originalName,string contentType,long length,string? caption,Stream content,CancellationToken ct=default)
    {
        if(!await db.Jobs.AnyAsync(x=>x.Id==jobId,ct))return Result.Fail<RepairPhoto>("Repair job not found.","photo.no-job");
        if(length<=0||length>MaxBytes)return Result.Fail<RepairPhoto>("Photo must be between 1 byte and 10 MB.","photo.bad-size");
        if(!Allowed.TryGetValue(contentType,out var extension))return Result.Fail<RepairPhoto>("Only JPEG, PNG, WebP, or PDF evidence is accepted.","photo.bad-type");
        Directory.CreateDirectory(Root);var stored=$"{Guid.NewGuid():N}{extension}";var path=Path.Combine(Root,stored);
        try
        {
            await using(var target=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true))await content.CopyToAsync(target,ct);
            var actual=new FileInfo(path).Length;if(actual!=length||actual>MaxBytes){File.Delete(path);return Result.Fail<RepairPhoto>("Uploaded content length did not match the file metadata.","photo.length-mismatch");}
            var row=new RepairPhoto{JobId=jobId,Kind=kind,StoredName=stored,OriginalName=Path.GetFileName(originalName),ContentType=contentType,Length=actual,Caption=caption,UploadedByName=user.Name??"System"};db.Add(row);await db.SaveChangesAsync(ct);return Result.Success(row);
        }
        catch{if(File.Exists(path))File.Delete(path);throw;}
    }
    public async Task<RepairPhotoFile?> GetAsync(int id,CancellationToken ct=default)
    {
        var row=await db.RepairPhotos.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==id,ct);if(row is null)return null;var path=Path.Combine(Root,row.StoredName);if(!File.Exists(path))return null;return new(row,await File.ReadAllBytesAsync(path,ct));
    }
    public async Task<Result> RemoveAsync(int id,CancellationToken ct=default)
    {
        var row=await db.RepairPhotos.FirstOrDefaultAsync(x=>x.Id==id,ct);if(row is null)return Result.Fail("Evidence not found.","photo.not-found");row.IsDeleted=true;await db.SaveChangesAsync(ct);var path=Path.Combine(Root,row.StoredName);if(File.Exists(path))File.Delete(path);return Result.Success();
    }
}
