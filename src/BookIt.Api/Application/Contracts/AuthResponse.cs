namespace BookIt.Api.Application.Contracts;

public record AuthResponse(string Token, int UserId, string Email, string FullName, string Role);
