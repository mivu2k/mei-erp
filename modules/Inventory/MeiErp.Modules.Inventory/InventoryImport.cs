using ClosedXML.Excel;
using MeiErp.Platform.Kernel;
using Microsoft.VisualBasic.FileIO;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Inventory;

public sealed record InventoryImportResult(int Imported, int OpeningBalances, IReadOnlyList<string> Errors);

public interface IInventoryImportService
{
    Task<InventoryImportResult> ImportAsync(Stream file, string fileName, int domainId, CancellationToken ct = default);
}

public sealed class InventoryImportService(
    ICatalogService catalog, IStockService stock, IStockTrackingService tracking,
    InventoryDbContext db, IClock clock)
    : IInventoryImportService
{
    private static readonly string[] Required = ["code", "name"];

    public async Task<InventoryImportResult> ImportAsync(
        Stream file, string fileName, int domainId, CancellationToken ct = default)
    {
        var rows = fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? ReadExcel(file)
            : ReadCsv(file);
        if (rows.Count == 0) return new(0, 0, ["The file has no data rows."]);
        var missing = Required.Where(x => !rows[0].ContainsKey(x)).ToArray();
        if (missing.Length > 0) return new(0, 0, [$"Missing required column(s): {string.Join(", ", missing)}."]);

        var imported = 0; var openings = 0; var errors = new List<string>();
        for (var index = 0; index < rows.Count; index++)
        {
            db.ChangeTracker.Clear();
            var r = rows[index]; var line = index + 2;
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                var code = Get(r, "code").Trim(); var name = Get(r, "name").Trim();
                if (code.Length == 0 || name.Length == 0) { errors.Add($"Row {line}: code and name are required."); continue; }
                var quantity = Decimal(r, "opening_quantity"); var cost = Decimal(r, "opening_unit_cost");
                var serialized = Bool(r, "serialized"); var batchTracked = Bool(r, "batch_tracked");
                var serials = Get(r, "serial_numbers").Split([';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (quantity < 0 || cost < 0) { errors.Add($"Row {line}: opening quantity and cost cannot be negative."); continue; }

                var saved = await catalog.SaveItemAsync(new Item
                {
                    DomainId = domainId, Code = code, Name = name,
                    Unit = Get(r, "unit", "each"), Barcode = Null(r, "barcode"),
                    SellingPrice = Decimal(r, "selling_price"), ReorderLevel = Decimal(r, "reorder_level"),
                    ReorderQuantity = Decimal(r, "reorder_quantity"), IsSerialized = serialized,
                    IsBatchTracked = batchTracked, IsActive = true
                }, ct);
                if (saved.Failed) { errors.Add($"Row {line}: {saved.Error}"); continue; }
                if (quantity <= 0) { await transaction.CommitAsync(ct); imported++; continue; }

                var tracked = await tracking.StageReceiptAsync(new(
                    saved.Value.Id, quantity, cost, clock.Today, Null(r, "batch_number"),
                    Date(r, "expiry_date"), serials, $"IMPORT-{fileName}"), ct);
                if (tracked.Failed) { errors.Add($"Row {line}: item created, opening stock failed — {tracked.Error}"); continue; }
                var received = await stock.ReceiveAsync(saved.Value.Id, quantity, cost, clock.Today,
                    StockMovementType.Opening, $"IMPORT-{fileName}", "Inventory import", null, ct);
                if (received.Failed) { errors.Add($"Row {line}: item created, opening stock failed — {received.Error}"); continue; }
                await transaction.CommitAsync(ct);
                imported++; openings++;
            }
            catch (Exception ex) { errors.Add($"Row {line}: {ex.Message}"); }
        }
        return new(imported, openings, errors);
    }

    private static List<Dictionary<string,string>> ReadExcel(Stream stream)
    {
        using var book = new XLWorkbook(stream); var sheet = book.Worksheets.First();
        var used = sheet.RangeUsed(); if (used is null) return [];
        var headers = used.FirstRow().Cells().Select(x => Key(x.GetString())).ToArray();
        return used.RowsUsed().Skip(1).Select(row => headers.Select((h,i) => (h, v: row.Cell(i+1).GetFormattedString()))
            .ToDictionary(x => x.h, x => x.v, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    private static List<Dictionary<string,string>> ReadCsv(Stream stream)
    {
        using var parser = new TextFieldParser(stream) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true };
        parser.SetDelimiters(","); var raw = new List<string[]>();
        while (!parser.EndOfData) raw.Add(parser.ReadFields() ?? []);
        if (raw.Count == 0) return [];
        var headers = raw[0].Select(Key).ToArray();
        return raw.Skip(1).Where(x => x.Any(v => !string.IsNullOrWhiteSpace(v))).Select(values => headers.Select((h,i) => (h, v: i < values.Length ? values[i] : ""))
            .ToDictionary(x => x.h, x => x.v, StringComparer.OrdinalIgnoreCase)).ToList();
    }
    private static string Key(string value) => value.Trim().ToLowerInvariant().Replace(' ', '_');
    private static string Get(Dictionary<string,string> r,string k,string d="") => r.GetValueOrDefault(k,d);
    private static string? Null(Dictionary<string,string> r,string k) => string.IsNullOrWhiteSpace(Get(r,k)) ? null : Get(r,k).Trim();
    private static decimal Decimal(Dictionary<string,string> r,string k) => decimal.TryParse(Get(r,k),out var v)?v:0;
    private static bool Bool(Dictionary<string,string> r,string k) => Get(r,k).Trim().ToLowerInvariant() is "true" or "yes" or "1";
    private static DateOnly? Date(Dictionary<string,string> r,string k) => DateOnly.TryParse(Get(r,k),out var v)?v:null;
}
