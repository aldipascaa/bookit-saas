using BookIt.Api.Domain;

namespace BookIt.Api.Application.Contracts;

public record BookingDto(int Id, int ServiceId, string ServiceName, int CustomerId, DateTime StartTime, DateTime EndTime,
    string Status, string? Notes, int? InvoiceId)
{
    public static BookingDto From(Booking b) => new(b.Id, b.ServiceId, b.Service?.Name ?? "", b.CustomerId,
        DateTime.SpecifyKind(b.StartTime, DateTimeKind.Utc), DateTime.SpecifyKind(b.EndTime, DateTimeKind.Utc),
        b.Status.ToString(), b.Notes, b.Invoice?.Id);
}
