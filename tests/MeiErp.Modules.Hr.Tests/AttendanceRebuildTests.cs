using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MeiErp.Modules.Hr.Tests;

[Collection("postgres")]
public sealed class AttendanceRebuildTests : IAsyncLifetime
{
    private static readonly string BaseConnection = Environment.GetEnvironmentVariable("MEIERP_TEST_DB")
        ?? "Host=127.0.0.1;Username=meierp;Password=DevPassword1!;";
    private readonly string _database = $"mei_att_{Guid.NewGuid():N}";
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero));
    private bool _available;
    private string Connection => BaseConnection + $"Database={_database};";
    private HrDbContext NewDb() => new(
        new DbContextOptionsBuilder<HrDbContext>().UseNpgsql(Connection).Options,
        new SystemUser("attendance-tests"), _clock);

    public async Task InitializeAsync()
    {
        try
        {
            await using var admin = new DbContext(new DbContextOptionsBuilder()
                .UseNpgsql(BaseConnection + "Database=postgres;").Options);
            await admin.Database.ExecuteSqlRawAsync($"CREATE DATABASE \"{_database}\";");
            await using var db = NewDb();
            await db.Database.EnsureCreatedAsync();
            await db.EnsureAuditTableForTestsAsync();
            _available = true;
        }
        catch (NpgsqlException) { _available = false; }
    }

    public async Task DisposeAsync()
    {
        if (!_available) return;
        try
        {
            await using var admin = new DbContext(new DbContextOptionsBuilder()
                .UseNpgsql(BaseConnection + "Database=postgres;").Options);
            await admin.Database.ExecuteSqlRawAsync($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE);");
        }
        catch { }
    }

    [SkippableFact]
    public async Task Rebuild_derives_week_from_punches_holiday_and_shift()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        var shift = new Shift { Name = "General", IsDefault = true, StartsAt = new(9, 0), EndsAt = new(17, 0) };
        var kamran = new Employee { Code = "EMP-1", FullName = "Kamran", JoinedOn = new(2026, 1, 1), Shift = shift };
        var sana = new Employee { Code = "EMP-2", FullName = "Sana", JoinedOn = new(2026, 1, 1), Shift = shift };
        db.AddRange(shift, kamran, sana);
        db.Holidays.Add(new() { Date = new(2026, 7, 28), Name = "Holiday" });
        await db.SaveChangesAsync();
        void Punch(Employee e, int day, int hour, int minute) => db.AttendancePunches.Add(new()
        { EmployeeId = e.Id, PunchedAt = new DateTime(2026, 7, day, hour, minute, 0), Method = PunchMethod.Card });
        Punch(kamran, 27, 8, 52); Punch(kamran, 27, 18, 12);
        Punch(sana, 27, 9, 31); Punch(sana, 27, 16, 20); Punch(kamran, 29, 8, 58);
        await db.SaveChangesAsync();

        Assert.Equal(8, await new AttendanceSyncService(db).RebuildAsync(new(2026, 7, 26), new(2026, 7, 29)));
        var days = await db.AttendanceDays.AsNoTracking().ToListAsync();
        AttendanceDay Day(Employee e, int day) => days.Single(d => d.EmployeeId == e.Id && d.Date == new DateOnly(2026, 7, day));
        Assert.Equal(AttendanceStatus.WeeklyOff, Day(kamran, 26).Status);
        Assert.Equal(AttendanceStatus.Present, Day(kamran, 27).Status);
        Assert.Equal(72, Day(kamran, 27).OvertimeMinutes);
        Assert.Equal(AttendanceStatus.Late, Day(sana, 27).Status);
        Assert.Equal(AttendanceStatus.Holiday, Day(sana, 28).Status);
        Assert.Equal(AttendanceStatus.Incomplete, Day(kamran, 29).Status);
        Assert.Equal(AttendanceStatus.Absent, Day(sana, 29).Status);
    }

    [SkippableFact]
    public async Task Rebuild_never_overwrites_a_manual_correction()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        var employee = new Employee { Code = "EMP-9", FullName = "Test", JoinedOn = new(2026, 1, 1) };
        db.Employees.Add(employee); await db.SaveChangesAsync();
        db.AttendanceDays.Add(new()
        {
            EmployeeId = employee.Id, Date = new(2026, 7, 27), FirstIn = new(9, 0), LastOut = new(17, 0),
            Status = AttendanceStatus.Present, Source = AttendanceSource.Manual, OverrideReason = "Gate register"
        });
        await db.SaveChangesAsync();
        Assert.Equal(0, await new AttendanceSyncService(db).RebuildAsync(new(2026, 7, 27), new(2026, 7, 27)));
        var day = await db.AttendanceDays.AsNoTracking().SingleAsync();
        Assert.Equal(AttendanceStatus.Present, day.Status);
        Assert.Equal(AttendanceSource.Manual, day.Source);
    }

    [SkippableFact]
    public async Task Employee_monthly_view_never_exposes_a_colleagues_attendance()
    {
        Skip.IfNot(_available, "No PostgreSQL available.");
        await using var db = NewDb();
        var mine = new Employee { Code="ME", FullName="Mine", UserId="employee-1", JoinedOn=new(2026,1,1) };
        var other = new Employee { Code="OTHER", FullName="Other", UserId="employee-2", JoinedOn=new(2026,1,1) };
        db.AddRange(mine, other); await db.SaveChangesAsync();
        db.AttendanceDays.AddRange(
            new() { EmployeeId=mine.Id, Date=new(2026,7,1), Status=AttendanceStatus.Present },
            new() { EmployeeId=other.Id, Date=new(2026,7,1), Status=AttendanceStatus.Late });
        await db.SaveChangesAsync();

        var service = new AttendanceService(db, new AttendanceSyncService(db), new EmployeeUser(), _clock);
        var rows = await service.MonthlyAsync(2026, 7);

        Assert.Equal(mine.Id, Assert.Single(rows).EmployeeId);
    }

    private sealed class EmployeeUser : ICurrentUser
    {
        public string? UserId => "employee-1"; public string? Name => "Employee"; public string? Email => null;
        public bool IsAuthenticated => true; public bool Can(string permission) => false;
        public bool InModule(string moduleKey) => true; public IReadOnlyCollection<string> Roles { get; } = [];
    }
}
