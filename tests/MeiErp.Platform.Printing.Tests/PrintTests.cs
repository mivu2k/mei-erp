using MeiErp.Platform.Printing;
using SkiaSharp;
using Xunit;
using ZXing;

namespace MeiErp.Platform.Printing.Tests;

/// <summary>
/// Every layout is actually rendered here, because QuestPDF only reports a
/// layout problem when it draws - a document that overflows its page looks
/// perfectly fine in source and throws the first time somebody presses Print.
///
/// The symbologies are round-tripped through a real decoder rather than
/// checked for "some bytes", since a barcode that scans as the wrong number is
/// worse than one that does not scan at all.
/// </summary>
public class PrintTests
{
    private static readonly Branding Company = new()
    {
        Name = "MEI",
        AddressLine1 = "12 Industrial Estate",
        AddressLine2 = "Lahore",
        Phone = "+92 42 111 2222",
        Email = "office@mei.com.pk",
        TaxNumber = "1234567-8",
        FooterNote = "This document is computer generated."
    };

    private static PrintDocument Invoice(PageSize size = PageSize.A4) => new()
    {
        Title = "Sales invoice",
        Reference = "INV-26-0042",
        Date = new DateOnly(2026, 8, 21),
        Size = size,
        Fields =
        [
            new PrintField("Customer", "A Customer (Pvt) Ltd"),
            new PrintField("Order", "SO-26-0011"),
            new PrintField("Terms", "30 days"),
            new PrintField("Delivered to", "Plot 5, Sundar Industrial Estate")
        ],
        Tables =
        [
            new PrintTable
            {
                Caption = "Items",
                Columns =
                [
                    new PrintColumn("Item", 3f),
                    new PrintColumn("Qty", 1f, AlignRight: true),
                    new PrintColumn("Rate", 1f, AlignRight: true),
                    new PrintColumn("Amount", 1.2f, AlignRight: true)
                ],
                Rows =
                [
                    ["Widget, 12mm", "10", "1,200.00", "12,000.00"],
                    ["Bracket, steel", "4", "3,500.00", "14,000.00"]
                ],
                FooterRow = ["Total", "14", "", "26,000.00"]
            }
        ],
        Totals =
        [
            new PrintField("Subtotal", "26,000.00"),
            new PrintField("Sales tax 18%", "4,680.00"),
            new PrintField("Payable", "30,680.00")
        ],
        Notes = "Goods remain our property until paid for in full.",
        Signatures = ["Prepared by", "Checked by", "Received by"]
    };

    // ---------- rendering ----------

    [Fact]
    public void An_A4_document_renders()
    {
        var pdf = new PrintService().ToPdf(Invoice(), Company);

        Assert.NotEmpty(pdf);

        // Every PDF starts with %PDF. Checking it catches a renderer that
        // returns something plausible-looking but wrong.
        Assert.Equal("%PDF"u8.ToArray(), pdf.Take(4).ToArray());
    }

    [Fact]
    public void An_80mm_roll_renders_without_overflowing()
    {
        // The narrow layouts are where overflow actually happens: the same
        // content that fits A4 comfortably busts a thermal roll.
        var pdf = new PrintService().ToPdf(Invoice(PageSize.Roll80), Company);
        Assert.NotEmpty(pdf);
    }

    [Fact]
    public void A_62mm_label_renders_without_overflowing()
    {
        var label = new PrintDocument
        {
            Title = "Asset label",
            Reference = "AST-26-0007",
            Size = PageSize.Label62,
            Fields = [new PrintField("Item", "Dell Latitude 5540"), new PrintField("Owner", "Accounts")]
        };

        var pdf = new PrintService().ToPdf(label, Company);
        Assert.NotEmpty(pdf);
    }

    [Fact]
    public void A_document_renders_when_the_company_profile_is_empty()
    {
        // A fresh install has no logo, no address and no tax number. It must
        // still print rather than throwing on the first document.
        var pdf = new PrintService().ToPdf(Invoice(), Branding.Empty);
        Assert.NotEmpty(pdf);
    }

    [Fact]
    public void A_document_with_no_reference_renders_without_symbologies()
    {
        var document = Invoice() with { Reference = null };

        // Encoding an empty string throws, so the layout has to skip the
        // barcode entirely rather than draw an empty one.
        var pdf = new PrintService().ToPdf(document, Company);
        Assert.NotEmpty(pdf);
    }

    [Fact]
    public void A_long_table_paginates_rather_than_overflowing()
    {
        var rows = Enumerable.Range(1, 200)
            .Select(i => (IReadOnlyList<string>)
                [$"Line item number {i} with a reasonably long description", $"{i}", "1,000.00", $"{i * 1000:N2}"])
            .ToList();

        var document = Invoice() with
        {
            Tables =
            [
                new PrintTable
                {
                    Columns =
                    [
                        new PrintColumn("Item", 3f),
                        new PrintColumn("Qty", 1f, AlignRight: true),
                        new PrintColumn("Rate", 1f, AlignRight: true),
                        new PrintColumn("Amount", 1.2f, AlignRight: true)
                    ],
                    Rows = rows
                }
            ]
        };

        var pdf = new PrintService().ToPdf(document, Company);
        Assert.NotEmpty(pdf);
    }

    [Fact]
    public void A_watermarked_draft_renders()
    {
        var pdf = new PrintService().ToPdf(Invoice() with { Watermark = "DRAFT" }, Company);
        Assert.NotEmpty(pdf);
    }

    // ---------- symbologies ----------

    [Fact]
    public void A_barcode_scans_back_as_the_number_that_went_in()
    {
        const string reference = "INV-26-0042";

        var png = Symbology.Barcode(reference);
        var decoded = Decode(png);

        // A barcode that scans as the wrong number is worse than one that does
        // not scan at all: it silently opens somebody else's document.
        Assert.Equal(reference, decoded);
    }

    [Fact]
    public void A_qr_code_scans_back_as_the_number_that_went_in()
    {
        const string reference = "JOB-26-0117";

        var png = Symbology.QrCode(reference);
        var decoded = Decode(png);

        // The bench scanner and a phone must land on the same record.
        Assert.Equal(reference, decoded);
    }

    [Fact]
    public void An_empty_value_is_refused_rather_than_encoded()
    {
        Assert.Throws<ArgumentException>(() => Symbology.Barcode(""));
        Assert.Throws<ArgumentException>(() => Symbology.QrCode("   "));
    }

    // ---------- excel ----------

    [Fact]
    public void An_export_is_a_real_workbook()
    {
        var table = new PrintTable
        {
            Caption = "Trial balance",
            Columns =
            [
                new PrintColumn("Code"), new PrintColumn("Account", 3f),
                new PrintColumn("Debit", 1f, AlignRight: true),
                new PrintColumn("Credit", 1f, AlignRight: true)
            ],
            Rows =
            [
                ["1100", "Cash in hand", "50,000.00", ""],
                ["4100", "Sales", "", "50,000.00"]
            ],
            FooterRow = ["", "Total", "50,000.00", "50,000.00"]
        };

        var bytes = new PrintService().ToExcel(table, "Trial balance", Company);

        Assert.NotEmpty(bytes);

        // xlsx is a zip. "PK" is its signature - a CSV renamed to .xlsx would
        // fail here, which is the usual way "Excel export" disappoints.
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);
    }

    [Fact]
    public void A_sheet_name_Excel_would_reject_is_made_safe()
    {
        var table = new PrintTable
        {
            Columns = [new PrintColumn("A")],
            Rows = [["1"]]
        };

        // Excel refuses these characters and anything past 31 characters, and
        // throws rather than truncating.
        var bytes = new PrintService().ToExcel(
            table, "Ledger: 2026/27 [draft] — a very long report name indeed", Company);

        Assert.NotEmpty(bytes);
    }

    /// <summary>
    /// Decodes through ZXing's raw luminance source rather than its SkiaSharp
    /// binding package, so the test needs no dependency the platform does not
    /// already have.
    /// </summary>
    private static string? Decode(byte[] png)
    {
        using var bitmap = SKBitmap.Decode(png);
        using var rgba = bitmap.Copy(SKColorType.Rgba8888);

        var source = new RGBLuminanceSource(
            rgba.Bytes, rgba.Width, rgba.Height, RGBLuminanceSource.BitmapFormat.RGBA32);

        var reader = new BarcodeReaderGeneric { AutoRotate = true };
        reader.Options.TryHarder = true;

        return reader.Decode(source)?.Text;
    }
}
