using BookIt.Api.Application.Abstractions;
using BookIt.Api.Application.Contracts;
using BookIt.Api.Application.Exceptions;
using BookIt.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookIt.Api.Application.Auth;

public class AuthService(IAppDbContext db, IPasswordHasher<User> hasher, ITokenService tokens, ILogger<AuthService> log) : IAuthService
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw new ConflictException("Email is already registered.");

        // Public registration only ever creates Customers; Admins are seeded/provisioned out of band.
        var user = new User { Email = email, FullName = request.FullName.Trim(), Role = Roles.Customer };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct); // the unique index on Email turns a registration race into ConflictException
        log.LogInformation("User {UserId} registered", user.Id);
        return ToResponse(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
        {
            log.LogWarning("Failed login attempt");
            throw new InvalidCredentialsException();
        }
        return ToResponse(user);
    }

    private AuthResponse ToResponse(User u) => new(tokens.CreateToken(u), u.Id, u.Email, u.FullName, u.Role);
}
