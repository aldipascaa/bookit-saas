namespace BookIt.Api.Application.Exceptions;

/// <summary>A domain rule was violated by otherwise well-formed input. Maps to 400.</summary>
public class BusinessRuleException(string message) : Exception(message);
