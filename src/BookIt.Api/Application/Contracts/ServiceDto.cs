using BookIt.Api.Domain;

namespace BookIt.Api.Application.Contracts;

public record ServiceDto(int Id, string Name, string? Description, decimal Price, int DurationMinutes, bool IsActive)
{
    public static ServiceDto From(Service s) => new(s.Id, s.Name, s.Description, s.Price, s.DurationMinutes, s.IsActive);
}
