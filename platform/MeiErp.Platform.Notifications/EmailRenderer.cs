using System.Net;

namespace MeiErp.Platform.Notifications;

/// <summary>Safe fallback used by hosts that do not provide company branding.</summary>
public sealed class BasicNotificationEmailRenderer : INotificationEmailRenderer
{
    public Task<RenderedNotificationEmail> RenderAsync(
        Notification notification, string? baseUrl, CancellationToken ct = default)
    {
        var link = Link(notification, baseUrl);
        var text = notification.Body + (link is null ? "" : $"\n\n{link}");
        var html = $"<h2>{WebUtility.HtmlEncode(notification.Subject)}</h2>" +
                   $"<p>{WebUtility.HtmlEncode(notification.Body).Replace("\n", "<br>")}</p>" +
                   (link is null ? "" : $"<p><a href=\"{WebUtility.HtmlEncode(link)}\">Open in MEI ERP</a></p>");
        return Task.FromResult(new RenderedNotificationEmail(text, html));
    }

    public static string? Link(Notification notification, string? baseUrl) =>
        string.IsNullOrWhiteSpace(notification.Url) || string.IsNullOrWhiteSpace(baseUrl)
            ? null
            : $"{baseUrl.TrimEnd('/')}{notification.Url}";
}
