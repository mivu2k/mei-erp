namespace MeiErp.Modules.Repair;

/// <summary>
/// Who the workshop can book a device in for.
///
/// The workshop used to keep its own customer list, separate from the one Sales
/// and Purchase use. That meant the same person existed twice, with two sets of
/// contact details drifting apart, and a repair invoice that could not be
/// reconciled against the customer's other business.
///
/// Now there is one party master and the workshop reads it through here. Repair
/// states what it needs; the host wires an adapter, the same arrangement as
/// <c>ITradeStockPort</c>. No implementation registered means no customer
/// picker - which is honest, rather than silently falling back to a private list.
/// </summary>
public interface IRepairCustomerDirectory
{
    Task<IReadOnlyList<RepairCustomer>> SearchAsync(string? term = null, CancellationToken ct = default);

    Task<RepairCustomer?> GetAsync(int partyId, CancellationToken ct = default);

    /// <summary>
    /// Books a walk-in straight from the counter, so receiving a device is not
    /// blocked on somebody else creating the customer first. Returns the
    /// existing party when the name already matches one.
    /// </summary>
    Task<RepairCustomer> EnsureAsync(string name, string? phone, CancellationToken ct = default);
}

/// <summary>A customer, flattened to what the workshop needs to see.</summary>
public sealed record RepairCustomer(int Id, string Code, string Name, string? Phone, string? Email);
