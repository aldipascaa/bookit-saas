using System.Net;
using System.Net.Http.Json;
using BookIt.Api.Data;
using BookIt.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookIt.Tests;

public class AuthTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    public AuthTests(ApiFactory f) => _f = f;

    [Fact]
    public void PasswordHasher_hashes_and_verifies()
    {
        var hasher = new PasswordHasher<User>();
        var user = new User { Email = "a@b.c" };
        var hash = hasher.HashPassword(user, "Secret#123");

        Assert.NotEqual("Secret#123", hash);
        Assert.DoesNotContain("Secret#123", hash);
        Assert.NotEqual(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(user, hash, "Secret#123"));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(user, hash, "wrong"));
        // salted: hashing twice gives different output
        Assert.NotEqual(hash, hasher.HashPassword(user, "Secret#123"));
    }

    [Fact]
    public async Task Register_stores_hashed_password_and_login_succeeds()
    {
        var client = _f.CreateClient();
        var email = $"reg{Guid.NewGuid():N}@test.local";
        var reg = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Passw0rd!x", fullName = "Reg User" });
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);

        using (var scope = _f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BookItDb>();
            var stored = await db.Users.SingleAsync(u => u.Email == email);
            Assert.NotEqual("Passw0rd!x", stored.PasswordHash);
        }

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!x" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<ApiFactory.Auth>();
        Assert.False(string.IsNullOrWhiteSpace(body!.Token));
    }

    [Fact]
    public async Task Login_with_wrong_password_is_401()
    {
        var client = _f.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new { email = DbSeeder.DemoEmail, password = "nope-nope-nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Seeded_demo_user_can_login()
    {
        var res = await _f.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = DbSeeder.DemoEmail, password = DbSeeder.DemoPassword });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Duplicate_registration_is_409()
    {
        var client = _f.CreateClient();
        var payload = new { email = $"dup{Guid.NewGuid():N}@test.local", password = "Passw0rd!x", fullName = "Dup" };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/register", payload)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/auth/register", payload)).StatusCode);
    }

    [Fact]
    public async Task Protected_endpoint_requires_token()
    {
        var res = await _f.CreateClient().GetAsync("/api/bookings");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
