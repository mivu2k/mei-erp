using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MeiErp.Modules.Hr;

/// <summary>
/// Brings the HR schema up and puts the standard Pakistani leave types in
/// place, so leave works the moment the module is installed.
///
/// Additive and idempotent, like every seeder here: it creates what is missing
/// and never overwrites what someone has changed.
/// </summary>
public sealed class HrSeeder(HrDbContext db)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        if (!await db.LeaveTypes.AnyAsync(ct))
        {
            db.LeaveTypes.AddRange(
                new LeaveType
                {
                    Code = "AL", Name = "Annual leave",
                    AnnualEntitlement = 14, IsPaid = true,
                    MaxCarryForward = 7, RequiresApproval = true
                },
                new LeaveType
                {
                    Code = "CL", Name = "Casual leave",
                    AnnualEntitlement = 10, IsPaid = true,
                    MaxCarryForward = 0, RequiresApproval = true
                },
                new LeaveType
                {
                    Code = "SL", Name = "Sick leave",
                    AnnualEntitlement = 8, IsPaid = true,
                    MaxCarryForward = 0, RequiresApproval = true
                },
                new LeaveType
                {
                    // Zero entitlement means unlimited: unpaid leave is not
                    // checked against a balance, it is checked by the approver.
                    Code = "UL", Name = "Unpaid leave",
                    AnnualEntitlement = 0, IsPaid = false,
                    MaxCarryForward = 0, RequiresApproval = true
                });

            await db.SaveChangesAsync(ct);
        }

        if (!await db.Holidays.AnyAsync(ct))
        {
            // Annual holidays match on day and month whatever year they carry,
            // so these do not need re-entering every January.
            db.Holidays.AddRange(
                new Holiday { Date = new DateOnly(2026, 2, 5), Name = "Kashmir Day", IsAnnual = true },
                new Holiday { Date = new DateOnly(2026, 3, 23), Name = "Pakistan Day", IsAnnual = true },
                new Holiday { Date = new DateOnly(2026, 5, 1), Name = "Labour Day", IsAnnual = true },
                new Holiday { Date = new DateOnly(2026, 8, 14), Name = "Independence Day", IsAnnual = true },
                new Holiday { Date = new DateOnly(2026, 12, 25), Name = "Quaid-e-Azam Day", IsAnnual = true });

            await db.SaveChangesAsync(ct);
        }
    }
}

public static class HrSeederExtensions
{
    public static async Task SeedHrAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HrDbContext>();
        await new HrSeeder(db).SeedAsync();
    }
}
