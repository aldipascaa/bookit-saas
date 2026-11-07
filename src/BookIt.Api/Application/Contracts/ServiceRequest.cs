using System.ComponentModel.DataAnnotations;

namespace BookIt.Api.Application.Contracts;

public record ServiceRequest(
    [Required, MinLength(1), MaxLength(100)] string Name,
    [MaxLength(500)] string? Description,
    [Range(0, 1000000)] decimal Price,
    [Range(5, 1440)] int DurationMinutes,
    bool IsActive = true);
