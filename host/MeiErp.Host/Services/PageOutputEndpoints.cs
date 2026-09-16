using MeiErp.Platform.Identity;
using MeiErp.Platform.Printing;

namespace MeiErp.Host.Services;

/// <summary>Turns the currently visible page into a branded, real PDF.</summary>
public static class PageOutputEndpoints
{
    public sealed record PageOutputRequest(
        string? Title,
        string? Path,
        string? Content,
        IReadOnlyList<PageOutputField>? Fields,
        IReadOnlyList<PageOutputTable>? Tables);

    public sealed record PageOutputField(string? Label, string? Value);
    public sealed record PageOutputTable(
        string? Caption,
        IReadOnlyList<string>? Headers,
        IReadOnlyList<IReadOnlyList<string>>? Rows);

    public static void MapPageOutputEndpoints(this WebApplication app)
    {
        app.MapPost("/page-output/pdf", async (
            PageOutputRequest request,
            IPrintService printer,
            ICompanyProfileService companies,
            CancellationToken ct) =>
        {
            var title = Clean(request.Title, 160) ?? "Page export";
            var fields = (request.Fields ?? [])
                .Take(100)
                .Select(x => new PrintField(Clean(x.Label, 100) ?? "Field", Clean(x.Value, 1000)))
                .ToList();

            var tables = new List<PrintTable>();
            foreach (var source in (request.Tables ?? []).Take(12))
            {
                var headers = (source.Headers ?? []).Take(20).Select(x => Clean(x, 120) ?? "").ToList();
                if (headers.Count == 0) continue;
                var rows = (source.Rows ?? []).Take(5000)
                    .Select(row => (IReadOnlyList<string>)row.Take(headers.Count)
                        .Select(x => Clean(x, 2000) ?? "").ToList())
                    .ToList();
                tables.Add(new PrintTable
                {
                    Caption = Clean(source.Caption, 160),
                    Columns = headers.Select(x => new PrintColumn(x)).ToList(),
                    Rows = rows
                });
            }

            var content = Clean(request.Content, 8000);
            if (fields.Count == 0 && tables.Count == 0 && content is null)
                return Results.BadRequest("The page has no printable content.");

            var profile = await companies.GetAsync(ct);
            var document = new PrintDocument
            {
                Title = title,
                Reference = Clean(request.Path, 200),
                Date = DateOnly.FromDateTime(DateTime.Today),
                Fields = fields,
                Tables = tables,
                Notes = content
            };
            var bytes = printer.ToPdf(document, ReportEndpoints.ToBranding(profile));
            return Results.File(bytes, "application/pdf", $"{FileName(title)} {DateTime.Today:yyyy-MM-dd}.pdf");
        })
        .RequireAuthorization()
        .DisableAntiforgery(); // Read-only file generation; it changes no server state.
    }

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var clean = value.Replace('\0', ' ').Trim();
        return clean.Length <= max ? clean : clean[..max];
    }

    private static string FileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Where(x => !invalid.Contains(x)).ToArray()).Trim();
    }
}
