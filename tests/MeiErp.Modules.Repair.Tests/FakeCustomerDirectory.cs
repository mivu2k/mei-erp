namespace MeiErp.Modules.Repair.Tests;

/// <summary>
/// Stands in for the party master.
///
/// The workshop no longer keeps its own customer list - it reads the one Sales
/// and Purchase use, through <see cref="IRepairCustomerDirectory"/>. Testing
/// the workshop against a real party database would be testing another module's
/// storage at the same time, so these tests supply the customer directly.
/// </summary>
public sealed class FakeCustomerDirectory : IRepairCustomerDirectory
{
    private readonly Dictionary<int, RepairCustomer> _rows = [];
    private int _next = 1;

    public RepairCustomer Add(string name, string? phone = null)
    {
        var row = new RepairCustomer(_next, $"C-{_next:D3}", name, phone, null);
        _rows[_next++] = row;
        return row;
    }

    public Task<IReadOnlyList<RepairCustomer>> SearchAsync(
        string? term = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RepairCustomer>>(
            [.. _rows.Values.Where(r => term is null || r.Name.Contains(term, StringComparison.OrdinalIgnoreCase))]);

    public Task<RepairCustomer?> GetAsync(int partyId, CancellationToken ct = default) =>
        Task.FromResult(_rows.TryGetValue(partyId, out var r) ? r : null);

    public Task<RepairCustomer> EnsureAsync(string name, string? phone, CancellationToken ct = default)
    {
        var existing = _rows.Values.FirstOrDefault(r => r.Name == name);
        return Task.FromResult(existing ?? Add(name, phone));
    }
}
