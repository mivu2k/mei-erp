using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MeiErp.Platform.Printing;

public interface IPrintService
{
    /// <summary>Renders a described document to PDF bytes.</summary>
    byte[] ToPdf(PrintDocument document, Branding branding);

    /// <summary>Renders a table to a real Excel workbook, not a CSV with the wrong extension.</summary>
    byte[] ToExcel(PrintTable table, string sheetName, Branding branding);
}

public sealed class PrintService : IPrintService
{
    static PrintService()
    {
        // QuestPDF's community licence, set once. Without it the first render
        // throws, which on a server means the first person to press Print.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private const string Ink = "#16202B";
    private const string Muted = "#5B6975";
    private const string Rule = "#DDE3E9";
    private const string Accent = "#1D4E7C";

    public byte[] ToPdf(PrintDocument document, Branding branding) =>
        Document.Create(container =>
        {
            container.Page(page =>
            {
                ConfigurePage(page, document.Size);

                page.Header().Element(h => Header(h, document, branding));
                page.Content().Element(c => Content(c, document));
                page.Footer().Element(f => Footer(f, branding, document.Size));
            });
        }).GeneratePdf();

    private static void ConfigurePage(PageDescriptor page, PageSize size)
    {
        switch (size)
        {
            case PageSize.Roll80:
                // A thermal roll has no fixed length: the page grows with the
                // content rather than paginating.
                page.ContinuousSize(80, Unit.Millimetre);
                page.Margin(4, Unit.Millimetre);
                page.DefaultTextStyle(t => t.FontSize(8).FontColor(Ink));
                break;

            case PageSize.Label62:
                page.ContinuousSize(62, Unit.Millimetre);
                page.Margin(2, Unit.Millimetre);
                page.DefaultTextStyle(t => t.FontSize(7).FontColor(Ink));
                break;

            default:
                page.Size(PageSizes.A4);
                page.Margin(14, Unit.Millimetre);
                page.DefaultTextStyle(t => t.FontSize(9).FontColor(Ink));
                break;
        }
    }

    private static void Header(IContainer container, PrintDocument document, Branding branding)
    {
        var narrow = document.Size is not PageSize.A4;

        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                if (branding.Logo is { Length: > 0 })
                {
                    row.ConstantItem(narrow ? 40 : 90)
                       .AlignMiddle()
                       .Image(branding.Logo).FitArea();
                    row.ConstantItem(8);
                }

                row.RelativeItem().Column(c =>
                {
                    if (!string.IsNullOrWhiteSpace(branding.Name))
                        c.Item().Text(branding.Name).FontSize(narrow ? 11 : 15).Bold();

                    foreach (var line in new[]
                             {
                                 branding.AddressLine1, branding.AddressLine2,
                                 Join(branding.Phone, branding.Email),
                                 branding.TaxNumber is null ? null : $"NTN {branding.TaxNumber}"
                             }.Where(l => !string.IsNullOrWhiteSpace(l)))
                    {
                        c.Item().Text(line).FontSize(narrow ? 6.5f : 8).FontColor(Muted);
                    }
                });

                // The symbologies only appear where there is room. A 62mm label
                // cannot hold bars and a QR side by side without one of them
                // overflowing the page, which QuestPDF reports as a layout
                // exception at render time.
                if (!narrow && !string.IsNullOrWhiteSpace(document.Reference))
                {
                    row.ConstantItem(150).AlignRight().Column(c =>
                    {
                        c.Item().Height(38).Image(Symbology.Barcode(document.Reference, 300, 70))
                                .FitArea();
                        c.Item().PaddingTop(1).AlignRight()
                                .Text(document.Reference).FontSize(7.5f).FontColor(Muted);
                    });

                    row.ConstantItem(6);
                    row.ConstantItem(44).AlignRight().AlignTop()
                       .Height(44).Image(Symbology.QrCode(document.Reference, 160)).FitArea();
                }
            });

            column.Item().PaddingTop(narrow ? 4 : 10)
                  .BorderBottom(narrow ? 0.5f : 1).BorderColor(Ink)
                  .PaddingBottom(3)
                  .Row(row =>
                  {
                      row.RelativeItem().Text(document.Title)
                         .FontSize(narrow ? 9 : 13).Bold().FontColor(Accent);

                      if (document.Date is not null)
                      {
                          row.ConstantItem(narrow ? 60 : 130).AlignRight()
                             .Text(document.Date.Value.ToString("d MMMM yyyy"))
                             .FontSize(narrow ? 7 : 9).FontColor(Muted);
                      }
                  });

            // On a narrow page the reference has nowhere else to go.
            if (narrow && !string.IsNullOrWhiteSpace(document.Reference))
            {
                column.Item().PaddingTop(2).AlignCenter()
                      .Height(26).Image(Symbology.Barcode(document.Reference, 300, 60)).FitArea();
                column.Item().AlignCenter().Text(document.Reference).FontSize(7).Bold();
            }
        });
    }

    /// <summary>Joins the parts that are actually present, so a missing phone leaves no stray separator.</summary>
    private static string? Join(params string?[] parts)
    {
        var present = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        return present.Length == 0 ? null : string.Join("  ·  ", present);
    }

    private static void Content(IContainer container, PrintDocument document)
    {
        var narrow = document.Size is not PageSize.A4;

        var body = container.PaddingTop(narrow ? 4 : 10);

        if (!string.IsNullOrWhiteSpace(document.Watermark))
        {
            // Drawn behind the content so a draft cannot be mistaken for the
            // real thing, without making the figures unreadable.
            body.Layers(layers =>
            {
                layers.Layer().AlignCenter().AlignMiddle()
                      .Text(document.Watermark!)
                      .FontSize(58).Bold().FontColor("#EFEFEF");

                layers.PrimaryLayer().Element(c => Body(c, document, narrow));
            });

            return;
        }

        body.Element(c => Body(c, document, narrow));
    }

    private static void Body(IContainer container, PrintDocument document, bool narrow)
    {
        container.Column(column =>
        {
            column.Spacing(narrow ? 4 : 10);

            if (document.Fields.Count > 0)
                column.Item().Element(c => Fields(c, document.Fields, narrow));

            foreach (var table in document.Tables)
                column.Item().Element(c => Table(c, table, narrow));

            if (document.Totals.Count > 0)
            {
                column.Item().AlignRight().Width(narrow ? 200 : 240).Column(totals =>
                {
                    foreach (var total in document.Totals)
                    {
                        totals.Item().BorderTop(0.5f).BorderColor(Rule).PaddingVertical(2).Row(row =>
                        {
                            row.RelativeItem().Text(total.Label).FontSize(narrow ? 7.5f : 9);
                            row.ConstantItem(narrow ? 80 : 100).AlignRight()
                               .Text(total.Value ?? "").FontSize(narrow ? 7.5f : 9).Bold();
                        });
                    }
                });
            }

            if (!string.IsNullOrWhiteSpace(document.Notes))
            {
                column.Item().PaddingTop(4).Text(document.Notes)
                      .FontSize(narrow ? 7 : 8.5f).FontColor(Muted);
            }

            if (document.Signatures.Count > 0 && !narrow)
            {
                column.Item().PaddingTop(34).Row(row =>
                {
                    foreach (var signature in document.Signatures)
                    {
                        row.RelativeItem().PaddingRight(18).Column(c =>
                        {
                            c.Item().BorderTop(0.5f).BorderColor(Ink).PaddingTop(3)
                                    .Text(signature).FontSize(8).FontColor(Muted);
                        });
                    }
                });
            }
        });
    }

    private static void Fields(IContainer container, IReadOnlyList<PrintField> fields, bool narrow)
    {
        // Two columns on A4, one on a roll - a 80mm page has no room to pair them.
        var perColumn = narrow ? fields.Count : (int)Math.Ceiling(fields.Count / 2.0);

        container.Row(row =>
        {
            for (var start = 0; start < fields.Count; start += perColumn)
            {
                var slice = fields.Skip(start).Take(perColumn).ToList();

                row.RelativeItem().PaddingRight(10).Column(column =>
                {
                    foreach (var field in slice)
                    {
                        column.Item().PaddingVertical(1).Row(line =>
                        {
                            line.ConstantItem(narrow ? 70 : 110)
                                .Text(field.Label).FontSize(narrow ? 7 : 8.5f).FontColor(Muted);

                            line.RelativeItem()
                                .Text(field.Value ?? "—").FontSize(narrow ? 7 : 8.5f);
                        });
                    }
                });
            }
        });
    }

    private static void Table(IContainer container, PrintTable table, bool narrow)
    {
        container.Column(column =>
        {
            if (!string.IsNullOrWhiteSpace(table.Caption))
            {
                column.Item().PaddingBottom(3)
                      .Text(table.Caption).FontSize(narrow ? 8 : 10).Bold();
            }

            column.Item().Table(grid =>
            {
                grid.ColumnsDefinition(columns =>
                {
                    foreach (var col in table.Columns)
                        columns.RelativeColumn(col.Width);
                });

                // Repeated on every page, so a long schedule stays readable
                // after the first sheet.
                grid.Header(header =>
                {
                    foreach (var col in table.Columns)
                    {
                        // The whole chain has to be built before Text() is
                        // called: assigning alignment to a container that
                        // already holds text gives it two children, which
                        // QuestPDF refuses outright.
                        var cell = header.Cell()
                            .BorderBottom(1).BorderColor(Ink)
                            .PaddingVertical(3).PaddingHorizontal(2);

                        if (col.AlignRight) cell = cell.AlignRight();

                        cell.Text(col.Header)
                            .FontSize(narrow ? 6.5f : 8).Bold().FontColor(Muted);
                    }
                });

                foreach (var row in table.Rows)
                {
                    for (var i = 0; i < table.Columns.Count; i++)
                    {
                        var value = i < row.Count ? row[i] : "";
                        var alignRight = table.Columns[i].AlignRight;

                        var cell = grid.Cell()
                            .BorderBottom(0.5f).BorderColor(Rule)
                            .PaddingVertical(2.5f).PaddingHorizontal(2);

                        if (alignRight) cell = cell.AlignRight();

                        cell.Text(value).FontSize(narrow ? 6.5f : 8.5f);
                    }
                }

                if (table.FooterRow is not null)
                {
                    for (var i = 0; i < table.Columns.Count; i++)
                    {
                        var value = i < table.FooterRow.Count ? table.FooterRow[i] : "";
                        var alignRight = table.Columns[i].AlignRight;

                        var cell = grid.Cell()
                            .BorderTop(1).BorderColor(Ink)
                            .PaddingVertical(3).PaddingHorizontal(2);

                        if (alignRight) cell = cell.AlignRight();

                        cell.Text(value).FontSize(narrow ? 7 : 8.5f).Bold();
                    }
                }
            });
        });
    }

    private static void Footer(IContainer container, Branding branding, PageSize size)
    {
        if (size is not PageSize.A4)
        {
            if (!string.IsNullOrWhiteSpace(branding.FooterNote))
            {
                container.PaddingTop(3).AlignCenter()
                         .Text(branding.FooterNote).FontSize(6).FontColor(Muted);
            }
            return;
        }

        container.PaddingTop(6).BorderTop(0.5f).BorderColor(Rule).PaddingTop(3).Row(row =>
        {
            row.RelativeItem().Text(branding.FooterNote ?? "")
               .FontSize(7).FontColor(Muted);

            // Stamped so a printout found on a desk can be dated, and so a
            // reader knows whether they are holding the current version.
            row.ConstantItem(220).AlignRight().Text(text =>
            {
                text.DefaultTextStyle(t => t.FontSize(7).FontColor(Muted));
                text.Span($"Printed {DateTime.Now:d MMM yyyy HH:mm}  ·  Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });
        });
    }

    public byte[] ToExcel(PrintTable table, string sheetName, Branding branding)
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook();

        // Excel refuses some characters and anything past 31 in a sheet name,
        // and throws rather than truncating.
        var safeName = new string(sheetName.Where(c => !"[]:*?/\\".Contains(c)).ToArray());
        if (safeName.Length > 31) safeName = safeName[..31];
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "Report";

        var sheet = workbook.Worksheets.Add(safeName);
        var rowIndex = 1;

        if (!string.IsNullOrWhiteSpace(branding.Name))
        {
            sheet.Cell(rowIndex, 1).Value = branding.Name;
            sheet.Cell(rowIndex, 1).Style.Font.Bold = true;
            sheet.Cell(rowIndex, 1).Style.Font.FontSize = 13;
            rowIndex++;
        }

        if (!string.IsNullOrWhiteSpace(table.Caption))
        {
            sheet.Cell(rowIndex, 1).Value = table.Caption;
            sheet.Cell(rowIndex, 1).Style.Font.Bold = true;
            rowIndex++;
        }

        sheet.Cell(rowIndex, 1).Value = $"Exported {DateTime.Now:d MMM yyyy HH:mm}";
        sheet.Cell(rowIndex, 1).Style.Font.FontColor = ClosedXML.Excel.XLColor.Gray;
        rowIndex += 2;

        var headerRow = rowIndex;

        for (var c = 0; c < table.Columns.Count; c++)
        {
            var cell = sheet.Cell(headerRow, c + 1);
            cell.Value = table.Columns[c].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#E8ECF1");
            cell.Style.Border.BottomBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
        }

        rowIndex++;

        foreach (var row in table.Rows)
        {
            for (var c = 0; c < table.Columns.Count; c++)
            {
                var value = c < row.Count ? row[c] : "";
                var cell = sheet.Cell(rowIndex, c + 1);

                // Numbers go in as numbers, so the recipient can sum a column
                // instead of retyping it. A dump of strings is the single most
                // common complaint about exported reports.
                if (table.Columns[c].AlignRight
                    && decimal.TryParse(value.Replace(",", ""), out var number))
                {
                    cell.Value = number;
                    cell.Style.NumberFormat.Format = "#,##0.00";
                }
                else
                {
                    cell.Value = value;
                }
            }

            rowIndex++;
        }

        if (table.FooterRow is not null)
        {
            for (var c = 0; c < table.Columns.Count; c++)
            {
                var value = c < table.FooterRow.Count ? table.FooterRow[c] : "";
                var cell = sheet.Cell(rowIndex, c + 1);

                if (table.Columns[c].AlignRight
                    && decimal.TryParse(value.Replace(",", ""), out var number))
                {
                    cell.Value = number;
                    cell.Style.NumberFormat.Format = "#,##0.00";
                }
                else
                {
                    cell.Value = value;
                }

                cell.Style.Font.Bold = true;
                cell.Style.Border.TopBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
            }
        }

        // Frozen headers and a filter row, so a long export is usable rather
        // than merely delivered.
        sheet.SheetView.FreezeRows(headerRow);
        sheet.Range(headerRow, 1, Math.Max(headerRow, rowIndex - 1), table.Columns.Count)
             .SetAutoFilter();
        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
