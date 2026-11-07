using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookIt.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BookIt.Tests.Support;

/// <summary>
/// Boots the real API (Development, demo data seeded, real migrations applied) against a private in-memory
/// SQLite database kept alive by an open connection. Subclasses tweak environment, settings or storage.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private SqliteConnection? _conn;
    private string? _dbFile;

    protected virtual string EnvironmentName => "Development";
    /// <summary>Use a real database file so several connections (like several app instances) can race.</summary>
    protected virtual bool UseFileDatabase => false;
    protected virtual Dictionary<string, string?> Settings => new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        var settings = new Dictionary<string, string?> { ["RateLimiting:AuthPermitLimit"] = "100000" };
        foreach (var kv in Settings) settings[kv.Key] = kv.Value;

        if (UseFileDatabase)
        {
            _dbFile = Path.Combine(Path.GetTempPath(), $"bookit-test-{Guid.NewGuid():N}.db");
            settings["ConnectionStrings:Default"] = $"Data Source={_dbFile}";
        }
        builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(settings));

        if (!UseFileDatabase)
        {
            _conn = new SqliteConnection("DataSource=:memory:");
            _conn.Open();
            builder.ConfigureServices(services =>
            {
                foreach (var d in services.Where(d => d.ServiceType == typeof(DbContextOptions<BookItDb>)).ToList())
                    services.Remove(d);
                services.AddDbContext<BookItDb>(o => o.UseSqlite(_conn));
            });
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        _conn?.Dispose();
        if (_dbFile is not null)
        {
            SqliteConnection.ClearAllPools();
            foreach (var f in new[] { _dbFile, _dbFile + "-journal", _dbFile + "-wal", _dbFile + "-shm" })
                try { File.Delete(f); } catch (IOException) { /* best effort */ }
        }
    }

    public record Auth(string Token, int UserId, string Email, string FullName, string Role);

    private static HttpClient WithToken(HttpClient client, Auth auth)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return client;
    }

    /// <summary>Registers a fresh random user and returns an authenticated client.</summary>
    public async Task<(HttpClient Client, Auth Auth)> NewUserClientAsync()
    {
        var client = CreateClient();
        var email = $"user{Guid.NewGuid():N}@test.local";
        var res = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Passw0rd!x", fullName = "Test User" });
        res.EnsureSuccessStatusCode();
        var auth = (await res.Content.ReadFromJsonAsync<Auth>())!;
        return (WithToken(client, auth), auth);
    }

    /// <summary>Logs in as the seeded demo admin.</summary>
    public async Task<(HttpClient Client, Auth Auth)> AdminClientAsync()
    {
        var client = CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new { email = DbInitializer.DemoAdminEmail, password = DbInitializer.DemoAdminPassword });
        res.EnsureSuccessStatusCode();
        var auth = (await res.Content.ReadFromJsonAsync<Auth>())!;
        return (WithToken(client, auth), auth);
    }
}

/// <summary>File-backed database: connections are independent, like separate app instances.</summary>
public class FileDbApiFactory : ApiFactory
{
    protected override bool UseFileDatabase => true;
}

/// <summary>Tight auth rate limit (3 requests per minute per IP).</summary>
public class RateLimitedApiFactory : ApiFactory
{
    protected override Dictionary<string, string?> Settings => new() { ["RateLimiting:AuthPermitLimit"] = "3" };
}

/// <summary>Production environment without any JWT key configured: must refuse to start.</summary>
public class MissingKeyApiFactory : ApiFactory
{
    protected override string EnvironmentName => "Production";
    protected override bool UseFileDatabase => true;
}

/// <summary>Production environment with the committed development key: must refuse to start.</summary>
public class DevKeyInProductionApiFactory : MissingKeyApiFactory
{
    protected override Dictionary<string, string?> Settings => new() { ["Jwt:Key"] = "DEV-ONLY-not-a-secret-do-not-use-in-production-0123456789abcdef" };
}

/// <summary>Production environment configured the supported way (key from configuration, no demo data).</summary>
public class ProductionApiFactory : MissingKeyApiFactory
{
    protected override Dictionary<string, string?> Settings => new()
    {
        ["Jwt:Key"] = "a-long-random-test-secret-0123456789-abcdefghijklmnopqrstuvwxyz",
        ["Seed:AdminEmail"] = "owner@prod.test",
        ["Seed:AdminPassword"] = "Prod#Admin-12345"
    };
}
