using System.Text.Json;
using MeiErp.Platform.Identity;
using MeiErp.Platform.Kernel;
using MeiErp.Platform.Notifications;
using MeiErp.Platform.Reporting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Host.Services;

public sealed class ReportScheduleWorker(
    IServiceScopeFactory scopes, IClock clock, ILogger<ReportScheduleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SweepAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Scheduled-report sweep failed"); }
            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    internal async Task SweepAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var catalog = scope.ServiceProvider.GetRequiredService<IReportCatalog>();
        var notifier = scope.ServiceProvider.GetRequiredService<INotifier>();
        var now = clock.UtcNow;
        var due = await db.ReportSchedules.Where(x => x.IsActive && x.NextRunUtc <= now)
            .OrderBy(x => x.NextRunUtc).Take(25).ToListAsync(ct);

        foreach (var schedule in due)
        {
            var definition = catalog.Find(schedule.ReportKey);
            var owner = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == schedule.UserId && x.IsActive, ct);
            try
            {
                if (definition is null) throw new InvalidOperationException("The report is no longer registered.");
                if (owner is null) throw new InvalidOperationException("The schedule owner is inactive or missing.");
                if (!await CanRunAsync(db, owner.Id, definition, ct))
                    throw new UnauthorizedAccessException("The owner no longer has permission to run this report.");
                var request = JsonSerializer.Deserialize<ReportRequest>(schedule.FiltersJson) ?? new ReportRequest();
                var result = await definition.Run(request, ct);
                schedule.LastRowCount = result.Rows.Count;
                schedule.LastError = null;
                await notifier.NotifyAsync(new NotificationRequest(
                    [new NotificationRecipient(owner.Id, owner.FullName, owner.Email)],
                    NotificationCategories.ReportScheduled, $"Scheduled report: {definition.Name}",
                    $"{schedule.Name} completed with {result.Rows.Count:N0} rows.",
                    $"/reports/{Uri.EscapeDataString(definition.Key)}", definition.ModuleKey,
                    EventKey: $"report-schedule:{schedule.Id}:{now:O}"), ct);
            }
            catch (Exception ex)
            {
                schedule.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                schedule.LastRowCount = null;
                logger.LogWarning(ex, "Scheduled report {ScheduleId} failed", schedule.Id);
            }
            schedule.LastRunUtc = now;
            schedule.ModifiedUtc = now;
            schedule.NextRunUtc = ReportScheduleCalculator.NextUtc(schedule, now, clock.TimeZone);
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task<bool> CanRunAsync(PlatformDbContext db, string userId, ReportDefinition report, CancellationToken ct)
    {
        var denied = await db.ModuleAccess.AnyAsync(x => x.UserId == userId && x.ModuleKey == report.ModuleKey && !x.Granted, ct);
        if (denied) return false;
        var roles = from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId == userId select r;
        if (await roles.AnyAsync(x => x.Name == PlatformPermissions.SuperAdminRole, ct)) return true;
        var roleIds = roles.Select(x => x.Id);
        return await db.RoleClaims.AnyAsync(x => roleIds.Contains(x.RoleId) && x.ClaimType == PermissionClaim.Type && x.ClaimValue == report.Permission, ct);
    }
}
