using Xunit;

namespace MeiErp.LegacyImport.Tests;

public sealed class TenderMappingTests
{
    private static readonly int[] LegacyStatuses = [0, 1, 2, 3, 4, 5, 6, 7];
    private static readonly int[] ExpectedStatuses = [0, 1, 2, 7, 4, 5, 8, 6];
    private static readonly int[] LegacyGuarantees = [0, 1, 2, 3, 4, 5, 6];
    private static readonly int[] ExpectedGuarantees = [0, 1, 2, 5, 3, 4, 6];

    [Fact]
    public void Legacy_tender_statuses_map_explicitly()
    {
        Assert.Equal(ExpectedStatuses, LegacyStatuses.Select(TenderMapping.Status));
    }

    [Fact]
    public void Legacy_guarantee_types_preserve_security_meaning()
    {
        Assert.Equal(ExpectedGuarantees, LegacyGuarantees.Select(TenderMapping.GuaranteeKind));
    }

    [Fact]
    public void Unknown_tender_enum_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => TenderMapping.Status(99));
        Assert.Throws<InvalidDataException>(() => TenderMapping.Bounded(4, 0, 2, "submission mode"));
    }

    [Fact]
    public void Negative_tender_amount_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => TenderMapping.NonNegative(-1, "estimate"));
    }
}
