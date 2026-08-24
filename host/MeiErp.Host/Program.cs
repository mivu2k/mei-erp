using MeiErp.Host.Components;
using MeiErp.Host.Services;
using MeiErp.Modules.Finance;
using MeiErp.Modules.Auto;
using MeiErp.Modules.GatePass;
using MeiErp.Modules.Hr;
using MeiErp.Modules.Repair;
using MeiErp.Modules.Tender;
using MeiErp.Modules.Inventory;
using MeiErp.Modules.Trade;
using MeiErp.Modules.Ledger;
using MeiErp.Platform.Identity;
using MeiErp.Platform.Kernel;
using MeiErp.Platform.Notifications;
using MeiErp.Platform.Printing;
using MeiErp.Platform.Reporting;
using MeiErp.Platform.Messaging;
using MeiErp.Platform.Workflow;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------- logging
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/mei-erp-.log",
        rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30));

// ---------------------------------------------------------------- database
var connection = builder.Configuration.GetConnectionString("Platform")
    ?? throw new InvalidOperationException(
        "No 'Platform' connection string. Copy appsettings.Development.json.example " +
        "to appsettings.Development.json and fill in the database password.");

builder.Services.AddDbContext<PlatformDbContext>(options =>
    options.UseNpgsql(connection, npgsql =>
    {
        npgsql.MigrationsHistoryTable("__migrations", PlatformDbContext.SchemaName);

        // A transient blip is still a blip, even on a LAN. Retrying means a
        // manual transaction must run through the execution strategy - check
        // CLAUDE.md before adding a BeginTransactionAsync anywhere.
        npgsql.EnableRetryOnFailure(3);
    }));

// ---------------------------------------------------------------- clock
// Every date comes from here. On a UTC server in a UTC+5 business,
// DateTime.Today and DateTime.UtcNow disagree for five hours every night.
var timeZoneId = builder.Configuration["Platform:TimeZone"] ?? "Asia/Karachi";
builder.Services.AddSingleton<IClock>(_ =>
    new SystemClock(TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)));
builder.Services.AddScoped<IGlobalSearchService, GlobalSearchService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IReportPreferenceService, ReportPreferenceService>();
builder.Services.AddScoped<IAccountEmailSender, AccountEmailSender>();

// ---------------------------------------------------------------- modules
// Resolved once at startup; nothing queries the database to find out which
// modules exist. Business modules will register themselves here.
// Every module the host composes. Adding one here puts it in the nav, the
// permission matrix, the report hub and the approval designer at once - there
// is no second place to register it.
builder.Services.AddSingleton<IModuleCatalog>(_ => new ModuleCatalog(
[
    HrModule.Descriptor,
    FinanceModule.Descriptor,
    InventoryModule.Descriptor,
    PurchaseModule.Descriptor,
    SalesModule.Descriptor,
    AutoModule.Descriptor,
    GatePassModule.Descriptor,
    RepairModule.Descriptor,
    TenderModule.Descriptor,
    LedgerModule.Descriptor
]));

builder.Services.AddHrModule(builder.Configuration);
builder.Services.AddAttendanceReports();
builder.Services.AddFinanceModule(builder.Configuration);
builder.Services.AddScoped<IPayrollAttendanceProvider, HrPayrollAttendanceProvider>();
builder.Services.AddFinanceReports();
builder.Services.AddRepairReports();
builder.Services.AddInventoryReports();
builder.Services.AddTradeReports();
builder.Services.AddTenderReports();
builder.Services.AddAutoReports();
builder.Services.AddGatePassReports();
builder.Services.AddLedgerReports();
builder.Services.AddInventoryModule(builder.Configuration);
builder.Services.AddTradeModule(builder.Configuration);

// The only code that knows both Trade and Inventory exist. Sales & Purchase
// owns the documents, Inventory owns the goods; this bridges the two so
// neither module has to reference the other.
builder.Services.AddScoped<ITradeStockPort, MeiErp.Host.Services.InventoryStockAdapter>();

// And lets Sales quote a workshop job straight off the work recorded against
// it. Optional by design: without this, the quote-a-job path simply is not
// offered.
builder.Services.AddScoped<ITradeJobSource, MeiErp.Host.Services.RepairJobSource>();

// ...and lets the workshop book devices in against the one party master,
// instead of keeping a second customer list of its own.
builder.Services.AddScoped<IRepairCustomerDirectory, MeiErp.Host.Services.TradeCustomerDirectory>();
builder.Services.AddAutoModule(builder.Configuration);
builder.Services.AddGatePassModule(builder.Configuration);
builder.Services.AddRepairModule(builder.Configuration);
builder.Services.AddTenderModule(builder.Configuration);
builder.Services.AddLedgerModule(builder.Configuration);

// ---------------------------------------------------------------- identity
builder.Services
    .AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        options.User.RequireUniqueEmail = true;

        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;

        // Slows online guessing to a crawl without locking real people out of
        // their own system for a working day.
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

        options.SignIn.RequireConfirmedEmail = true;
    })
    .AddEntityFrameworkStores<PlatformDbContext>()
    .AddClaimsPrincipalFactory<PlatformClaimsPrincipalFactory>()
    .AddDefaultTokenProviders();

builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    // The previous platform stamped module access at sign-in and never
    // rechecked, so a revoked permission stayed live until the user signed out
    // - on an office PC, potentially days. Two minutes is the compromise
    // between that and a database hit on every request.
    options.ValidationInterval = TimeSpan.FromMinutes(2);
});

builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
    options.TokenLifespan = TimeSpan.FromHours(2));

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/sign-in";
    options.LogoutPath = "/sign-out";
    options.AccessDeniedPath = "/denied";
    options.ExpireTimeSpan = TimeSpan.FromHours(10);   // one working day
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// Permissions are data: any namespaced string becomes a policy on demand, so
// adding one never means registering it here.
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, ModuleAccessHandler>();
builder.Services.AddCascadingAuthenticationState();

// ---------------------------------------------------------------- platform services
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<IModuleAccessService, ModuleAccessService>();
builder.Services.AddScoped<IUserDirectory, UserDirectory>();
builder.Services.AddScoped<ICompanyProfileService, CompanyProfileService>();
builder.Services.AddScoped<ILabelTemplateService, LabelTemplateService>();
LabelFieldCatalog.Register(LabelDocumentTypes.RepairDevice,
[
    new("customer","Customer"),new("device","Device"),new("serial","Serial number"),
    new("fault","Reported fault"),new("status","Status"),new("intake","Intake number")
]);
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<IWorkflowAdminService, WorkflowAdminService>();
builder.Services.AddScoped<IApprovalEngine, ApprovalEngine>();
builder.Services.AddScoped<IApproverResolver, ApproverResolver>();
builder.Services.AddScoped<ApprovalSlaService>();
builder.Services.AddHostedService<ApprovalSlaWorker>();
builder.Services.AddHostedService<ReportScheduleWorker>();
builder.Services.AddScoped<INotificationManagementService, NotificationManagementService>();
builder.Services.AddScoped<IOutboxSource>(sp => new EfOutboxSource<InventoryDbContext>(
    sp.GetRequiredService<InventoryDbContext>(), "inventory"));
builder.Services.AddScoped<IOutboxSource>(sp => new EfOutboxSource<RepairDbContext>(
    sp.GetRequiredService<RepairDbContext>(), "repair"));
builder.Services.AddScoped<IOutboxManagementService, OutboxManagementService>();
builder.Services.AddSingleton<OutboxDispatcher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<OutboxDispatcher>());

// Notifications. The channels, the notifier and the background dispatcher come
// from the platform; the two storage interfaces are wired here because the
// tables live on PlatformDbContext - which is what lets a notification be
// written in the same transaction as the approval that raised it.
builder.Services.AddNotifications(builder.Configuration);
builder.Services.AddScoped<INotificationEmailRenderer, CompanyNotificationEmailRenderer>();
builder.Services.AddScoped<INotificationStore, NotificationStore>();
builder.Services.AddScoped<INotificationOutbox, NotificationOutbox>();

// Printing and reporting. The catalog is built from whatever the modules
// registered, so a report appears in the hub by being declared - not by anyone
// editing the hub.
builder.Services.AddSingleton<IPrintService, PrintService>();
builder.Services.AddScoped<IReportCatalog>(sp =>
    new ReportCatalog(sp.GetServices<ReportDefinition>()));
builder.Services.AddScoped<PlatformSeeder>();
builder.Services.AddHttpContextAccessor();

// ---------------------------------------------------------------- web
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<PlatformDbContext>("database");

// Slows credential stuffing against sign-in. Built into ASP.NET Core, so there
// is nothing extra to deploy.
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("sign-in", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

// ---------------------------------------------------------------- pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Security headers. Cheap, and their absence is the sort of thing that surfaces
// in an audit long after adding them would have been trivial.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

app.UseAntiforgery();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode()
   // A module's pages 404 unless its assembly is listed BOTH here and in
   // Routes.razor. Missing either is a silent routing failure.
   .AddAdditionalAssemblies(
       typeof(HrModule).Assembly,
       typeof(FinanceModule).Assembly,
       typeof(InventoryModule).Assembly,
       typeof(PurchaseModule).Assembly,
       typeof(AutoModule).Assembly,
       typeof(GatePassModule).Assembly,
       typeof(RepairModule).Assembly,
       typeof(TenderModule).Assembly,
       typeof(LedgerModule).Assembly);


app.MapAuthEndpoints();
app.MapReportEndpoints();
app.MapRecordPrintEndpoints();
app.MapKioskEndpoints();

// ---------------------------------------------------------------- start
await app.Services.SeedPlatformAsync();
await app.Services.SeedHrAsync();
await app.Services.SeedFinanceAsync();
// Trade before Inventory: the migration that moves the commercial documents
// into the trade schema has to copy them out before Inventory's migration drops
// the tables they came from. Same reason Trade runs before Repair below.
await app.Services.SeedTradeAsync();
await app.Services.SeedInventoryAsync();
await app.Services.SeedAutoAsync();
await app.Services.SeedGatePassAsync();
await app.Services.SeedRepairAsync();
await app.Services.SeedTenderAsync();
await app.Services.SeedLedgerAsync();

app.Run();

// Public entry point for the isolated HTTP test host.
public partial class Program { }
