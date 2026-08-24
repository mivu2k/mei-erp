namespace MeiErp.Modules.Inventory;

/// <summary>
/// Which stock book the person is currently working in, held for the life of
/// their circuit.
///
/// Scoped rather than passed page to page: someone working the workshop's
/// spares moves between items, transfers and counts all afternoon, and having
/// each screen default back to the main store would be a standing invitation to
/// receive a part into the wrong book. Choosing once and having it stick is the
/// behaviour that makes two books usable rather than merely possible.
///
/// This is a convenience, not a control. It decides what a screen shows, never
/// what a person is allowed to do - the services take the book as an argument
/// and the permission checks are unchanged.
/// </summary>
public sealed class StockBookContext
{
    private int? _selected;

    /// <summary>Raised when the book changes, so open screens can reload.</summary>
    public event Action? Changed;

    /// <summary>
    /// The chosen book, or null before anything has been chosen. Screens should
    /// prefer <see cref="EnsureAsync"/>, which settles on the default.
    /// </summary>
    public int? SelectedId => _selected;

    public void Select(int domainId)
    {
        if (_selected == domainId) return;
        _selected = domainId;
        Changed?.Invoke();
    }

    /// <summary>
    /// The chosen book, falling back to the default one on first use. Every
    /// working screen calls this rather than reading <see cref="SelectedId"/>,
    /// so a fresh circuit lands somewhere real instead of showing both books
    /// merged together.
    /// </summary>
    public async Task<int> EnsureAsync(IStockDomainService domains, CancellationToken ct = default)
    {
        if (_selected is { } chosen) return chosen;

        var fallback = await domains.DefaultAsync(ct);
        _selected = fallback.Id;
        return fallback.Id;
    }
}
