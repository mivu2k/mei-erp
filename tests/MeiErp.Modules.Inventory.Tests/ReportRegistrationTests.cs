using MeiErp.Modules.Inventory;
using MeiErp.Platform.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MeiErp.Modules.Inventory.Tests;

public sealed class ReportRegistrationTests
{
    [Fact]
    /// <summary>
    /// Outstanding purchase orders is deliberately absent: it moved to Purchase
    /// with the orders themselves, and is asserted there.
    /// </summary>
    public void Every_legacy_inventory_report_subject_is_registered_once()
    {
        var services=new ServiceCollection();services.AddInventoryReports();using var provider=services.BuildServiceProvider();var reports=provider.GetServices<ReportDefinition>().ToList();
        Assert.Equal(7,reports.Count);Assert.Equal(reports.Count,reports.Select(x=>x.Key).Distinct().Count());
        Assert.Equal(["inventory.batches","inventory.by-warehouse","inventory.low-stock","inventory.movements","inventory.serials","inventory.stock-levels","inventory.valuation"],reports.Select(x=>x.Key).Order().ToArray());
    }
}
