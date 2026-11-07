using System.ComponentModel.DataAnnotations;

namespace BookIt.Api.Application.Contracts;

public record LoginRequest([Required, MaxLength(254)] string Email, [Required, MaxLength(128)] string Password);
