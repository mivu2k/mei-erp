using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MeiErp.Modules.Hr;

public interface IKioskService
{
    Task<KioskResult> ScanAsync(string stationToken, string scanned, CancellationToken ct = default);
    Task<AttendanceStation?> ResolveStationAsync(string stationToken, CancellationToken ct = default);
}

public sealed record KioskResult(
    bool Accepted, string? EmployeeName, PunchDirection Direction, DateTime? At, string Message);

/// <summary>Turns keyboard-emulating NFC/QR reads into immutable punches.</summary>
public sealed class KioskService(
    HrDbContext db, IAttendanceTokenService tokens, IAttendanceSyncService sync,
    IClock clock, ILogger<KioskService> logger) : IKioskService
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(45);

    public Task<AttendanceStation?> ResolveStationAsync(string stationToken, CancellationToken ct = default) =>
        db.AttendanceStations.AsNoTracking()
            .FirstOrDefaultAsync(s => s.AccessToken == stationToken && s.IsEnabled, ct);

    public async Task<KioskResult> ScanAsync(
        string stationToken, string scanned, CancellationToken ct = default)
    {
        var station = await db.AttendanceStations
            .FirstOrDefaultAsync(s => s.AccessToken == stationToken && s.IsEnabled, ct);
        if (station is null) return Reject("This station is not recognised. Ask HR to re-issue its link.");
        scanned = scanned.Trim();
        if (scanned.Length == 0) return Reject("Nothing scanned.");

        var (employee, method, evidence) = await IdentifyAsync(scanned, ct);
        if (employee is null)
            return Reject(method == PunchMethod.QrCode
                ? "That code has expired. Let your screen refresh and try again."
                : "Card not recognised. Ask HR to register it against your record.");
        if (!employee.IsEmployedOn(clock.Today))
            return Reject("This employee is not active today. Ask HR for help.");

        var now = clock.Now.DateTime;
        var recent = await db.AttendancePunches.AsNoTracking()
            .Where(p => p.EmployeeId == employee.Id && p.PunchedAt > now - Debounce)
            .OrderByDescending(p => p.PunchedAt).FirstOrDefaultAsync(ct);
        if (recent is not null)
            return new(false, employee.FullName, recent.Direction, recent.PunchedAt,
                $"Already recorded at {recent.PunchedAt:HH:mm}. You're set.");

        var start = clock.Today.ToDateTime(TimeOnly.MinValue);
        var end = clock.Today.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var earlier = await db.AttendancePunches.CountAsync(
            p => p.EmployeeId == employee.Id && p.PunchedAt >= start && p.PunchedAt < end, ct);
        var direction = earlier == 0 ? PunchDirection.In : PunchDirection.Out;
        db.AttendancePunches.Add(new()
        {
            AttendanceStationId = station.Id, EmployeeId = employee.Id, PunchedAt = now,
            Direction = direction, Method = method, Evidence = evidence
        });
        station.LastPunchAtUtc = clock.UtcNow;
        station.LastPunchDescription = $"{employee.FullName} — {direction} at {now:HH:mm}";

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Duplicate kiosk punch for employee {EmployeeId}.", employee.Id);
            return new(false, employee.FullName, direction, now, "That punch was already recorded.");
        }
        await sync.RebuildAsync(clock.Today, clock.Today, employee.Id, ct);
        logger.LogInformation("Kiosk punch: {Employee} {Direction} at {Station} via {Method}",
            employee.FullName, direction, station.Name, method);
        return new(true, employee.FullName, direction, now,
            direction == PunchDirection.In ? "Welcome in" : "Goodbye");
    }

    private async Task<(Employee? Employee, PunchMethod Method, string? Evidence)> IdentifyAsync(
        string scanned, CancellationToken ct)
    {
        if (tokens.Parse(scanned) is { } token)
        {
            var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == token.EmployeeId, ct);
            if (employee?.QrSecret is null || !tokens.Verify(token, employee.QrSecret, clock.UtcNow))
                return (null, PunchMethod.QrCode, null);
            return (employee, PunchMethod.QrCode, $"qr step {token.Step}");
        }
        // Readers differ on case and on padding: the same fob reads as "04A1B2",
        // "04a1b2" or " 04A1B2 " depending on the make. Matching those exactly is
        // indistinguishable, to the person holding the card, from not being
        // registered at all.
        var byCard = await db.Employees.FirstOrDefaultAsync(
            e => e.CardNumber != null && EF.Functions.ILike(e.CardNumber.Trim(), scanned), ct);

        return (byCard, PunchMethod.Card, byCard is null ? null : $"card {scanned}");
    }

    private static KioskResult Reject(string message) =>
        new(false, null, PunchDirection.Unspecified, null, message);
}
