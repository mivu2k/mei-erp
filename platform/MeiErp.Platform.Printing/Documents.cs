using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MeiErp.Platform.Printing;

/// <summary>
/// The company, flattened for print. Dependency-free on purpose so a module's
/// document DTO can carry it without pulling in EF or Identity.
/// </summary>
public sealed record Branding
{
    public string Name { get; init; } = "";
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Website { get; init; }
    public string? TaxNumber { get; init; }
    public string? FooterNote { get; init; }
    public byte[]? Logo { get; init; }

    public string Currency { get; init; } = "PKR";

    /// <summary>A profile nobody has filled in yet. Prints without falling over.</summary>
    public static Branding Empty => new() { Name = "" };
}

/// <summary>
/// One printable document, described rather than drawn.
///
/// A module builds this and never touches QuestPDF, which is what keeps every
/// document in the suite looking the same and means a layout fix lands
/// everywhere at once.
/// </summary>
public sealed record PrintDocument
{
    public required string Title { get; init; }

    /// <summary>The document's own number. Printed, and encoded into both symbologies.</summary>
    public string? Reference { get; init; }

    public DateOnly? Date { get; init; }

    /// <summary>Label/value pairs printed in two columns under the header.</summary>
    public IReadOnlyList<PrintField> Fields { get; init; } = [];

    public IReadOnlyList<PrintTable> Tables { get; init; } = [];

    /// <summary>Totals printed right-aligned under the last table.</summary>
    public IReadOnlyList<PrintField> Totals { get; init; } = [];

    public string? Notes { get; init; }

    /// <summary>Lines people sign on. Empty means no signature block.</summary>
    public IReadOnlyList<string> Signatures { get; init; } = [];

    public PageSize Size { get; init; } = PageSize.A4;

    /// <summary>Stamped across the page for a draft or a copy.</summary>
    public string? Watermark { get; init; }
}

public sealed record PrintField(string Label, string? Value);

public sealed record PrintTable
{
    public string? Caption { get; init; }
    public required IReadOnlyList<PrintColumn> Columns { get; init; }
    public required IReadOnlyList<IReadOnlyList<string>> Rows { get; init; }

    /// <summary>Printed in bold under the rows. Same width as the columns.</summary>
    public IReadOnlyList<string>? FooterRow { get; init; }
}

/// <param name="Width">Relative width. Columns share the page in proportion.</param>
/// <param name="AlignRight">Right-aligns the column. Use for money and quantities.</param>
public sealed record PrintColumn(string Header, float Width = 1f, bool AlignRight = false);

public enum PageSize
{
    A4 = 0,

    /// <summary>Thermal roll, 80mm — receipts and short delivery notes.</summary>
    Roll80 = 1,

    /// <summary>Label roll, 62mm — asset and device stickers.</summary>
    Label62 = 2
}
