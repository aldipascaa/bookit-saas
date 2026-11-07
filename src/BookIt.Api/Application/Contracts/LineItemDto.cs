namespace BookIt.Api.Application.Contracts;

public record LineItemDto(string Description, int Quantity, decimal UnitPrice, decimal LineTotal);
