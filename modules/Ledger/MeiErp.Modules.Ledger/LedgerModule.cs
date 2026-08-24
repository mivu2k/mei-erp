using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeiErp.Modules.Ledger;

public static class LedgerModule
{
    public const string Key = "ledger";

    public static ModuleDescriptor Descriptor => new()
    {
        Key = Key,
        Name = "Plain Ledger",
        Description = "Hand-ledger books, nested sub-ledgers and paired transfers.",
        BasePath = "/ledger",
        Icon = "AccountTree",
        Color = "#C2185B",
        SortOrder = 10,
        Schema = "ledger",
        Permissions =
        [
            new(LedgerPermissions.View, "Ledgers", "View ledgers and statements"),
            new(LedgerPermissions.Manage, "Ledgers", "Open, edit and close ledgers"),
            new(LedgerPermissions.EntryRecord, "Entries", "Record entries and transfers"),
            new(LedgerPermissions.EntryAmend, "Entries", "Amend or remove an existing entry"),
            new(LedgerPermissions.ReportsView, "Reports", "View ledger trees and balances"),
            new(LedgerPermissions.HeadsManage, "Heads", "Maintain ledger classifications")
        ],
        Nav =
        [
            new("Ledgers", "/ledger/ledgers", "MenuBook", LedgerPermissions.View),
            new("Ledger tree", "/ledger/tree", "AccountTree", LedgerPermissions.View),
            new("Reports", "/ledger/reports", "Assessment", LedgerPermissions.ReportsView),
            new("Heads", "/ledger/heads", "Category", LedgerPermissions.HeadsManage)
        ],
        RoleTemplates =
        [
            new("Ledger Manager", "Full control of plain ledgers.", LedgerPermissions.All),
            new("Ledger Clerk", "Records daily entries and transfers.",
                [LedgerPermissions.View, LedgerPermissions.EntryRecord, LedgerPermissions.ReportsView]),
            new("Ledger Viewer", "Read-only ledger access.",
                [LedgerPermissions.View, LedgerPermissions.ReportsView])
        ]
    };

    public static IServiceCollection AddLedgerModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Platform")
            ?? throw new InvalidOperationException("No 'Platform' connection string for Plain Ledger.");
        services.AddDbContext<LedgerDbContext>(options => options.UseNpgsql(connection, npgsql =>
        {
            npgsql.MigrationsHistoryTable("__migrations", "ledger");
            npgsql.EnableRetryOnFailure(3);
        }));
        services.AddScoped<ILedgerService, LedgerService>();
        services.AddScoped<ILedgerHeadService, LedgerHeadService>();
        return services;
    }

    public static async Task SeedLedgerAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<LedgerDbContext>().Database.MigrateAsync();
    }
}
