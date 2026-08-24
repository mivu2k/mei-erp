using MeiErp.Platform.Kernel;
using MeiErp.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Trade;

public class TradeDbContext(
    DbContextOptions<TradeDbContext> options, ICurrentUser currentUser, IClock clock)
    : ModuleDbContext(options, currentUser, clock)
{
    protected override string Schema => "trade";

    public DbSet<Party> Parties => Set<Party>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<GoodsReceiptLine> GoodsReceiptLines => Set<GoodsReceiptLine>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderLine> SalesOrderLines => Set<SalesOrderLine>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<DeliveryLine> DeliveryLines => Set<DeliveryLine>();

    // Quotation -> order -> invoice, in both directions.
    public DbSet<Quotation> Quotations => Set<Quotation>();
    public DbSet<QuotationLine> QuotationLines => Set<QuotationLine>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

    // The workshop's parts buying, moved out of Repair.
    public DbSet<Part> Parts => Set<Part>();
    public DbSet<PartPurchase> PartPurchases => Set<PartPurchase>();
    public DbSet<PartPurchaseLine> PartPurchaseLines => Set<PartPurchaseLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Party>(b =>
        {
            b.Property(p => p.Code).HasMaxLength(50).IsRequired();
            b.Property(p => p.Name).HasMaxLength(200).IsRequired();
            b.Property(p => p.Phone).HasMaxLength(50);
            b.Property(p => p.Email).HasMaxLength(200);
            b.Property(p => p.Address).HasMaxLength(500);
            b.Property(p => p.TaxNumber).HasMaxLength(50);
            b.Property(p => p.Notes).HasMaxLength(1000);

            b.HasIndex(p => p.Code).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.HasIndex(p => p.Name);

            b.Ignore(p => p.Sides);
        });

        modelBuilder.Entity<PurchaseOrder>(b =>
        {
            b.Property(o => o.Number).HasMaxLength(50).IsRequired();
            b.Property(o => o.PartyName).HasMaxLength(200);
            b.Property(o => o.Notes).HasMaxLength(1000);
            b.Property(o => o.DecisionComment).HasMaxLength(1000);

            b.HasIndex(o => o.Number).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.HasIndex(o => o.Date);

            b.HasOne(o => o.Party).WithMany()
             .HasForeignKey(o => o.PartyId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasMany(o => o.Lines).WithOne(l => l.Order)
             .HasForeignKey(l => l.PurchaseOrderId)
             .OnDelete(DeleteBehavior.Cascade);

            b.Ignore(o => o.Total);
            b.Ignore(o => o.IsFullyReceived);
        });

        modelBuilder.Entity<PurchaseOrderLine>(b =>
        {
            b.Property(l => l.ItemCode).HasMaxLength(50);
            b.Property(l => l.ItemName).HasMaxLength(200);
            b.Ignore(l => l.LineTotal);
            b.Ignore(l => l.Outstanding);
        });

        modelBuilder.Entity<GoodsReceipt>(b =>
        {
            b.Property(r => r.Number).HasMaxLength(50).IsRequired();
            b.Property(r => r.PartyName).HasMaxLength(200);
            b.Property(r => r.Notes).HasMaxLength(1000);

            b.HasIndex(r => r.Number).IsUnique().HasFilter("\"IsDeleted\" = false");

            b.HasOne(r => r.Order).WithMany()
             .HasForeignKey(r => r.PurchaseOrderId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasMany(r => r.Lines).WithOne(l => l.Receipt)
             .HasForeignKey(l => l.GoodsReceiptId)
             .OnDelete(DeleteBehavior.Cascade);

            b.Ignore(r => r.Total);
        });

        modelBuilder.Entity<GoodsReceiptLine>(b =>
        {
            b.Property(l => l.ItemCode).HasMaxLength(50);
            b.Property(l => l.ItemName).HasMaxLength(200);
        });

        modelBuilder.Entity<SalesOrder>(b =>
        {
            b.Property(o => o.Number).HasMaxLength(50).IsRequired();
            b.Property(o => o.PartyName).HasMaxLength(200);
            b.Property(o => o.Notes).HasMaxLength(1000);

            b.HasIndex(o => o.Number).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.HasIndex(o => o.Date);

            b.HasOne(o => o.Party).WithMany()
             .HasForeignKey(o => o.PartyId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasMany(o => o.Lines).WithOne(l => l.Order)
             .HasForeignKey(l => l.SalesOrderId)
             .OnDelete(DeleteBehavior.Cascade);

            b.Ignore(o => o.Total);
            b.Ignore(o => o.IsFullyDelivered);
        });

        modelBuilder.Entity<SalesOrderLine>(b =>
        {
            b.Property(l => l.ItemCode).HasMaxLength(50);
            b.Property(l => l.ItemName).HasMaxLength(200);
            b.Ignore(l => l.LineTotal);
            b.Ignore(l => l.Outstanding);
            b.Ignore(l => l.Margin);
        });

        modelBuilder.Entity<Delivery>(b =>
        {
            b.Property(d => d.Number).HasMaxLength(50).IsRequired();
            b.Property(d => d.PartyName).HasMaxLength(200);
            b.Property(d => d.CollectedBy).HasMaxLength(200);
            b.Property(d => d.Notes).HasMaxLength(1000);

            b.HasIndex(d => d.Number).IsUnique().HasFilter("\"IsDeleted\" = false");

            b.HasOne(d => d.Order).WithMany()
             .HasForeignKey(d => d.SalesOrderId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasMany(d => d.Lines).WithOne(l => l.Delivery)
             .HasForeignKey(l => l.DeliveryId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeliveryLine>(b =>
        {
            b.Property(l => l.ItemCode).HasMaxLength(50);
            b.Property(l => l.ItemName).HasMaxLength(200);
            b.Ignore(l => l.Margin);
        });

        modelBuilder.Entity<Quotation>(b =>
        {
            b.Property(x => x.Number).HasMaxLength(50).IsRequired();
            b.Property(x => x.PartyName).HasMaxLength(200);
            b.Property(x => x.JobReference).HasMaxLength(50);
            b.Property(x => x.Notes).HasMaxLength(2000);
            b.Property(x => x.Terms).HasMaxLength(2000);
            b.Property(x => x.DecisionComment).HasMaxLength(1000);

            b.HasIndex(x => x.Number).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.HasIndex(x => new { x.Direction, x.Status });
            b.HasIndex(x => x.Date);

            b.HasOne(x => x.Party).WithMany()
             .HasForeignKey(x => x.PartyId).OnDelete(DeleteBehavior.Restrict);

            b.HasMany(x => x.Lines).WithOne(l => l.Quotation)
             .HasForeignKey(l => l.QuotationId).OnDelete(DeleteBehavior.Cascade);

            b.Ignore(x => x.Subtotal); b.Ignore(x => x.Taxable);
            b.Ignore(x => x.Tax); b.Ignore(x => x.Total);
        });

        modelBuilder.Entity<QuotationLine>(b =>
        {
            b.Property(x => x.Description).HasMaxLength(500).IsRequired();
            b.Property(x => x.ItemCode).HasMaxLength(50);
            b.Ignore(x => x.LineTotal);
        });

        modelBuilder.Entity<Invoice>(b =>
        {
            b.Property(x => x.Number).HasMaxLength(50).IsRequired();
            b.Property(x => x.PartyName).HasMaxLength(200);
            b.Property(x => x.TheirReference).HasMaxLength(100);
            b.Property(x => x.Notes).HasMaxLength(2000);
            b.Property(x => x.DecisionComment).HasMaxLength(1000);

            b.HasIndex(x => x.Number).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.HasIndex(x => new { x.Direction, x.Status });
            b.HasIndex(x => x.DueDate);

            b.HasOne(x => x.Party).WithMany()
             .HasForeignKey(x => x.PartyId).OnDelete(DeleteBehavior.Restrict);

            b.HasMany(x => x.Lines).WithOne(l => l.Invoice)
             .HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);

            b.Ignore(x => x.Subtotal); b.Ignore(x => x.Taxable);
            b.Ignore(x => x.Tax); b.Ignore(x => x.Total);
            b.Ignore(x => x.Balance); b.Ignore(x => x.IsSettled);
        });

        modelBuilder.Entity<InvoiceLine>(b =>
        {
            b.Property(x => x.Description).HasMaxLength(500).IsRequired();
            b.Property(x => x.ItemCode).HasMaxLength(50);
            b.Ignore(x => x.LineTotal);
        });

        modelBuilder.Entity<Part>(b =>
        {
            b.Property(p => p.Name).HasMaxLength(200).IsRequired();
            b.Property(p => p.Sku).HasMaxLength(60);
            b.Property(p => p.Brand).HasMaxLength(100);
            b.Property(p => p.Model).HasMaxLength(100);
            b.Property(p => p.LastSupplierName).HasMaxLength(200);

            b.HasIndex(p => p.Sku).IsUnique().HasFilter("\"Sku\" IS NOT NULL AND \"IsDeleted\" = false");
            b.HasIndex(p => p.Name);

            b.Ignore(p => p.MarginPercent);
        });

        modelBuilder.Entity<PartPurchase>(b =>
        {
            b.Property(x => x.Number).HasMaxLength(50).IsRequired();
            b.Property(x => x.PartyName).HasMaxLength(200);
            b.Property(x => x.SupplierInvoiceNumber).HasMaxLength(100);
            b.Property(x => x.ReceivedByName).HasMaxLength(200);
            b.Property(x => x.Notes).HasMaxLength(1000);

            b.HasIndex(x => x.Number).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.HasIndex(x => x.PurchasedOn);

            b.HasOne(x => x.Party).WithMany()
             .HasForeignKey(x => x.PartyId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasMany(x => x.Lines).WithOne(l => l.Purchase)
             .HasForeignKey(l => l.PartPurchaseId)
             .OnDelete(DeleteBehavior.Cascade);

            b.Ignore(x => x.Subtotal);
            b.Ignore(x => x.Total);
        });

        modelBuilder.Entity<PartPurchaseLine>(b =>
        {
            b.Property(x => x.Remarks).HasMaxLength(500);

            // Restrict: a part with purchase history is what gives the averages
            // their meaning, so it cannot be deleted out from under them.
            b.HasOne(x => x.Part).WithMany()
             .HasForeignKey(x => x.PartId)
             .OnDelete(DeleteBehavior.Restrict);

            b.Ignore(x => x.LineTotal);
        });
    }
}
