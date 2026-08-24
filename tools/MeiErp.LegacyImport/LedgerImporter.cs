using System.Text.Json;
using System.Runtime.CompilerServices;
using MySqlConnector;
using Npgsql;

namespace MeiErp.LegacyImport;

internal static class LedgerImporter
{
    private static readonly JsonSerializerOptions ReportJson = new(){WriteIndented=true};

    public static async Task<int> RunAsync(ImportOptions options,string mysqlPassword)
    {
        await using var source=new MySqlConnection($"Server={options.MySqlHost};Port={options.MySqlPort};Database=erp_ledger;User ID={options.MySqlUser};Password={mysqlPassword};SslMode=Preferred");
        await source.OpenAsync();
        var heads=await ReadHeads(source);var ledgers=await ReadLedgers(source);var entries=await ReadEntries(source);
        var rejects=Validate(heads,ledgers,entries);
        Console.WriteLine(JsonSerializer.Serialize(new {Module="ledger",options.Apply,Heads=heads.Count,Ledgers=ledgers.Count,Entries=entries.Count,Rejections=rejects},ReportJson));
        if(rejects.Count>0){Console.Error.WriteLine("Validation rejected source rows; nothing was written.");return 2;}
        if(!options.Apply)return 0;
        var pgPassword=Environment.GetEnvironmentVariable("MEIERP_DB_PASSWORD");
        if(string.IsNullOrEmpty(pgPassword)){Console.Error.WriteLine("Set MEIERP_DB_PASSWORD for an apply run.");return 2;}
        await using var target=new NpgsqlConnection($"Host={options.PgHost};Port={options.PgPort};Database={options.PgDatabase};Username={options.PgUser};Password={pgPassword}");
        await target.OpenAsync();await Apply(target,heads,ledgers,entries);
        Console.WriteLine("Ledger import committed and target counts reconciled.");return 0;
    }

    internal sealed record Head(int Id,string Name,string? Code,int? ParentId,string? Notes,bool Active,Audit Audit);
    internal sealed record Ledger(int Id,string Name,string Party,string? Phone,string? Address,int Nature,int? ParentId,decimal Opening,DateOnly Opened,int Status,string? Reference,string? Notes,int? HeadId,Audit Audit);
    internal sealed record Entry(int Id,int LedgerId,DateOnly Date,int Direction,int Kind,decimal Amount,string Description,string? Reference,int Method,int? CounterId,Guid? Group,int? HeadId,string RecordedById,string RecordedByName,Audit Audit);
    internal sealed record Audit(DateTime Created,string? CreatedBy,DateTime? Modified,string? ModifiedBy,bool Deleted,DateTime? DeletedAt,string? DeletedBy);

    private static async Task<List<Head>> ReadHeads(MySqlConnection db)
    {
        const string sql="SELECT Id,Name,Code,ParentHeadId,Notes,IsActive,CreatedAtUtc,CreatedBy,ModifiedAtUtc,ModifiedBy,IsDeleted,DeletedAtUtc,DeletedBy FROM Heads ORDER BY Id";
        await using var cmd=new MySqlCommand(sql,db);await using var r=await cmd.ExecuteReaderAsync();var rows=new List<Head>();
        while(await r.ReadAsync())rows.Add(new(r.GetInt32(0),r.GetString(1),NString(r,2),NInt(r,3),NString(r,4),r.GetBoolean(5),AuditOf(r,6)));
        return rows;
    }
    private static async Task<List<Ledger>> ReadLedgers(MySqlConnection db)
    {
        const string sql="SELECT Id,Name,CounterpartyName,CounterpartyPhone,CounterpartyAddress,Nature,ParentLedgerId,OpeningBalance,OpenedOn,Status,Reference,Notes,HeadId,CreatedAtUtc,CreatedBy,ModifiedAtUtc,ModifiedBy,IsDeleted,DeletedAtUtc,DeletedBy FROM Ledgers ORDER BY Id";
        await using var cmd=new MySqlCommand(sql,db);await using var r=await cmd.ExecuteReaderAsync();var rows=new List<Ledger>();
        while(await r.ReadAsync())rows.Add(new(r.GetInt32(0),r.GetString(1),r.GetString(2),NString(r,3),NString(r,4),r.GetInt32(5),NInt(r,6),r.GetDecimal(7),DateOnly.FromDateTime(r.GetDateTime(8)),r.GetInt32(9),NString(r,10),NString(r,11),NInt(r,12),AuditOf(r,13)));
        return rows;
    }
    private static async Task<List<Entry>> ReadEntries(MySqlConnection db)
    {
        const string sql="SELECT Id,PlainLedgerId,Date,Direction,Kind,Amount,Description,Reference,Method,CounterLedgerId,TransferGroup,HeadId,RecordedById,RecordedByName,CreatedAtUtc,CreatedBy,ModifiedAtUtc,ModifiedBy,IsDeleted,DeletedAtUtc,DeletedBy FROM Entries ORDER BY Id";
        await using var cmd=new MySqlCommand(sql,db);await using var r=await cmd.ExecuteReaderAsync();var rows=new List<Entry>();
        while(await r.ReadAsync())rows.Add(new(r.GetInt32(0),r.GetInt32(1),DateOnly.FromDateTime(r.GetDateTime(2)),r.GetInt32(3),r.GetInt32(4),r.GetDecimal(5),r.GetString(6),NString(r,7),r.GetInt32(8),NInt(r,9),NGuid(r,10),NInt(r,11),r.GetString(12),r.GetString(13),AuditOf(r,14)));
        return rows;
    }

    private static List<string> Validate(List<Head> heads,List<Ledger> ledgers,List<Entry> entries)
    {
        var errors=new List<string>();var headIds=heads.Select(x=>x.Id).ToHashSet();var ledgerIds=ledgers.Select(x=>x.Id).ToHashSet();
        errors.AddRange(LedgerMapping.ValidateHierarchy(heads.ToDictionary(x=>x.Id,x=>x.ParentId),"Head"));
        errors.AddRange(LedgerMapping.ValidateHierarchy(ledgers.ToDictionary(x=>x.Id,x=>x.ParentId),"Ledger"));
        foreach(var l in ledgers)
        {
            if(l.HeadId is not null&&!headIds.Contains(l.HeadId.Value))errors.Add($"Ledger {l.Id} references missing head {l.HeadId}.");
            try{LedgerMapping.Nature(l.Nature);LedgerMapping.Status(l.Status);}catch(Exception e){errors.Add($"Ledger {l.Id}: {e.Message}");}
        }
        foreach(var e in entries)
        {
            if(!ledgerIds.Contains(e.LedgerId))errors.Add($"Entry {e.Id} references missing ledger {e.LedgerId}.");
            if(e.CounterId is not null&&!ledgerIds.Contains(e.CounterId.Value))errors.Add($"Entry {e.Id} references missing counter-ledger {e.CounterId}.");
            if(e.HeadId is not null&&!headIds.Contains(e.HeadId.Value))errors.Add($"Entry {e.Id} references missing head {e.HeadId}.");
            try{LedgerMapping.Direction(e.Direction);LedgerMapping.Kind(e.Kind);LedgerMapping.Method(e.Method);LedgerMapping.PositiveAmount(e.Amount,e.Id);}catch(Exception x){errors.Add(x.Message);}
            if(e.Kind==1&&(e.Group is null||e.CounterId is null))errors.Add($"Transfer entry {e.Id} lacks its group or counter-ledger.");
        }
        foreach(var group in entries.Where(x=>x.Kind==1&&x.Group is not null).GroupBy(x=>x.Group))
        {
            var pair=group.ToList();
            if(pair.Count!=2){errors.Add($"Transfer group {group.Key} has {pair.Count} rows instead of two.");continue;}
            if(pair[0].Amount!=pair[1].Amount)errors.Add($"Transfer group {group.Key} has unequal amounts.");
            if(pair[0].Direction==pair[1].Direction)errors.Add($"Transfer group {group.Key} does not have opposite directions.");
            if(pair[0].CounterId!=pair[1].LedgerId||pair[1].CounterId!=pair[0].LedgerId)errors.Add($"Transfer group {group.Key} does not reference reciprocal ledgers.");
        }
        return errors.Distinct().ToList();
    }

    private static async Task Apply(NpgsqlConnection db,List<Head> heads,List<Ledger> ledgers,List<Entry> entries)
    {
        await using var tx=await db.BeginTransactionAsync();
        await using(var count=new NpgsqlCommand("SELECT (SELECT COUNT(*) FROM ledger.\"Heads\")+(SELECT COUNT(*) FROM ledger.\"Ledgers\")+(SELECT COUNT(*) FROM ledger.\"Entries\")",db,tx))if(Convert.ToInt64(await count.ExecuteScalarAsync())!=0)throw new InvalidOperationException("Ledger target is not empty.");
        foreach(var h in heads)await Execute(db,tx,"INSERT INTO ledger.\"Heads\" (\"Id\",\"Name\",\"Code\",\"ParentHeadId\",\"Notes\",\"IsActive\",\"CreatedUtc\",\"CreatedBy\",\"ModifiedUtc\",\"ModifiedBy\",\"IsDeleted\",\"DeletedUtc\",\"DeletedBy\") VALUES (@id,@name,@code,NULL,@notes,@active,@created,@createdBy,@modified,@modifiedBy,@deleted,@deletedAt,@deletedBy)",("id",h.Id),("name",h.Name),("code",h.Code),("notes",h.Notes),("active",h.Active),AuditArgs(h.Audit));
        foreach(var l in ledgers)await Execute(db,tx,"INSERT INTO ledger.\"Ledgers\" (\"Id\",\"Name\",\"CounterpartyName\",\"CounterpartyPhone\",\"CounterpartyAddress\",\"Nature\",\"ParentLedgerId\",\"OpeningBalance\",\"OpenedOn\",\"Status\",\"Reference\",\"Notes\",\"HeadId\",\"CreatedUtc\",\"CreatedBy\",\"ModifiedUtc\",\"ModifiedBy\",\"IsDeleted\",\"DeletedUtc\",\"DeletedBy\") VALUES (@id,@name,@party,@phone,@address,@nature,NULL,@opening,@opened,@status,@reference,@notes,NULL,@created,@createdBy,@modified,@modifiedBy,@deleted,@deletedAt,@deletedBy)",("id",l.Id),("name",l.Name),("party",l.Party),("phone",l.Phone),("address",l.Address),("nature",l.Nature),("opening",l.Opening),("opened",l.Opened),("status",l.Status),("reference",l.Reference),("notes",l.Notes),AuditArgs(l.Audit));
        foreach(var h in heads.Where(x=>x.ParentId is not null))await Execute(db,tx,"UPDATE ledger.\"Heads\" SET \"ParentHeadId\"=@parent WHERE \"Id\"=@id",("parent",h.ParentId),("id",h.Id));
        foreach(var l in ledgers.Where(x=>x.ParentId is not null||x.HeadId is not null))await Execute(db,tx,"UPDATE ledger.\"Ledgers\" SET \"ParentLedgerId\"=@parent,\"HeadId\"=@head WHERE \"Id\"=@id",("parent",l.ParentId),("head",l.HeadId),("id",l.Id));
        foreach(var e in entries)await Execute(db,tx,"INSERT INTO ledger.\"Entries\" (\"Id\",\"PlainLedgerId\",\"Date\",\"Direction\",\"Kind\",\"Amount\",\"Description\",\"Reference\",\"Method\",\"CounterLedgerId\",\"TransferGroup\",\"HeadId\",\"RecordedById\",\"RecordedByName\",\"CreatedUtc\",\"CreatedBy\",\"ModifiedUtc\",\"ModifiedBy\",\"IsDeleted\",\"DeletedUtc\",\"DeletedBy\") VALUES (@id,@ledger,@date,@direction,@kind,@amount,@description,@reference,@method,@counter,@group,@head,@byId,@byName,@created,@createdBy,@modified,@modifiedBy,@deleted,@deletedAt,@deletedBy)",("id",e.Id),("ledger",e.LedgerId),("date",e.Date),("direction",e.Direction),("kind",e.Kind),("amount",e.Amount),("description",e.Description),("reference",e.Reference),("method",e.Method),("counter",e.CounterId),("group",e.Group),("head",e.HeadId),("byId",e.RecordedById),("byName",e.RecordedByName),AuditArgs(e.Audit));
        foreach(var table in new[]{"Heads","Ledgers","Entries"})await Execute(db,tx,$"SELECT setval(pg_get_serial_sequence('ledger.\"{table}\"','Id'),GREATEST(COALESCE((SELECT MAX(\"Id\") FROM ledger.\"{table}\"),1),1),true)");
        await using(var check=new NpgsqlCommand("SELECT (SELECT COUNT(*) FROM ledger.\"Heads\"),(SELECT COUNT(*) FROM ledger.\"Ledgers\"),(SELECT COUNT(*) FROM ledger.\"Entries\")",db,tx))await using(var r=await check.ExecuteReaderAsync()){await r.ReadAsync();if(r.GetInt64(0)!=heads.Count||r.GetInt64(1)!=ledgers.Count||r.GetInt64(2)!=entries.Count)throw new InvalidOperationException("Ledger count reconciliation failed.");}
        await tx.CommitAsync();
    }

    private static (string,object?)[] AuditArgs(Audit a)=>[("created",AutoMapping.Utc(a.Created)),("createdBy",a.CreatedBy),("modified",AutoMapping.Utc(a.Modified)),("modifiedBy",a.ModifiedBy),("deleted",a.Deleted),("deletedAt",AutoMapping.Utc(a.DeletedAt)),("deletedBy",a.DeletedBy)];
    private static async Task Execute(NpgsqlConnection db,NpgsqlTransaction tx,string sql,params object[] args)
    {
        await using var cmd=new NpgsqlCommand(sql,db,tx);
        foreach(var item in args)
        {
            if(item is ITuple pair&&pair.Length==2&&pair[0] is string name)cmd.Parameters.AddWithValue(name,pair[1]??DBNull.Value);
            else if(item is (string,object?)[] group)foreach(var pair2 in group)cmd.Parameters.AddWithValue(pair2.Item1,pair2.Item2??DBNull.Value);
        }
        await cmd.ExecuteNonQueryAsync();
    }
    private static Audit AuditOf(MySqlDataReader r,int i)=>new(r.GetDateTime(i),NString(r,i+1),NTime(r,i+2),NString(r,i+3),r.GetBoolean(i+4),NTime(r,i+5),NString(r,i+6));
    private static string? NString(MySqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);
    private static int? NInt(MySqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetInt32(i);
    private static DateTime? NTime(MySqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetDateTime(i);
    private static Guid? NGuid(MySqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetGuid(i);
}
