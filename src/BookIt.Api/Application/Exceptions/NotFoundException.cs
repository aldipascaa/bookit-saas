namespace BookIt.Api.Application.Exceptions;

/// <summary>The requested resource does not exist or is not visible to the caller. Maps to 404.</summary>
public class NotFoundException(string message) : Exception(message);
