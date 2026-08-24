namespace MeiErp.Platform.Persistence;

public sealed class AuditLogEntry
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string ModuleKey { get; set; } = "";
    public string EntityName { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Action { get; set; } = "";
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
}
