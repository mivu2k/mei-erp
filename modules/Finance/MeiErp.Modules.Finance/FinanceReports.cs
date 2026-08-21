using MeiErp.Platform.Printing;
using MeiErp.Platform.Reporting;
using Microsoft.Extensions.DependencyInjection;
using static MeiErp.Platform.Reporting.ReportRowBuilder;

namespace MeiErp.Modules.Finance;

/// <summary>
/// Finance's reports, registered into the shared catalog.
///
/// Each one only says how to fetch and shape its rows. The screen, the Excel
/// file and the PDF are all rendered from that one shape, so a figure cannot
/// differ between what somebody reads and what they forward.
/// </summary>
public static class FinanceReportRegistration
{
    public static IServiceCollection AddFinanceReports(this IServiceCollection services)
    {
        services.AddScoped(sp => TrialBalance(sp));
        services.AddScoped(sp => IncomeStatement(sp));
        services.AddScoped(sp => BalanceSheet(sp));
        services.AddScoped(sp => VoucherRegister(sp));
        services.AddScoped(sp => PaymentRequests(sp));
        services.AddScoped(sp => ExpenseByHead(sp));

        return services;
    }

    private static ReportDefinition TrialBalance(IServiceProvider sp) => new()
    {
        Key = "finance.trial-balance",
        ModuleKey = FinanceModule.Key,
        Group = "Statements",
        Name = "Trial balance",
        Description = "Every account's net position, as at a date.",
        Permission = FinanceModule.ReportsView,
        Uses = ReportFilters.AsAtDate,
        SortOrder = 1,

        Run = async (request, ct) =>
        {
            var reports = sp.GetRequiredService<IFinanceReports>();
            var asAt = request.AsAt ?? DateOnly.FromDateTime(DateTime.Today);
            var trial = await reports.TrialBalanceAsync(asAt, ct);

            return new ReportResult
            {
                Columns =
                [
                    new ReportColumn("code", "Code", ReportValueKind.Text, 0.8f),
                    new ReportColumn("name", "Account", ReportValueKind.Text, 3f),
                    new ReportColumn("type", "Type", ReportValueKind.Text, 1f),
                    new ReportColumn("debit", "Debit", ReportValueKind.Money, 1.2f),
                    new ReportColumn("credit", "Credit", ReportValueKind.Money, 1.2f)
                ],

                Rows = [.. trial.Rows.Select(r => Row(null,
                    ("code", r.Code), ("name", r.Name), ("type", r.Type.ToString()),
                    ("debit", r.Debit == 0 ? null : r.Debit),
                    ("credit", r.Credit == 0 ? null : r.Credit)))],

                Totals =
                [
                    new ReportTotal("debit", trial.TotalDebit),
                    new ReportTotal("credit", trial.TotalCredit)
                ],

                Header =
                [
                    new PrintField("As at", asAt.ToString("d MMMM yyyy")),

                    // Stated on the report itself. If it is ever "No", something
                    // reached the ledger without a balanced entry and the reader
                    // needs to know before they act on the figures.
                    new PrintField("Balanced", trial.IsBalanced ? "Yes" : "NO — investigate")
                ],

                EmptyMessage = "Nothing has been posted on or before this date."
            };
        }
    };

    private static ReportDefinition IncomeStatement(IServiceProvider sp) => new()
    {
        Key = "finance.income-statement",
        ModuleKey = FinanceModule.Key,
        Group = "Statements",
        Name = "Income statement",
        Description = "Income and expenses over a period, and what was left.",
        Permission = FinanceModule.ReportsView,
        Uses = ReportFilters.DateRange,
        SortOrder = 2,

        Run = async (request, ct) =>
        {
            var reports = sp.GetRequiredService<IFinanceReports>();
            var from = request.From ?? DateOnly.MinValue;
            var to = request.To ?? DateOnly.FromDateTime(DateTime.Today);

            var statement = await reports.IncomeStatementAsync(from, to, ct);

            var rows = statement.Income
                .Select(l => Row(null, ("section", "Income"), ("code", l.Code),
                                 ("name", l.Name), ("amount", l.Amount)))
                .Concat(statement.Expenses
                    .Select(l => Row(null, ("section", "Expenses"), ("code", l.Code),
                                     ("name", l.Name), ("amount", l.Amount))))
                .ToList();

            return new ReportResult
            {
                Columns =
                [
                    new ReportColumn("section", "Section", ReportValueKind.Text, 1f),
                    new ReportColumn("code", "Code", ReportValueKind.Text, 0.8f),
                    new ReportColumn("name", "Account", ReportValueKind.Text, 3f),
                    new ReportColumn("amount", "Amount", ReportValueKind.Money, 1.2f)
                ],

                Rows = rows,

                Header =
                [
                    new PrintField("Income", statement.TotalIncome.ToString("N2")),
                    new PrintField("Expenses", statement.TotalExpenses.ToString("N2")),
                    new PrintField(
                        statement.NetProfit >= 0 ? "Net profit" : "Net loss",
                        Math.Abs(statement.NetProfit).ToString("N2"))
                ],

                EmptyMessage = "No income or expenses were posted in this period."
            };
        }
    };

    private static ReportDefinition BalanceSheet(IServiceProvider sp) => new()
    {
        Key = "finance.balance-sheet",
        ModuleKey = FinanceModule.Key,
        Group = "Statements",
        Name = "Balance sheet",
        Description = "What the business owns and owes, as at a date.",
        Permission = FinanceModule.ReportsView,
        Uses = ReportFilters.AsAtDate,
        SortOrder = 3,

        Run = async (request, ct) =>
        {
            var reports = sp.GetRequiredService<IFinanceReports>();
            var asAt = request.AsAt ?? DateOnly.FromDateTime(DateTime.Today);
            var sheet = await reports.BalanceSheetAsync(asAt, ct);

            var rows = sheet.Assets
                .Select(l => Row(null, ("section", "Assets"), ("name", l.Name), ("amount", l.Amount)))
                .Concat(sheet.Liabilities
                    .Select(l => Row(null, ("section", "Liabilities"), ("name", l.Name), ("amount", l.Amount))))
                .Concat(sheet.Equity
                    .Select(l => Row(null, ("section", "Equity"), ("name", l.Name), ("amount", l.Amount))))
                .ToList();

            if (sheet.RetainedThisPeriod != 0)
            {
                // Carried as its own line rather than folded into equity: until
                // the year is closed it is not equity yet, and without it the
                // sheet fails to balance and looks like a bug.
                rows.Add(Row(null,
                    ("section", "Equity"),
                    ("name", "Profit for the period (not yet closed)"),
                    ("amount", sheet.RetainedThisPeriod)));
            }

            return new ReportResult
            {
                Columns =
                [
                    new ReportColumn("section", "Section", ReportValueKind.Text, 1f),
                    new ReportColumn("name", "Account", ReportValueKind.Text, 3f),
                    new ReportColumn("amount", "Amount", ReportValueKind.Money, 1.2f)
                ],

                Rows = rows,

                Header =
                [
                    new PrintField("Assets", sheet.TotalAssets.ToString("N2")),
                    new PrintField("Liabilities and equity", sheet.TotalFunding.ToString("N2")),
                    new PrintField("Balanced", sheet.IsBalanced ? "Yes" : "NO — investigate")
                ],

                EmptyMessage = "Nothing has been posted on or before this date."
            };
        }
    };

    private static ReportDefinition VoucherRegister(IServiceProvider sp) => new()
    {
        Key = "finance.voucher-register",
        ModuleKey = FinanceModule.Key,
        Group = "Registers",
        Name = "Voucher register",
        Description = "Every entry posted in a period, with what raised it.",
        Permission = FinanceModule.VouchersView,
        Uses = ReportFilters.DateRange | ReportFilters.Status,
        SortOrder = 4,

        Run = async (request, ct) =>
        {
            var vouchers = sp.GetRequiredService<IVoucherService>();

            VoucherStatus? status = Enum.TryParse<VoucherStatus>(request.Status, true, out var parsed)
                ? parsed
                : null;

            var list = await vouchers.ListAsync(new VoucherFilter(
                request.From, request.To, null, status, Search: request.Search, Take: 2000), ct);

            return new ReportResult
            {
                Columns =
                [
                    new ReportColumn("date", "Date", ReportValueKind.Date, 1f),
                    new ReportColumn("number", "Number", ReportValueKind.Text, 1f),
                    new ReportColumn("type", "Type", ReportValueKind.Text, 0.8f),
                    new ReportColumn("narration", "Narration", ReportValueKind.Text, 3f),
                    new ReportColumn("source", "Raised by", ReportValueKind.Text, 1f),
                    new ReportColumn("status", "Status", ReportValueKind.Status, 0.8f),
                    new ReportColumn("amount", "Amount", ReportValueKind.Money, 1.2f)
                ],

                // Every row links back to the entry behind it.
                Rows = [.. list.Select(v => Row($"/finance/vouchers?id={v.Id}",
                    ("date", v.Date), ("number", v.Number), ("type", v.Type.ToString()),
                    ("narration", v.Narration),
                    ("source", v.SourceModule ?? "Manual"),
                    ("status", v.Status.ToString()),
                    ("amount", v.TotalDebit)))],

                Totals = [new ReportTotal("amount", list.Sum(v => v.TotalDebit))],

                EmptyMessage = "No vouchers match those filters."
            };
        }
    };

    private static ReportDefinition PaymentRequests(IServiceProvider sp) => new()
    {
        Key = "finance.payment-requests",
        ModuleKey = FinanceModule.Key,
        Group = "Registers",
        Name = "Payment requests",
        Description = "Requests raised, where each one got to, and what it cost.",
        Permission = FinanceModule.ReportsView,
        Uses = ReportFilters.Status,
        SortOrder = 5,

        Run = async (request, ct) =>
        {
            var requests = sp.GetRequiredService<IPaymentRequestService>();

            PaymentRequestStatus? status =
                Enum.TryParse<PaymentRequestStatus>(request.Status, true, out var parsed)
                    ? parsed
                    : null;

            var list = await requests.ListAsync(status, mineOnly: false, ct);

            return new ReportResult
            {
                Columns =
                [
                    new ReportColumn("reference", "Reference", ReportValueKind.Text, 1f),
                    new ReportColumn("title", "For", ReportValueKind.Text, 2.5f),
                    new ReportColumn("requester", "Raised by", ReportValueKind.Text, 1.2f),
                    new ReportColumn("payee", "Payee", ReportValueKind.Text, 1.2f),
                    new ReportColumn("head", "Charged to", ReportValueKind.Text, 1.2f),
                    new ReportColumn("status", "Status", ReportValueKind.Status, 1f),
                    new ReportColumn("amount", "Amount", ReportValueKind.Money, 1.2f)
                ],

                Rows = [.. list.Select(r => Row($"/finance/requests?id={r.Id}",
                    ("reference", r.Reference), ("title", r.Title),
                    ("requester", r.RequestedByName), ("payee", r.PayeeName),
                    ("head", r.ExpenseAccount?.Name),
                    ("status", r.Status.ToString()), ("amount", r.Amount)))],

                Totals = [new ReportTotal("amount", list.Sum(r => r.Amount))],

                EmptyMessage = "No payment requests match those filters."
            };
        }
    };

    private static ReportDefinition ExpenseByHead(IServiceProvider sp) => new()
    {
        Key = "finance.expense-by-head",
        ModuleKey = FinanceModule.Key,
        Group = "Analysis",
        Name = "Expenses by head",
        Description = "What was spent under each expense account over a period.",
        Permission = FinanceModule.ReportsView,
        Uses = ReportFilters.DateRange,
        SortOrder = 6,

        Run = async (request, ct) =>
        {
            var reports = sp.GetRequiredService<IFinanceReports>();
            var from = request.From ?? DateOnly.MinValue;
            var to = request.To ?? DateOnly.FromDateTime(DateTime.Today);

            var statement = await reports.IncomeStatementAsync(from, to, ct);
            var total = statement.TotalExpenses;

            return new ReportResult
            {
                Columns =
                [
                    new ReportColumn("code", "Code", ReportValueKind.Text, 0.8f),
                    new ReportColumn("name", "Head", ReportValueKind.Text, 3f),
                    new ReportColumn("amount", "Spent", ReportValueKind.Money, 1.2f),
                    new ReportColumn("share", "Share", ReportValueKind.Text, 0.8f)
                ],

                Rows = [.. statement.Expenses
                    .OrderByDescending(l => l.Amount)
                    .Select(l => Row(null,
                        ("code", l.Code), ("name", l.Name), ("amount", l.Amount),
                        // Guarded: a period with no spend at all would divide by
                        // zero and take the whole report down.
                        ("share", total == 0 ? "—" : $"{l.Amount / total:P1}")))],

                Totals = [new ReportTotal("amount", total)],

                EmptyMessage = "Nothing was spent in this period."
            };
        }
    };
}
