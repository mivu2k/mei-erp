using MeiErp.Platform.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MeiErp.Modules.Repair.Tests;

public sealed class ReportRegistrationTests
{
    [Fact]
    /// <summary>
    /// Nine, down from the original seventeen. What is left is the workshop:
    /// jobs, pipeline, ageing, turnaround, diagnoses, technicians, failures,
    /// symptoms, warranty mix.
    ///
    /// The eight that went were about money rather than work - quotations,
    /// receivables, collections, customer activity, daily takings, the summary,
    /// part cost history and supplier spend. They moved to Sales and Purchase
    /// with the documents they report on.
    /// </summary>
    public void Repair_registers_full_legacy_catalog_plus_operational_registers()
    {
        var services=new ServiceCollection();services.AddRepairReports();using var provider=services.BuildServiceProvider();var reports=provider.GetServices<ReportDefinition>().ToList();Assert.Equal(9,reports.Count);Assert.Equal(9,reports.Select(x=>x.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());Assert.All(reports,x=>Assert.Equal(RepairModule.Key,x.ModuleKey));
    }
}
