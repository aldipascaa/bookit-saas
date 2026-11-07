namespace BookIt.Api.Domain;

/// <remarks>
/// The numeric value of <see cref="Cancelled"/> is referenced by the overlap-guard triggers created in the
/// AddBookingOverlapGuard migration. Do not renumber without adding a migration that rewrites them.
/// </remarks>
public enum BookingStatus { Pending = 0, Confirmed = 1, Cancelled = 2, Completed = 3 }
