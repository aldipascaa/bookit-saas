namespace BookIt.Api.Application.Exceptions;

/// <summary>Login failed; deliberately does not say whether the email or the password was wrong. Maps to 401.</summary>
public class InvalidCredentialsException() : Exception("Invalid email or password.");
