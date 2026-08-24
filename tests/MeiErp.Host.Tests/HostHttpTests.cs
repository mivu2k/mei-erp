using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Xunit;

namespace MeiErp.Host.Tests;

/// <summary>
/// A narrow HTTP smoke gate for the host boundary. Module services have broad
/// coverage; this catches route registration, middleware and auth regressions.
/// Each run gets a disposable PostgreSQL database so it never mutates dev data.
/// </summary>
public sealed class HostHttpTests
{
    private static readonly string BaseConnection = Environment.GetEnvironmentVariable("MEIERP_TEST_DB")
        ?? "Host=127.0.0.1;Username=meierp;Password=DevPassword1!;";

    [SkippableFact]
    public async Task Health_endpoints_are_anonymous_and_ready()
    {
        await using var database = await IsolatedDatabase.TryCreateAsync();
        Skip.If(database is null, "No PostgreSQL available.");

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", database!.ConnectionString);
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var live = await client.GetAsync("/health/live");
        var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("Healthy", (await live.Content.ReadAsStringAsync()).Trim());
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Healthy", (await ready.Content.ReadAsStringAsync()).Trim());
    }

    [SkippableFact]
    public async Task Protected_legacy_alias_keeps_the_sign_in_gate()
    {
        await using var database = await IsolatedDatabase.TryCreateAsync();
        Skip.If(database is null, "No PostgreSQL available.");

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", database!.ConnectionString);
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/finance/admin/audit");

        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther,
            $"Expected auth redirect, got {(int)response.StatusCode}.");
        Assert.Contains("/sign-in", response.Headers.Location?.ToString() ?? "");
    }

    private sealed class IsolatedDatabase : IAsyncDisposable
    {
        private readonly string _name;
        private readonly string _adminConnection;
        public string ConnectionString { get; }

        private IsolatedDatabase(string name, string adminConnection, string connectionString)
        {
            _name = name;
            _adminConnection = adminConnection;
            ConnectionString = connectionString;
        }

        public static async Task<IsolatedDatabase?> TryCreateAsync()
        {
            var name = $"mei_http_{Guid.NewGuid():N}";
            var admin = BaseConnection + "Database=postgres;";
            try
            {
                await using var connection = new NpgsqlConnection(admin);
                await connection.OpenAsync();
                await using (var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\";", connection))
                    await command.ExecuteNonQueryAsync();
                return new(name, admin, BaseConnection + $"Database={name};");
            }
            catch (NpgsqlException)
            {
                return null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var connection = new NpgsqlConnection(_adminConnection);
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE);", connection);
                await command.ExecuteNonQueryAsync();
            }
            catch (NpgsqlException) { }
        }
    }
}
