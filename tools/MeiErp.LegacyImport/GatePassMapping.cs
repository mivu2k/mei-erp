namespace MeiErp.LegacyImport;

public static class GatePassMapping
{
    // Legacy direction: Inward=0, Outward=1. Rebuild: Outward=0, Inward=1.
    public static int Direction(int legacy)=>legacy switch{0=>1,1=>0,_=>throw new InvalidDataException($"Unknown legacy gate-pass direction {legacy}.")};
    // Legacy: Issued=0, Completed=1, Returned=2, Cancelled=3.
    // Rebuild adds PartiallyReturned=3, so legacy Cancelled maps explicitly to 4.
    public static int Status(int legacy)=>legacy switch{0=>0,1=>1,2=>2,3=>4,_=>throw new InvalidDataException($"Unknown legacy gate-pass status {legacy}.")};
    public static decimal ReturnedQuantity(int status,decimal quantity)=>status==2?quantity:0m;
    public static int DemoStatus(int legacy)=>legacy is >=0 and <=3?legacy:throw new InvalidDataException($"Unknown legacy demo status {legacy}.");
}
