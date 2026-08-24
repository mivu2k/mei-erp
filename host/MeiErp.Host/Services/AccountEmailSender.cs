using MeiErp.Platform.Kernel;
using MeiErp.Platform.Notifications;

namespace MeiErp.Host.Services;

public interface IAccountEmailSender
{
    Task<Result> SendAsync(string address, string subject, string body, string url, CancellationToken ct = default);
}

public sealed class AccountEmailSender(IEnumerable<INotificationChannel> channels, IClock clock) : IAccountEmailSender
{
    public async Task<Result> SendAsync(string address, string subject, string body, string url, CancellationToken ct = default)
    {
        var email = channels.FirstOrDefault(x => x.Key == "email")
            ?? throw new InvalidOperationException("The email channel is not registered.");
        var notification = new Notification
        {
            Category = "account.security", Subject = subject, Body = body,
            Url = url, CreatedUtc = clock.UtcNow
        };
        return await email.SendAsync(notification, address, ct);
    }
}
