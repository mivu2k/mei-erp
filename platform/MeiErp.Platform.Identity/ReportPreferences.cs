using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Platform.Identity;

public sealed class SavedReportView
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string ReportKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string FiltersJson { get; set; } = "{}";
    public bool IsDefault { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }
}

public enum ReportScheduleFrequency { Daily = 1, Weekly = 2, Monthly = 3 }

public sealed class ReportSchedule
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string ReportKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string FiltersJson { get; set; } = "{}";
    public ReportScheduleFrequency Frequency { get; set; } = ReportScheduleFrequency.Weekly;
    public TimeOnly RunAtLocal { get; set; } = new(8, 0);
    public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Monday;
    public int DayOfMonth { get; set; } = 1;
    public DateTime NextRunUtc { get; set; }
    public DateTime? LastRunUtc { get; set; }
    public int? LastRowCount { get; set; }
    public string? LastError { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }
}

public interface IReportPreferenceService
{
    Task<IReadOnlyList<SavedReportView>> ViewsAsync(string reportKey, CancellationToken ct = default);
    Task<SavedReportView> SaveViewAsync(int? id, string reportKey, string name, string filtersJson, bool isDefault, CancellationToken ct = default);
    Task DeleteViewAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<ReportSchedule>> SchedulesAsync(string reportKey, CancellationToken ct = default);
    Task<ReportSchedule> SaveScheduleAsync(int? id, string reportKey, string name, string filtersJson, ReportScheduleFrequency frequency, TimeOnly runAt, DayOfWeek dayOfWeek, int dayOfMonth, bool active, CancellationToken ct = default);
    Task DeleteScheduleAsync(int id, CancellationToken ct = default);
}

public sealed class ReportPreferenceService(PlatformDbContext db, ICurrentUser user, IClock clock) : IReportPreferenceService
{
    private string UserId => user.UserId ?? throw new UnauthorizedAccessException("Sign in to manage report views.");

    public async Task<IReadOnlyList<SavedReportView>> ViewsAsync(string reportKey, CancellationToken ct = default) =>
        await db.SavedReportViews.AsNoTracking().Where(x => x.UserId == UserId && x.ReportKey == reportKey)
            .OrderByDescending(x => x.IsDefault).ThenBy(x => x.Name).ToListAsync(ct);

    public async Task<SavedReportView> SaveViewAsync(int? id, string reportKey, string name, string filtersJson, bool isDefault, CancellationToken ct = default)
    {
        Validate(reportKey, name, filtersJson);
        var row = id is null ? null : await db.SavedReportViews.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId, ct);
        if (id is not null && row is null) throw new KeyNotFoundException("Saved view was not found.");
        row ??= new SavedReportView { UserId = UserId, CreatedUtc = clock.UtcNow };
        row.ReportKey = reportKey; row.Name = name.Trim(); row.FiltersJson = filtersJson; row.IsDefault = isDefault; row.ModifiedUtc = clock.UtcNow;
        if (isDefault)
            await db.SavedReportViews.Where(x => x.UserId == UserId && x.ReportKey == reportKey && x.Id != row.Id).ExecuteUpdateAsync(x => x.SetProperty(v => v.IsDefault, false), ct);
        if (row.Id == 0) db.SavedReportViews.Add(row);
        await db.SaveChangesAsync(ct);
        return row;
    }

    public async Task DeleteViewAsync(int id, CancellationToken ct = default)
    {
        var row = await db.SavedReportViews.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId, ct)
            ?? throw new KeyNotFoundException("Saved view was not found.");
        db.Remove(row); await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ReportSchedule>> SchedulesAsync(string reportKey, CancellationToken ct = default) =>
        await db.ReportSchedules.AsNoTracking().Where(x => x.UserId == UserId && x.ReportKey == reportKey)
            .OrderBy(x => x.Name).ToListAsync(ct);

    public async Task<ReportSchedule> SaveScheduleAsync(int? id, string reportKey, string name, string filtersJson, ReportScheduleFrequency frequency, TimeOnly runAt, DayOfWeek dayOfWeek, int dayOfMonth, bool active, CancellationToken ct = default)
    {
        Validate(reportKey, name, filtersJson);
        if (dayOfMonth is < 1 or > 28) throw new ArgumentOutOfRangeException(nameof(dayOfMonth), "Use a day from 1 to 28.");
        var row = id is null ? null : await db.ReportSchedules.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId, ct);
        if (id is not null && row is null) throw new KeyNotFoundException("Schedule was not found.");
        row ??= new ReportSchedule { UserId = UserId, CreatedUtc = clock.UtcNow };
        row.ReportKey = reportKey; row.Name = name.Trim(); row.FiltersJson = filtersJson; row.Frequency = frequency;
        row.RunAtLocal = runAt; row.DayOfWeek = dayOfWeek; row.DayOfMonth = dayOfMonth; row.IsActive = active; row.ModifiedUtc = clock.UtcNow;
        row.NextRunUtc = ReportScheduleCalculator.NextUtc(row, clock.UtcNow, clock.TimeZone);
        if (row.Id == 0) db.ReportSchedules.Add(row);
        await db.SaveChangesAsync(ct);
        return row;
    }

    public async Task DeleteScheduleAsync(int id, CancellationToken ct = default)
    {
        var row = await db.ReportSchedules.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId, ct)
            ?? throw new KeyNotFoundException("Schedule was not found.");
        db.Remove(row); await db.SaveChangesAsync(ct);
    }

    private static void Validate(string reportKey, string name, string json)
    {
        if (string.IsNullOrWhiteSpace(reportKey)) throw new ArgumentException("Report is required.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120) throw new ArgumentException("Enter a name up to 120 characters.");
        try { System.Text.Json.JsonDocument.Parse(json); } catch (System.Text.Json.JsonException) { throw new ArgumentException("Report filters are invalid."); }
    }
}

public static class ReportScheduleCalculator
{
    public static DateTime NextUtc(ReportSchedule schedule, DateTime afterUtc, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc), zone);
        var date = DateOnly.FromDateTime(local);
        for (var i = 0; i <= 370; i++)
        {
            var candidateDate = date.AddDays(i);
            var matches = schedule.Frequency switch
            {
                ReportScheduleFrequency.Daily => true,
                ReportScheduleFrequency.Weekly => candidateDate.DayOfWeek == schedule.DayOfWeek,
                ReportScheduleFrequency.Monthly => candidateDate.Day == schedule.DayOfMonth,
                _ => false
            };
            if (!matches) continue;
            var candidate = candidateDate.ToDateTime(schedule.RunAtLocal, DateTimeKind.Unspecified);
            if (candidate <= local) continue;
            return TimeZoneInfo.ConvertTimeToUtc(candidate, zone);
        }
        throw new InvalidOperationException("Could not calculate the next report run.");
    }
}
