using System.Security.Cryptography;
using System.Text;

namespace MeiErp.Modules.Hr;

public interface IAttendanceTokenService
{
    string Issue(int employeeId, string secret, DateTime? nowUtc = null);
    int SecondsRemaining(DateTime? nowUtc = null);
    ScannedAttendanceToken? Parse(string payload);
    bool Verify(ScannedAttendanceToken token, string secret, DateTime? nowUtc = null);
    string NewSecret();
}

public sealed record ScannedAttendanceToken(int EmployeeId, long Step, string Mac);

public sealed class AttendanceTokenService : IAttendanceTokenService
{
    private const int StepSeconds = 30;
    private const int Tolerance = 1;
    private const string Prefix = "MEIATT1";

    public string Issue(int employeeId, string secret, DateTime? nowUtc = null)
    {
        var step = Step(nowUtc);
        return $"{Prefix}:{employeeId}:{step}:{Mac(employeeId, step, secret)}";
    }

    public int SecondsRemaining(DateTime? nowUtc = null) =>
        StepSeconds - (int)(Unix(nowUtc ?? DateTime.UtcNow) % StepSeconds);

    public ScannedAttendanceToken? Parse(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        var parts = payload.Trim().Split(':');
        return parts.Length == 4 && parts[0] == Prefix
            && int.TryParse(parts[1], out var employeeId)
            && long.TryParse(parts[2], out var step)
                ? new(employeeId, step, parts[3]) : null;
    }

    public bool Verify(ScannedAttendanceToken token, string secret, DateTime? nowUtc = null)
    {
        if (Math.Abs(Step(nowUtc) - token.Step) > Tolerance) return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(Mac(token.EmployeeId, token.Step, secret)),
                Encoding.ASCII.GetBytes(token.Mac));
        }
        catch (FormatException) { return false; }
    }

    public string NewSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static long Step(DateTime? at) => Unix(at ?? DateTime.UtcNow) / StepSeconds;
    private static long Unix(DateTime at) =>
        new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)).ToUnixTimeSeconds();
    private static string Mac(int employeeId, long step, string secret)
    {
        using var hmac = new HMACSHA256(Convert.FromBase64String(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{employeeId}:{step}")))[..8];
    }
}
