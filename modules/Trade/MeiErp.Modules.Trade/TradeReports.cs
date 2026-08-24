using MeiErp.Platform.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static MeiErp.Platform.Reporting.ReportRowBuilder;

namespace MeiErp.Modules.Trade;

/// <summary>
/// Reports over the commercial documents.
///
/// The two parts-buying reports moved here with the buying itself: they read
/// purchase lines, which are now Purchase's data rather than the workshop's.
/// </summary>
public static class TradeReportRegistration
{
    public static IServiceCollection AddTradeReports(this IServiceCollection services)
    {
        services.AddScoped(sp => PartCostHistory(sp));
        services.AddScoped(sp => SupplierSpend(sp));
        services.AddScoped(sp => QuotationOutcomes(sp, TradeDirection.Sales));
        services.AddScoped(sp => QuotationOutcomes(sp, TradeDirection.Purchase));
        services.AddScoped(sp => Outstanding(sp, TradeDirection.Sales));
        services.AddScoped(sp => Outstanding(sp, TradeDirection.Purchase));
        return services;
    }

    private static (DateOnly From, DateOnly To) Range(ReportRequest r, IServiceProvider sp)
    {
        var today = sp.GetRequiredService<MeiErp.Platform.Kernel.IClock>().Today;
        return (r.From ?? today.AddMonths(-1), r.To ?? today);
    }

    /// <summary>
    /// What was quoted and what came of it. Came over from Repair with the
    /// quotations themselves, and now covers both sides of the business.
    /// </summary>
    private static ReportDefinition QuotationOutcomes(IServiceProvider sp, TradeDirection direction)
    {
        var sales = direction == TradeDirection.Sales;

        return new ReportDefinition
        {
            Key = sales ? "sales.quotation-outcomes" : "purchase.quotation-outcomes",
            Name = "Quotation outcomes",
            Description = sales
                ? "What was quoted to customers, and whether it was won."
                : "What suppliers quoted, and whether it was taken up.",
            ModuleKey = sales ? SalesModule.Key : PurchaseModule.Key,
            Group = "Quotations",
            Permission = sales ? SalesModule.QuotationsView : PurchaseModule.QuotationsView,
            Uses = ReportFilters.DateRange | ReportFilters.Status,
            SortOrder = 3,
            Run = async (r, ct) =>
            {
                var (from, to) = Range(r, sp);

                var q = sp.GetRequiredService<TradeDbContext>().Quotations
                    .AsNoTracking().Include(x => x.Lines)
                    .Where(x => x.Direction == direction && x.Date >= from && x.Date <= to);

                if (Enum.TryParse<DocumentStatus>(r.Status, true, out var status))
                    q = q.Where(x => x.Status == status);

                var rows = await q.OrderByDescending(x => x.Date).ToListAsync(ct);

                return new ReportResult
                {
                    Columns =
                    [
                        new("date", "Date", ReportValueKind.Date, 1),
                        new("number", "Quotation", ReportValueKind.Text, 1.2f),
                        new("party", sales ? "Customer" : "Supplier", ReportValueKind.Text, 2),
                        new("status", "Status", ReportValueKind.Status, 1),
                        new("value", "Value", ReportValueKind.Money, 1.2f)
                    ],
                    Rows =
                    [
                        .. rows.Select(x => Row(
                            $"/{(sales ? "sales" : "purchase")}/quotations/{x.Id}",
                            ("date", x.Date), ("number", x.Number), ("party", x.PartyName),
                            ("status", x.Status.ToString()), ("value", x.Total)))
                    ],
                    Totals = [new("value", rows.Sum(x => x.Total))]
                };
            }
        };
    }

    /// <summary>
    /// Money still outstanding. Receivables on the sales side, payables on the
    /// purchase side - the same query read from opposite ends.
    /// </summary>
    private static ReportDefinition Outstanding(IServiceProvider sp, TradeDirection direction)
    {
        var sales = direction == TradeDirection.Sales;

        return new ReportDefinition
        {
            Key = sales ? "sales.receivables" : "purchase.payables",
            Name = sales ? "Receivables" : "Payables",
            Description = sales
                ? "Posted invoices customers have not settled."
                : "Posted invoices the business has not settled.",
            ModuleKey = sales ? SalesModule.Key : PurchaseModule.Key,
            Group = "Invoices",
            Permission = sales ? SalesModule.InvoicesView : PurchaseModule.InvoicesView,
            Uses = ReportFilters.None,
            SortOrder = 4,
            Run = async (_, ct) =>
            {
                var today = sp.GetRequiredService<MeiErp.Platform.Kernel.IClock>().Today;

                // Only posted invoices owe anything: a draft is a working note.
                var rows = await sp.GetRequiredService<TradeDbContext>().Invoices
                    .AsNoTracking().Include(x => x.Lines)
                    .Where(x => x.Direction == direction && x.Status == DocumentStatus.Posted)
                    .OrderBy(x => x.DueDate)
                    .ToListAsync(ct);

                var open = rows.Where(x => !x.IsSettled).ToList();

                return new ReportResult
                {
                    Columns =
                    [
                        new("number", "Invoice", ReportValueKind.Text, 1.2f),
                        new("party", sales ? "Customer" : "Supplier", ReportValueKind.Text, 2),
                        new("date", "Date", ReportValueKind.Date, 1),
                        new("due", "Due", ReportValueKind.Date, 1),
                        new("overdue", "Overdue", ReportValueKind.Status, 1),
                        new("total", "Total", ReportValueKind.Money, 1),
                        new("settled", "Settled", ReportValueKind.Money, 1),
                        new("balance", "Balance", ReportValueKind.Money, 1)
                    ],
                    Rows =
                    [
                        .. open.Select(x => Row(
                            $"/{(sales ? "sales" : "purchase")}/invoices/{x.Id}",
                            ("number", x.Number), ("party", x.PartyName), ("date", x.Date),
                            ("due", x.DueDate),
                            ("overdue", x.IsOverdueOn(today) ? "Yes" : "No"),
                            ("total", x.Total), ("settled", x.AmountSettled), ("balance", x.Balance)))
                    ],
                    Totals =
                    [
                        new("total", open.Sum(x => x.Total)),
                        new("settled", open.Sum(x => x.AmountSettled)),
                        new("balance", open.Sum(x => x.Balance))
                    ]
                };
            }
        };
    }

    private static ReportDefinition PartCostHistory(IServiceProvider sp) => new()
    {
        Key = "purchase.part-cost-history",
        Name = "Part cost history",
        Description = "Purchase quantities, suppliers and cost movements.",
        ModuleKey = PurchaseModule.Key,
        Group = "Workshop parts",
        Permission = PurchaseModule.CostsView,
        Uses = ReportFilters.DateRange,
        SortOrder = 1,
        Run = async (r, ct) =>
        {
            var (from, to) = Range(r, sp);

            var rows = await sp.GetRequiredService<TradeDbContext>().PartPurchaseLines
                .AsNoTracking()
                .Include(x => x.Part)
                .Include(x => x.Purchase)
                .Where(x => x.Purchase!.PurchasedOn >= from && x.Purchase.PurchasedOn <= to)
                .OrderByDescending(x => x.Purchase!.PurchasedOn)
                .ToListAsync(ct);

            return new ReportResult
            {
                Columns =
                [
                    new("date", "Date", ReportValueKind.Date, 1),
                    new("part", "Part", ReportValueKind.Text, 2),
                    new("supplier", "Supplier", ReportValueKind.Text, 2),
                    new("qty", "Qty", ReportValueKind.Number, 1),
                    new("cost", "Unit cost", ReportValueKind.Money, 1),
                    new("value", "Value", ReportValueKind.Money, 1)
                ],
                Rows =
                [
                    .. rows.Select(x => Row(null,
                        ("date", x.Purchase!.PurchasedOn),
                        ("part", x.Part!.Name),
                        ("supplier", x.Purchase.PartyName),
                        ("qty", x.Quantity),
                        ("cost", x.UnitCost),
                        ("value", x.LineTotal)))
                ],
                Totals =
                [
                    new("qty", rows.Sum(x => x.Quantity)),
                    new("value", rows.Sum(x => x.LineTotal))
                ]
            };
        }
    };

    private static ReportDefinition SupplierSpend(IServiceProvider sp) => new()
    {
        Key = "purchase.supplier-spend",
        Name = "Supplier spend",
        Description = "Workshop parts purchasing by supplier.",
        ModuleKey = PurchaseModule.Key,
        Group = "Workshop parts",
        Permission = PurchaseModule.CostsView,
        Uses = ReportFilters.DateRange,
        SortOrder = 2,
        Run = async (r, ct) =>
        {
            var (from, to) = Range(r, sp);

            var rows = await sp.GetRequiredService<TradeDbContext>().PartPurchases
                .AsNoTracking()
                .Include(x => x.Lines)
                .Where(x => x.PurchasedOn >= from && x.PurchasedOn <= to)
                .ToListAsync(ct);

            var groups = rows
                .GroupBy(x => new { x.PartyId, Name = x.PartyName })
                .Select(x => new { x.Key.Name, Receipts = x.Count(), Value = x.Sum(y => y.Total) })
                .OrderByDescending(x => x.Value)
                .ToList();

            return new ReportResult
            {
                Columns =
                [
                    new("supplier", "Supplier", ReportValueKind.Text, 3),
                    new("receipts", "Receipts", ReportValueKind.Number, 1),
                    new("spend", "Spend", ReportValueKind.Money, 1.5f)
                ],
                Rows =
                [
                    .. groups.Select(x => Row(null,
                        ("supplier", x.Name),
                        ("receipts", x.Receipts),
                        ("spend", x.Value)))
                ],
                Totals =
                [
                    new("receipts", groups.Sum(x => x.Receipts)),
                    new("spend", groups.Sum(x => x.Value))
                ]
            };
        }
    };
}
