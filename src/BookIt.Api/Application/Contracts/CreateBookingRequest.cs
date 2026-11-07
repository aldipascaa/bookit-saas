using System.ComponentModel.DataAnnotations;

namespace BookIt.Api.Application.Contracts;

public record CreateBookingRequest(
    [Range(1, int.MaxValue)] int ServiceId,
    [Required] DateTime StartTime,
    [MaxLength(500)] string? Notes);
