namespace BookIt.Api.Application.Exceptions;

/// <summary>The request conflicts with current state (duplicates, already paid, ...). Maps to 409.</summary>
public class ConflictException(string message) : Exception(message);
