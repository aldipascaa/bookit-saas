using BookIt.Api.Application.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BookIt.Api.Web.Middleware;

/// <summary>
/// Single place that turns exceptions into RFC 7807 ProblemDetails responses. Application exceptions map to
/// their intended status; anything else is logged and returned as an opaque 500 (no internals leaked).
/// </summary>
public class GlobalExceptionHandler(IProblemDetailsService problems, ILogger<GlobalExceptionHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title, detail) = exception switch
        {
            NotFoundException e => (StatusCodes.Status404NotFound, "Not found", e.Message),
            ConflictException e => (StatusCodes.Status409Conflict, "Conflict", e.Message),
            BusinessRuleException e => (StatusCodes.Status400BadRequest, "Bad request", e.Message),
            InvalidCredentialsException e => (StatusCodes.Status401Unauthorized, "Unauthorized", e.Message),
            PaymentDeclinedException e => (StatusCodes.Status402PaymentRequired, "Payment declined", e.Message),
            BadHttpRequestException e => (StatusCodes.Status400BadRequest, "Bad request", e.Message),
            _ => (StatusCodes.Status500InternalServerError, "Server error", "An unexpected error occurred.")
        };

        if (status == StatusCodes.Status500InternalServerError)
            log.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);

        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail }
        });
    }
}
