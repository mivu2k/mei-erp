using System.Net;
using MeiErp.Platform.Notifications;

namespace MeiErp.Platform.Identity;

/// <summary>Renders notification email on the same company identity used by printed documents.</summary>
public sealed class CompanyNotificationEmailRenderer(ICompanyProfileService companies)
    : INotificationEmailRenderer
{
    public async Task<RenderedNotificationEmail> RenderAsync(
        Notification notification, string? baseUrl, CancellationToken ct = default)
    {
        var company = await companies.GetAsync(ct);
        var name = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(company.Name) ? "MEI ERP" : company.Name);
        var subject = WebUtility.HtmlEncode(notification.Subject);
        var body = WebUtility.HtmlEncode(notification.Body).Replace("\n", "<br>");
        var link = BasicNotificationEmailRenderer.Link(notification, baseUrl);
        var action = link is null ? "" :
            $"<p style=\"margin:28px 0\"><a href=\"{WebUtility.HtmlEncode(link)}\" style=\"background:#1565c0;color:white;padding:12px 18px;text-decoration:none;border-radius:4px\">Open in MEI ERP</a></p>";
        var footer = WebUtility.HtmlEncode(company.FooterNote ?? company.Website ?? "Sent by MEI ERP");

        var html = $"""
            <!doctype html><html><body style="margin:0;background:#f4f6f8;font-family:Arial,sans-serif;color:#1f2937">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center" style="padding:28px 12px">
            <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="max-width:600px;background:white;border-radius:8px;overflow:hidden">
            <tr><td style="background:#0d47a1;color:white;padding:20px 28px;font-size:21px;font-weight:bold">{name}</td></tr>
            <tr><td style="padding:28px"><h2 style="margin-top:0">{subject}</h2><p style="line-height:1.6">{body}</p>{action}</td></tr>
            <tr><td style="padding:16px 28px;background:#eef2f7;color:#667085;font-size:12px">{footer}</td></tr>
            </table></td></tr></table></body></html>
            """;
        var text = notification.Body + (link is null ? "" : $"\n\n{link}") + $"\n\n— {company.Name}";
        return new RenderedNotificationEmail(text, html);
    }
}
