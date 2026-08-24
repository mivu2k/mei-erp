using MeiErp.Platform.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MeiErp.Modules.Trade.Tests;

public sealed class ReportRegistrationTests
{
    /// <summary>
    /// The commercial reports, registered against whichever module now owns the
    /// data they read. Six of these came over from Repair with the documents.
    /// </summary>
    [Fact]
    public void Commercial_reports_are_registered_against_the_module_that_owns_them()
    {
        var services = new ServiceCollection();
        services.AddTradeReports();
        using var provider = services.BuildServiceProvider();

        var reports = provider.GetServices<ReportDefinition>().ToList();

        Assert.Equal(
            ["purchase.part-cost-history", "purchase.payables", "purchase.quotation-outcomes",
             "purchase.supplier-spend", "sales.quotation-outcomes", "sales.receivables"],
            reports.Select(x => x.Key).Order().ToArray());

        // Each report belongs to the module whose data it reads, so a buyer
        // never sees the sales figures in their report list.
        Assert.All(reports, x => Assert.Equal(
            x.Key.StartsWith("sales.") ? SalesModule.Key : PurchaseModule.Key, x.ModuleKey));
    }
}
