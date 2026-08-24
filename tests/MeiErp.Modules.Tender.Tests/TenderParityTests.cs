using MeiErp.Modules.Tender;
using Xunit;

namespace MeiErp.Modules.Tender.Tests;

public class TenderParityTests
{
    [Fact]
    public void Document_metadata_requires_a_name()
    {
        var result = TenderParityRules.ValidateDocument(new TenderDocument());
        Assert.True(result.Failed);
        Assert.Equal("document.no-name", result.Code);
    }

    [Fact]
    public void Competitor_validation_rejects_negative_amount_and_nonpositive_rank()
    {
        var amount = TenderParityRules.ValidateCompetitor(new TenderCompetitor { BidderName = "Bidder", QuotedAmount = -1 });
        var rank = TenderParityRules.ValidateCompetitor(new TenderCompetitor { BidderName = "Bidder", Rank = 0 });

        Assert.Equal("competitor.bad-amount", amount.Code);
        Assert.Equal("competitor.bad-rank", rank.Code);
    }

    [Fact]
    public void Competitor_validation_accepts_an_observed_bid()
    {
        var result = TenderParityRules.ValidateCompetitor(new TenderCompetitor
        {
            BidderName = "MEI Engineering", QuotedAmount = 125_000, Rank = 1, IsOwnBid = true
        });

        Assert.True(result.Ok);
    }
}
