using System.Security.Cryptography;
using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Hr;

public interface IAttendanceAdminService
{
    Task<IReadOnlyList<Shift>> ShiftsAsync(CancellationToken ct = default);
    Task<Result<Shift>> SaveShiftAsync(Shift shift, CancellationToken ct = default);
    Task<IReadOnlyList<Holiday>> HolidaysAsync(CancellationToken ct = default);
    Task<Result<Holiday>> SaveHolidayAsync(Holiday holiday, CancellationToken ct = default);
    Task<Result> DeleteHolidayAsync(int holidayId, CancellationToken ct = default);
    Task<IReadOnlyList<AttendanceStation>> StationsAsync(CancellationToken ct = default);
    Task<Result<AttendanceStation>> SaveStationAsync(AttendanceStation station, CancellationToken ct = default);
    Task<Result<string>> ReissueStationAsync(int stationId, CancellationToken ct = default);
    Task<Result> DeleteStationAsync(int stationId, CancellationToken ct = default);
    Task<Result> EnrollCardAsync(int employeeId, string? cardNumber, CancellationToken ct = default);
    Task<Result<string>> EnsureQrSecretAsync(int employeeId, bool reissue, CancellationToken ct = default);
    Task<Employee?> MeAsync(CancellationToken ct = default);
}

public sealed class AttendanceAdminService(
    HrDbContext db, ICurrentUser currentUser, IAttendanceTokenService tokens)
    : IAttendanceAdminService
{
    public async Task<IReadOnlyList<Shift>> ShiftsAsync(CancellationToken ct = default) =>
        await db.Shifts.AsNoTracking().OrderByDescending(s => s.IsDefault).ThenBy(s => s.Name).ToListAsync(ct);

    public async Task<Result<Shift>> SaveShiftAsync(Shift shift, CancellationToken ct = default)
    {
        if (!CanSetup()) return Result.Fail<Shift>("You cannot configure attendance.", "attendance.forbidden");
        if (string.IsNullOrWhiteSpace(shift.Name)) return Result.Fail<Shift>("A shift needs a name.", "shift.no-name");
        if (shift.EndsAt <= shift.StartsAt) return Result.Fail<Shift>("Shift end must be after its start.", "shift.bad-times");
        if (shift.MinimumMinutes < 0 || shift.HalfDayMinutes < shift.MinimumMinutes)
            return Result.Fail<Shift>("Half-day minutes cannot be below minimum minutes.", "shift.bad-thresholds");
        if (await db.Shifts.AnyAsync(s => s.Name == shift.Name && s.Id != shift.Id, ct))
            return Result.Fail<Shift>("A shift with that name already exists.", "shift.duplicate");

        Shift row;
        if (shift.Id == 0) { row = shift; db.Shifts.Add(row); }
        else
        {
            row = await db.Shifts.FirstOrDefaultAsync(s => s.Id == shift.Id, ct)
                ?? shift;
            if (row == shift && shift.Id != 0) return Result.Fail<Shift>("Shift not found.", "shift.not-found");
            db.Entry(row).CurrentValues.SetValues(shift);
        }
        if (row.IsDefault)
            await db.Shifts.Where(s => s.Id != row.Id && s.IsDefault)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.IsDefault, false), ct);
        await db.SaveChangesAsync(ct);
        return Result.Success(row);
    }

    public async Task<IReadOnlyList<AttendanceStation>> StationsAsync(CancellationToken ct = default) =>
        await db.AttendanceStations.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);

    public async Task<Result<AttendanceStation>> SaveStationAsync(
        AttendanceStation station, CancellationToken ct = default)
    {
        if (!CanSetup()) return Result.Fail<AttendanceStation>("You cannot configure attendance.", "attendance.forbidden");
        if (string.IsNullOrWhiteSpace(station.Name))
            return Result.Fail<AttendanceStation>("A station needs a name.", "station.no-name");
        AttendanceStation row;
        if (station.Id == 0)
        {
            station.AccessToken = NewStationToken(); row = station; db.AttendanceStations.Add(row);
        }
        else
        {
            row = await db.AttendanceStations.FirstOrDefaultAsync(s => s.Id == station.Id, ct)
                ?? station;
            if (row == station) return Result.Fail<AttendanceStation>("Station not found.", "station.not-found");
            row.Name = station.Name; row.Location = station.Location; row.IsEnabled = station.IsEnabled;
        }
        await db.SaveChangesAsync(ct);
        return Result.Success(row);
    }

    public async Task<IReadOnlyList<Holiday>> HolidaysAsync(CancellationToken ct = default) =>
        await db.Holidays.AsNoTracking().OrderBy(h => h.Date).ToListAsync(ct);

    public async Task<Result<Holiday>> SaveHolidayAsync(Holiday holiday, CancellationToken ct = default)
    {
        if (!CanSetup()) return Result.Fail<Holiday>("You cannot configure attendance.", "attendance.forbidden");
        if (string.IsNullOrWhiteSpace(holiday.Name))
            return Result.Fail<Holiday>("A holiday needs a name.", "holiday.no-name");
        if (await db.Holidays.AnyAsync(h => h.Date == holiday.Date && h.Id != holiday.Id, ct))
            return Result.Fail<Holiday>("A holiday already exists on that date.", "holiday.duplicate");
        Holiday row;
        if (holiday.Id == 0) { row = holiday; db.Holidays.Add(row); }
        else
        {
            row = await db.Holidays.FirstOrDefaultAsync(h => h.Id == holiday.Id, ct)
                ?? holiday;
            if (row == holiday) return Result.Fail<Holiday>("Holiday not found.", "holiday.not-found");
            db.Entry(row).CurrentValues.SetValues(holiday);
        }
        await db.SaveChangesAsync(ct); return Result.Success(row);
    }

    public async Task<Result> DeleteHolidayAsync(int holidayId, CancellationToken ct = default)
    {
        if (!CanSetup()) return Result.Fail("You cannot configure attendance.", "attendance.forbidden");
        var row = await db.Holidays.FirstOrDefaultAsync(h => h.Id == holidayId, ct);
        if (row is null) return Result.Fail("Holiday not found.", "holiday.not-found");
        db.Holidays.Remove(row); await db.SaveChangesAsync(ct); return Result.Success();
    }

    public async Task<Result<string>> ReissueStationAsync(int stationId, CancellationToken ct = default)
    {
        if (!CanSetup()) return Result.Fail<string>("You cannot configure attendance.", "attendance.forbidden");
        var row = await db.AttendanceStations.FirstOrDefaultAsync(s => s.Id == stationId, ct);
        if (row is null) return Result.Fail<string>("Station not found.", "station.not-found");
        row.AccessToken = NewStationToken(); await db.SaveChangesAsync(ct);
        return Result.Success(row.AccessToken);
    }

    public async Task<Result> DeleteStationAsync(int stationId, CancellationToken ct = default)
    {
        if (!CanSetup()) return Result.Fail("You cannot configure attendance.", "attendance.forbidden");
        var row = await db.AttendanceStations.FirstOrDefaultAsync(s => s.Id == stationId, ct);
        if (row is null) return Result.Fail("Station not found.", "station.not-found");
        db.AttendanceStations.Remove(row); await db.SaveChangesAsync(ct); return Result.Success();
    }

    public async Task<Result> EnrollCardAsync(
        int employeeId, string? cardNumber, CancellationToken ct = default)
    {
        if (!CanSetup()) return Result.Fail("You cannot configure attendance.", "attendance.forbidden");
        cardNumber = string.IsNullOrWhiteSpace(cardNumber) ? null : cardNumber.Trim();
        if (cardNumber is not null && await db.Employees.AnyAsync(e => e.Id != employeeId && e.CardNumber == cardNumber, ct))
            return Result.Fail("That card is already assigned to another employee.", "attendance.card-taken");
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null) return Result.Fail("Employee not found.", "employee.not-found");
        employee.CardNumber = cardNumber; await db.SaveChangesAsync(ct); return Result.Success();
    }

    public async Task<Result<string>> EnsureQrSecretAsync(
        int employeeId, bool reissue, CancellationToken ct = default)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null) return Result.Fail<string>("Employee not found.", "employee.not-found");
        var isSelf = employee.UserId is not null && employee.UserId == currentUser.UserId;
        if (!isSelf && !CanSetup()) return Result.Fail<string>("You cannot issue this code.", "attendance.forbidden");
        if (employee.QrSecret is null || reissue)
        {
            employee.QrSecret = tokens.NewSecret(); await db.SaveChangesAsync(ct);
        }
        return Result.Success(employee.QrSecret);
    }

    public Task<Employee?> MeAsync(CancellationToken ct = default) => currentUser.UserId is null
        ? Task.FromResult<Employee?>(null)
        : db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.UserId == currentUser.UserId, ct);

    private bool CanSetup() => currentUser.Can(HrModule.AttendanceSetup);
    private static string NewStationToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
}
