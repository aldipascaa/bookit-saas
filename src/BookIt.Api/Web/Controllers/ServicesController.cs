using BookIt.Api.Application.Catalog;
using BookIt.Api.Application.Contracts;
using BookIt.Api.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookIt.Api.Web.Controllers;

[Route("api/services")]
public class ServicesController(IServiceCatalog catalog) : ApiControllerBase
{
    /// <summary>Public. <c>includeInactive</c> is honoured for Admins only.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<ServiceDto>> List([FromQuery] bool includeInactive = false, CancellationToken ct = default) =>
        await catalog.ListAsync(includeInactive && User.IsInRole(Roles.Admin), ct);

    [HttpGet("{id:int}")]
    public async Task<ServiceDto> Get(int id, CancellationToken ct) => await catalog.GetAsync(id, ct);

    [Authorize(Roles = Roles.Admin), HttpPost]
    public async Task<ActionResult<ServiceDto>> Create(ServiceRequest request, CancellationToken ct)
    {
        var created = await catalog.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [Authorize(Roles = Roles.Admin), HttpPut("{id:int}")]
    public async Task<ServiceDto> Update(int id, ServiceRequest request, CancellationToken ct) =>
        await catalog.UpdateAsync(id, request, ct);

    [Authorize(Roles = Roles.Admin), HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await catalog.DeleteAsync(id, ct);
        return NoContent();
    }
}
