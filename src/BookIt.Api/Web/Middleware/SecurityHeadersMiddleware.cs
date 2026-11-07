namespace BookIt.Api.Web.Middleware;

/// <summary>Adds baseline browser hardening headers. The CSP is skipped for Swagger UI, which needs inline scripts.</summary>
public class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string Csp = "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; " +
                               "object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'";

    public Task InvokeAsync(HttpContext ctx)
    {
        ctx.Response.OnStarting(() =>
        {
            var h = ctx.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "no-referrer";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            if (!ctx.Request.Path.StartsWithSegments("/swagger"))
                h["Content-Security-Policy"] = Csp;
            return Task.CompletedTask;
        });
        return next(ctx);
    }
}
