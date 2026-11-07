namespace BookIt.Api.Application.Exceptions;

/// <summary>The (mock) payment was declined. Maps to 402.</summary>
public class PaymentDeclinedException() : Exception("Mock payment declined.");
