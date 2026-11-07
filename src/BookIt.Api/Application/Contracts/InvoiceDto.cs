using BookIt.Api.Domain;

namespace BookIt.Api.Application.Contracts;

public record InvoiceDto(int Id, string Number, int BookingId, DateTime IssuedAt, IReadOnlyList<LineItemDto> LineItems,
    decimal Total, bool IsPaid, DateTime? PaidAt, string? PaymentReference)
{
    public static InvoiceDto From(Invoice i) => new(i.Id, i.Number, i.BookingId, DateTime.SpecifyKind(i.IssuedAt, DateTimeKind.Utc),
        i.LineItems.Select(l => new LineItemDto(l.Description, l.Quantity, l.UnitPrice, l.LineTotal)).ToList(),
        i.Total, i.IsPaid, i.PaidAt is { } p ? DateTime.SpecifyKind(p, DateTimeKind.Utc) : null, i.PaymentReference);
}
