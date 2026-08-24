using MeiErp.Platform.Kernel;
using MeiErp.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Inventory;

public class InventoryDbContext(
    DbContextOptions<InventoryDbContext> options, ICurrentUser currentUser, IClock clock)
    : ModuleDbContext(options, currentUser, clock)
{
    protected override string Schema => "inventory";

    public DbSet<StockDomain> StockDomains => Set<StockDomain>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<ItemCategory> Categories => Set<ItemCategory>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<WarehouseBalance> WarehouseBalances => Set<WarehouseBalance>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferLine> StockTransferLines => Set<StockTransferLine>();
    public DbSet<InventoryCount> InventoryCounts => Set<InventoryCount>();
    public DbSet<InventoryCountLine> InventoryCountLines => Set<InventoryCountLine>();
    public DbSet<StockUnit> StockUnits => Set<StockUnit>();
    public DbSet<StockBatch> StockBatches => Set<StockBatch>();
    public DbSet<ProductFamily> ProductFamilies => Set<ProductFamily>();
    public DbSet<InventoryReturn> InventoryReturns => Set<InventoryReturn>();
    public DbSet<InventoryReturnLine> InventoryReturnLines => Set<InventoryReturnLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<StockDomain>(b =>
        {
            b.Property(d => d.Code).HasMaxLength(20).IsRequired();
            b.Property(d => d.Name).HasMaxLength(100).IsRequired();
            b.Property(d => d.Description).HasMaxLength(500);
            b.HasIndex(d => d.Code).IsUnique().HasFilter("\"IsDeleted\" = false");
        });

        modelBuilder.Entity<Item>(b =>
        {
            b.Property(i => i.Code).HasMaxLength(50).IsRequired();
            b.Property(i => i.Name).HasMaxLength(200).IsRequired();
            b.Property(i => i.Unit).HasMaxLength(20);
            b.Property(i=>i.Barcode).HasMaxLength(100);
            b.HasIndex(i=>i.Barcode).IsUnique().HasFilter("\"Barcode\" IS NOT NULL AND \"IsDeleted\" = false");

            // Unique per book, not globally: the main store and the workshop
            // number their goods independently, and a spare part called
            // "CABLE-01" must not block the trading item of the same code.
            b.HasIndex(i => new { i.DomainId, i.Code }).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.HasIndex(i => i.Name);

            // Restrict, not cascade: deleting a book must not silently take its
            // items - and with them their stock history - down with it.
            b.HasOne(i => i.Domain).WithMany()
             .HasForeignKey(i => i.DomainId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(i => i.Category).WithMany()
             .HasForeignKey(i => i.CategoryId)
             .OnDelete(DeleteBehavior.SetNull);
            b.HasOne(i=>i.ProductFamily).WithMany(x=>x.Items).HasForeignKey(i=>i.ProductFamilyId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(i=>i.ParentItem).WithMany().HasForeignKey(i=>i.ParentItemId).OnDelete(DeleteBehavior.Restrict);

            b.Ignore(i => i.StockValue);
        });
        modelBuilder.Entity<ProductFamily>(b=>{b.Property(x=>x.Name).HasMaxLength(200).IsRequired();b.Property(x=>x.Category).HasMaxLength(100);b.Property(x=>x.SkuPrefix).HasMaxLength(30);b.HasIndex(x=>x.Name).IsUnique().HasFilter("\"IsDeleted\" = false");});

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

            // The stock ledger is read one book at a time. No FK: the movement
            // carries the domain the way it carries the item code, as a
            // snapshot, so history stays readable however the books are later
            // reorganised.
            b.HasIndex(m => new { m.DomainId, m.Date });
            b.HasOne(m=>m.Warehouse).WithMany().HasForeignKey(m=>m.WarehouseId).OnDelete(DeleteBehavior.Restrict);

            b.Ignore(m => m.Value);
        });

        modelBuilder.Entity<Warehouse>(b=>{b.Property(x=>x.Name).HasMaxLength(150).IsRequired();b.Property(x=>x.Code).HasMaxLength(30);b.Property(x=>x.Address).HasMaxLength(500);b.Property(x=>x.Notes).HasMaxLength(1000);b.HasIndex(x=>x.Code).IsUnique().HasFilter("\"Code\" IS NOT NULL AND \"IsDeleted\" = false");b.HasIndex(x=>x.DomainId);b.HasOne(x=>x.Domain).WithMany().HasForeignKey(x=>x.DomainId).OnDelete(DeleteBehavior.Restrict);});
        modelBuilder.Entity<WarehouseBalance>(b=>{b.HasIndex(x=>new{x.WarehouseId,x.ItemId}).IsUnique();b.HasOne(x=>x.Warehouse).WithMany().HasForeignKey(x=>x.WarehouseId).OnDelete(DeleteBehavior.Restrict);b.HasOne(x=>x.Item).WithMany().HasForeignKey(x=>x.ItemId).OnDelete(DeleteBehavior.Restrict);b.HasQueryFilter(x=>!x.Item!.IsDeleted);});
        modelBuilder.Entity<StockTransfer>(b=>{b.Property(x=>x.Number).HasMaxLength(30).IsRequired();b.HasIndex(x=>x.Number).IsUnique().HasFilter("\"IsDeleted\" = false");b.HasOne(x=>x.FromWarehouse).WithMany().HasForeignKey(x=>x.FromWarehouseId).OnDelete(DeleteBehavior.Restrict);b.HasOne(x=>x.ToWarehouse).WithMany().HasForeignKey(x=>x.ToWarehouseId).OnDelete(DeleteBehavior.Restrict);b.HasMany(x=>x.Lines).WithOne(x=>x.Transfer).HasForeignKey(x=>x.StockTransferId).OnDelete(DeleteBehavior.Cascade);});
        modelBuilder.Entity<StockTransferLine>(b=>{b.Property(x=>x.ItemCode).HasMaxLength(50);b.Property(x=>x.ItemName).HasMaxLength(200);b.HasOne(x=>x.Item).WithMany().HasForeignKey(x=>x.ItemId).OnDelete(DeleteBehavior.Restrict);b.Ignore(x=>x.Shortfall);b.HasQueryFilter(x=>!x.Transfer!.IsDeleted);});
        modelBuilder.Entity<InventoryCount>(b=>{b.Property(x=>x.Number).HasMaxLength(30).IsRequired();b.HasIndex(x=>x.Number).IsUnique().HasFilter("\"IsDeleted\" = false");b.HasOne(x=>x.Warehouse).WithMany().HasForeignKey(x=>x.WarehouseId).OnDelete(DeleteBehavior.Restrict);b.HasMany(x=>x.Lines).WithOne(x=>x.Count).HasForeignKey(x=>x.InventoryCountId).OnDelete(DeleteBehavior.Cascade);b.Ignore(x=>x.VarianceCount);});
        modelBuilder.Entity<InventoryCountLine>(b=>{b.Property(x=>x.ItemCode).HasMaxLength(50);b.Property(x=>x.ItemName).HasMaxLength(200);b.HasOne(x=>x.Item).WithMany().HasForeignKey(x=>x.ItemId).OnDelete(DeleteBehavior.Restrict);b.Ignore(x=>x.Variance);b.HasQueryFilter(x=>!x.Count!.IsDeleted);});
        modelBuilder.Entity<StockBatch>(b=>{b.Property(x=>x.BatchNumber).HasMaxLength(100).IsRequired();b.HasIndex(x=>new{x.ItemId,x.BatchNumber}).IsUnique().HasFilter("\"IsDeleted\" = false");b.HasOne(x=>x.Item).WithMany().HasForeignKey(x=>x.ItemId).OnDelete(DeleteBehavior.Restrict);b.HasOne(x=>x.Warehouse).WithMany().HasForeignKey(x=>x.WarehouseId).OnDelete(DeleteBehavior.Restrict);});
        modelBuilder.Entity<StockUnit>(b=>{b.Property(x=>x.SerialNumber).HasMaxLength(150).IsRequired();b.HasIndex(x=>new{x.ItemId,x.SerialNumber}).IsUnique().HasFilter("\"IsDeleted\" = false");b.HasOne(x=>x.Item).WithMany().HasForeignKey(x=>x.ItemId).OnDelete(DeleteBehavior.Restrict);b.HasOne(x=>x.Warehouse).WithMany().HasForeignKey(x=>x.WarehouseId).OnDelete(DeleteBehavior.Restrict);b.HasOne(x=>x.Batch).WithMany().HasForeignKey(x=>x.StockBatchId).OnDelete(DeleteBehavior.SetNull);b.Ignore(x=>x.CountsAsStock);});
        modelBuilder.Entity<InventoryReturn>(b=>{b.Property(x=>x.Number).HasMaxLength(30).IsRequired();b.HasIndex(x=>x.Number).IsUnique().HasFilter("\"IsDeleted\" = false");b.HasIndex(x=>x.PartyId);b.HasMany(x=>x.Lines).WithOne(x=>x.Return).HasForeignKey(x=>x.InventoryReturnId).OnDelete(DeleteBehavior.Cascade);b.Ignore(x=>x.Total);});
        modelBuilder.Entity<InventoryReturnLine>(b=>{b.Property(x=>x.ItemCode).HasMaxLength(50);b.Property(x=>x.ItemName).HasMaxLength(200);b.HasOne(x=>x.Item).WithMany().HasForeignKey(x=>x.ItemId).OnDelete(DeleteBehavior.Restrict);b.HasQueryFilter(x=>!x.Return!.IsDeleted);});










        modelBuilder.Entity<DocumentSequenceCounter>(b =>
        {
            b.ToTable("document_sequences");
            b.HasIndex(c => new { c.Key, c.Year }).IsUnique();
        });
    }
}
