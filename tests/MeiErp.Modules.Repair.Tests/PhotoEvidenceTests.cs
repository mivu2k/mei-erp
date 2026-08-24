using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace MeiErp.Modules.Repair.Tests;

[Collection("postgres")]
public sealed class PhotoEvidenceTests:IAsyncLifetime
{
    private static readonly string BaseConnection=Environment.GetEnvironmentVariable("MEIERP_TEST_DB")??"Host=127.0.0.1;Username=meierp;Password=DevPassword1!;";
    private readonly string _database=$"mei_repair_photo_{Guid.NewGuid():N}";private readonly string _root=Path.Combine(Path.GetTempPath(),$"mei-photo-{Guid.NewGuid():N}");private readonly FixedClock _clock=new(new DateTime(2026,8,22,9,0,0,DateTimeKind.Utc));private readonly SystemUser _user=new("Photo Tester");private readonly FakeCustomerDirectory _customers=new();
    private bool _available;private int _jobId;private string Connection=>BaseConnection+$"Database={_database};";private RepairDbContext NewDb()=>new(new DbContextOptionsBuilder<RepairDbContext>().UseNpgsql(Connection).Options,_user,_clock);
    public async Task InitializeAsync(){try{Directory.CreateDirectory(_root);await using var admin=new DbContext(new DbContextOptionsBuilder().UseNpgsql(BaseConnection+"Database=postgres;").Options);await admin.Database.ExecuteSqlRawAsync($"CREATE DATABASE \"{_database}\";");await using var db=NewDb();await db.Database.EnsureCreatedAsync();await db.EnsureAuditTableForTestsAsync();var c=_customers.Add("Customer","0300");var j=new Job{Number="JOB-P",CustomerId=c.Id,CustomerName=c.Name,ReceivedOn=_clock.Today,DeviceType="Phone",ReportedFault="Damage"};db.Add(j);await db.SaveChangesAsync();_jobId=j.Id;_available=true;}catch(NpgsqlException){_available=false;}}
    public async Task DisposeAsync(){if(_available)try{await using var admin=new DbContext(new DbContextOptionsBuilder().UseNpgsql(BaseConnection+"Database=postgres;").Options);await admin.Database.ExecuteSqlRawAsync($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE);");}catch{}if(Directory.Exists(_root))Directory.Delete(_root,true);}
    private RepairPhotoService Service(RepairDbContext db)=>new(db,new TestEnvironment(_root),_user);
    [SkippableFact]public async Task Evidence_is_private_randomly_named_retrievable_and_removable(){Skip.IfNot(_available,"No PostgreSQL available.");await using var db=NewDb();var service=Service(db);var bytes=new byte[]{1,2,3,4};await using var stream=new MemoryStream(bytes);var added=await service.UploadAsync(_jobId,RepairPhotoKind.Damage,"../../damage.jpg","image/jpeg",bytes.Length,"Corner",stream);Assert.True(added.Ok,added.Error);Assert.NotEqual("damage.jpg",added.Value.StoredName);Assert.Equal("damage.jpg",added.Value.OriginalName);var file=await service.GetAsync(added.Value.Id);Assert.Equal(bytes,file!.Content);Assert.True((await service.RemoveAsync(added.Value.Id)).Ok);Assert.Null(await service.GetAsync(added.Value.Id));}
    [SkippableFact]public async Task Evidence_rejects_unsafe_type_and_oversize_metadata(){Skip.IfNot(_available,"No PostgreSQL available.");await using var db=NewDb();var service=Service(db);await using var stream=new MemoryStream([1]);Assert.True((await service.UploadAsync(_jobId,RepairPhotoKind.Other,"x.exe","application/x-msdownload",1,null,stream)).Failed);Assert.True((await service.UploadAsync(_jobId,RepairPhotoKind.Other,"x.jpg","image/jpeg",10*1024*1024+1,null,stream)).Failed);Assert.Empty(db.RepairPhotos);}
    private sealed class TestEnvironment(string root):IHostEnvironment{public string EnvironmentName{get;set;}="Test";public string ApplicationName{get;set;}="Tests";public string ContentRootPath{get;set;}=root;public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();}
}
