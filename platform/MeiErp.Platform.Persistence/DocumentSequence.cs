using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Platform.Persistence;

/// <summary>
/// Hands out document numbers - PO-26-0001, INV-26-0042 - that are unique,
/// gapless within a series, and safe when two people save at the same moment.
/// </summary>
public interface IDocumentSequence
{
    /// <summary>
    /// The next number in a series, formatted. Must be called inside the same
    /// transaction as the document it numbers, so a rolled-back save does not
    /// burn a number and leave a hole in the register.
    /// </summary>
    Task<string> NextAsync(string key, CancellationToken ct = default);
}

/// <summary>
/// One counter, e.g. purchase orders for fiscal year 2026.
/// </summary>
public class DocumentSequenceCounter
{
    public int Id { get; set; }

    /// <summary>Series key, e.g. "inventory.purchase-order".</summary>
    public string Key { get; set; } = "";

    /// <summary>Fiscal year the series belongs to. Numbering restarts each year.</summary>
    public int Year { get; set; }

    /// <summary>Printed ahead of the number, e.g. "PO".</summary>
    public string Prefix { get; set; } = "";

    public int Next { get; set; } = 1;

    /// <summary>Zero-padding width, so PO-26-0001 sorts as text the way it reads.</summary>
    public int Padding { get; set; } = 4;
}

/// <summary>
/// PostgreSQL-backed sequence.
///
/// Correctness here rests on one thing: the counter row is locked with
/// <c>FOR UPDATE</c> before it is read. Read-then-write without the lock is the
/// classic duplicate-document-number bug - it survives every test and then two
/// people press Save within the same second and both get PO-26-0042.
/// </summary>
public sealed class DocumentSequence(DbContext db, IClock clock) : IDocumentSequence
{
    public async Task<string> NextAsync(string key, CancellationToken ct = default)
    {
        // The fiscal year the number belongs to comes from the business clock,
        // never DateTime.Today: on a UTC server in a UTC+5 business those
        // disagree for five hours nightly, and a document raised at 2am on
        // 1 July would be numbered into the wrong year.
        var year = clock.Today.Year;

        var counter = await db.Set<DocumentSequenceCounter>()
            .FromSqlRaw(
                """
                SELECT * FROM document_sequences
                WHERE key = {0} AND year = {1}
                FOR UPDATE
                """,
                key, year)
            .FirstOrDefaultAsync(ct);

        if (counter is null)
        {
            counter = new DocumentSequenceCounter
            {
                Key = key,
                Year = year,
                Prefix = DefaultPrefix(key),
                Next = 1
            };
            db.Set<DocumentSequenceCounter>().Add(counter);
        }

        var number = counter.Next;
        counter.Next++;

        await db.SaveChangesAsync(ct);

        return Format(counter, number, year);
    }

    /// <summary>PO-26-0001: prefix, two-digit year, zero-padded serial.</summary>
    private static string Format(DocumentSequenceCounter counter, int number, int year) =>
        $"{counter.Prefix}-{year % 100:D2}-{number.ToString().PadLeft(counter.Padding, '0')}";

    /// <summary>
    /// "inventory.purchase-order" becomes "PO". A series with no configured
    /// prefix still produces something readable rather than failing at the
    /// moment someone saves their first document.
    /// </summary>
    private static string DefaultPrefix(string key)
    {
        var last = key.Split('.').Last();
        var initials = last.Split('-', StringSplitOptions.RemoveEmptyEntries)
                           .Select(part => char.ToUpperInvariant(part[0]));
        return string.Concat(initials);
    }
}
