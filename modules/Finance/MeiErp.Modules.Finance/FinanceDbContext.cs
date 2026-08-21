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
    public DbSet<FiscalYear> FiscalYears => Set<FiscalYear>();
    public DbSet<ThirdParty> ThirdParties => Set<ThirdParty>();
    public DbSet<PettyCashBox> PettyCashBoxes => Set<PettyCashBox>();
    public DbSet<PettyCashEntry> PettyCashEntries => Set<PettyCashEntry>();
    public DbSet<UtilityConnection> UtilityConnections => Set<UtilityConnection>();
    public DbSet<UtilityBill> UtilityBills => Set<UtilityBill>();

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

            b.HasMany(v => v.Lines).WithOne(l => l.Voucher)
             .HasForeignKey(l => l.VoucherId)
             .OnDelete(DeleteBehavior.Cascade);

            b.Ignore(v => v.TotalDebit);
            b.Ignore(v => v.TotalCredit);
            b.Ignore(v => v.IsBalanced);
            b.Ignore(v => v.IsPosted);
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

        modelBuilder.Entity<DocumentSequenceCounter>(b =>
        {
            b.ToTable("document_sequences");
            b.HasIndex(c => new { c.Key, c.Year }).IsUnique();
        });
    }
}
