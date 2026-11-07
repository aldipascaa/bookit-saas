using BookIt.Api.Application.Contracts;

namespace BookIt.Api.Application.Invoices;

/// <summary>All operations are scoped to the given customer; invoices of other customers behave as non-existent.</summary>
public interface IInvoiceService
{
    Task<IReadOnlyList<InvoiceDto>> ListAsync(int customerId, CancellationToken ct = default);
    Task<InvoiceDto> GetAsync(int customerId, int id, CancellationToken ct = default);
    Task<InvoiceDto> CreateForBookingAsync(int customerId, int bookingId, CancellationToken ct = default);
    Task<InvoiceDto> PayAsync(int customerId, int id, bool succeed, CancellationToken ct = default);
}
