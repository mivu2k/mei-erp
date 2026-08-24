using MailKit.Net.Smtp;
using MailKit.Security;
using MeiErp.Platform.Kernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace MeiErp.Platform.Notifications;

/// <summary>
/// The bell in the app bar.
///
/// There is nothing to send: the notification row <i>is</i> the in-app message,
/// readable the moment it commits. The channel exists anyway so that the bell
/// obeys the same preference rules as everything else, and so "was this person
/// told?" has one answer shape across every channel.
/// </summary>
public sealed class InAppChannel : INotificationChannel
{
    public string Key => "inapp";
    public string DisplayName => "In the app";

    /// <summary>
    /// On for everything. Someone who has turned off every channel would
    /// otherwise be assigned approvals that they are never told about, and the
    /// queue would look to everyone else like they were ignoring it.
    /// </summary>
    public bool EnabledByDefault(string category) => true;

    public string? AddressFor(NotificationRecipient recipient) => recipient.UserId;

    public Task<Result> SendAsync(
        Notification notification, string address, CancellationToken ct = default) =>
        Task.FromResult(Result.Success());
}

/// <summary>SMTP settings. Never in appsettings.json - environment or user-secrets.</summary>
public sealed class EmailOptions
{
    public const string Section = "Notifications:Email";

    /// <summary>
    /// Off unless configured. A half-configured mail server that throws on every
    /// send would fill the dead-letter list on a developer machine and teach
    /// everyone to ignore it.
    /// </summary>
    public bool Enabled { get; set; }

    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;

    public string? Username { get; set; }
    public string? Password { get; set; }

    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "MEI ERP";

    /// <summary>
    /// Sends every message here instead of to the real recipient. For staging,
    /// where the database is a copy of production and the addresses on it are
    /// real people who did not ask to be tested against.
    /// </summary>
    public string? RedirectAllTo { get; set; }

    /// <summary>Prefixed to the app-relative Url on a notification to make a clickable link.</summary>
    public string? BaseUrl { get; set; }
}

/// <inheritdoc />
public sealed class EmailChannel(
    IOptions<EmailOptions> options,
    INotificationEmailRenderer renderer,
    ILogger<EmailChannel> logger) : INotificationChannel
{
    private readonly EmailOptions _options = options.Value;

    public string Key => "email";
    public string DisplayName => "Email";

    /// <summary>
    /// Only for things that block somebody. Emailing every status change is how
    /// a system teaches its users to filter it into a folder they never open,
    /// at which point the one message that mattered is lost with the rest.
    /// </summary>
    public bool EnabledByDefault(string category) =>
        category is NotificationCategories.ApprovalAssigned
                 or NotificationCategories.ApprovalSettled
                 or NotificationCategories.ApprovalReminder
                 or NotificationCategories.ApprovalEscalated
                 or NotificationCategories.ReportScheduled;

    public string? AddressFor(NotificationRecipient recipient) =>
        !_options.Enabled ? null
        : string.IsNullOrWhiteSpace(recipient.Email) ? null
        : recipient.Email;

    public async Task<Result> SendAsync(
        Notification notification, string address, CancellationToken ct = default)
    {
        if (!_options.Enabled)
            return Result.Fail("Email is not configured.", "email.disabled");

        var to = string.IsNullOrWhiteSpace(_options.RedirectAllTo)
            ? address
            : _options.RedirectAllTo;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = notification.Subject;

        var rendered = await renderer.RenderAsync(notification, _options.BaseUrl, ct);
        var builder = new BodyBuilder { TextBody = rendered.Text, HtmlBody = rendered.Html };
        if (!string.IsNullOrWhiteSpace(_options.RedirectAllTo))
        {
            builder.TextBody += $"\n\n---\nRedirected here from {address} by Notifications:Email:RedirectAllTo.";
            builder.HtmlBody += $"<hr><p style=\"color:#666;font-size:12px\">Redirected from {System.Net.WebUtility.HtmlEncode(address)} by staging configuration.</p>";
        }
        message.Body = builder.ToMessageBody();

        try
        {
            using var client = new SmtpClient();

            await client.ConnectAsync(
                _options.Host, _options.Port,
                _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto,
                ct);

            // An empty password is a real configuration - some relays authenticate
            // on the username alone - so it is passed through rather than skipped.
            if (!string.IsNullOrWhiteSpace(_options.Username))
                await client.AuthenticateAsync(_options.Username, _options.Password ?? "", ct);

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            return Result.Success();
        }
        catch (Exception ex)
        {
            // Returned rather than thrown: a mail server being down is the
            // ordinary case this queue exists to survive, not a bug.
            logger.LogWarning(ex, "Email delivery to {Address} failed.", to);
            return Result.Fail(ex.Message, "email.send-failed");
        }
    }
}

public interface INotificationEmailRenderer
{
    Task<RenderedNotificationEmail> RenderAsync(
        Notification notification, string? baseUrl, CancellationToken ct = default);
}

public sealed record RenderedNotificationEmail(string Text, string Html);

/// <summary>
/// The categories notifications are grouped under.
///
/// Constants rather than free strings because they are the key a person's
/// opt-outs hang off: a typo in one call site would create a second category
/// nobody has a preference for, which would then quietly ignore their opt-out.
/// </summary>
public static class NotificationCategories
{
    /// <summary>Something has landed in your approvals inbox.</summary>
    public const string ApprovalAssigned = "approval.assigned";

    /// <summary>Something you raised was approved, rejected or returned.</summary>
    public const string ApprovalSettled = "approval.settled";

    /// <summary>Still sitting in your inbox past its reminder time.</summary>
    public const string ApprovalReminder = "approval.reminder";

    /// <summary>Overdue and now visible to somebody above the approver.</summary>
    public const string ApprovalEscalated = "approval.escalated";

    /// <summary>A report the person explicitly scheduled has finished running.</summary>
    public const string ReportScheduled = "report.scheduled";

    public static IReadOnlyList<string> All =>
    [
        ApprovalAssigned, ApprovalSettled, ApprovalReminder, ApprovalEscalated, ReportScheduled
    ];
}
