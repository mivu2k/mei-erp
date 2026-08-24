using MeiErp.Platform.Kernel;

namespace MeiErp.Modules.Hr;

public class AttendanceStation : AuditableEntity
{
    public string Name { get; set; } = "";
    public string? Location { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string AccessToken { get; set; } = "";
    public DateTime? LastPunchAtUtc { get; set; }
    public string? LastPunchDescription { get; set; }
}

/// <summary>Immutable evidence from a kiosk or imported device.</summary>
public class AttendancePunch : Entity
{
    public int? AttendanceStationId { get; set; }
    public AttendanceStation? AttendanceStation { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DateTime PunchedAt { get; set; }
    public PunchDirection Direction { get; set; }
    public PunchMethod Method { get; set; }
    public string? Evidence { get; set; }
}

public class AttendanceDay : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly? FirstIn { get; set; }
    public TimeOnly? LastOut { get; set; }
    public int PunchCount { get; set; }
    public AttendanceStatus Status { get; set; }
    public AttendanceSource Source { get; set; }
    public int WorkedMinutes { get; set; }
    public int LateMinutes { get; set; }
    public int EarlyLeaveMinutes { get; set; }
    public int OvertimeMinutes { get; set; }
    public string? OverriddenById { get; set; }
    public string? OverriddenByName { get; set; }
    public DateTime? OverriddenAtUtc { get; set; }
    public string? OverrideReason { get; set; }
    public int? LeaveRequestId { get; set; }
    public LeaveRequest? LeaveRequest { get; set; }
    public string? Notes { get; set; }

    public bool IsPayable => Status is AttendanceStatus.Present or AttendanceStatus.Late
        or AttendanceStatus.HalfDay or AttendanceStatus.OnLeave
        or AttendanceStatus.Holiday or AttendanceStatus.WeeklyOff;
}

public class Shift : AuditableEntity
{
    public string Name { get; set; } = "";
    public TimeOnly StartsAt { get; set; } = new(9, 0);
    public TimeOnly EndsAt { get; set; } = new(17, 0);
    public int GraceMinutes { get; set; } = 15;
    public int HalfDayMinutes { get; set; } = 240;
    public int MinimumMinutes { get; set; } = 60;
    public int OvertimeAfterMinutes { get; set; } = 30;
    public int BreakMinutes { get; set; }
    public int WeeklyOffMask { get; set; } = 1 << (int)DayOfWeek.Sunday;
    public bool IsDefault { get; set; }
    public bool IsWeeklyOff(DayOfWeek day) => (WeeklyOffMask & (1 << (int)day)) != 0;
}

public enum PunchDirection { Unspecified, In, Out, BreakOut, BreakIn, OvertimeIn, OvertimeOut }
public enum PunchMethod { Unknown, Card, QrCode, Manual }
public enum AttendanceStatus { Absent, Present, Late, HalfDay, OnLeave, Holiday, WeeklyOff, Incomplete }
public enum AttendanceSource { Device, Manual, Leave, Holiday, WeeklyOff }
