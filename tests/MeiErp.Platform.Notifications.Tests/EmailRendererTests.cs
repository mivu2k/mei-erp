using MeiErp.Platform.Notifications;
using Xunit;

namespace MeiErp.Platform.Notifications.Tests;

public sealed class EmailRendererTests
{
    [Fact]
    public async Task Renderer_encodes_content_and_adds_an_absolute_action_link()
    {
        var notification = new Notification
        {
            Subject = "Approve <chairs>", Body = "Raised by A & B\nReview it.", Url = "/approvals/7"
        };

        var rendered = await new BasicNotificationEmailRenderer()
            .RenderAsync(notification, "https://erp.mei.local/");

        Assert.Contains("Approve &lt;chairs&gt;", rendered.Html);
        Assert.Contains("A &amp; B<br>Review it.", rendered.Html);
        Assert.Contains("https://erp.mei.local/approvals/7", rendered.Html);
        Assert.Contains("https://erp.mei.local/approvals/7", rendered.Text);
        Assert.DoesNotContain("<chairs>", rendered.Html);
    }

    [Fact]
    public async Task Renderer_omits_the_action_when_no_base_url_is_configured()
    {
        var notification = new Notification { Subject = "Hello", Body = "World", Url = "/x" };
        var rendered = await new BasicNotificationEmailRenderer().RenderAsync(notification, null);
        Assert.DoesNotContain("href", rendered.Html);
        Assert.Equal("World", rendered.Text);
    }
}
