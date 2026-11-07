using System.ComponentModel.DataAnnotations;

namespace BookIt.Api.Application.Contracts;

public record RegisterRequest(
    [Required, EmailAddress, MaxLength(254)] string Email,
    [Required, MinLength(8), MaxLength(128)] string Password,
    [Required, MinLength(1), MaxLength(100)] string FullName);
