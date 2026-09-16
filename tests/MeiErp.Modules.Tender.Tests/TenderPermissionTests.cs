using Xunit;

namespace MeiErp.Modules.Tender.Tests;

public class TenderPermissionTests
{
    [Fact]
    public void File_registry_roles_keep_view_and_manage_separate()
    {
        var roles = TenderModule.Descriptor.RoleTemplates.ToDictionary(x => x.Name);

        Assert.Contains(TenderModule.FilesView, roles["Bid Manager"].Permissions);
        Assert.DoesNotContain(TenderModule.FilesManage, roles["Bid Manager"].Permissions);

        Assert.Contains(TenderModule.FilesView, roles["Project Manager"].Permissions);
        Assert.DoesNotContain(TenderModule.FilesManage, roles["Project Manager"].Permissions);

        Assert.DoesNotContain(TenderModule.FilesView, roles["Project Member"].Permissions);
        Assert.DoesNotContain(TenderModule.FilesManage, roles["Project Member"].Permissions);

        Assert.Contains(TenderModule.FilesView, roles["Records Clerk"].Permissions);
        Assert.Contains(TenderModule.FilesManage, roles["Records Clerk"].Permissions);
    }
}
