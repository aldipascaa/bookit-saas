namespace BookIt.Api.Application.Contracts;

/// <summary>Mock payment: no card data is accepted or stored. Set Succeed=false to simulate a decline.</summary>
public record MockPaymentRequest(bool Succeed = true);
