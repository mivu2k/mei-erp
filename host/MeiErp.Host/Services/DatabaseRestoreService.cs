using System.Diagnostics;
using Npgsql;

namespace MeiErp.Host.Services;

public sealed record DatabaseRestoreResult(bool Ok, string Message, string? SafetyBackupName = null);

public interface IDatabaseRestoreService
{
    Task<DatabaseRestoreResult> RestoreAsync(Stream backup, CancellationToken ct = default);
}

/// <summary>
/// Validates and atomically restores a PostgreSQL custom-format archive.
/// A safety archive is retained before any restore command is allowed to run.
/// </summary>
public sealed class DatabaseRestoreService(
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<DatabaseRestoreService> logger) : IDatabaseRestoreService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<DatabaseRestoreResult> RestoreAsync(Stream backup, CancellationToken ct = default)
    {
        if (!await Gate.WaitAsync(0, ct))
            return new(false, "A backup or restore operation is already running.");

        var uploadPath = Path.Combine(Path.GetTempPath(), $"mei-erp-restore-{Guid.NewGuid():N}.backup");
        try
        {
            await using (var file = new FileStream(uploadPath, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 128 * 1024, FileOptions.Asynchronous))
                await backup.CopyToAsync(file, ct);

            if (new FileInfo(uploadPath).Length == 0)
                return new(false, "The selected backup file is empty.");

            var validation = await RunAsync("pg_restore", ["--list", uploadPath], null, ct);
            if (validation.ExitCode != 0)
                return new(false, "This is not a valid PostgreSQL custom-format backup.");

            var connection = Connection();
            var backupDirectory = Path.Combine(environment.ContentRootPath, "backups");
            Directory.CreateDirectory(backupDirectory);
            var safetyName = $"pre-restore-{DateTime.Now:yyyy-MM-dd-HHmmss}.backup";
            var safetyPath = Path.Combine(backupDirectory, safetyName);

            var dump = await RunAsync("pg_dump",
                ConnectionArgs(connection,
                    ["--format=custom", "--compress=6", "--no-owner", "--no-privileges", "--file", safetyPath]),
                connection.Password, ct);
            if (dump.ExitCode != 0)
            {
                logger.LogError("Pre-restore safety backup failed: {Error}", dump.Error);
                return new(false, "Restore stopped because the automatic safety backup could not be created.");
            }

            // One transaction means a failed restore rolls back instead of
            // leaving half of one database mixed with half of another.
            NpgsqlConnection.ClearAllPools();
            var restore = await RunAsync("pg_restore",
                ConnectionArgs(connection,
                ["--clean", "--if-exists", "--no-owner", "--no-privileges",
                 "--exit-on-error", "--single-transaction", uploadPath]),
                connection.Password, ct);

            if (restore.ExitCode != 0)
            {
                logger.LogError("Database restore failed: {Error}", restore.Error);
                return new(false,
                    "Restore failed and was rolled back. The pre-restore safety backup was retained.", safetyName);
            }

            NpgsqlConnection.ClearAllPools();
            logger.LogCritical("The complete ERP database was restored. Safety backup: {Backup}", safetyName);
            return new(true,
                "Restore completed. Restart the ERP service before anybody resumes work.", safetyName);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new(false, "Restore was cancelled before completion.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected database restore failure");
            return new(false, $"Restore could not run: {ex.GetBaseException().Message}");
        }
        finally
        {
            try { File.Delete(uploadPath); } catch { }
            Gate.Release();
        }
    }

    private NpgsqlConnectionStringBuilder Connection()
    {
        var raw = configuration.GetConnectionString("Platform")
            ?? throw new InvalidOperationException("The database connection is not configured.");
        var connection = new NpgsqlConnectionStringBuilder(raw);
        if (string.IsNullOrWhiteSpace(connection.Database) || string.IsNullOrWhiteSpace(connection.Username))
            throw new InvalidOperationException("The database name or restore user is not configured.");
        return connection;
    }

    private static List<string> ConnectionArgs(NpgsqlConnectionStringBuilder connection, IEnumerable<string> tail)
    {
        var args = new List<string>
        {
            "--host", string.IsNullOrWhiteSpace(connection.Host) ? "localhost" : connection.Host,
            "--port", connection.Port.ToString(), "--username", connection.Username!,
            "--dbname", connection.Database!
        };
        args.AddRange(tail);
        return args;
    }

    private static async Task<(int ExitCode, string Error)> RunAsync(
        string command, IEnumerable<string> arguments, string? password, CancellationToken ct)
    {
        var start = new ProcessStartInfo
        {
            FileName = command,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (password is not null) start.Environment["PGPASSWORD"] = password;

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"{command} could not be started.");
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        try { await process.WaitForExitAsync(ct); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        await output;
        return (process.ExitCode, await error);
    }
}
