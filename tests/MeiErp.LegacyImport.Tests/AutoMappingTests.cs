using Xunit;
namespace MeiErp.LegacyImport.Tests;
public sealed class AutoMappingTests
{
    private static readonly int[] LegacyVehicleStatuses = [0, 1, 2];
    private static readonly int[] ExpectedVehicleStatuses = [0, 2, 3];
    private static readonly int[] LegacyMaintenanceTypes = [0, 1, 2, 3, 4];
    private static readonly int[] ExpectedServiceKinds = [0, 1, 7, 4, 6];

    [Fact]public void Disposed_statuses_follow_rebuild_enum()=>Assert.Equal(ExpectedVehicleStatuses,LegacyVehicleStatuses.Select(AutoMapping.VehicleStatus));
    [Fact]public void Legacy_maintenance_types_have_explicit_targets()=>Assert.Equal(ExpectedServiceKinds,LegacyMaintenanceTypes.Select(AutoMapping.ServiceKind));
    [Fact]public void Fractional_odometer_is_rejected_instead_of_silently_rounded()=>Assert.Throws<InvalidDataException>(()=>AutoMapping.Odometer(10.5m,"test"));
    [Fact]public void Whole_odometer_is_preserved()=>Assert.Equal(1234,AutoMapping.Odometer(1234m,"test"));
}

public sealed class LedgerMappingTests
{
    [Fact]public void Existing_ledger_enums_are_preserved()=>Assert.Equal(5,LedgerMapping.Method(5));
    [Fact]public void Unknown_enum_is_rejected()=>Assert.Throws<InvalidDataException>(()=>LedgerMapping.Status(3));
    [Fact]public void Non_positive_entry_amount_is_rejected()=>Assert.Throws<InvalidDataException>(()=>LedgerMapping.PositiveAmount(0,12));
    [Fact]public void Hierarchy_cycle_is_rejected()
    {
        var errors=LedgerMapping.ValidateHierarchy(new Dictionary<int,int?>{{1,2},{2,1}},"Ledger");
        Assert.NotEmpty(errors);
    }
    [Fact]public void Valid_hierarchy_passes()=>Assert.Empty(LedgerMapping.ValidateHierarchy(new Dictionary<int,int?>{{1,null},{2,1}},"Ledger"));
}
