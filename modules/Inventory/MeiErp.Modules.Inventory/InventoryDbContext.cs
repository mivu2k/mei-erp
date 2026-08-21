using MeiErp.Platform.Kernel;
using MeiErp.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Inventory;

public class InventoryDbContext(
    DbContextOptions<InventoryDbContext> options, ICurrentUser currentUser, IClock clock)
    : ModuleDbContext(options, currentUser, clock)
{
    protected override string Schema => "inventory";

    public DbSet<Item> Items => Set<Item>();
    public DbSet<ItemCategory> Categories => Set<ItemCategory>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Party> Parties => Set<Party>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderLine> SalesOrderLines => Set<SalesOrderLine>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<DeliveryLine> DeliveryLines => Set<DeliveryLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Item>(b =>
        {
            b.Property(i => i.Code).HasMaxLength(50).IsRequired();
            b.Property(i => i.Name).HasMaxLength(200).IsRequired();
            b.Property(i => i.Unit).HasMaxLength(20);
            b.HasIndex(i => i.Code).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.HasIndex(i => i.Name);

            b.HasOne(i => i.Category).WithMany()
             .HasForeignKey(i => i.CategoryId)
             .OnDelete(DeleteBehavior.SetNull);

            b.Ignore(i => i.StockValue);
        });

        modelBuilder.Entity<ItemCategory>(b =>
        {
            b.Property(c => c.Name).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<StockMovement>(b =>
        {
            b.Property(m => m.ItemCode).HasMaxLength(50);
            b.Property(m => m.ItemName).HasMaxLength(200);
            b.Property(m => m.Reference).HasMaxLength(50);
            b.Property(m => m.Narration).HasMaxLength(500);

            // EF warns that Item carries a soft-delete filter while this
            // navigation is required: a deleted item could filter its own
            // movements out, and the stock ledger would stop explaining the
            // quantity on hand.
            //
            // CatalogService refuses to delete any item with stock or history -
            // deactivation is the only option there, and a deactivated item is
            // not filtered. There are tests pinning both refusals.
            b.HasOne(m => m.Item).WithMany()
             .HasForeignKey(m => m.ItemId)
             .OnDelete(DeleteBehavior.Restrict);

            // The movement report and the rebuild both read this way.
            b.HasIndex(m => new { m.ItemId, m.Date });
            b.HasIndex(m => m.Date);

            b.Ignore(m => m.Value);
        });

        modelBuilder.Entity<Party>(b =>
        {
            b.Property(p => p.Code).HasMaxLength(50).IsRequired();
            b.Property(p => p.Name).HasMaxLength(200).IsRequired();
            b.HasIndex(p => p.Code).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.HasIndex(p => p.Name);
        });

        modelBuilder.Entity<PurchaseOrder>(b =>
        {
            b.Property(o => o.Number).HasMaxLength(30).IsRequired();
            b.HasIndex(o => o.Number).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.Property(o => o.PartyName).HasMaxLength(200);
            b.Property(o => o.Notes).HasMaxLength(1000);
            b.Property(o => o.DecisionComment).HasMaxLength(2000);

            b.HasOne(o => o.Party).WithMany()
             .HasForeignKey(o => o.PartyId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasMany(o => o.Lines).WithOne(l => l.Order)
             .HasForeignKey(l => l.PurchaseOrderId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(o => o.Status);
            b.Ignore(o => o.Total);
            b.Ignore(o => o.IsFullyReceived);
        });

        modelBuilder.Entity<PurchaseOrderLine>(b =>
        {
            b.Property(l => l.ItemCode).HasMaxLength(50);
            b.Property(l => l.ItemName).HasMaxLength(200);
            b.HasOne(l => l.Item).WithMany()
             .HasForeignKey(l => l.ItemId).OnDelete(DeleteBehavior.Restrict);
            b.Ignore(l => l.LineTotal);
            b.Ignore(l => l.Outstanding);

            // Children restate the parent's filter, or querying lines directly
            // returns rows belonging to soft-deleted orders.
            b.HasQueryFilter(l => !l.Order!.IsDeleted);
        });

        modelBuilder.Entity<GoodsReceipt>(b =>
        {
            b.Property(r => r.Number).HasMaxLength(30).IsRequired();
            b.HasIndex(r => r.Number).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.Property(r => r.PartyName).HasMaxLength(200);

            b.HasOne(r => r.Order).WithMany()
             .HasForeignKey(r => r.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);

            b.HasMany(r => r.Lines).WithOne(l => l.Receipt)
             .HasForeignKey(l => l.GoodsReceiptId).OnDelete(DeleteBehavior.Cascade);

            b.Ignore(r => r.Total);
        });

        modelBuilder.Entity<GoodsReceiptLine>(b =>
        {
            b.Property(l => l.ItemCode).HasMaxLength(50);
            b.Property(l => l.ItemName).HasMaxLength(200);
            b.HasQueryFilter(l => !l.Receipt!.IsDeleted);
        });

        modelBuilder.Entity<SalesOrder>(b =>
        {
            b.Property(o => o.Number).HasMaxLength(30).IsRequired();
            b.HasIndex(o => o.Number).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.Property(o => o.PartyName).HasMaxLength(200);
            b.Property(o => o.Notes).HasMaxLength(1000);

            b.HasOne(o => o.Party).WithMany()
             .HasForeignKey(o => o.PartyId).OnDelete(DeleteBehavior.Restrict);

            b.HasMany(o => o.Lines).WithOne(l => l.Order)
             .HasForeignKey(l => l.SalesOrderId).OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(o => o.Status);
            b.Ignore(o => o.Total);
            b.Ignore(o => o.IsFullyDelivered);
        });

        modelBuilder.Entity<SalesOrderLine>(b =>
        {
            b.Property(l => l.ItemCode).HasMaxLength(50);
            b.Property(l => l.ItemName).HasMaxLength(200);
            b.HasOne(l => l.Item).WithMany()
             .HasForeignKey(l => l.ItemId).OnDelete(DeleteBehavior.Restrict);
            b.Ignore(l => l.LineTotal);
            b.Ignore(l => l.Outstanding);
            b.Ignore(l => l.Margin);
            b.HasQueryFilter(l => !l.Order!.IsDeleted);
        });

        modelBuilder.Entity<Delivery>(b =>
        {
            b.Property(d => d.Number).HasMaxLength(30).IsRequired();
            b.HasIndex(d => d.Number).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.Property(d => d.PartyName).HasMaxLength(200);
            b.Property(d => d.CollectedBy).HasMaxLength(200);

            b.HasOne(d => d.Order).WithMany()
             .HasForeignKey(d => d.SalesOrderId).OnDelete(DeleteBehavior.Restrict);

            b.HasMany(d => d.Lines).WithOne(l => l.Delivery)
             .HasForeignKey(l => l.DeliveryId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeliveryLine>(b =>
        {
            b.Property(l => l.ItemCode).HasMaxLength(50);
            b.Property(l => l.ItemName).HasMaxLength(200);
            b.Ignore(l => l.Margin);
            b.HasQueryFilter(l => !l.Delivery!.IsDeleted);
        });

        modelBuilder.Entity<DocumentSequenceCounter>(b =>
        {
            b.ToTable("document_sequences");
            b.HasIndex(c => new { c.Key, c.Year }).IsUnique();
        });
    }
}
