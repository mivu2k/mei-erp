using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeiErp.Platform.Notifications;

public static class NotificationRegistration
{
    /// <summary>
    /// The notification platform, minus its storage.
    ///
    /// <see cref="INotificationStore"/> and <see cref="INotificationQueue"/> are
    /// left to the caller because the tables live on whichever context also owns
    /// the thing that raised the notification - that shared context is the only
    /// reason a notification can be written in the same transaction as an
    /// approval.
    /// </summary>
    public static IServiceCollection AddNotifications(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.Section));

        // Order matters only in that it is the order deliveries are planned in;
        // in-app first keeps the bell at the top of a notification's row list,
        // which is what the detail view reads best.
        services.AddScoped<INotificationChannel, InAppChannel>();
        services.AddScoped<INotificationChannel, EmailChannel>();
        services.TryAddScoped<INotificationEmailRenderer, BasicNotificationEmailRenderer>();

        services.AddScoped<INotifier, NotificationService>();

        services.AddHostedService<NotificationDispatcher>();

        return services;
    }
}
