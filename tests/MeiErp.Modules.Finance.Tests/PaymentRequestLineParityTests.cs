using MeiErp.Modules.Finance;
using Xunit;

namespace MeiErp.Modules.Finance.Tests;

public sealed class PaymentRequestLineParityTests
{
    [Fact]
    public void Itemized_input_keeps_each_line_reason_and_account()
    {
        var lines = new[]
        {
            new PaymentRequestLineInput("Travel", 1250, "Taxi to site", "", 41),
            new PaymentRequestLineInput("Meals", 750, "Client meeting", null, 42)
        };

        Assert.Equal(2, lines.Length);
        Assert.Equal("Taxi to site", lines[0].Reason);
        Assert.Equal(41, lines[0].ExpenseAccountId);
        Assert.Equal(750, lines[1].Amount);
    }

    [Fact]
    public void Itemized_request_total_is_the_sum_of_its_lines()
    {
        var lines = new[]
        {
            new PaymentRequestLineInput("Travel", 1250, "Taxi", null, 41),
            new PaymentRequestLineInput("Meals", 750, "Meeting", null, 42)
        };

        var request = new PaymentRequest { Amount = lines.Sum(line => line.Amount) };

        Assert.Equal(2000, request.Amount);
    }

    [Fact]
    public void Payment_request_line_defaults_are_safe_for_draft_editing()
    {
        var line = new PaymentRequestLine();

        Assert.Equal(0, line.Amount);
        Assert.Null(line.ExpenseAccountId);
        Assert.Null(line.Reason);
    }
}
