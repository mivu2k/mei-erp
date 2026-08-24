using MeiErp.Platform.Kernel;
using MeiErp.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Finance;

public class FinanceDbContext(
    DbContextOptions<FinanceDbContext> options, ICurrentUser currentUser, IClock clock)
    : ModuleDbContext(options, currentUser, clock)
{
    protected override string Schema => "finance";

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<VoucherLine> VoucherLines => Set<VoucherLine>();
    public DbSet<PaymentRequest> PaymentRequests => Set<PaymentRequest>();
    public DbSet<PaymentRequestLine> PaymentRequestLines => Set<PaymentRequestLine>();
    public DbSet<FiscalYear> FiscalYears => Set<FiscalYear>();
    public DbSet<ThirdParty> ThirdParties => Set<ThirdParty>();
    public DbSet<PettyCashBox> PettyCashBoxes => Set<PettyCashBox>();
    public DbSet<PettyCashEntry> PettyCashEntries => Set<PettyCashEntry>();
    public DbSet<UtilityConnection> UtilityConnections => Set<UtilityConnection>();
    public DbSet<UtilityBill> UtilityBills => Set<UtilityBill>();
    public DbSet<Advance> Advances => Set<Advance>();
    public DbSet<AdvanceExpense> AdvanceExpenses => Set<AdvanceExpense>();
    public DbSet<PayrollEmployee> PayrollEmployees => Set<PayrollEmployee>();
    public DbSet<PayComponent> PayComponents => Set<PayComponent>();
    public DbSet<SalaryStructure> SalaryStructures => Set<SalaryStructure>();
    public DbSet<SalaryLine> SalaryLines => Set<SalaryLine>();
    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();
    public DbSet<Payslip> Payslips => Set<Payslip>();
    public DbSet<PayslipLine> PayslipLines => Set<PayslipLine>();
    public DbSet<Reconciliation> Reconciliations => Set<Reconciliation>();
    public DbSet<ReconciliationLine> ReconciliationLines => Set<ReconciliationLine>();
    public DbSet<PostingRule> PostingRules => Set<PostingRule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Account>(b =>
        {
            b.Property(a => a.Code).HasMaxLength(20).IsRequired();
            b.Property(a => a.Name).HasMaxLength(200).IsRequired();
            b.HasIndex(a => a.Code).IsUnique().HasFilter("\"IsDeleted\" = false");

            // Restrict, not cascade: deleting a heading must never silently take
            // the accounts beneath it - and their history - with it.
            b.HasOne(a => a.Parent).WithMany(a => a.Children)
             .HasForeignKey(a => a.ParentId)
             .OnDelete(DeleteBehavior.Restrict);

            b.Ignore(a => a.IsDebitNatured);
        });

        modelBuilder.Entity<Voucher>(b =>
        {
            b.Property(v => v.Number).HasMaxLength(30).IsRequired();
            b.HasIndex(v => v.Number).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.Property(v => v.Narration).HasMaxLength(500);

            // The ledger and every report read by date.
            b.HasIndex(v => new { v.Date, v.Status });

            // Finding the voucher a module's document produced, and vice versa.
            b.HasIndex(v => new { v.SourceModule, v.SourceDocumentType, v.SourceDocumentId });
            b.Property(v => v.SourceIdempotencyKey).HasMaxLength(200);
            b.HasIndex(v => new { v.SourceModule, v.SourceDocumentType, v.SourceIdempotencyKey })
             .IsUnique().HasFilter("\"SourceIdempotencyKey\" IS NOT NULL AND \"IsDeleted\" = false");

            b.HasMany(v => v.Lines).WithOne(l => l.Voucher)
             .HasForeignKey(l => l.VoucherId)
             .OnDelete(DeleteBehavior.Cascade);

            b.Ignore(v => v.TotalDebit);
            b.Ignore(v => v.TotalCredit);
            b.Ignore(v => v.IsBalanced);
            b.Ignore(v => v.IsPosted);
        });

        modelBuilder.Entity<PostingRule>(b =>
        {
            b.Property(r => r.EventType).HasMaxLength(200).IsRequired();
            b.Property(r => r.Name).HasMaxLength(200).IsRequired();
            b.HasIndex(r => r.EventType).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.HasOne(r => r.DebitAccount).WithMany().HasForeignKey(r => r.DebitAccountId)
             .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(r => r.CreditAccount).WithMany().HasForeignKey(r => r.CreditAccountId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<VoucherLine>(b =>
        {
            b.Property(l => l.AccountCode).HasMaxLength(20);
            b.Property(l => l.AccountName).HasMaxLength(200);
            b.Property(l => l.Narration).HasMaxLength(500);

            // EF warns that Account carries a soft-delete filter while this
            // navigation is required: a deleted account could filter its own
            // voucher lines out of the trial balance, which would silently
            // unbalance the books.
            //
            // What makes that unreachable is AccountService refusing to delete
            // any account that has entries against it - deactivation is the
            // only option there, and a deactivated account is not filtered.
            // There is a test pinning that guarantee; do not relax it.
            b.HasOne(l => l.Account).WithMany()
             .HasForeignKey(l => l.AccountId)
             .OnDelete(DeleteBehavior.Restrict);

            // The account ledger's hot path.
            b.HasIndex(l => l.AccountId);
            b.HasIndex(l => l.PersonId);

            // Match Account's soft-delete filter on the required navigation.
            // AccountService prevents deleting an account with history, so this
            // cannot hide a legitimate posted line and removes EF's model warning.
            b.HasQueryFilter(l => !l.Account!.IsDeleted);

            b.Ignore(l => l.SignedAmount);
        });

        modelBuilder.Entity<PaymentRequest>(b =>
        {
            b.Property(r => r.Reference).HasMaxLength(30).IsRequired();
            b.HasIndex(r => r.Reference).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.Property(r => r.Title).HasMaxLength(200).IsRequired();
            b.Property(r => r.Description).HasMaxLength(2000);
            b.Property(r => r.DecisionComment).HasMaxLength(2000);
            b.Property(r => r.PayeeName).HasMaxLength(200);
            b.HasIndex(r => r.IsDirectorRequest);

            b.HasOne(r => r.ExpenseAccount).WithMany()
             .HasForeignKey(r => r.ExpenseAccountId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(r => r.PaidFromAccount).WithMany()
             .HasForeignKey(r => r.PaidFromAccountId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(r => r.Voucher).WithMany()
             .HasForeignKey(r => r.VoucherId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(r => r.Status);

            b.HasMany(r => r.Lines).WithOne(l => l.PaymentRequest)
             .HasForeignKey(l => l.PaymentRequestId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PaymentRequestLine>(b =>
        {
            b.Property(l => l.Category).HasMaxLength(120);
            b.Property(l => l.Reason).HasMaxLength(500);
            b.Property(l => l.Description).HasMaxLength(1000);
            b.HasOne(l => l.ExpenseAccount).WithMany()
             .HasForeignKey(l => l.ExpenseAccountId)
             .OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(l => l.PaymentRequestId);
        });

        modelBuilder.Entity<FiscalYear>(b =>
        {
            b.Property(y => y.Name).HasMaxLength(50).IsRequired();
            b.HasIndex(y => new { y.StartDate, y.EndDate });
        });

        modelBuilder.Entity<ThirdParty>(b =>
        {
            b.Property(p => p.Name).HasMaxLength(200).IsRequired();
            b.Property(p => p.Phone).HasMaxLength(40);
            b.Property(p => p.Cnic).HasMaxLength(20);
            b.Property(p => p.Notes).HasMaxLength(1000);

            // One party per account: two parties sharing one would merge their
            // statements into a single unreadable ledger.
            b.HasIndex(p => p.AccountId).IsUnique().HasFilter("\"IsDeleted\" = false");

            b.HasOne(p => p.Account).WithMany()
             .HasForeignKey(p => p.AccountId).OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(p => p.Name);
        });

        modelBuilder.Entity<PettyCashBox>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(100).IsRequired();
            b.Property(x => x.CustodianName).HasMaxLength(200).IsRequired();

            b.HasOne(x => x.Account).WithMany()
             .HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);

            b.HasMany(x => x.Entries).WithOne(e => e.Box)
             .HasForeignKey(e => e.BoxId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PettyCashEntry>(b =>
        {
            b.Property(e => e.Description).HasMaxLength(500).IsRequired();
            b.Property(e => e.PaidTo).HasMaxLength(200);
            b.Property(e => e.ReceiptNumber).HasMaxLength(50);

            b.HasOne(e => e.ExpenseAccount).WithMany()
             .HasForeignKey(e => e.ExpenseAccountId).OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(e => new { e.BoxId, e.Date });
            b.HasQueryFilter(e => !e.Box!.IsDeleted);
        });

        modelBuilder.Entity<UtilityConnection>(b =>
        {
            b.Property(c => c.Name).HasMaxLength(150).IsRequired();
            b.Property(c => c.ConnectionNumber).HasMaxLength(60);
            b.Property(c => c.Provider).HasMaxLength(150);
            b.Property(c => c.Location).HasMaxLength(200);

            b.HasOne(c => c.ExpenseAccount).WithMany()
             .HasForeignKey(c => c.ExpenseAccountId).OnDelete(DeleteBehavior.Restrict);

            b.HasMany(c => c.Bills).WithOne(x => x.Connection)
             .HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UtilityBill>(b =>
        {
            b.Property(x => x.BillNumber).HasMaxLength(60);

            // One bill per connection per month, so entering August twice is
            // caught rather than quietly doubling the cost.
            b.HasIndex(x => new { x.ConnectionId, x.BillingMonth })
             .IsUnique().HasFilter("\"IsDeleted\" = false");

            b.HasIndex(x => x.PaidOn);

            b.Ignore(x => x.IsPaid);
            b.HasQueryFilter(x => !x.Connection!.IsDeleted);
        });

        modelBuilder.Entity<Advance>(b =>
        {
            b.Property(a => a.Reference).HasMaxLength(30).IsRequired();
            b.HasIndex(a => a.Reference).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.HasIndex(a => a.IsDirectorRequest);
            b.Property(a => a.Purpose).HasMaxLength(500).IsRequired();
            b.Property(a => a.PersonName).HasMaxLength(200);
            b.Property(a => a.DecisionComment).HasMaxLength(2000);

            b.HasMany(a => a.Expenses).WithOne(e => e.Advance)
             .HasForeignKey(e => e.AdvanceId).OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(a => new { a.PersonId, a.Status });
            b.HasIndex(a => a.Status);

            b.Ignore(a => a.Difference);
            b.Ignore(a => a.OutstandingDifference);
            b.Ignore(a => a.IsOpen);
        });

        modelBuilder.Entity<AdvanceExpense>(b =>
        {
            b.Property(e => e.Description).HasMaxLength(500).IsRequired();
            b.Property(e => e.ReceiptNumber).HasMaxLength(60);

            b.HasOne(e => e.ExpenseAccount).WithMany()
             .HasForeignKey(e => e.ExpenseAccountId).OnDelete(DeleteBehavior.Restrict);

            b.HasQueryFilter(e => !e.Advance!.IsDeleted);
        });

        modelBuilder.Entity<PayrollEmployee>(b =>
        {
            b.Property(e => e.Code).HasMaxLength(30).IsRequired();
            b.Property(e => e.FullName).HasMaxLength(200).IsRequired();
            b.Property(e => e.Designation).HasMaxLength(120);

            // Two people on one staff number would merge their payslips.
            b.HasIndex(e => e.Code).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.HasIndex(e => e.UserId);

            b.HasMany(e => e.Structures).WithOne(s => s.Employee)
             .HasForeignKey(s => s.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PayComponent>(b =>
        {
            b.Property(c => c.Name).HasMaxLength(120).IsRequired();
            b.Property(c => c.Code).HasMaxLength(30);
            b.HasOne(c => c.Account).WithMany()
             .HasForeignKey(c => c.AccountId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SalaryStructure>(b =>
        {
            b.HasMany(s => s.Lines).WithOne(l => l.Structure)
             .HasForeignKey(l => l.SalaryStructureId).OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(s => new { s.EmployeeId, s.EffectiveFrom });
            b.HasQueryFilter(s => !s.Employee!.IsDeleted);
        });

        modelBuilder.Entity<SalaryLine>(b =>
        {
            b.HasOne(l => l.Component).WithMany()
             .HasForeignKey(l => l.ComponentId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(l => !l.Structure!.Employee!.IsDeleted);
        });

        modelBuilder.Entity<PayrollRun>(b =>
        {
            b.Property(r => r.Reference).HasMaxLength(30).IsRequired();

            // One run per month. Two would double every salary in it.
            b.HasIndex(r => r.Month).IsUnique().HasFilter("\"IsDeleted\" = false");

            b.HasMany(r => r.Payslips).WithOne(p => p.Run)
             .HasForeignKey(p => p.RunId).OnDelete(DeleteBehavior.Cascade);

            b.Ignore(r => r.TotalGross);
            b.Ignore(r => r.TotalDeductions);
            b.Ignore(r => r.TotalNet);
            b.Ignore(r => r.IsEditable);
        });

        modelBuilder.Entity<Payslip>(b =>
        {
            b.Property(p => p.EmployeeCode).HasMaxLength(30);
            b.Property(p => p.EmployeeName).HasMaxLength(200);

            b.HasMany(p => p.Lines).WithOne(l => l.Payslip)
             .HasForeignKey(l => l.PayslipId).OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(p => new { p.RunId, p.EmployeeId }).IsUnique();
            b.HasIndex(p => p.UserId);

            b.Ignore(p => p.Gross);
            b.Ignore(p => p.TotalDeductions);
            b.Ignore(p => p.Net);

            b.HasQueryFilter(p => !p.Run!.IsDeleted);
        });

        modelBuilder.Entity<PayslipLine>(b =>
        {
            b.Property(l => l.Name).HasMaxLength(200).IsRequired();
            b.HasQueryFilter(l => !l.Payslip!.Run!.IsDeleted);
        });

        modelBuilder.Entity<Reconciliation>(b =>
        {
            b.Property(r => r.Notes).HasMaxLength(1000);

            b.HasOne(r => r.Account).WithMany()
             .HasForeignKey(r => r.AccountId).OnDelete(DeleteBehavior.Restrict);

            b.HasMany(r => r.Lines).WithOne(l => l.Reconciliation)
             .HasForeignKey(l => l.ReconciliationId).OnDelete(DeleteBehavior.Cascade);

            // One sheet per account per statement date.
            b.HasIndex(r => new { r.AccountId, r.StatementDate })
             .IsUnique().HasFilter("\"IsDeleted\" = false");

            b.HasIndex(r => r.IsClosed);

            b.Ignore(r => r.Uncleared);
            b.Ignore(r => r.Adjusted);
            b.Ignore(r => r.Difference);
            b.Ignore(r => r.IsReconciled);
        });

        modelBuilder.Entity<ReconciliationLine>(b =>
        {
            b.Property(l => l.VoucherNumber).HasMaxLength(30);
            b.Property(l => l.Narration).HasMaxLength(500);
            b.HasIndex(l => l.VoucherLineId);
            b.Ignore(l => l.SignedAmount);
            b.HasQueryFilter(l => !l.Reconciliation!.IsDeleted);
        });

        modelBuilder.Entity<DocumentSequenceCounter>(b =>
        {
            b.ToTable("document_sequences");
            b.HasIndex(c => new { c.Key, c.Year }).IsUnique();
        });
    }
}
