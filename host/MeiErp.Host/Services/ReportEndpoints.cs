using MeiErp.Platform.Identity;
using MeiErp.Platform.Kernel;
using MeiErp.Platform.Printing;
using MeiErp.Platform.Reporting;

namespace MeiErp.Host.Services;

/// <summary>
/// Downloads a report as PDF or Excel.
///
/// An endpoint rather than a Blazor component because a file download needs a
/// real HTTP response with its own content type - something an interactive
/// circuit cannot produce.
/// </summary>
public static class ReportEndpoints
{
    public static void MapReportEndpoints(this WebApplication app)
    {
        app.MapGet("/reports/export/{key}", async (
            string key,
            HttpContext http,
            IReportCatalog catalog,
            IPrintService printer,
            ICurrentUser user,
            ICompanyProfileService company,
            CancellationToken ct) =>
        {
            var report = catalog.Find(key);
            if (report is null) return Results.NotFound();

            // The same permission the hub uses to decide whether to show it.
            // Without this check, anyone who guesses a report key downloads it.
            if (!user.Can(report.Permission)) return Results.Forbid();

            var request = ReadRequest(http.Request.Query);
            var result = await report.Run(request, ct);

            var profile = await company.GetAsync(ct);
            var branding = ToBranding(profile);

            var format = http.Request.Query["format"].ToString().ToLowerInvariant();
            var stamp = DateTime.Now.ToString("yyyy-MM-dd");
            var safeName = string.Concat(report.Name.Split(Path.GetInvalidFileNameChars()));

            if (format is "xlsx" or "excel")
            {
                var table = ReportRenderer.ToTable(result, report.Name);
                var bytes = printer.ToExcel(table, report.Name, branding);

                return Results.File(bytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"{safeName} {stamp}.xlsx");
            }

            var document = ReportRenderer.ToDocument(report, result, request);
            return Results.File(printer.ToPdf(document, branding),
                "application/pdf", $"{safeName} {stamp}.pdf");
        })
        .RequireAuthorization();
    }

    private static ReportRequest ReadRequest(IQueryCollection query) => new()
    {
        From = ParseDate(query["from"]),
        To = ParseDate(query["to"]),
        AsAt = ParseDate(query["asAt"]),
        Status = Trim(query["status"]),
        Search = Trim(query["search"]),
        PersonId = Trim(query["personId"]),
        DepartmentId = Trim(query["departmentId"]),
        PartyId = ParseInt(query["partyId"]),
        ProjectId = ParseInt(query["projectId"]),
        AccountId = ParseInt(query["accountId"]),
        ItemId = ParseInt(query["itemId"])
    };

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParse(value, out var date) ? date : null;

    private static int? ParseInt(string? value) =>
        int.TryParse(value, out var number) ? number : null;

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Flattens the stored profile into the dependency-free shape print code reads.</summary>
    public static Branding ToBranding(CompanyProfile profile) => new()
    {
        Name = profile.Name,
        AddressLine1 = profile.AddressLine1,
        AddressLine2 = string.Join(", ",
            new[] { profile.AddressLine2, profile.City, profile.Country }
                .Where(p => !string.IsNullOrWhiteSpace(p))),
        Phone = profile.Phone,
        Email = profile.Email,
        Website = profile.Website,
        TaxNumber = profile.TaxNumber,
        FooterNote = profile.FooterNote,
        Logo = profile.Logo,
        Currency = profile.Currency
    };
}
