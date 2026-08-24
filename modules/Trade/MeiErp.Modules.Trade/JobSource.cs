namespace MeiErp.Modules.Trade;

/// <summary>
/// Lets a quotation be raised straight off a workshop job.
///
/// The workshop records what a device needs as it goes - parts, labour, a
/// courier charge - and the quotation is that list priced up. Re-typing it into
/// the quote is where the two drift apart: a line gets missed, or a price gets
/// keyed differently, and the customer is billed for something other than the
/// work recorded.
///
/// Trade states what it needs here and the host wires an adapter over Repair,
/// the same arrangement as <see cref="ITradeStockPort"/>. No implementation is
/// registered by default, so a business without the workshop simply never sees
/// the option.
/// </summary>
public interface ITradeJobSource
{
    /// <summary>
    /// The job, its customer, and its billable work priced as quotation lines.
    /// Null when the job has gone.
    /// </summary>
    Task<QuotableJob?> JobAsync(int jobId, CancellationToken ct = default);

    /// <summary>
    /// Every device on one intake, folded into a single quotation.
    ///
    /// A customer who brings in six machines wants one price, not six - and the
    /// device name goes into each line so the paperwork still says which
    /// machine each charge belongs to.
    /// </summary>
    Task<QuotableJob?> IntakeAsync(int intakeId, CancellationToken ct = default);
}

/// <param name="Reference">The job or intake number, shown on the quotation.</param>
/// <param name="PartyId">
/// The customer, as the party master knows them. Zero when the workshop's
/// customer has no matching party yet - the caller has to deal with that rather
/// than silently quoting the wrong company.
/// </param>
public sealed record QuotableJob(
    int Id,
    string Reference,
    string Description,
    int PartyId,
    string PartyName,
    IReadOnlyList<DocumentLineInput> Lines);
