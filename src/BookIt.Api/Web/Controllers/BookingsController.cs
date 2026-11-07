using BookIt.Api.Application.Bookings;
using BookIt.Api.Application.Contracts;
using BookIt.Api.Application.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookIt.Api.Web.Controllers;

[Authorize]
[Route("api/bookings")]
public class BookingsController(IBookingService bookings, IInvoiceService invoices) : ApiControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<BookingDto>> List(CancellationToken ct) => await bookings.ListAsync(CurrentUserId, ct);

    [HttpGet("{id:int}")]
    public async Task<BookingDto> Get(int id, CancellationToken ct) => await bookings.GetAsync(CurrentUserId, id, ct);

    [HttpPost]
    public async Task<ActionResult<BookingDto>> Create(CreateBookingRequest request, CancellationToken ct)
    {
        var created = await bookings.CreateAsync(CurrentUserId, request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<BookingDto> Reschedule(int id, RescheduleRequest request, CancellationToken ct) =>
        await bookings.RescheduleAsync(CurrentUserId, id, request, ct);

    [HttpPost("{id:int}/cancel")]
    public async Task<BookingDto> Cancel(int id, CancellationToken ct) => await bookings.CancelAsync(CurrentUserId, id, ct);

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await bookings.DeleteAsync(CurrentUserId, id, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/invoice")]
    public async Task<ActionResult<InvoiceDto>> GenerateInvoice(int id, CancellationToken ct)
    {
        var invoice = await invoices.CreateForBookingAsync(CurrentUserId, id, ct);
        return CreatedAtAction("Get", "Invoices", new { id = invoice.Id }, invoice);
    }
}
