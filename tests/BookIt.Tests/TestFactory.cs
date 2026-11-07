using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookIt.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookIt.Tests;

/// <summary>Boots the real API against a private in-memory SQLite database (kept alive by an open connection).</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _conn = new("DataSource=:memory:");

    public ApiFactory() => _conn.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            var existing = services.Where(d => d.ServiceType == typeof(DbContextOptions<BookItDb>)).ToList();
            foreach (var d in existing) services.Remove(d);
            services.AddDbContext<BookItDb>(o => o.UseSqlite(_conn));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _conn.Dispose();
    }

    public record Auth(string Token, int UserId, string Email, string FullName);

    /// <summary>Registers a fresh random user and returns an authenticated client.</summary>
    public async Task<(HttpClient Client, Auth Auth)> NewUserClientAsync()
    {
        var client = CreateClient();
        var email = $"user{Guid.NewGuid():N}@test.local";
        var res = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Passw0rd!x", fullName = "Test User" });
        res.EnsureSuccessStatusCode();
        var auth = (await res.Content.ReadFromJsonAsync<Auth>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return (client, auth);
    }
}
