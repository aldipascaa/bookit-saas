using System.Net;
using System.Net.Http.Json;
using BookIt.Api.Infrastructure.Persistence;
using BookIt.Tests.Support;

namespace BookIt.Tests;

public class HealthAndHeadersTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Health_endpoints_report_healthy()
    {
        var c = f.CreateClient();
        var health = await c.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("Healthy", await health.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public async Task Responses_carry_security_headers()
    {
        var res = await f.CreateClient().GetAsync("/");
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", res.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", res.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("default-src 'self'", res.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Frontend_is_still_served()
    {
        var html = await f.CreateClient().GetStringAsync("/");
        Assert.Contains("BookIt", html);
        Assert.Contains("svc-dialog", html);
    }

    [Fact]
    public async Task Cross_origin_requests_are_not_allowed_by_default()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/services");
        req.Headers.Add("Origin", "https://evil.example");
        var res = await f.CreateClient().SendAsync(req);
        Assert.False(res.Headers.Contains("Access-Control-Allow-Origin"));
    }
}

public class RateLimitTests(RateLimitedApiFactory f) : IClassFixture<RateLimitedApiFactory>
{
    [Fact]
    public async Task Login_is_rate_limited_per_client()
    {
        var c = f.CreateClient();
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
            codes.Add((await c.PostAsJsonAsync("/api/auth/login", new { email = "x@y.z", password = "wrong-password" })).StatusCode);

        Assert.Equal(3, codes.Count(s => s == HttpStatusCode.Unauthorized));
        Assert.Equal(2, codes.Count(s => s == HttpStatusCode.TooManyRequests));

        // other endpoints are unaffected
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/services")).StatusCode);
    }
}

/// <summary>Secrets and environment: the app must not start with a missing or committed-demo signing key.</summary>
public class ConfigurationTests
{
    private static string Flatten(Exception? e)
    {
        var parts = new List<string>();
        for (; e is not null; e = e.InnerException) parts.Add(e.Message);
        return string.Join(" | ", parts);
    }

    [Fact]
    public void Startup_fails_fast_without_a_jwt_key_outside_development()
    {
        using var factory = new MissingKeyApiFactory();
        var ex = Record.Exception(() => factory.CreateClient());
        Assert.NotNull(ex);
        Assert.Contains("Jwt:Key", Flatten(ex));
    }

    [Fact]
    public void Startup_rejects_the_committed_dev_key_outside_development()
    {
        using var factory = new DevKeyInProductionApiFactory();
        var ex = Record.Exception(() => factory.CreateClient());
        Assert.NotNull(ex);
        Assert.Contains("development-only", Flatten(ex));
    }

    [Fact]
    public async Task Production_with_a_configured_key_starts_without_demo_data_and_hides_swagger()
    {
        using var factory = new ProductionApiFactory();
        var c = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/swagger/v1/swagger.json")).StatusCode);

        // demo accounts are not seeded...
        var demo = await c.PostAsJsonAsync("/api/auth/login", new { email = DbInitializer.DemoAdminEmail, password = DbInitializer.DemoAdminPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, demo.StatusCode);
        Assert.Empty((await c.GetFromJsonAsync<List<ServiceResp>>("/api/services"))!);

        // ...but the explicitly configured admin is
        var admin = await c.PostAsJsonAsync("/api/auth/login", new { email = "owner@prod.test", password = "Prod#Admin-12345" });
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        Assert.Equal("Admin", (await admin.Content.ReadFromJsonAsync<ApiFactory.Auth>())!.Role);
    }

    [Fact]
    public async Task Demo_admin_is_seeded_in_development()
    {
        using var factory = new ApiFactory();
        var (_, auth) = await factory.AdminClientAsync();
        Assert.Equal("Admin", auth.Role);
    }
}
