using BookIt.Api.Application.Abstractions;
using BookIt.Api.Application.Contracts;
using BookIt.Api.Application.Exceptions;
using BookIt.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookIt.Api.Application.Invoices;

public class InvoiceService(IAppDbContext db, TimeProvider clock, ILogger<InvoiceService> log) : IInvoiceService
{
    private IQueryable<Invoice> Mine(int customerId) =>
        db.Invoices.Include(i => i.LineItems).Where(i => i.CustomerId == customerId);

    public async Task<IReadOnlyList<InvoiceDto>> ListAsync(int customerId, CancellationToken ct = default) =>
        (await Mine(customerId).AsNoTracking().OrderByDescending(i => i.IssuedAt).ToListAsync(ct)).Select(InvoiceDto.From).ToList();

    public async Task<InvoiceDto> GetAsync(int customerId, int id, CancellationToken ct = default) =>
        InvoiceDto.From(await Find(customerId, id, ct));

    public async Task<InvoiceDto> CreateForBookingAsync(int customerId, int bookingId, CancellationToken ct = default)
    {
        var b = await db.Bookings.Include(x => x.Service).Include(x => x.Invoice)
            .FirstOrDefaultAsync(x => x.Id == bookingId && x.CustomerId == customerId, ct)
            ?? throw new NotFoundException("Booking not found.");
        if (b.Status == BookingStatus.Cancelled) throw new BusinessRuleException("Cannot invoice a cancelled booking.");
        if (b.Invoice is not null) throw new ConflictException("Invoice already exists for this booking.");

        var now = clock.GetUtcNow().UtcDateTime;
        var items = InvoiceCalculator.BuildLineItems(b.Service!);
        var invoice = new Invoice
        {
            BookingId = b.Id, CustomerId = b.CustomerId, LineItems = items, Total = InvoiceCalculator.Total(items), IssuedAt = now,
            Number = $"INV-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}"
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct); // the unique BookingId index turns a creation race into ConflictException
        log.LogInformation("Invoice {InvoiceNumber} created for booking {BookingId}", invoice.Number, b.Id);
        return InvoiceDto.From(invoice);
    }

    /// <summary>MOCK payment: no provider, no keys, no card data - it just flips the paid flag.</summary>
    public async Task<InvoiceDto> PayAsync(int customerId, int id, bool succeed, CancellationToken ct = default)
    {
        var existing = await Find(customerId, id, ct);
        if (existing.IsPaid) throw new ConflictException("Invoice is already paid.");
        if (!succeed)
        {
            log.LogInformation("Mock payment declined for invoice {InvoiceId}", id);
            throw new PaymentDeclinedException();
        }

        var reference = $"MOCK-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}";
        var paidAt = clock.GetUtcNow().UtcDateTime;
        // Conditional UPDATE: of two concurrent pay requests only one can flip IsPaid.
        var rows = await db.Invoices.Where(i => i.Id == id && i.CustomerId == customerId && !i.IsPaid)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.IsPaid, true).SetProperty(i => i.PaidAt, paidAt)
                .SetProperty(i => i.PaymentReference, reference), ct);
        if (rows == 0) throw new ConflictException("Invoice is already paid.");

        log.LogInformation("Invoice {InvoiceId} paid ({Reference})", id, reference);
        return InvoiceDto.From(await Find(customerId, id, ct, noTracking: true));
    }

    private async Task<Invoice> Find(int customerId, int id, CancellationToken ct, bool noTracking = false)
    {
        var q = Mine(customerId);
        if (noTracking) q = q.AsNoTracking();
        return await q.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Invoice not found.");
    }
}
