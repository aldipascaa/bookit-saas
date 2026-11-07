using BookIt.Api.Application.Contracts;
using BookIt.Api.Application.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookIt.Api.Web.Controllers;

[Authorize]
[Route("api/invoices")]
public class InvoicesController(IInvoiceService invoices) : ApiControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<InvoiceDto>> List(CancellationToken ct) => await invoices.ListAsync(CurrentUserId, ct);

    [HttpGet("{id:int}")]
    public async Task<InvoiceDto> Get(int id, CancellationToken ct) => await invoices.GetAsync(CurrentUserId, id, ct);

    /// <summary>MOCK payment endpoint. No provider, no keys, no card data. Body <c>{"succeed": false}</c> simulates a decline (402).</summary>
    [HttpPost("{id:int}/pay")]
    public async Task<InvoiceDto> Pay(int id, MockPaymentRequest? request, CancellationToken ct) =>
        await invoices.PayAsync(CurrentUserId, id, request?.Succeed ?? true, ct);
}
