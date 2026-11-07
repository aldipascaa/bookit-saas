using BookIt.Api.Application.Auth;
using BookIt.Api.Application.Contracts;
using BookIt.Api.Web.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BookIt.Api.Web.Controllers;

[Route("api/auth")]
[EnableRateLimiting(RateLimitOptions.AuthPolicy)]
public class AuthController(IAuthService auth) : ApiControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct) =>
        await auth.RegisterAsync(request, ct);

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct) =>
        await auth.LoginAsync(request, ct);
}
