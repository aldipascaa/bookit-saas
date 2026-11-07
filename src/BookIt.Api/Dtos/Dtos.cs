using System.ComponentModel.DataAnnotations;
using BookIt.Api.Domain;

namespace BookIt.Api.Dtos;

public record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(8)] string Password,
    [Required, MinLength(1)] string FullName);

public record LoginRequest([Required] string Email, [Required] string Password);

public record AuthResponse(string Token, int UserId, string Email, string FullName);

public record ServiceRequest(
    [Required, MinLength(1)] string Name,
    string? Description,
    [Range(0, 1000000)] decimal Price,
    [Range(5, 1440)] int DurationMinutes,
    bool IsActive = true);

public record CreateBookingRequest([Required] int ServiceId, [Required] DateTime StartTime, string? Notes);

public record RescheduleRequest([Required] DateTime StartTime);

public record BookingDto(int Id, int ServiceId, string ServiceName, int CustomerId, DateTime StartTime, DateTime EndTime,
    string Status, string? Notes, int? InvoiceId)
{
    public static BookingDto From(Booking b) => new(b.Id, b.ServiceId, b.Service?.Name ?? "", b.CustomerId,
        DateTime.SpecifyKind(b.StartTime, DateTimeKind.Utc), DateTime.SpecifyKind(b.EndTime, DateTimeKind.Utc),
        b.Status.ToString(), b.Notes, b.Invoice?.Id);
}

public record LineItemDto(string Description, int Quantity, decimal UnitPrice, decimal LineTotal);

public record InvoiceDto(int Id, string Number, int BookingId, DateTime IssuedAt, IReadOnlyList<LineItemDto> LineItems,
    decimal Total, bool IsPaid, DateTime? PaidAt, string? PaymentReference)
{
    public static InvoiceDto From(Invoice i) => new(i.Id, i.Number, i.BookingId, i.IssuedAt,
        i.LineItems.Select(l => new LineItemDto(l.Description, l.Quantity, l.UnitPrice, l.LineTotal)).ToList(),
        i.Total, i.IsPaid, i.PaidAt, i.PaymentReference);
}

/// <summary>Mock payment: no card data is accepted or stored. Set Succeed=false to simulate a decline.</summary>
public record MockPaymentRequest(bool Succeed = true);
