using Microsoft.EntityFrameworkCore;
using MeiErp.Platform.Kernel;

namespace MeiErp.Platform.Identity;

public sealed class LabelTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string DocumentType { get; set; } = "";
    public decimal WidthMm { get; set; } = 62;
    public decimal? HeightMm { get; set; }
    public decimal MarginMm { get; set; } = 3;
    public string FieldKeys { get; set; } = "";
    public bool ShowTitle { get; set; } = true;
    public bool ShowCompanyName { get; set; } = true;
    public bool ShowBarcode { get; set; } = true;
    public bool ShowQrCode { get; set; }
    public decimal FontScale { get; set; } = 1;
    public bool IsDefault { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }
    public IReadOnlyList<string> SelectedFields() => FieldKeys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public static class LabelDocumentTypes
{
    public const string RepairDevice = "repair.device";
    public static IReadOnlyList<(string Key,string Name)> All => [(RepairDevice,"Repair — device label")];
    public static string Describe(string key) => All.FirstOrDefault(x=>x.Key==key).Name ?? key;
}

public sealed record LabelField(string Key,string Label);
public static class LabelFieldCatalog
{
    private static readonly Dictionary<string,IReadOnlyList<LabelField>> Fields=[];
    private static readonly Lock Gate=new();
    public static void Register(string type,IReadOnlyList<LabelField> fields){lock(Gate)Fields[type]=fields;}
    public static IReadOnlyList<LabelField> For(string type){lock(Gate)return Fields.GetValueOrDefault(type,[]);}
}

public interface ILabelTemplateService
{
    Task<List<LabelTemplate>> ListAsync(string? type=null,CancellationToken ct=default);
    Task<LabelTemplate?> GetAsync(int id,CancellationToken ct=default);
    Task<LabelTemplate?> GetDefaultAsync(string type,CancellationToken ct=default);
    Task<LabelTemplate> SaveAsync(LabelTemplate value,string? actor=null,CancellationToken ct=default);
    Task DeleteAsync(int id,CancellationToken ct=default);
}

public sealed class LabelTemplateService(PlatformDbContext db, IClock clock):ILabelTemplateService
{
    public Task<List<LabelTemplate>> ListAsync(string? type=null,CancellationToken ct=default)=>db.LabelTemplates.AsNoTracking().Where(x=>type==null||x.DocumentType==type).OrderBy(x=>x.DocumentType).ThenBy(x=>x.Name).ToListAsync(ct);
    public Task<LabelTemplate?> GetAsync(int id,CancellationToken ct=default)=>db.LabelTemplates.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==id,ct);
    public Task<LabelTemplate?> GetDefaultAsync(string type,CancellationToken ct=default)=>db.LabelTemplates.AsNoTracking().Where(x=>x.DocumentType==type&&x.IsDefault).FirstOrDefaultAsync(ct);
    public async Task<LabelTemplate> SaveAsync(LabelTemplate value,string? actor=null,CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(value.Name)||string.IsNullOrWhiteSpace(value.DocumentType))throw new InvalidOperationException("Name and document type are required.");
        if(value.WidthMm<=0||value.HeightMm is <=0||value.MarginMm<0)throw new InvalidOperationException("Label dimensions are invalid.");
        if(value.FontScale is <0.5m or >3m)throw new InvalidOperationException("Font scale must be between 0.5 and 3.");
        var allowed=LabelFieldCatalog.For(value.DocumentType).Select(x=>x.Key).ToHashSet();
        if(value.SelectedFields().Any(x=>!allowed.Contains(x)))throw new InvalidOperationException("The template contains a field unavailable for this document type.");
        value.ModifiedAtUtc=clock.UtcNow;value.ModifiedBy=actor;
        if(value.IsDefault)await db.LabelTemplates.Where(x=>x.DocumentType==value.DocumentType&&x.Id!=value.Id&&x.IsDefault).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsDefault,false),ct);
        if(value.Id==0)db.LabelTemplates.Add(value);else db.Entry(value).State=EntityState.Modified;
        await db.SaveChangesAsync(ct);return value;
    }
    public async Task DeleteAsync(int id,CancellationToken ct=default){var row=await db.LabelTemplates.FindAsync([id],ct);if(row is null)return;db.Remove(row);await db.SaveChangesAsync(ct);}
}
