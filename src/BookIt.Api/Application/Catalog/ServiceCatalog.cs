using BookIt.Api.Application.Abstractions;
using BookIt.Api.Application.Contracts;
using BookIt.Api.Application.Exceptions;
using BookIt.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookIt.Api.Application.Catalog;

public class ServiceCatalog(IAppDbContext db, ILogger<ServiceCatalog> log) : IServiceCatalog
{
    public async Task<IReadOnlyList<ServiceDto>> ListAsync(bool includeInactive, CancellationToken ct = default) =>
        (await db.Services.AsNoTracking().Where(s => includeInactive || s.IsActive).OrderBy(s => s.Id).ToListAsync(ct))
            .Select(ServiceDto.From).ToList();

    public async Task<ServiceDto> GetAsync(int id, CancellationToken ct = default) =>
        ServiceDto.From(await Find(id, ct));

    public async Task<ServiceDto> CreateAsync(ServiceRequest r, CancellationToken ct = default)
    {
        var s = new Service { Name = r.Name.Trim(), Description = r.Description, Price = r.Price, DurationMinutes = r.DurationMinutes, IsActive = r.IsActive };
        db.Services.Add(s);
        await db.SaveChangesAsync(ct);
        log.LogInformation("Service {ServiceId} created", s.Id);
        return ServiceDto.From(s);
    }

    public async Task<ServiceDto> UpdateAsync(int id, ServiceRequest r, CancellationToken ct = default)
    {
        var s = await Find(id, ct);
        s.Name = r.Name.Trim(); s.Description = r.Description; s.Price = r.Price; s.DurationMinutes = r.DurationMinutes; s.IsActive = r.IsActive;
        await db.SaveChangesAsync(ct);
        log.LogInformation("Service {ServiceId} updated", s.Id);
        return ServiceDto.From(s);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var s = await Find(id, ct);
        if (await db.Bookings.AnyAsync(b => b.ServiceId == id, ct))
            throw new ConflictException("Service has bookings; deactivate it instead (IsActive=false).");
        db.Services.Remove(s);
        await db.SaveChangesAsync(ct);
        log.LogInformation("Service {ServiceId} deleted", id);
    }

    private async Task<Service> Find(int id, CancellationToken ct) =>
        await db.Services.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Service not found.");
}
