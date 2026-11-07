using BookIt.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace BookIt.Api.Application.Abstractions;

/// <summary>
/// The slice of the EF Core context the application services need. Keeps Application independent of the
/// concrete Infrastructure DbContext; EF Core itself is accepted as the unit-of-work/repository abstraction.
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Service> Services { get; }
    DbSet<Booking> Bookings { get; }
    DbSet<Invoice> Invoices { get; }

    /// <summary>
    /// Persists changes. Database constraint violations that represent business conflicts are surfaced as
    /// ConflictException / BookingConflictException.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
