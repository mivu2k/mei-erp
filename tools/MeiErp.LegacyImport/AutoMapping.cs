namespace MeiErp.LegacyImport;

public sealed record LegacyVehicle(
    int Id,string Make,string Model,string Registration,int? Year,string? Vin,string? Color,
    DateOnly? PurchaseDate,int Status,decimal CurrentOdometer,string? Notes,DateTime CreatedUtc,
    string? CreatedBy,DateTime? ModifiedUtc,string? ModifiedBy,bool IsDeleted,DateTime? DeletedUtc,string? DeletedBy);

public sealed record LegacyMaintenance(
    int Id,int VehicleId,DateOnly Date,int Type,decimal? Odometer,string Description,decimal Cost,
    string? Vendor,DateOnly? NextDueDate,decimal? NextDueOdometer,string PerformedById,
    string PerformedByName,DateTime CreatedUtc,string? CreatedBy,DateTime? ModifiedUtc,
    string? ModifiedBy,bool IsDeleted,DateTime? DeletedUtc,string? DeletedBy);

public static class AutoMapping
{
    // Legacy: Active=0, Sold=1, Scrapped=2. Rebuild inserts UnderRepair at 1.
    public static int VehicleStatus(int legacy) => legacy switch { 0=>0, 1=>2, 2=>3, _=>throw new InvalidDataException($"Unknown legacy vehicle status {legacy}.") };
    // Legacy: Service, Repair, Inspection, Insurance, Other.
    // Rebuild retains Inspection at 7 so the legacy meaning is not collapsed into Other.
    public static int ServiceKind(int legacy) => legacy switch { 0=>0, 1=>1, 2=>7, 3=>4, 4=>6, _=>throw new InvalidDataException($"Unknown legacy maintenance type {legacy}.") };
    public static int? Odometer(decimal? value,string field)
    {
        if(value is null)return null;
        if(value<0||value>int.MaxValue)throw new InvalidDataException($"{field} odometer {value} is outside the rebuild range.");
        if(decimal.Truncate(value.Value)!=value)throw new InvalidDataException($"{field} odometer {value} has a fractional unit and needs business review.");
        return decimal.ToInt32(value.Value);
    }
    public static DateTime Utc(DateTime value)=>value.Kind==DateTimeKind.Utc?value:DateTime.SpecifyKind(value,DateTimeKind.Utc);
    public static DateTime? Utc(DateTime? value)=>value is null?null:Utc(value.Value);
}
