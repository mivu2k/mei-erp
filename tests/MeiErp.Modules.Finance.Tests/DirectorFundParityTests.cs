using MeiErp.Modules.Finance;
using Xunit;

namespace MeiErp.Modules.Finance.Tests;

public sealed class DirectorFundParityTests
{
    [Fact]
    public void Advances_are_ordinary_by_default()
    {
        var input = new AdvanceInput(null, "Office supplies", 1000,
            new DateOnly(2026, 8, 25), null, null, null);

        Assert.False(input.IsDirectorRequest);
    }

    [Fact]
    public void Director_fund_mode_is_explicit_and_survives_the_advance_input_contract()
    {
        var input = new AdvanceInput(null, "Director travel", 1000,
            new DateOnly(2026, 8, 25), null, null, null, IsDirectorRequest: true);

        Assert.True(input.IsDirectorRequest);
    }

    [Fact]
    public void Director_fund_records_are_distinguishable_from_employee_advances()
    {
        var ordinary = new Advance { Reference = "ADV-26-0001" };
        var director = new Advance { Reference = "DFR-26-0001", IsDirectorRequest = true };

        Assert.False(ordinary.IsDirectorRequest);
        Assert.True(director.IsDirectorRequest);
        Assert.StartsWith("DFR-", director.Reference);
    }
}
