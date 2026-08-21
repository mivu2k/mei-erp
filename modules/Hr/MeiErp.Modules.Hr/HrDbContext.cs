using MeiErp.Platform.Kernel;
using MeiErp.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Hr;

public class HrDbContext(
    DbContextOptions<HrDbContext> options, ICurrentUser currentUser, IClock clock)
    : ModuleDbContext(options, currentUser, clock)
{
    protected override string Schema => "hr";

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeaveBalance> LeaveBalances => Set<LeaveBalance>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<Holiday> Holidays => Set<Holiday>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Employee>(b =>
        {
            b.Property(e => e.Code).HasMaxLength(30).IsRequired();
            b.Property(e => e.FullName).HasMaxLength(200).IsRequired();

            // Two people sharing a staff number merges their leave and their
            // attendance, which is worse than having neither.
            b.HasIndex(e => e.Code).IsUnique().HasFilter("\"IsDeleted\" = false");

            // One login must not map to two employees, or "my leave" becomes
            // ambiguous. Filtered so the many staff without a login are fine.
            b.HasIndex(e => e.UserId).IsUnique()
             .HasFilter("\"UserId\" IS NOT NULL AND \"IsDeleted\" = false");

            b.HasMany(e => e.LeaveBalances)
             .WithOne(l => l.Employee)
             .HasForeignKey(l => l.EmployeeId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LeaveType>(b =>
        {
            b.Property(t => t.Name).HasMaxLength(100).IsRequired();
            b.Property(t => t.Code).HasMaxLength(20).IsRequired();
            b.HasIndex(t => t.Code).IsUnique().HasFilter("\"IsDeleted\" = false");
        });

        modelBuilder.Entity<LeaveBalance>(b =>
        {
            // One balance per employee, per type, per year.
            b.HasIndex(l => new { l.EmployeeId, l.LeaveTypeId, l.Year })
             .IsUnique().HasFilter("\"IsDeleted\" = false");

            b.HasOne(l => l.LeaveType).WithMany()
             .HasForeignKey(l => l.LeaveTypeId)
             .OnDelete(DeleteBehavior.Restrict);

            // Derived from the other columns; storing it would let the two disagree.
            b.Ignore(l => l.Available);
        });

        modelBuilder.Entity<LeaveRequest>(b =>
        {
            b.Property(r => r.Reference).HasMaxLength(30).IsRequired();
            b.HasIndex(r => r.Reference).IsUnique().HasFilter("\"IsDeleted\" = false");

            b.Property(r => r.Reason).HasMaxLength(1000);
            b.Property(r => r.DecisionComment).HasMaxLength(2000);

            b.HasOne(r => r.Employee).WithMany()
             .HasForeignKey(r => r.EmployeeId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(r => r.LeaveType).WithMany()
             .HasForeignKey(r => r.LeaveTypeId)
             .OnDelete(DeleteBehavior.Restrict);

            // The list's hot path: this employee's requests, newest first.
            b.HasIndex(r => new { r.EmployeeId, r.FromDate });
            b.HasIndex(r => r.Status);
        });

        modelBuilder.Entity<Holiday>(b =>
        {
            b.Property(h => h.Name).HasMaxLength(100).IsRequired();
            b.HasIndex(h => h.Date);
        });

        modelBuilder.Entity<DocumentSequenceCounter>(b =>
        {
            b.ToTable("document_sequences");
            b.HasIndex(c => new { c.Key, c.Year }).IsUnique();
        });
    }
}
