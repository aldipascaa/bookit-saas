using BookIt.Api.Application.Contracts;

namespace BookIt.Api.Application.Auth;

public interface IAuthService
{
    /// <summary>Creates a Customer account. Throws ConflictException when the email is taken.</summary>
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);

    /// <summary>Throws InvalidCredentialsException on unknown email or wrong password.</summary>
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
}
