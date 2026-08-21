using MeiErp.Platform.Kernel;
using MeiErp.Platform.Printing;

namespace MeiErp.Platform.Reporting;

/// <summary>
/// A report a module offers.
///
/// The module says what the report is and how to run it; everything after that
/// - the screen, the Excel file and the PDF - is shared. That is what stops the
/// three disagreeing, which is the usual way a report loses people's trust.
/// </summary>
public sealed record ReportDefinition
{
    /// <summary>Namespaced, e.g. "finance.trial-balance".</summary>
    public required string Key { get; init; }

    public required string Name { get; init; }
    public required string Description { get; init; }

    /// <summary>Which module owns it, so the hub can group and filter.</summary>
    public required string ModuleKey { get; init; }

    /// <summary>Grouping within the module, e.g. "Statements".</summary>
    public string Group { get; init; } = "General";

    /// <summary>Permission needed to run it. A report you cannot see never appears.</summary>
    public required string Permission { get; init; }

    /// <summary>Which filters this report actually uses. The panel shows only these.</summary>
    public ReportFilters Uses { get; init; } = ReportFilters.DateRange;

    /// <summary>Runs the report. Returns the shaped table the whole pipeline reads.</summary>
    public required Func<ReportRequest, CancellationToken, Task<ReportResult>> Run { get; init; }

    public int SortOrder { get; init; }
}

/// <summary>Which parts of the filter bar a report needs. Showing filters a report ignores is a lie.</summary>
[Flags]
public enum ReportFilters
{
    None = 0,
    DateRange = 1,
    AsAtDate = 2,
    Party = 4,
    Person = 8,
    Department = 16,
    Project = 32,
    Status = 64,
    Account = 128,
    Item = 256
}

public sealed record ReportRequest
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public DateOnly? AsAt { get; init; }

    public int? PartyId { get; init; }
    public string? PersonId { get; init; }
    public string? DepartmentId { get; init; }
    public int? ProjectId { get; init; }
    public int? AccountId { get; init; }
    public int? ItemId { get; init; }
    public string? Status { get; init; }

    /// <summary>Free text, matched however the individual report sees fit.</summary>
    public string? Search { get; init; }

    /// <summary>Column key to group rows by at run time. Null leaves the report flat.</summary>
    public string? GroupBy { get; init; }
}

/// <summary>
/// Every report ends up in this shape.
///
/// One shape means the screen, the Excel export and the PDF are rendered from
/// the same rows by the same code - so a figure cannot differ between what
/// somebody sees and what they send on.
/// </summary>
public sealed record ReportResult
{
    public required IReadOnlyList<ReportColumn> Columns { get; init; }
    public required IReadOnlyList<ReportRow> Rows { get; init; }

    /// <summary>Printed under the rows, and repeated per group when grouping is on.</summary>
    public IReadOnlyList<ReportTotal> Totals { get; init; } = [];

    /// <summary>Shown above the table — the period covered, the account, whatever matters.</summary>
    public IReadOnlyList<PrintField> Header { get; init; } = [];

    /// <summary>Said plainly when the report legitimately has nothing to show.</summary>
    public string EmptyMessage { get; init; } = "Nothing to report for these filters.";

    public static ReportResult Empty(IReadOnlyList<ReportColumn> columns, string? message = null) =>
        new()
        {
            Columns = columns,
            Rows = [],
            EmptyMessage = message ?? "Nothing to report for these filters."
        };
}

/// <param name="Key">Stable identifier, used for grouping and drill-through.</param>
/// <param name="Kind">Decides alignment, formatting and whether it can be totalled.</param>
public sealed record ReportColumn(
    string Key, string Header, ReportValueKind Kind = ReportValueKind.Text, float Width = 1f);

public enum ReportValueKind
{
    Text = 0,
    Number = 1,

    /// <summary>Formatted to two places and right-aligned; can be totalled.</summary>
    Money = 2,

    Date = 3,

    /// <summary>Rendered as a chip on screen rather than plain text.</summary>
    Status = 4
}

/// <param name="Values">Keyed by column key. A missing key prints blank rather than throwing.</param>
/// <param name="DrillUrl">Where this row came from. Nothing in a report should be a dead number.</param>
public sealed record ReportRow(IReadOnlyDictionary<string, object?> Values, string? DrillUrl = null)
{
    public object? this[string key] => Values.GetValueOrDefault(key);
}

public sealed record ReportTotal(string ColumnKey, decimal Value, string? Label = null);

/// <summary>
/// Every report in the suite, resolved once at startup from what the modules
/// registered. Nothing queries the database to find out which reports exist.
/// </summary>
public interface IReportCatalog
{
    IReadOnlyList<ReportDefinition> All { get; }

    ReportDefinition? Find(string key);

    /// <summary>Only the reports this user may actually run.</summary>
    IReadOnlyList<ReportDefinition> Available(ICurrentUser user);
}

public sealed class ReportCatalog(IEnumerable<ReportDefinition> reports) : IReportCatalog
{
    public IReadOnlyList<ReportDefinition> All { get; } =
        reports.OrderBy(r => r.ModuleKey)
               .ThenBy(r => r.SortOrder)
               .ThenBy(r => r.Name)
               .ToArray();

    public ReportDefinition? Find(string key) =>
        All.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<ReportDefinition> Available(ICurrentUser user) =>
        [.. All.Where(r => user.Can(r.Permission))];
}

/// <summary>
/// Turns a result into the shapes the screen, Excel and PDF all read.
///
/// Deliberately the only place a value becomes a string, so a figure is
/// formatted identically wherever it appears.
/// </summary>
public static class ReportRenderer
{
    public static string Format(object? value, ReportValueKind kind) => value switch
    {
        null => "",
        decimal d when kind is ReportValueKind.Money => d.ToString("N2"),
        decimal d => d.ToString("0.##"),
        double d => d.ToString("N2"),
        int i when kind is ReportValueKind.Money => i.ToString("N2"),
        int i => i.ToString("N0"),
        DateOnly d => d.ToString("d MMM yyyy"),
        DateTime d => d.ToString("d MMM yyyy"),
        bool b => b ? "Yes" : "No",
        _ => value.ToString() ?? ""
    };

    public static PrintTable ToTable(ReportResult result, string? caption = null)
    {
        var columns = result.Columns
            .Select(c => new PrintColumn(
                c.Header, c.Width,
                AlignRight: c.Kind is ReportValueKind.Money or ReportValueKind.Number))
            .ToList();

        var rows = result.Rows
            .Select(r => (IReadOnlyList<string>)
                [.. result.Columns.Select(c => Format(r[c.Key], c.Kind))])
            .ToList();

        IReadOnlyList<string>? footer = null;

        if (result.Totals.Count > 0)
        {
            var byColumn = result.Totals.ToDictionary(t => t.ColumnKey, t => t.Value);

            footer = [.. result.Columns.Select((c, index) =>
                byColumn.TryGetValue(c.Key, out var total)
                    ? Format(total, c.Kind)

                    // The word "Total" goes in the first column that carries no
                    // figure, so the footer reads as a sentence rather than a
                    // row of orphaned numbers.
                    : index == 0 ? "Total" : "")];
        }

        return new PrintTable
        {
            Caption = caption,
            Columns = columns,
            Rows = rows,
            FooterRow = footer
        };
    }

    public static PrintDocument ToDocument(
        ReportDefinition definition, ReportResult result, ReportRequest request)
    {
        var header = new List<PrintField>(result.Header);

        // The period is stated on the page. A printed report found on a desk
        // with no dates on it is worse than useless - it looks authoritative
        // and nobody can tell what it covers.
        if (request.From is not null || request.To is not null)
        {
            header.Insert(0, new PrintField("Period",
                $"{request.From?.ToString("d MMM yyyy") ?? "start"} to " +
                $"{request.To?.ToString("d MMM yyyy") ?? "date"}"));
        }
        else if (request.AsAt is not null)
        {
            header.Insert(0, new PrintField("As at", request.AsAt.Value.ToString("d MMM yyyy")));
        }

        return new PrintDocument
        {
            Title = definition.Name,
            Date = request.AsAt ?? request.To,
            Fields = header,
            Tables = [ToTable(result)],
            Notes = result.Rows.Count == 0 ? result.EmptyMessage : null
        };
    }

    /// <summary>
    /// Splits rows into groups by a column, each with its own money subtotals.
    /// Returns one group when grouping is off, so callers render the same way
    /// either way.
    /// </summary>
    public static IReadOnlyList<ReportGroup> Group(ReportResult result, string? groupBy)
    {
        if (string.IsNullOrWhiteSpace(groupBy))
            return [new ReportGroup(null, result.Rows, result.Totals)];

        var column = result.Columns.FirstOrDefault(c => c.Key == groupBy);
        if (column is null)
            return [new ReportGroup(null, result.Rows, result.Totals)];

        var moneyColumns = result.Columns
            .Where(c => c.Kind is ReportValueKind.Money)
            .Select(c => c.Key)
            .ToList();

        return [.. result.Rows
            .GroupBy(r => Format(r[groupBy], column.Kind))
            .OrderBy(g => g.Key)
            .Select(g => new ReportGroup(
                g.Key,
                [.. g],
                [.. moneyColumns.Select(key => new ReportTotal(
                    key,
                    g.Sum(r => r[key] is decimal d ? d : 0m)))]))];
    }
}

/// <param name="Key">Null when the report is not grouped.</param>
public sealed record ReportGroup(
    string? Key, IReadOnlyList<ReportRow> Rows, IReadOnlyList<ReportTotal> Totals);

/// <summary>Convenience for building rows without repeating dictionary noise.</summary>
public static class ReportRowBuilder
{
    public static ReportRow Row(string? drillUrl = null, params (string Key, object? Value)[] values) =>
        new(values.ToDictionary(v => v.Key, v => v.Value), drillUrl);
}
