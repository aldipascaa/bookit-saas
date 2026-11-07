using BookIt.Api.Web.Middleware;
using BookIt.Api.Web.Options;

namespace BookIt.Api.Web.Extensions;

public static class WebApplicationExtensions
{
    /// <summary>Request pipeline. Order matters: errors first, then headers, static files, auth, endpoints.</summary>
    public static WebApplication UseBookItPipeline(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages(); // ProblemDetails bodies for bare 401/403/404 responses
        if (!app.Environment.IsDevelopment()) app.UseHsts();

        app.UseMiddleware<SecurityHeadersMiddleware>();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.UseRouting();
        app.UseCors(CorsSettings.PolicyName);
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapHealthChecks("/health");                                              // liveness + dependencies
        app.MapHealthChecks("/health/live", new() { Predicate = _ => false });       // process is up
        return app;
    }
}
