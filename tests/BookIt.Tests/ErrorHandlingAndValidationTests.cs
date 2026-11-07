using System.Net;
using System.Net.Http.Json;
using System.Text;
using BookIt.Tests.Support;

namespace BookIt.Tests;

public class ErrorHandlingAndValidationTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private static async Task<ProblemResp> Problem(HttpResponseMessage r)
    {
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        return (await r.Content.ReadFromJsonAsync<ProblemResp>())!;
    }

    [Fact]
    public async Task Registration_validation_returns_400_with_field_errors()
    {
        var res = await f.CreateClient().PostAsJsonAsync("/api/auth/register", new { email = "not-an-email", password = "short", fullName = "" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var p = await Problem(res);
        Assert.NotNull(p.Errors);
        Assert.Contains(p.Errors!.Keys, k => k.Equals("Email", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(p.Errors.Keys, k => k.Equals("Password", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(p.Errors.Keys, k => k.Equals("FullName", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Service_request_ranges_are_validated()
    {
        var (admin, _) = await f.AdminClientAsync();
        var res = await admin.PostAsJsonAsync("/api/services", new { name = "x", price = -5, durationMinutes = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var p = await Problem(res);
        Assert.Contains(p.Errors!.Keys, k => k.Equals("Price", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(p.Errors.Keys, k => k.Equals("DurationMinutes", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Booking_request_validation_and_business_rules_return_400()
    {
        var (c, _) = await f.NewUserClientAsync();
        var badId = await c.PostAsJsonAsync("/api/bookings", new { serviceId = 0, startTime = DateTime.UtcNow.AddDays(3) });
        Assert.Equal(HttpStatusCode.BadRequest, badId.StatusCode);

        var past = await c.PostAsJsonAsync("/api/bookings", new { serviceId = 1, startTime = DateTime.UtcNow.AddDays(-3) });
        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);
        Assert.Equal("Start time must be in the future.", (await Problem(past)).Detail);

        var longNote = await c.PostAsJsonAsync("/api/bookings", new { serviceId = 1, startTime = DateTime.UtcNow.AddDays(3), notes = new string('x', 501) });
        Assert.Equal(HttpStatusCode.BadRequest, longNote.StatusCode);
    }

    [Fact]
    public async Task Malformed_json_is_400_problem_details()
    {
        var (c, _) = await f.NewUserClientAsync();
        var res = await c.PostAsync("/api/bookings", new StringContent("{ not json", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Unknown_resources_return_404_problem_details()
    {
        var (c, _) = await f.NewUserClientAsync();
        var res = await c.GetAsync("/api/bookings/123456");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal(404, (await Problem(res)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/services/123456")).StatusCode);
    }

    [Fact]
    public async Task Unauthorized_and_forbidden_responses_are_problem_details()
    {
        var anon = await f.CreateClient().GetAsync("/api/bookings");
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
        Assert.Equal(401, (await Problem(anon)).Status);

        var (user, _) = await f.NewUserClientAsync();
        var forbidden = await user.PostAsJsonAsync("/api/services", new { name = "x", price = 1, durationMinutes = 15 });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(403, (await Problem(forbidden)).Status);
    }

    [Fact]
    public async Task Unhandled_exceptions_become_opaque_500_problem_details()
    {
        // Developer exception page would hide the handler in Development, so exercise the handler directly.
        var handler = new BookIt.Api.Web.Middleware.GlobalExceptionHandler(
            new FakeProblemDetailsService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<BookIt.Api.Web.Middleware.GlobalExceptionHandler>.Instance);
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        var handled = await handler.TryHandleAsync(ctx, new InvalidOperationException("secret internals: connection string xyz"), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(500, ctx.Response.StatusCode);
        Assert.Equal("An unexpected error occurred.", FakeProblemDetailsService.LastDetail);
    }

    private sealed class FakeProblemDetailsService : Microsoft.AspNetCore.Http.IProblemDetailsService
    {
        public static string? LastDetail;
        public ValueTask WriteAsync(Microsoft.AspNetCore.Http.ProblemDetailsContext context) { LastDetail = context.ProblemDetails.Detail; return ValueTask.CompletedTask; }
        public ValueTask<bool> TryWriteAsync(Microsoft.AspNetCore.Http.ProblemDetailsContext context) { LastDetail = context.ProblemDetails.Detail; return ValueTask.FromResult(true); }
    }
}
