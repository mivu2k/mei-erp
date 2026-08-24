using Xunit;

namespace MeiErp.Modules.Hr.Tests;

public sealed class AttendanceTokenTests
{
    private static readonly DateTime Noon = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);
    private readonly AttendanceTokenService _tokens = new();

    [Fact] public void Fresh_code_verifies_and_is_bound_to_employee_and_secret()
    {
        var secret = _tokens.NewSecret();
        var token = _tokens.Parse(_tokens.Issue(42, secret, Noon))!;
        Assert.True(_tokens.Verify(token, secret, Noon));
        Assert.False(_tokens.Verify(token with { EmployeeId = 99 }, secret, Noon));
        Assert.False(_tokens.Verify(token, _tokens.NewSecret(), Noon));
    }

    [Theory]
    [InlineData(45, true)]
    [InlineData(-30, true)]
    [InlineData(120, false)]
    [InlineData(-120, false)]
    public void Code_has_a_small_clock_drift_window(int seconds, bool expected)
    {
        var secret = _tokens.NewSecret();
        var token = _tokens.Parse(_tokens.Issue(42, secret, Noon))!;
        Assert.Equal(expected, _tokens.Verify(token, secret, Noon.AddSeconds(seconds)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1042")]
    [InlineData("MEIATT1:42:1")]
    [InlineData("OTHER:42:1:abcdef01")]
    [InlineData("MEIATT1:notanumber:1:abcd")]
    public void Non_attendance_payloads_are_ignored(string payload) => Assert.Null(_tokens.Parse(payload));

    [Fact] public void Code_rotates_and_countdown_matches_the_window()
    {
        var secret = _tokens.NewSecret();
        Assert.NotEqual(_tokens.Issue(42, secret, Noon), _tokens.Issue(42, secret, Noon.AddSeconds(30)));
        Assert.Equal(30, _tokens.SecondsRemaining(Noon));
        Assert.Equal(1, _tokens.SecondsRemaining(Noon.AddSeconds(29)));
    }
}
