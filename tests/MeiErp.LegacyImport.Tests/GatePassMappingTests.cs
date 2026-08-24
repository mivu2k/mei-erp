using Xunit;

namespace MeiErp.LegacyImport.Tests;

public sealed class GatePassMappingTests
{
    [Fact] public void Legacy_inward_direction_maps_to_rebuild_inward()=>Assert.Equal(1,GatePassMapping.Direction(0));
    [Fact] public void Legacy_outward_direction_maps_to_rebuild_outward()=>Assert.Equal(0,GatePassMapping.Direction(1));
    [Fact] public void Returned_pass_marks_each_item_returned()=>Assert.Equal(3m,GatePassMapping.ReturnedQuantity(2,3m));
    [Fact] public void Legacy_cancelled_maps_to_rebuild_cancelled()=>Assert.Equal(4,GatePassMapping.Status(3));
    [Fact] public void Unknown_status_is_rejected()=>Assert.Throws<InvalidDataException>(()=>GatePassMapping.Status(4));
}
