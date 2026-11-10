namespace BookIt.Api.Application.Contracts;

/// <summary>A taken time range (UTC) of a service. Deliberately carries no customer or booking identity.</summary>
public record BusySlotDto(DateTime StartTime, DateTime EndTime);
