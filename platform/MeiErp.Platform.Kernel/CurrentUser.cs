namespace MeiErp.Platform.Kernel;

/// <summary>
/// Who is acting. Injected wherever a service needs to stamp a record or make
/// an authorization decision, so no service reaches for HttpContext.
/// </summary>
public interface ICurrentUser
{
    /// <summary>Identity user id, or null for background work and the kiosk.</summary>
    string? UserId { get; }

    /// <summary>Display name, snapshotted onto records alongside the id.</summary>
    string? Name { get; }

    string? Email { get; }

    bool IsAuthenticated { get; }

    /// <summary>True when the user holds this namespaced permission, e.g. "finance.vouchers.post".</summary>
    bool Can(string permission);

    /// <summary>True when the user may enter this module at all.</summary>
    bool InModule(string moduleKey);

    /// <summary>Every role the user holds, for approval routing.</summary>
    IReadOnlyCollection<string> Roles { get; }
}

/// <summary>
/// The actor for work with no signed-in person behind it - the outbox
/// dispatcher, the nightly summary rebuild, the seeder. Named rather than null
/// so an audit row always says who, even when the answer is "the system".
/// </summary>
public sealed class SystemUser(string name = "system") : ICurrentUser
{
    public string? UserId => null;
    public string? Name { get; } = name;
    public string? Email => null;
    public bool IsAuthenticated => false;

    /// <summary>
    /// Background work is trusted: it runs code we shipped, not input a person
    /// supplied. Permission checks exist to constrain people.
    /// </summary>
    public bool Can(string permission) => true;

    public bool InModule(string moduleKey) => true;

    public IReadOnlyCollection<string> Roles { get; } = [];
}
