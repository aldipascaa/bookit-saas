using BookIt.Api.Application.Contracts;

namespace BookIt.Api.Application.Bookings;

/// <summary>All operations are scoped to the given customer; bookings of other customers behave as non-existent.</summary>
public interface IBookingService
{
    Task<IReadOnlyList<BookingDto>> ListAsync(int customerId, CancellationToken ct = default);
    Task<BookingDto> GetAsync(int customerId, int id, CancellationToken ct = default);
    Task<BookingDto> CreateAsync(int customerId, CreateBookingRequest request, CancellationToken ct = default);
    Task<BookingDto> RescheduleAsync(int customerId, int id, RescheduleRequest request, CancellationToken ct = default);
    Task<BookingDto> CancelAsync(int customerId, int id, CancellationToken ct = default);
    /// <summary>Taken time ranges of a service (any customer) inside [from, to). Exposes times only.</summary>
    Task<IReadOnlyList<BusySlotDto>> BusyAsync(int serviceId, DateTime from, DateTime to, CancellationToken ct = default);
    Task DeleteAsync(int customerId, int id, CancellationToken ct = default);
}
