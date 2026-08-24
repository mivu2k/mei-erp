using MeiErp.Modules.Auto;
using MeiErp.Modules.GatePass;
using MeiErp.Modules.Ledger;
using MeiErp.Modules.Tender;
using MeiErp.Platform.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MeiErp.Modules.Tender.Tests;

public sealed class RemainingReportRegistrationTests
{
    [Fact]
    public void Remaining_legacy_report_catalogs_register_exact_stable_keys()
    {
        var services=new ServiceCollection();
        services.AddTenderReports().AddAutoReports().AddGatePassReports().AddLedgerReports();
        using var provider=services.BuildServiceProvider();
        var keys=provider.GetServices<ReportDefinition>().Select(x=>x.Key).OrderBy(x=>x).ToArray();
        Assert.Equal([
            "auto.maintenance-cost",
            "gatepass.outstanding","gatepass.overdue",
            "ledger.by-head","ledger.outstanding","ledger.tree-rollup",
            "tender.authority","tender.bank-exposure","tender.expiry","tender.pipeline","tender.security-register","tender.win-loss"
        ],keys);
        Assert.Equal(keys.Length,keys.Distinct().Count());
    }
}
