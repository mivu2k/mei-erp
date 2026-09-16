using System.Diagnostics;
using MeiErp.Platform.Identity;
using Microsoft.AspNetCore.Authorization;
using Npgsql;

namespace MeiErp.Host.Services;

/// <summary>Creates a complete PostgreSQL custom-format backup for Super Admin.</summary>
public static class BackupEndpoints
{
    private static readonly SemaphoreSlim BackupGate = new(1, 1);

    public static void MapBackupEndpoints(this WebApplication app)
    {
        app.MapGet("/admin/backup/download", async (
            IConfiguration configuration,
            ILoggerFactory loggerFactory,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!await BackupGate.WaitAsync(0, ct))
                return (IResult)Results.Conflict("Another database backup is already being generated.");

            string? temporaryPath = null;
            try
            {
                var raw = configuration.GetConnectionString("Platform");
                if (string.IsNullOrWhiteSpace(raw))
                    return Results.Problem("The database connection is not configured.");

                var connection = new NpgsqlConnectionStringBuilder(raw);
                if (string.IsNullOrWhiteSpace(connection.Database) ||
                    string.IsNullOrWhiteSpace(connection.Username))
                    return Results.Problem("The database name or backup user is not configured.");
                temporaryPath = Path.Combine(Path.GetTempPath(), $"mei-erp-{Guid.NewGuid():N}.backup");

                var start = new ProcessStartInfo
                {
                    FileName = "pg_dump",
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                start.ArgumentList.Add("--format=custom");
                start.ArgumentList.Add("--compress=6");
                start.ArgumentList.Add("--no-owner");
                start.ArgumentList.Add("--no-privileges");
                start.ArgumentList.Add("--file");
                start.ArgumentList.Add(temporaryPath);
                start.ArgumentList.Add("--host");
                start.ArgumentList.Add(string.IsNullOrWhiteSpace(connection.Host) ? "localhost" : connection.Host);
                start.ArgumentList.Add("--port");
                start.ArgumentList.Add(connection.Port.ToString());
                start.ArgumentList.Add("--username");
                start.ArgumentList.Add(connection.Username!);
                start.ArgumentList.Add("--dbname");
                start.ArgumentList.Add(connection.Database!);
                start.Environment["PGPASSWORD"] = connection.Password ?? "";

                using var process = Process.Start(start)
                    ?? throw new InvalidOperationException("The PostgreSQL backup process could not be started.");
                var errorTask = process.StandardError.ReadToEndAsync(ct);
                try
                {
                    await process.WaitForExitAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    throw;
                }

                var error = await errorTask;
                if (process.ExitCode != 0 || !File.Exists(temporaryPath))
                {
                    loggerFactory.CreateLogger("DatabaseBackup")
                        .LogError("pg_dump failed with exit code {ExitCode}: {Error}", process.ExitCode, error);
                    TryDelete(temporaryPath);
                    temporaryPath = null;
                    return Results.Problem("The database backup could not be created. Check the server log for details.");
                }

                var user = http.User.Identity?.Name ?? "unknown";
                loggerFactory.CreateLogger("DatabaseBackup")
                    .LogWarning("A complete database backup was downloaded by {User}", user);

                var downloadName = $"MEI-ERP-full-backup-{DateTime.Now:yyyy-MM-dd-HHmmss}.backup";
                var result = new DeleteAfterDownloadResult(temporaryPath, downloadName);
                temporaryPath = null; // The result owns cleanup from here.
                return result;
            }
            finally
            {
                if (temporaryPath is not null) TryDelete(temporaryPath);
                BackupGate.Release();
            }
        })
        .RequireAuthorization(new AuthorizeAttribute { Roles = PlatformPermissions.SuperAdminRole });
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { /* Startup cleanup can remove an abandoned temp file later. */ }
    }

    private sealed class DeleteAfterDownloadResult(string path, string downloadName) : IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        {
            try
            {
                var info = new FileInfo(path);
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "application/vnd.postgresql.pg-dump";
                context.Response.ContentLength = info.Length;
                context.Response.Headers.ContentDisposition =
                    $"attachment; filename=\"{downloadName}\"";
                await using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 128 * 1024, FileOptions.Asynchronous);
                await file.CopyToAsync(context.Response.Body, context.RequestAborted);
            }
            finally { TryDelete(path); }
        }
    }
}
