namespace BookIt.Api.Application.Exceptions;

/// <summary>The requested slot overlaps an existing booking for the same service. Maps to 409.</summary>
public class BookingConflictException(string message = "That time slot overlaps an existing booking.") : ConflictException(message);
