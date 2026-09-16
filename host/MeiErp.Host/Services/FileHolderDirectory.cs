using MeiErp.Modules.Tender;
using MeiErp.Platform.Identity;

namespace MeiErp.Host.Services;

public sealed class FileHolderDirectory(IUserDirectory users) : IFileHolderDirectory
{
    public async Task<IReadOnlyList<FileHolderOption>> SearchAsync(string? search, CancellationToken ct = default) =>
        (await users.SearchAsync(search, take: 50, ct)).Where(x => x.IsActive)
            .Select(x => new FileHolderOption(x.Id, x.FullName, x.Designation, x.DepartmentName)).ToList();
}
