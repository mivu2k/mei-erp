using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MeiErp.Modules.Repair.Tests;

[Collection("postgres")]
public sealed class IntakeProcurementTests : IAsyncLifetime
{
    private static readonly string BaseConnection=Environment.GetEnvironmentVariable("MEIERP_TEST_DB")??"Host=127.0.0.1;Username=meierp;Password=DevPassword1!;";
    private readonly string _database=$"mei_repair_depth_{Guid.NewGuid():N}";
    private readonly FixedClock _clock=new(new DateTime(2026,8,22,9,0,0,DateTimeKind.Utc));
    private readonly SystemUser _user=new("Workshop Tester");
    private readonly FakeCustomerDirectory _customers=new();
    private bool _available;private int _customerId;
    private string Connection=>BaseConnection+$"Database={_database};";
    private RepairDbContext NewDb()=>new(new DbContextOptionsBuilder<RepairDbContext>().UseNpgsql(Connection).Options,_user,_clock);

    public async Task InitializeAsync()
    {
        try
        {
            await using var admin=new DbContext(new DbContextOptionsBuilder().UseNpgsql(BaseConnection+"Database=postgres;").Options);await admin.Database.ExecuteSqlRawAsync($"CREATE DATABASE \"{_database}\";");
            await using var db=NewDb();await db.Database.EnsureCreatedAsync();await db.EnsureAuditTableForTestsAsync();await db.SaveChangesAsync();_customerId=_customers.Add("Customer","0300").Id;_available=true;
        }catch(NpgsqlException){_available=false;}
    }
    public async Task DisposeAsync(){if(!_available)return;try{await using var admin=new DbContext(new DbContextOptionsBuilder().UseNpgsql(BaseConnection+"Database=postgres;").Options);await admin.Database.ExecuteSqlRawAsync($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE);");}catch{}}

    [SkippableFact]
    public async Task One_intake_creates_one_independently_numbered_job_per_device()
    {
        Skip.IfNot(_available,"No PostgreSQL available.");await using var db=NewDb();var service=new RepairIntakeService(db,_clock,_user,_customers);
        var result=await service.ReceiveAsync(new(_customerId,"Counter receipt",[new("Laptop","Dell","5400","A1",DeviceCondition.Good,"No power",RepairPriority.High,_clock.Today.AddDays(3),"Charger"),new("Phone","Apple","14","B2",DeviceCondition.Damaged,"Broken screen",RepairPriority.Normal,null,"Case")],IntakePaymentBasis.Warranty));
        Assert.True(result.Ok,result.Error);Assert.StartsWith("INT-26-",result.Value.Number);Assert.Equal(IntakePaymentBasis.Warranty,result.Value.PaymentBasis);Assert.Equal(2,result.Value.Jobs.Count);Assert.Equal(2,result.Value.Jobs.Select(x=>x.Number).Distinct().Count());Assert.All(result.Value.Jobs,x=>Assert.Equal(result.Value.Id,x.IntakeId));
    }

    [SkippableFact]
    public async Task Invalid_intake_is_atomic_and_creates_nothing()
    {
        Skip.IfNot(_available,"No PostgreSQL available.");await using var db=NewDb();var service=new RepairIntakeService(db,_clock,_user,_customers);var result=await service.ReceiveAsync(new(_customerId,null,[new("Laptop",null,null,null,DeviceCondition.Good,"",RepairPriority.Normal,null,null)]));Assert.True(result.Failed);Assert.Empty(db.RepairIntakes);Assert.Empty(db.Jobs);
    }



    [SkippableFact]
    public async Task First_diagnosis_assigns_the_technician_and_records_a_real_transition()
    {
        Skip.IfNot(_available,"No PostgreSQL available.");await using var db=NewDb();var intake=new RepairIntakeService(db,_clock,_user,_customers);var received=await intake.ReceiveAsync(new(_customerId,null,[new("Laptop",null,null,null,DeviceCondition.Good,"No power",RepairPriority.Normal,null,null)]));var job=received.Value.Jobs.Single();var depth=new RepairWorkshopDepthService(db,_user,_clock);var result=await depth.AddDiagnosisAsync(job.Id,new("Power rail short","MOSFET","Board repair",2,3,"Bench tested",null));Assert.True(result.Ok,result.Error);db.ChangeTracker.Clear();var saved=await db.Jobs.SingleAsync(x=>x.Id==job.Id);Assert.Equal(JobStatus.Diagnosing,saved.Status);Assert.Equal("Workshop Tester",saved.AssignedToName);var history=await db.RepairStatusHistory.OrderBy(x=>x.Id).ToListAsync();Assert.Equal(2,history.Count);Assert.Equal(JobStatus.Received,history[1].FromStatus);Assert.Equal(JobStatus.Diagnosing,history[1].ToStatus);
    }

    [SkippableFact]
    public async Task Catalog_names_are_unique_without_case_sensitivity()
    {
        Skip.IfNot(_available,"No PostgreSQL available.");await using var db=NewDb();var depth=new RepairWorkshopDepthService(db,_user,_clock);Assert.True((await depth.AddCatalogAsync(RepairCatalogKind.Brand,"Dell",null)).Ok);var duplicate=await depth.AddCatalogAsync(RepairCatalogKind.Brand,"dell",null);Assert.True(duplicate.Failed);Assert.Equal("catalog.duplicate",duplicate.Code);
    }

    [SkippableFact]
    public async Task Delivery_preserves_collector_identity_release_actor_and_note()
    {
        Skip.IfNot(_available,"No PostgreSQL available.");await using var db=NewDb();var job=new Job{Number="JOB-D",CustomerId=_customerId,CustomerName="Customer",ReceivedOn=_clock.Today,DeviceType="Phone",ReportedFault="Fault",Status=JobStatus.Completed};db.Add(job);await db.SaveChangesAsync();var service=new RepairService(db,_user,_clock,_customers);var result=await service.DeliverAsync(job.Id,new("Ali","03001234567","35202-1234567-1","ID checked"));Assert.True(result.Ok,result.Error);Assert.Equal("Ali",result.Value.CollectedBy);Assert.Equal("03001234567",result.Value.CollectedByPhone);Assert.Equal("35202-1234567-1",result.Value.CollectedByCnic);Assert.Equal("Workshop Tester",result.Value.DeliveredByName);Assert.Equal("ID checked",result.Value.DeliveryNote);
    }
}
