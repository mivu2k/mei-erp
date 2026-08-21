namespace MeiErp.Platform.Kernel;

/// <summary>
/// Every persisted record. Integer keys because this is a single-instance,
/// on-premise system: they sort, they read aloud over a phone, and they index
/// far better than GUIDs on the report tables that dominate this workload.
/// </summary>
public abstract class Entity
{
    public int Id { get; set; }
}

/// <summary>
/// A record that carries who touched it and when. <c>ModuleDbContext</c> fills
/// every field here on save; no service should ever set them by hand.
/// </summary>
public abstract class AuditableEntity : Entity
{
    public DateTime CreatedUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedUtc { get; set; }
    public string? ModifiedBy { get; set; }

    /// <summary>
    /// Soft delete. Nothing in this system is ever physically removed - history
    /// has to keep resolving, and a deleted row that a voucher still points at
    /// would leave the books referencing nothing.
    /// </summary>
    public bool IsDeleted { get; set; }
    public DateTime? DeletedUtc { get; set; }
    public string? DeletedBy { get; set; }
}

/// <summary>
/// Marks a record where a lost update costs money, stock, or someone's work.
/// PostgreSQL's system <c>xmin</c> column backs this, so unlike the previous
/// platform there is no token to re-stamp by hand and no way to forget to.
/// </summary>
public interface IConcurrencyChecked
{
    uint Version { get; set; }
}

/// <summary>
/// A record that belongs to one module. Used by the reporting and audit layers
/// to attribute a row without knowing the module's own types.
/// </summary>
public interface IModuleOwned
{
    static abstract string ModuleKey { get; }
}
