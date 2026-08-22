using MeiErp.Platform.Kernel;
using MeiErp.Platform.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeiErp.Modules.Finance;

public static class FinanceModule
{
    public const string Key = "finance";

    public const string AccountsView = "finance.accounts.view";
    public const string AccountsManage = "finance.accounts.manage";
    public const string VouchersView = "finance.vouchers.view";
    public const string VouchersPost = "finance.vouchers.post";
    public const string VouchersReverse = "finance.vouchers.reverse";
    public const string RequestsRaise = "finance.requests.raise";
    public const string RequestsPay = "finance.requests.pay";
    public const string ReportsView = "finance.reports.view";
    public const string YearClose = "finance.year.close";
    public const string PartiesManage = "finance.parties.manage";
    public const string PettyCashManage = "finance.petty-cash.manage";
    public const string UtilitiesManage = "finance.utilities.manage";
    public const string AdvancesRaise = "finance.advances.raise";
    public const string AdvancesManage = "finance.advances.manage";
    public const string PayrollView = "finance.payroll.view";
    public const string PayrollManage = "finance.payroll.manage";
    public const string PayrollPay = "finance.payroll.pay";
    public const string ReconcileManage = "finance.reconcile.manage";

    public static ModuleDescriptor Descriptor => new()
    {
        Key = Key,
        Name = "Finance",
        Description = "Chart of accounts, vouchers, the ledger and financial statements.",
        BasePath = "/finance",
        Icon = "Payments",
        Color = "#1976d2",
        SortOrder = 2,
        Schema = "finance",

        Permissions =
        [
            new(AccountsView,     "Accounts", "See the chart of accounts and balances"),
            new(AccountsManage,   "Accounts", "Add and edit accounts"),
            new(VouchersView,     "Vouchers", "See vouchers and the ledger"),
            new(VouchersPost,     "Vouchers", "Post entries into the books"),
            new(VouchersReverse,  "Vouchers", "Reverse a posted voucher"),
            new(RequestsRaise,    "Payments", "Raise a payment request"),
            new(RequestsPay,      "Payments", "Pay an approved request and post its voucher"),
            new(ReportsView,      "Reports",  "See the trial balance and financial statements"),
            new(YearClose,        "Period",   "Close a fiscal year"),
            new(PartiesManage,    "Parties",  "Manage third parties and record their payments"),
            new(PettyCashManage,  "Cash",     "Run petty cash boxes"),
            new(UtilitiesManage,  "Cash",     "Record utility connections and their bills"),
            new(AdvancesRaise,    "Advances", "Ask for an advance and account for it"),
            new(AdvancesManage,   "Advances", "Pay out, accept receipts and settle advances"),
            new(PayrollView,      "Payroll",  "See payroll runs and payslips"),
            new(PayrollManage,    "Payroll",  "Set salaries and build payroll runs"),
            new(PayrollPay,       "Payroll",  "Approve and pay a payroll run"),
            new(ReconcileManage,  "Period",   "Reconcile a bank account to its statement")
        ],

        RoleTemplates =
        [
            new("Accountant", "Full access to the books, short of closing the year.",
                [AccountsView, AccountsManage, VouchersView, VouchersPost,
                 VouchersReverse, RequestsRaise, RequestsPay, ReportsView,
                 PartiesManage, PettyCashManage, UtilitiesManage,
                 AdvancesRaise, AdvancesManage, PayrollView, ReconcileManage]),

            new("Finance Manager", "Everything an accountant can do, plus closing the year.",
                [AccountsView, AccountsManage, VouchersView, VouchersPost, VouchersReverse,
                 RequestsRaise, RequestsPay, ReportsView, YearClose,
                 PartiesManage, PettyCashManage, UtilitiesManage,
                 AdvancesRaise, AdvancesManage,
                 PayrollView, PayrollManage, PayrollPay, ReconcileManage]),

            new("Requester", "Can ask for money and follow their own requests.",
                [RequestsRaise, AdvancesRaise])
        ],

        Nav =
        [
            new("Chart of accounts", "/finance/accounts", "AccountTree", AccountsView),
            new("Vouchers",          "/finance/vouchers", "ReceiptLong", VouchersView),
            new("Day book",          "/finance/day-book", "MenuBook", VouchersView),

            new("Payment requests",  "/finance/requests", "RequestQuote", RequestsRaise, "Spending"),
            new("Advances",          "/finance/advances", "AccountBalanceWallet", AdvancesRaise, "Spending"),
            new("Petty cash",        "/finance/petty-cash", "Savings", PettyCashManage, "Spending"),
            new("Utilities",         "/finance/utilities", "Bolt", UtilitiesManage, "Spending"),

            new("Third parties",     "/finance/third-parties", "Handshake", PartiesManage, "People"),
            new("Payroll",           "/finance/payroll", "Payments", PayrollView, "People"),
            new("My payslips",       "/finance/my-payslips", "Description", null, "People"),

            new("Reports",           "/finance/reports", "Assessment", ReportsView, "Period"),
            new("Reconciliation",    "/finance/reconcile", "Rule", ReconcileManage, "Period"),
            new("Fiscal years",      "/finance/year-end", "EventAvailable", YearClose, "Period")
        ],

        Approvables =
        [
            new(PaymentRequestService.DocumentType, "Payment request", "Amount requested"),
            new(AdvanceService.DocumentType, "Advance request", "Amount requested")
        ]
    };

    public static IServiceCollection AddFinanceModule(
        this IServiceCollection services, IConfiguration config)
    {
        var connection = config.GetConnectionString("Platform")
            ?? throw new InvalidOperationException("No 'Platform' connection string for the Finance module.");

        services.AddDbContext<FinanceDbContext>(options =>
            options.UseNpgsql(connection, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__migrations", "finance");
                npgsql.EnableRetryOnFailure(3);
            }));

        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IVoucherService, VoucherService>();
        services.AddScoped<IFinanceReports, FinanceReports>();
        services.AddScoped<IPaymentRequestService, PaymentRequestService>();
        services.AddScoped<IApprovalSink, PaymentRequestApprovalSink>();
        services.AddScoped<IThirdPartyService, ThirdPartyService>();
        services.AddScoped<IPettyCashService, PettyCashService>();
        services.AddScoped<IUtilityService, UtilityService>();
        services.AddScoped<IAdvanceService, AdvanceService>();
        services.AddScoped<IApprovalSink, AdvanceApprovalSink>();
        services.AddScoped<IPayrollService, PayrollService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();
        services.AddScoped<IYearEndService, YearEndService>();

        return services;
    }
}

/// <summary>
/// Puts a working chart of accounts in place.
///
/// Additive: it adds only codes that are missing, so an existing install picks
/// up new heads on startup without an administrator's own accounts being
/// touched or renamed.
/// </summary>
public sealed class FinanceSeeder(FinanceDbContext db, IClock clock)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);
        await SeedChartAsync(ct);
        await SeedFiscalYearAsync(ct);
    }

    private async Task SeedChartAsync(CancellationToken ct)
    {
        var existing = await db.Accounts.Select(a => a.Code).ToListAsync(ct);
        var have = existing.ToHashSet(StringComparer.Ordinal);

        // (code, name, type, postable, parent code)
        (string Code, string Name, AccountType Type, bool Postable, string? Parent)[] chart =
        [
            ("1000", "Assets",              AccountType.Asset,     false, null),
            ("1100", "Cash in hand",        AccountType.Asset,     true,  "1000"),
            ("1200", "Bank accounts",       AccountType.Asset,     false, "1000"),
            ("1210", "Current account",     AccountType.Asset,     true,  "1200"),
            ("1600", "Receivables",         AccountType.Asset,     false, "1000"),
            ("1610", "Trade debtors",       AccountType.Asset,     true,  "1600"),
            ("1700", "Employee advances",   AccountType.Asset,     true,  "1000"),
            ("1800", "Fixed assets",        AccountType.Asset,     true,  "1000"),

            ("2000", "Liabilities",         AccountType.Liability, false, null),
            ("2100", "Payables",            AccountType.Liability, false, "2000"),
            ("2110", "Trade creditors",     AccountType.Liability, true,  "2100"),
            ("2200", "Salaries payable",    AccountType.Liability, true,  "2000"),
            ("2300", "Tax payable",         AccountType.Liability, true,  "2000"),

            ("3000", "Equity",              AccountType.Equity,    false, null),
            ("3100", "Capital",             AccountType.Equity,    true,  "3000"),
            ("3200", "Retained earnings",   AccountType.Equity,    true,  "3000"),

            ("4000", "Income",              AccountType.Income,    false, null),
            ("4100", "Sales",               AccountType.Income,    true,  "4000"),
            ("4200", "Service income",      AccountType.Income,    true,  "4000"),
            ("4900", "Other income",        AccountType.Income,    true,  "4000"),

            ("5000", "Expenses",            AccountType.Expense,   false, null),
            ("5100", "Cost of sales",       AccountType.Expense,   true,  "5000"),
            ("5200", "Employee expenses",   AccountType.Expense,   false, "5000"),
            ("5210", "Salaries and wages",  AccountType.Expense,   true,  "5200"),
            ("5220", "Staff travel",        AccountType.Expense,   true,  "5200"),
            ("5230", "Staff welfare",       AccountType.Expense,   true,  "5200"),

            // Director spend sits in its own sub-tree so the income statement
            // separates it from staff spend with no filtering.
            ("5400", "Director expenses",   AccountType.Expense,   false, "5000"),
            ("5410", "Director travel",     AccountType.Expense,   true,  "5400"),
            ("5420", "Director other",      AccountType.Expense,   true,  "5400"),

            ("5500", "Office and admin",    AccountType.Expense,   false, "5000"),
            ("5510", "Rent",                AccountType.Expense,   true,  "5500"),
            ("5520", "Utilities",           AccountType.Expense,   true,  "5500"),
            ("5530", "Stationery",          AccountType.Expense,   true,  "5500"),
            ("5540", "Repairs and upkeep",  AccountType.Expense,   true,  "5500"),
            ("5600", "Vehicle running",     AccountType.Expense,   true,  "5000"),
            ("5900", "Other expenses",      AccountType.Expense,   true,  "5000")
        ];

        var missing = chart.Where(a => !have.Contains(a.Code)).ToList();
        if (missing.Count == 0) return;

        // Parents before children, so a parent id is always available.
        var byCode = new Dictionary<string, Account>(StringComparer.Ordinal);

        foreach (var row in chart)
        {
            var account = await db.Accounts.FirstOrDefaultAsync(a => a.Code == row.Code, ct);

            if (account is null)
            {
                account = new Account
                {
                    Code = row.Code,
                    Name = row.Name,
                    Type = row.Type,
                    IsPostable = row.Postable,

                    // Seeded heads are depended on by code and by reports, so
                    // they cannot be deleted out from under either.
                    IsSystem = true,
                    IsActive = true
                };
                db.Accounts.Add(account);
                await db.SaveChangesAsync(ct);
            }

            byCode[row.Code] = account;

            if (row.Parent is not null
                && byCode.TryGetValue(row.Parent, out var parent)
                && account.ParentId != parent.Id)
            {
                account.ParentId = parent.Id;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task SeedFiscalYearAsync(CancellationToken ct)
    {
        var today = clock.Today;
        if (await db.FiscalYears.AnyAsync(y => y.StartDate <= today && y.EndDate >= today, ct)) return;

        // Pakistan's fiscal year runs July to June.
        var startYear = today.Month >= 7 ? today.Year : today.Year - 1;

        db.FiscalYears.Add(new FiscalYear
        {
            Name = $"FY {startYear}-{(startYear + 1) % 100:D2}",
            StartDate = new DateOnly(startYear, 7, 1),
            EndDate = new DateOnly(startYear + 1, 6, 30)
        });

        await db.SaveChangesAsync(ct);
    }
}

public static class FinanceSeederExtensions
{
    public static async Task SeedFinanceAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        await new FinanceSeeder(db, clock).SeedAsync();
    }
}
