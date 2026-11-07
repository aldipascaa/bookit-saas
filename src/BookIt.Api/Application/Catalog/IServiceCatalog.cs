using BookIt.Api.Application.Contracts;

namespace BookIt.Api.Application.Catalog;

public interface IServiceCatalog
{
    Task<IReadOnlyList<ServiceDto>> ListAsync(bool includeInactive, CancellationToken ct = default);
    Task<ServiceDto> GetAsync(int id, CancellationToken ct = default);
    Task<ServiceDto> CreateAsync(ServiceRequest request, CancellationToken ct = default);
    Task<ServiceDto> UpdateAsync(int id, ServiceRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
