using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using BookIt.Api.Domain;
using BookIt.Api.Infrastructure.Persistence;
using BookIt.Tests.Support;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookIt.Tests;

public class AuthTests(ApiFactory f) : IClassFixture<ApiFactory>
{
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
        Assert.NotEqual(hash, hasher.HashPassword(user, "Secret#123")); // salted
    }

    [Fact]
    public async Task Register_stores_hashed_password_and_login_succeeds()
    {
        var client = f.CreateClient();
        var email = $"reg{Guid.NewGuid():N}@test.local";
        var reg = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Passw0rd!x", fullName = "Reg User" });
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);

        using (var scope = f.Services.CreateScope())
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
    public async Task Login_with_wrong_password_is_401_problem_details()
    {
        var res = await f.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = DbInitializer.DemoEmail, password = "nope-nope-nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        var problem = await res.Content.ReadFromJsonAsync<ProblemResp>();
        Assert.Equal(401, problem!.Status);
        Assert.Equal("Invalid email or password.", problem.Detail);
    }

    [Fact]
    public async Task Seeded_demo_user_can_login_and_is_a_customer()
    {
        var res = await f.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = DbInitializer.DemoEmail, password = DbInitializer.DemoPassword });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(Roles.Customer, (await res.Content.ReadFromJsonAsync<ApiFactory.Auth>())!.Role);
    }

    [Fact]
    public async Task Duplicate_registration_is_409()
    {
        var client = f.CreateClient();
        var payload = new { email = $"dup{Guid.NewGuid():N}@test.local", password = "Passw0rd!x", fullName = "Dup" };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/register", payload)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/auth/register", payload)).StatusCode);
    }

    [Fact]
    public async Task Protected_endpoint_requires_token()
    {
        var res = await f.CreateClient().GetAsync("/api/bookings");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Registration_always_creates_a_customer_even_if_a_role_is_sent()
    {
        var res = await f.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { email = $"sneaky{Guid.NewGuid():N}@test.local", password = "Passw0rd!x", fullName = "Sneaky", role = "Admin" });
        var auth = (await res.Content.ReadFromJsonAsync<ApiFactory.Auth>())!;
        Assert.Equal(Roles.Customer, auth.Role);
    }

    [Fact]
    public async Task Token_carries_the_role_claim()
    {
        var (_, admin) = await f.AdminClientAsync();
        var (_, customer) = await f.NewUserClientAsync();
        var handler = new JwtSecurityTokenHandler();
        Assert.Contains(handler.ReadJwtToken(admin.Token).Claims, c => c.Value == Roles.Admin);
        Assert.Contains(handler.ReadJwtToken(customer.Token).Claims, c => c.Value == Roles.Customer);
        Assert.Equal(Roles.Admin, admin.Role);
    }

    [Fact]
    public async Task Tampered_token_is_rejected()
    {
        var (client, auth) = await f.NewUserClientAsync();
        client.DefaultRequestHeaders.Authorization = new("Bearer", auth.Token[..^3] + "abc");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/bookings")).StatusCode);
    }
}
