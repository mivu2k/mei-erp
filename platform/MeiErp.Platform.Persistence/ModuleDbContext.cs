using System.Linq.Expressions;
using System.Text.Json;
using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Platform.Persistence;

/// <summary>
/// The base every module's DbContext derives from.
///
/// Three things happen here so that no module has to remember them: audit
/// stamping, soft delete, and outbox dispatch. Each was a rule the previous
/// platform enforced by convention and therefore sometimes did not.
/// </summary>
public abstract class ModuleDbContext(
    DbContextOptions options,
    ICurrentUser currentUser,
    IClock clock) : DbContext(options)
{
    /// <summary>
    /// The PostgreSQL schema this module owns. One database, one schema per
    /// module: isolated, but a report can still join across them and one backup
    /// covers everything.
    /// </summary>
    protected abstract string Schema { get; }

    protected ICurrentUser CurrentUser => currentUser;
    protected IClock Clock => clock;

    /// <summary>
    /// Integration events waiting to be dispatched. Written in the *same*
    /// transaction as the business change that raised them, which is what makes
    /// "the stock moved but the voucher never posted" impossible rather than
    /// merely unlikely.
    /// </summary>
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        base.OnModelCreating(modelBuilder);

        ConfigureOutbox(modelBuilder);
        modelBuilder.Entity<AuditLogEntry>(b =>
        {
            b.ToTable("AuditLogs", "platform", t => t.ExcludeFromMigrations());
            b.HasKey(x => x.Id);
            b.Property(x => x.ModuleKey).HasMaxLength(50).IsRequired();
            b.Property(x => x.EntityName).HasMaxLength(150).IsRequired();
            b.Property(x => x.EntityId).HasMaxLength(80).IsRequired();
            b.Property(x => x.Action).HasMaxLength(30).IsRequired();
        });

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clr = entityType.ClrType;

            // Soft delete: a global filter means every query in every module
            // excludes deleted rows without a single service remembering to.
            if (typeof(AuditableEntity).IsAssignableFrom(clr))
            {
                var parameter = Expression.Parameter(clr, "e");
                var property = Expression.Property(parameter, nameof(AuditableEntity.IsDeleted));
                var filter = Expression.Lambda(Expression.Not(property), parameter);
                modelBuilder.Entity(clr).HasQueryFilter(filter);

                modelBuilder.Entity(clr).HasIndex(nameof(AuditableEntity.IsDeleted));
            }

            // Concurrency: PostgreSQL's own xmin. Unlike the previous platform
            // there is no token to re-stamp by hand, so there is no way to
            // forget to - the database does it.
            if (typeof(IConcurrencyChecked).IsAssignableFrom(clr))
            {
                modelBuilder.Entity(clr)
                     .Property(nameof(IConcurrencyChecked.Version))
                     .HasColumnName("xmin")
                     .HasColumnType("xid")
                     .ValueGeneratedOnAddOrUpdate()
                     .IsConcurrencyToken();
            }

            // Money is decimal(18,4) everywhere. Left to convention, one
            // property eventually gets declared as double and rounding errors
            // start appearing in a trial balance that no longer balances.
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?))
                    property.SetColumnType("numeric(18,4)");
            }
        }
    }

    private static void ConfigureOutbox(ModelBuilder modelBuilder)
    {
        var outbox = modelBuilder.Entity<OutboxMessage>();
        outbox.ToTable("outbox_messages");
        outbox.HasKey(m => m.Id);
        outbox.Property(m => m.EventType).HasMaxLength(200).IsRequired();
        outbox.Property(m => m.Payload).IsRequired();

        // The dispatcher's hot path: undispatched, oldest first.
        outbox.HasIndex(m => new { m.DispatchedUtc, m.OccurredUtc })
              .HasFilter(null);
    }

    public override int SaveChanges()
    {
        StampAudit();
        var audits = PrepareAuditRows();
        if (audits.Count == 0) return base.SaveChanges();
        using var transaction = Database.CurrentTransaction is null ? Database.BeginTransaction() : null;
        var result = base.SaveChanges();
        CompleteEntityIds(audits);
        AuditLogs.AddRange(audits.Select(x => x.Row));
        base.SaveChanges();
        transaction?.Commit();
        return result;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAudit();
        var audits = PrepareAuditRows();
        if (audits.Count == 0) return await base.SaveChangesAsync(cancellationToken);
        async Task<int> SaveAsync()
        {
            await using var transaction = Database.CurrentTransaction is null ? await Database.BeginTransactionAsync(cancellationToken) : null;
            var result = await base.SaveChangesAsync(cancellationToken);
            CompleteEntityIds(audits);
            AuditLogs.AddRange(audits.Select(x => x.Row));
            await base.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return result;
        }
        return Database.CurrentTransaction is not null
            ? await SaveAsync()
            : await Database.CreateExecutionStrategy().ExecuteAsync(SaveAsync);
    }

    private sealed record PendingAudit(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry Entry, AuditLogEntry Row);
    private List<PendingAudit> PrepareAuditRows()
    {
        var rows = new List<PendingAudit>();
        foreach (var entry in ChangeTracker.Entries().Where(x => x.Entity is not AuditLogEntry and not OutboxMessage && x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var action = entry.State == EntityState.Added ? "Created" : entry.Entity is AuditableEntity { IsDeleted: true } ? "SoftDeleted" : entry.State == EntityState.Deleted ? "Deleted" : "Modified";
            var changed = entry.Properties.Where(p => entry.State == EntityState.Added || entry.State == EntityState.Deleted || p.IsModified).Where(p => !Sensitive(p.Metadata.Name)).ToList();
            string? oldValues = entry.State == EntityState.Added ? null : JsonSerializer.Serialize(changed.ToDictionary(p => p.Metadata.Name, p => Safe(p.OriginalValue)));
            string? newValues = entry.State == EntityState.Deleted ? null : JsonSerializer.Serialize(changed.ToDictionary(p => p.Metadata.Name, p => Safe(p.CurrentValue)));
            rows.Add(new(entry,new(){TimestampUtc=clock.UtcNow,ModuleKey=Schema,EntityName=entry.Metadata.ClrType.Name,Action=action,UserId=currentUser.UserId,UserName=currentUser.Name,OldValues=oldValues,NewValues=newValues}));
        }
        return rows;
    }
    private static void CompleteEntityIds(IEnumerable<PendingAudit> rows){foreach(var pending in rows){var key=pending.Entry.Properties.FirstOrDefault(x=>x.Metadata.IsPrimaryKey());pending.Row.EntityId=key?.CurrentValue?.ToString()??"";}}
    private static bool Sensitive(string name)=>name.Contains("Password",StringComparison.OrdinalIgnoreCase)||name.Contains("Token",StringComparison.OrdinalIgnoreCase)||name.Contains("Secret",StringComparison.OrdinalIgnoreCase);
    private static object? Safe(object? value)=>value is byte[] bytes?$"[binary {bytes.Length} bytes]":value;

    /// <summary>Represents the platform-owned audit table in isolated EnsureCreated test databases.</summary>
    public Task EnsureAuditTableForTestsAsync(CancellationToken ct=default)=>Database.ExecuteSqlRawAsync("""
        CREATE SCHEMA IF NOT EXISTS platform;
        CREATE TABLE IF NOT EXISTS platform."AuditLogs" (
            "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            "TimestampUtc" timestamp with time zone NOT NULL,
            "ModuleKey" character varying(50) NOT NULL,
            "EntityName" character varying(150) NOT NULL,
            "EntityId" character varying(80) NOT NULL,
            "Action" character varying(30) NOT NULL,
            "UserId" character varying(450), "UserName" character varying(200),
            "OldValues" text, "NewValues" text);
        """,ct);

    /// <summary>
    /// Fill in who and when, and turn deletes into flag updates.
    ///
    /// Nothing in this system is physically removed: history has to keep
    /// resolving, and a row a voucher still points at would leave the books
    /// referencing nothing.
    /// </summary>
    private void StampAudit()
    {
        var now = clock.UtcNow;
        var who = currentUser.UserId ?? currentUser.Name ?? "system";

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedUtc = now;
                    entry.Entity.CreatedBy = who;
                    break;

                case EntityState.Modified:
                    entry.Entity.ModifiedUtc = now;
                    entry.Entity.ModifiedBy = who;

                    // Created* is set once, at insert. Without this an update
                    // that happens to carry a stale entity can rewrite history.
                    entry.Property(e => e.CreatedUtc).IsModified = false;
                    entry.Property(e => e.CreatedBy).IsModified = false;
                    break;

                case EntityState.Deleted:
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                    entry.Entity.DeletedUtc = now;
                    entry.Entity.DeletedBy = who;
                    break;
            }
        }
    }
}

/// <summary>
/// One integration event, durably queued.
///
/// The outbox pattern: publishing writes this row inside the publisher's own
/// transaction, and a dispatcher delivers it after that transaction commits.
/// If the process dies between the two, the row is still there and delivery
/// retries. If the transaction rolls back, the event was never real.
/// </summary>
public class OutboxMessage
{
    public long Id { get; set; }

    /// <summary>Fully-qualified event name, e.g. "inventory.goods-receipt.posted".</summary>
    public string EventType { get; set; } = "";

    /// <summary>JSON body. Deliberately a string - the dispatcher has no reference to the module's types.</summary>
    public string Payload { get; set; } = "";

    public DateTime OccurredUtc { get; set; }

    /// <summary>Null until delivered. The dispatcher's index reads this.</summary>
    public DateTime? DispatchedUtc { get; set; }

    public int Attempts { get; set; }

    /// <summary>Why the last attempt failed. Kept so the dead-letter screen can explain itself.</summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Set once a message has failed too often to keep retrying. It stops being
    /// dispatched and starts being someone's problem on the dead-letter screen -
    /// an event bus without one loses money quietly.
    /// </summary>
    public DateTime? DeadLetteredUtc { get; set; }

    /// <summary>Who caused this, carried through so the resulting write is attributed to a person.</summary>
    public string? CausedByUserId { get; set; }
}
