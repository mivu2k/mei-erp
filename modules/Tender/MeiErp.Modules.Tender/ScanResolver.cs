using MeiErp.Platform.Kernel;

namespace MeiErp.Modules.Tender;

/// <summary>
/// The barcode stuck on the spine of a physical file. Scanning it is how
/// somebody standing at the cabinet finds out who is holding the folder.
/// </summary>
public sealed class TenderFileScanResolver(IFileRegistryService files) : IScanResolver
{
    public string ModuleKey => TenderModule.Key;

    public async Task<IReadOnlyList<ScanHit>> ResolveAsync(string code, CancellationToken ct = default)
    {
        var file = await files.GetByNumberAsync(code, ct);
        if (file is null) return [];

        var holder = file.HolderName is { Length: > 0 } name
            ? $"{file.Status} - {name}"
            : file.Status.ToString();

        return
        [
            new ScanHit(
                file.FileNumber,
                $"{file.OwnerTitle} ({holder})",
                $"/tender/files/{file.Id}",
                ModuleKey,
                TenderModule.FilesView,
                "Folder")
        ];
    }
}
