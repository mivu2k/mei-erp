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
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<AttendanceStation> AttendanceStations => Set<AttendanceStation>();
    public DbSet<AttendancePunch> AttendancePunches => Set<AttendancePunch>();
    public DbSet<AttendanceDay> AttendanceDays => Set<AttendanceDay>();
    public DbSet<EmployeeDocument> EmployeeDocuments => Set<EmployeeDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Employee>(b =>
        {
            b.Property(e => e.Code).HasMaxLength(30).IsRequired();
            b.Property(e => e.FullName).HasMaxLength(200).IsRequired();
            b.Property(e => e.FatherName).HasMaxLength(200);
            b.Property(e => e.BloodGroup).HasMaxLength(20);

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
            b.HasMany(e=>e.Documents).WithOne(x=>x.Employee).HasForeignKey(x=>x.EmployeeId).OnDelete(DeleteBehavior.Cascade);

            b.Property(e => e.CardNumber).HasMaxLength(100);
            b.Property(e => e.QrSecret).HasMaxLength(100);
            b.Property(e => e.Cnic).HasMaxLength(40);
            b.Property(e => e.Email).HasMaxLength(200);
            b.Property(e => e.Phone).HasMaxLength(50);
            b.Property(e => e.AlternatePhone).HasMaxLength(50);
            b.Property(e => e.Address).HasMaxLength(500);
            b.Property(e => e.City).HasMaxLength(100);
            b.Property(e => e.EmergencyContactName).HasMaxLength(200);
            b.Property(e => e.EmergencyContactPhone).HasMaxLength(50);
            b.Property(e => e.DepartmentName).HasMaxLength(150);
            b.Property(e => e.Designation).HasMaxLength(150);
            b.Property(e => e.WorkLocation).HasMaxLength(150);
            b.Property(e => e.ReportsToEmployeeCode).HasMaxLength(30);
            b.Property(e => e.LeavingReason).HasMaxLength(500);
            b.Property(e => e.BankName).HasMaxLength(150);
            b.Property(e => e.BankAccountNumber).HasMaxLength(100);
            b.Property(e => e.BankAccountTitle).HasMaxLength(200);
            b.Property(e => e.TaxNumber).HasMaxLength(50);
            b.Property(e => e.SocialSecurityNumber).HasMaxLength(50);
            b.Property(e => e.Notes).HasMaxLength(2000);
            b.HasIndex(e => e.CardNumber).IsUnique()
             .HasFilter("\"CardNumber\" IS NOT NULL AND \"IsDeleted\" = false");
            b.HasOne(e => e.Shift).WithMany().HasForeignKey(e => e.ShiftId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<EmployeeDocument>(b=>
        {
            b.Property(x=>x.Title).HasMaxLength(200).IsRequired();b.Property(x=>x.FileName).HasMaxLength(255);
            b.Property(x=>x.ContentType).HasMaxLength(150);b.Property(x=>x.Notes).HasMaxLength(1000);b.HasIndex(x=>x.ExpiresOn);
            b.Property(x=>x.Content).HasColumnType("bytea");b.HasQueryFilter(x=>!x.IsDeleted&&!x.Employee!.IsDeleted);
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

        modelBuilder.Entity<Shift>(b =>
        {
            b.Property(s => s.Name).HasMaxLength(100).IsRequired();
            b.HasIndex(s => s.Name).IsUnique().HasFilter("\"IsDeleted\" = false");
        });

        modelBuilder.Entity<AttendanceStation>(b =>
        {
            b.Property(s => s.Name).HasMaxLength(150).IsRequired();
            b.Property(s => s.Location).HasMaxLength(150);
            b.Property(s => s.AccessToken).HasMaxLength(64).IsRequired();
            b.Property(s => s.LastPunchDescription).HasMaxLength(300);
            b.HasIndex(s => s.AccessToken).IsUnique().HasFilter("\"IsDeleted\" = false");
        });

        modelBuilder.Entity<AttendancePunch>(b =>
        {
            // A punch belongs to the business-local day shown on the terminal.
            // Storing it as timestamptz would convert it and can move it across
            // midnight when grouped into attendance days.
            b.Property(p => p.PunchedAt).HasColumnType("timestamp without time zone");
            b.Property(p => p.Evidence).HasMaxLength(200);
            b.HasIndex(p => new { p.EmployeeId, p.PunchedAt }).IsUnique();
            b.HasOne(p => p.AttendanceStation).WithMany().HasForeignKey(p => p.AttendanceStationId)
             .OnDelete(DeleteBehavior.SetNull);
            b.HasOne(p => p.Employee).WithMany().HasForeignKey(p => p.EmployeeId)
             .OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(p => !p.Employee!.IsDeleted);
        });

        modelBuilder.Entity<AttendanceDay>(b =>
        {
            b.HasIndex(d => new { d.EmployeeId, d.Date }).IsUnique()
             .HasFilter("\"IsDeleted\" = false");
            b.HasIndex(d => d.Date);
            b.Property(d => d.OverriddenById).HasMaxLength(450);
            b.Property(d => d.OverriddenByName).HasMaxLength(200);
            b.Property(d => d.OverrideReason).HasMaxLength(500);
            b.Property(d => d.Notes).HasMaxLength(1000);
            b.HasOne(d => d.Employee).WithMany().HasForeignKey(d => d.EmployeeId)
             .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(d => d.LeaveRequest).WithMany().HasForeignKey(d => d.LeaveRequestId)
             .OnDelete(DeleteBehavior.SetNull);
            b.Property(d => d.Version).HasColumnName("xmin").HasColumnType("xid")
             .ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
            b.HasQueryFilter(d => !d.IsDeleted && !d.Employee!.IsDeleted);
        });

        modelBuilder.Entity<DocumentSequenceCounter>(b =>
        {
            b.ToTable("document_sequences");
            b.HasIndex(c => new { c.Key, c.Year }).IsUnique();
        });
    }
}
