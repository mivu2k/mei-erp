using System.Text.Json;
using MySqlConnector;
using Npgsql;
using MeiErp.LegacyImport;

var options=ImportOptions.Parse(args);
if(options.Module is not ("auto" or "ledger" or "gatepass" or "identity" or "tender"))return Fail("Implemented modules: auto, ledger, gatepass, identity, tender.");
var mysqlPassword=Environment.GetEnvironmentVariable("LEGACY_MYSQL_PASSWORD");
if(string.IsNullOrEmpty(mysqlPassword))return Fail("Set LEGACY_MYSQL_PASSWORD.");
if(options.Apply&&!options.ConfirmEmptyTarget)return Fail("--apply also requires --confirm-empty-target.");
if(options.Module=="ledger")return await LedgerImporter.RunAsync(options,mysqlPassword);
if(options.Module=="gatepass")return await GatePassImporter.RunAsync(options,mysqlPassword);
if(options.Module=="identity")return await IdentityImporter.RunAsync(options,mysqlPassword);
if(options.Module=="tender")return await TenderImporter.RunAsync(options,mysqlPassword);
var source=new MySqlConnection($"Server={options.MySqlHost};Port={options.MySqlPort};Database=erp_auto;User ID={options.MySqlUser};Password={mysqlPassword};SslMode=Preferred");
await source.OpenAsync();
var vehicles=await ReadVehicles(source);var maintenance=await ReadMaintenance(source);
var rejects=Validate(vehicles,maintenance);
var report=new ImportReport("auto",options.Apply,vehicles.Count,maintenance.Count,rejects);
Console.WriteLine(JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
if(rejects.Count>0)return Fail("Validation rejected source rows; nothing was written.");
if(!options.Apply)return 0;
var pgPassword=Environment.GetEnvironmentVariable("MEIERP_DB_PASSWORD");
if(string.IsNullOrEmpty(pgPassword))return Fail("Set MEIERP_DB_PASSWORD for an apply run.");
var target=new NpgsqlConnection($"Host={options.PgHost};Port={options.PgPort};Database={options.PgDatabase};Username={options.PgUser};Password={pgPassword}");
await target.OpenAsync();
await Apply(target,vehicles,maintenance);
Console.WriteLine("Fleet import committed and target counts reconciled.");return 0;

static int Fail(string message){Console.Error.WriteLine(message);return 2;}
static async Task<List<LegacyVehicle>> ReadVehicles(MySqlConnection db)
{
    const string sql="SELECT Id,Make,Model,RegistrationNumber,Year,Vin,Color,PurchaseDate,Status,CurrentOdometer,Notes,CreatedAtUtc,CreatedBy,ModifiedAtUtc,ModifiedBy,IsDeleted,DeletedAtUtc,DeletedBy FROM Vehicles ORDER BY Id";
    await using var cmd=new MySqlCommand(sql,db);await using var r=await cmd.ExecuteReaderAsync();var rows=new List<LegacyVehicle>();
    while(await r.ReadAsync())rows.Add(new(r.GetInt32(0),r.GetString(1),r.GetString(2),r.GetString(3),NInt(r,4),NString(r,5),NString(r,6),NDate(r,7),r.GetInt32(8),r.GetDecimal(9),NString(r,10),r.GetDateTime(11),NString(r,12),NTime(r,13),NString(r,14),r.GetBoolean(15),NTime(r,16),NString(r,17)));
    return rows;
}
static async Task<List<LegacyMaintenance>> ReadMaintenance(MySqlConnection db)
{
    const string sql="SELECT Id,VehicleId,Date,Type,OdometerAtService,Description,Cost,VendorName,NextDueDate,NextDueOdometer,PerformedById,PerformedByName,CreatedAtUtc,CreatedBy,ModifiedAtUtc,ModifiedBy,IsDeleted,DeletedAtUtc,DeletedBy FROM MaintenanceRecords ORDER BY Id";
    await using var cmd=new MySqlCommand(sql,db);await using var r=await cmd.ExecuteReaderAsync();var rows=new List<LegacyMaintenance>();
    while(await r.ReadAsync())rows.Add(new(r.GetInt32(0),r.GetInt32(1),DateOnly.FromDateTime(r.GetDateTime(2)),r.GetInt32(3),NDecimal(r,4),r.GetString(5),r.GetDecimal(6),NString(r,7),NDate(r,8),NDecimal(r,9),r.GetString(10),r.GetString(11),r.GetDateTime(12),NString(r,13),NTime(r,14),NString(r,15),r.GetBoolean(16),NTime(r,17),NString(r,18)));
    return rows;
}
static List<string> Validate(List<LegacyVehicle> vehicles,List<LegacyMaintenance> rows)
{
    var errors=new List<string>();var ids=vehicles.Select(x=>x.Id).ToHashSet();
    foreach(var group in vehicles.GroupBy(x=>x.Registration.Trim(),StringComparer.OrdinalIgnoreCase).Where(x=>x.Count()>1))errors.Add($"Duplicate registration: {group.Key}");
    foreach(var v in vehicles)try{_ = AutoMapping.VehicleStatus(v.Status);_ = AutoMapping.Odometer(v.CurrentOdometer,$"Vehicle {v.Id}");}catch(Exception e){errors.Add(e.Message);}
    foreach(var m in rows){if(!ids.Contains(m.VehicleId))errors.Add($"Maintenance {m.Id} references missing vehicle {m.VehicleId}.");try{_ = AutoMapping.ServiceKind(m.Type);_ = AutoMapping.Odometer(m.Odometer,$"Maintenance {m.Id}");_ = AutoMapping.Odometer(m.NextDueOdometer,$"Maintenance {m.Id} next-due");}catch(Exception e){errors.Add(e.Message);}}
    return errors;
}
static async Task Apply(NpgsqlConnection db,List<LegacyVehicle> vehicles,List<LegacyMaintenance> maintenance)
{
    await using var tx=await db.BeginTransactionAsync();
    await using(var count=new NpgsqlCommand("SELECT (SELECT COUNT(*) FROM auto.\"Vehicles\")+(SELECT COUNT(*) FROM auto.\"Services\")",db,tx))if(Convert.ToInt64(await count.ExecuteScalarAsync())!=0)throw new InvalidOperationException("Fleet target is not empty.");
    foreach(var v in vehicles){await using var cmd=new NpgsqlCommand("INSERT INTO auto.\"Vehicles\" (\"Id\",\"Registration\",\"Make\",\"Model\",\"Year\",\"ChassisNumber\",\"Color\",\"PurchasedOn\",\"CurrentOdometer\",\"Status\",\"Notes\",\"CreatedUtc\",\"CreatedBy\",\"ModifiedUtc\",\"ModifiedBy\",\"IsDeleted\",\"DeletedUtc\",\"DeletedBy\") VALUES (@id,@reg,@make,@model,@year,@vin,@color,@purchase,@odo,@status,@notes,@created,@createdBy,@modified,@modifiedBy,@deleted,@deletedUtc,@deletedBy)",db,tx);Add(cmd,"id",v.Id);Add(cmd,"reg",v.Registration.Trim().ToUpperInvariant());Add(cmd,"make",v.Make);Add(cmd,"model",v.Model);Add(cmd,"year",v.Year);Add(cmd,"vin",v.Vin);Add(cmd,"color",v.Color);Add(cmd,"purchase",v.PurchaseDate);Add(cmd,"odo",AutoMapping.Odometer(v.CurrentOdometer,$"Vehicle {v.Id}"));Add(cmd,"status",AutoMapping.VehicleStatus(v.Status));Add(cmd,"notes",v.Notes);Add(cmd,"created",AutoMapping.Utc(v.CreatedUtc));Add(cmd,"createdBy",v.CreatedBy);Add(cmd,"modified",AutoMapping.Utc(v.ModifiedUtc));Add(cmd,"modifiedBy",v.ModifiedBy);Add(cmd,"deleted",v.IsDeleted);Add(cmd,"deletedUtc",AutoMapping.Utc(v.DeletedUtc));Add(cmd,"deletedBy",v.DeletedBy);await cmd.ExecuteNonQueryAsync();}
    var registrations=vehicles.ToDictionary(x=>x.Id,x=>x.Registration.Trim().ToUpperInvariant());
    foreach(var m in maintenance){await using var cmd=new NpgsqlCommand("INSERT INTO auto.\"Services\" (\"Id\",\"VehicleId\",\"VehicleRegistration\",\"Date\",\"Kind\",\"Description\",\"Vendor\",\"Cost\",\"Odometer\",\"NextDueDate\",\"NextDueOdometer\",\"CreatedUtc\",\"CreatedBy\",\"ModifiedUtc\",\"ModifiedBy\",\"IsDeleted\",\"DeletedUtc\",\"DeletedBy\") VALUES (@id,@vehicle,@reg,@date,@kind,@description,@vendor,@cost,@odo,@due,@dueOdo,@created,@createdBy,@modified,@modifiedBy,@deleted,@deletedUtc,@deletedBy)",db,tx);Add(cmd,"id",m.Id);Add(cmd,"vehicle",m.VehicleId);Add(cmd,"reg",registrations[m.VehicleId]);Add(cmd,"date",m.Date);Add(cmd,"kind",AutoMapping.ServiceKind(m.Type));Add(cmd,"description",m.Description);Add(cmd,"vendor",m.Vendor);Add(cmd,"cost",m.Cost);Add(cmd,"odo",AutoMapping.Odometer(m.Odometer,$"Maintenance {m.Id}"));Add(cmd,"due",m.NextDueDate);Add(cmd,"dueOdo",AutoMapping.Odometer(m.NextDueOdometer,$"Maintenance {m.Id} next-due"));Add(cmd,"created",AutoMapping.Utc(m.CreatedUtc));Add(cmd,"createdBy",m.CreatedBy);Add(cmd,"modified",AutoMapping.Utc(m.ModifiedUtc));Add(cmd,"modifiedBy",m.ModifiedBy);Add(cmd,"deleted",m.IsDeleted);Add(cmd,"deletedUtc",AutoMapping.Utc(m.DeletedUtc));Add(cmd,"deletedBy",m.DeletedBy);await cmd.ExecuteNonQueryAsync();}
    await using(var seq=new NpgsqlCommand("SELECT setval(pg_get_serial_sequence('auto.\"Vehicles\"','Id'),GREATEST(COALESCE((SELECT MAX(\"Id\") FROM auto.\"Vehicles\"),1),1),true); SELECT setval(pg_get_serial_sequence('auto.\"Services\"','Id'),GREATEST(COALESCE((SELECT MAX(\"Id\") FROM auto.\"Services\"),1),1),true)",db,tx))await seq.ExecuteNonQueryAsync();
    await using(var check=new NpgsqlCommand("SELECT (SELECT COUNT(*) FROM auto.\"Vehicles\"),(SELECT COUNT(*) FROM auto.\"Services\")",db,tx))await using(var r=await check.ExecuteReaderAsync()){await r.ReadAsync();if(r.GetInt64(0)!=vehicles.Count||r.GetInt64(1)!=maintenance.Count)throw new InvalidOperationException("Fleet count reconciliation failed.");}
    await tx.CommitAsync();
}
static void Add(NpgsqlCommand cmd,string name,object? value)=>cmd.Parameters.AddWithValue(name,value??DBNull.Value);
static string? NString(MySqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);
static int? NInt(MySqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetInt32(i);
static decimal? NDecimal(MySqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetDecimal(i);
static DateTime? NTime(MySqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetDateTime(i);
static DateOnly? NDate(MySqlDataReader r,int i)=>r.IsDBNull(i)?null:DateOnly.FromDateTime(r.GetDateTime(i));

sealed record ImportReport(string Module,bool Apply,int Vehicles,int Maintenance,IReadOnlyList<string> Rejections);
internal sealed record ImportOptions(string Module,bool Apply,bool ConfirmEmptyTarget,string MySqlHost,uint MySqlPort,string MySqlUser,string PgHost,int PgPort,string PgDatabase,string PgUser)
{
    public static ImportOptions Parse(string[] args)=>new(Value(args,"--module")??"auto",args.Contains("--apply"),args.Contains("--confirm-empty-target"),Environment.GetEnvironmentVariable("LEGACY_MYSQL_HOST")??"127.0.0.1",uint.TryParse(Environment.GetEnvironmentVariable("LEGACY_MYSQL_PORT"),out var mp)?mp:3306,Environment.GetEnvironmentVariable("LEGACY_MYSQL_USER")??"finance",Environment.GetEnvironmentVariable("MEIERP_DB_HOST")??"127.0.0.1",int.TryParse(Environment.GetEnvironmentVariable("MEIERP_DB_PORT"),out var pp)?pp:5432,Environment.GetEnvironmentVariable("MEIERP_DB_NAME")??"mei_erp",Environment.GetEnvironmentVariable("MEIERP_DB_USER")??"meierp");
    private static string? Value(string[] args,string key){var i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
}
