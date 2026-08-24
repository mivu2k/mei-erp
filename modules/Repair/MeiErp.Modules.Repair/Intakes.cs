using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Repair;

public enum DeviceCondition { New, Good, Fair, Damaged }
public enum RepairPriority { Low, Normal, High, Urgent }
public enum IntakePaymentBasis { Cash, Credit, BankTransfer, Warranty, Card }

public class RepairIntake : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }
    public string Number { get; set; } = "";
    /// <summary>The customer as the party master knows them; see <see cref="Job.CustomerId"/>.</summary>
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = "";
    public DateTime ReceivedUtc { get; set; }
    public string ReceivedById { get; set; } = "";
    public string ReceivedByName { get; set; } = "";
    public string? Notes { get; set; }
    public IntakePaymentBasis PaymentBasis { get; set; }
    public List<Job> Jobs { get; set; } = [];
}

public sealed record IntakeDeviceInput(string DeviceType, string? Make, string? Model,
    string? SerialNumber, DeviceCondition Condition, string ReportedFault,
    RepairPriority Priority, DateOnly? PromisedOn, string? Accessories, string? Symptoms = null);
public sealed record IntakeInput(int CustomerId, string? Notes, IReadOnlyList<IntakeDeviceInput> Devices,IntakePaymentBasis PaymentBasis=IntakePaymentBasis.Cash);

public interface IRepairIntakeService
{
    Task<IReadOnlyList<RepairIntake>> ListAsync(string? search, CancellationToken ct = default);
    Task<RepairIntake?> GetAsync(int id, CancellationToken ct = default);
    Task<Result<RepairIntake>> ReceiveAsync(IntakeInput input, CancellationToken ct = default);
}

// The customer directory is optional so Repair stays buildable without the
// party master; without it, receiving a device refuses rather than inventing
// a customer.
public sealed class RepairIntakeService(RepairDbContext db, IClock clock, ICurrentUser user,
    IRepairCustomerDirectory? customers = null)
    : IRepairIntakeService
{
    public async Task<IReadOnlyList<RepairIntake>> ListAsync(string? search,CancellationToken ct=default)
    {
        var q=db.RepairIntakes.AsNoTracking().Include(x=>x.Jobs).AsSplitQuery().AsQueryable();
        if(!string.IsNullOrWhiteSpace(search)){var p=$"%{search.Trim()}%";q=q.Where(x=>EF.Functions.ILike(x.Number,p)||EF.Functions.ILike(x.CustomerName,p));}
        return await q.OrderByDescending(x=>x.Id).Take(300).ToListAsync(ct);
    }

    public Task<RepairIntake?> GetAsync(int id,CancellationToken ct=default)=>db.RepairIntakes.AsNoTracking().Include(x=>x.Jobs).AsSplitQuery().FirstOrDefaultAsync(x=>x.Id==id,ct);

    public async Task<Result<RepairIntake>> ReceiveAsync(IntakeInput input,CancellationToken ct=default)
    {
        var customer=customers is null?null:await customers.GetAsync(input.CustomerId,ct);
        if(customer is null)return Result.Fail<RepairIntake>("Select a valid customer.","intake.no-customer");
        if(input.Devices.Count==0)return Result.Fail<RepairIntake>("Add at least one device.","intake.no-devices");
        if(input.Devices.Any(x=>string.IsNullOrWhiteSpace(x.DeviceType)||string.IsNullOrWhiteSpace(x.ReportedFault)))return Result.Fail<RepairIntake>("Every device needs a type and reported fault.","intake.bad-device");
        var strategy=db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async()=>
        {
            await using var tx=await db.Database.BeginTransactionAsync(ct);
            var year=clock.Today.Year;var intakeStem=$"INT-{year%100:D2}-";var intakeCount=await db.RepairIntakes.IgnoreQueryFilters().CountAsync(x=>x.Number.StartsWith(intakeStem),ct);
            var jobStem=$"JOB-{year%100:D2}-";var jobCount=await db.Jobs.IgnoreQueryFilters().CountAsync(x=>x.Number.StartsWith(jobStem),ct);
            var row=new RepairIntake{Number=intakeStem+$"{intakeCount+1:D4}",CustomerId=customer.Id,CustomerName=customer.Name,ReceivedUtc=clock.UtcNow,ReceivedById=user.UserId??"system",ReceivedByName=user.Name??"System",Notes=input.Notes,PaymentBasis=input.PaymentBasis};
            for(var i=0;i<input.Devices.Count;i++)
            {
                var x=input.Devices[i];var job=new Job{Number=jobStem+$"{jobCount+i+1:D4}",Intake=row,CustomerId=customer.Id,CustomerName=customer.Name,ReceivedOn=clock.Today,DeviceType=x.DeviceType.Trim(),Make=x.Make,Model=x.Model,SerialNumber=x.SerialNumber,Condition=x.Condition,ReportedFault=x.ReportedFault.Trim(),Priority=x.Priority,PromisedOn=x.PromisedOn,Accessories=x.Accessories,Symptoms=x.Symptoms,Status=JobStatus.Received};job.StatusHistory.Add(new(){FromStatus=JobStatus.Received,ToStatus=JobStatus.Received,ChangedById=user.UserId??"system",ChangedByName=user.Name??"System",ChangedUtc=clock.UtcNow,Note="Received at counter"});row.Jobs.Add(job);
            }
            db.RepairIntakes.Add(row);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Result.Success(row);
        });
    }
}
