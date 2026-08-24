namespace MeiErp.Platform.Kernel;

/// <summary>
/// What a module tells the platform about itself. A module registers one of
/// these and the shell, the nav, the permission catalog, the report hub and the
/// approval engine all pick it up - there is no second place to register.
/// </summary>
public sealed record ModuleDescriptor
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }

    /// <summary>Route prefix the module's pages live under, e.g. "/finance".</summary>
    public required string BasePath { get; init; }

    /// <summary>MudBlazor icon name, resolved by the shell.</summary>
    public required string Icon { get; init; }

    public required string Color { get; init; }

    public int SortOrder { get; init; }

    /// <summary>
    /// The database schema this module owns. One PostgreSQL database, one schema
    /// per module: modules stay isolated, but a report can still join across
    /// them and one backup covers everything.
    /// </summary>
    public required string Schema { get; init; }

    /// <summary>Every permission this module defines, with a description for the admin screen.</summary>
    public IReadOnlyList<PermissionDescriptor> Permissions { get; init; } = [];

    /// <summary>Roles created on a fresh install so the module is usable out of the box.</summary>
    public IReadOnlyList<RoleTemplate> RoleTemplates { get; init; } = [];

    /// <summary>Document types this module can route through the approval engine.</summary>
    public IReadOnlyList<ApprovableDocument> Approvables { get; init; } = [];

    /// <summary>
    /// The module's own nav, shown beneath it in the shell.
    ///
    /// Without this a module is reachable only at its base path, and every page
    /// behind it may as well not exist - which is exactly what happened before
    /// this was added.
    /// </summary>
    public IReadOnlyList<NavItem> Nav { get; init; } = [];

    /// <summary>
    /// Shortcuts this module puts directly on the app bar, beside approvals and
    /// the notification bell.
    ///
    /// For the handful of pages someone opens many times a day from wherever
    /// they happen to be - the attendance QR code being the case that forced it.
    /// Buried in a module's nav that was four clicks with a queue waiting at a
    /// door; on the app bar it is one. Declared here rather than hard-coded into
    /// the shell, so the shell needs no reference to the module that owns it and
    /// any module can add one the same way.
    ///
    /// Keep this list short: everything that earns a place on the app bar takes
    /// attention from everything already on it.
    /// </summary>
    public IReadOnlyList<QuickAction> QuickActions { get; init; } = [];
}

/// <summary>
/// A module's shortcut on the app bar.
/// </summary>
/// <param name="Tooltip">Shown on hover; the app bar has no room for a label.</param>
/// <param name="Path">Where it goes.</param>
/// <param name="Icon">MudBlazor icon name, resolved by the shell.</param>
/// <param name="Permission">
/// Hidden unless the person holds this. Deliberately a permission check rather
/// than a data check: the shell renders on every page, and asking the database
/// whether this login maps to an employee record would cost a query each time.
/// The target page explains itself when the person has no such record.
/// </param>
public sealed record QuickAction(
    string Tooltip, string Path, string Icon, string? Permission = null);

/// <summary>
/// One entry in a module's own navigation.
/// </summary>
/// <param name="Label">What it is called. Written for the person, not the route.</param>
/// <param name="Path">Where it goes.</param>
/// <param name="Icon">MudBlazor icon name, resolved by the shell.</param>
/// <param name="Permission">
/// Hidden unless the person holds this. Null shows it to anyone who can enter
/// the module at all.
/// </param>
/// <param name="Group">Optional heading, so a long module's nav reads in sections.</param>
public sealed record NavItem(
    string Label, string Path, string Icon, string? Permission = null, string? Group = null);

/// <param name="Key">Namespaced, e.g. "finance.vouchers.post".</param>
/// <param name="Group">Groups rows in the permission matrix, e.g. "Vouchers".</param>
/// <param name="Description">Written for whoever administers roles, not for a developer.</param>
public sealed record PermissionDescriptor(string Key, string Group, string Description);

/// <summary>A role shipped with the module, so a fresh install is not a blank permission matrix.</summary>
public sealed record RoleTemplate(string Name, string Description, IReadOnlyList<string> Permissions);

/// <summary>
/// A document type that can carry an approval workflow. The engine needs to
/// know it exists to offer it in the designer; the module supplies a callback
/// that applies the decision to its own record.
/// </summary>
/// <param name="Key">Namespaced, e.g. "inventory.purchase-order".</param>
/// <param name="Name">Shown in the workflow designer and the approvals inbox.</param>
/// <param name="AmountLabel">What the routed amount means here, e.g. "Order value". Null when the flow has no amount.</param>
public sealed record ApprovableDocument(string Key, string Name, string? AmountLabel = null);

/// <summary>
/// Every module the host has composed, resolved once at startup. Nothing
/// queries the database to find out what modules exist.
/// </summary>
public interface IModuleCatalog
{
    IReadOnlyList<ModuleDescriptor> All { get; }

    ModuleDescriptor? Find(string key);

    /// <summary>Maps a request path back to the module that owns it.</summary>
    ModuleDescriptor? FromPath(string? path);

    /// <summary>Every permission across every module, for the admin matrix.</summary>
    IReadOnlyList<PermissionDescriptor> AllPermissions { get; }

    /// <summary>Every approvable document type across every module.</summary>
    IReadOnlyList<ApprovableDocument> AllApprovables { get; }
}

/// <inheritdoc />
public sealed class ModuleCatalog(IEnumerable<ModuleDescriptor> modules) : IModuleCatalog
{
    public IReadOnlyList<ModuleDescriptor> All { get; } =
        modules.OrderBy(m => m.SortOrder).ThenBy(m => m.Name).ToArray();

    public ModuleDescriptor? Find(string key) =>
        All.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase));

    public ModuleDescriptor? FromPath(string? path) =>
        string.IsNullOrEmpty(path)
            ? null
            : All.FirstOrDefault(m =>
                path.StartsWith(m.BasePath + "/", StringComparison.OrdinalIgnoreCase) ||
                path.Equals(m.BasePath, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<PermissionDescriptor> AllPermissions { get; } =
        modules.SelectMany(m => m.Permissions)
               .DistinctBy(p => p.Key)
               .OrderBy(p => p.Key)
               .ToArray();

    public IReadOnlyList<ApprovableDocument> AllApprovables { get; } =
        modules.SelectMany(m => m.Approvables)
               .DistinctBy(a => a.Key)
               .OrderBy(a => a.Name)
               .ToArray();
}
