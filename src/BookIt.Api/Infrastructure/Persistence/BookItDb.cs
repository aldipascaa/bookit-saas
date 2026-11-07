using BookIt.Api.Application.Abstractions;
using BookIt.Api.Application.Exceptions;
using BookIt.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BookIt.Api.Infrastructure.Persistence;

public class BookItDb(DbContextOptions<BookItDb> options) : DbContext(options), IAppDbContext
{
    /// <summary>Message raised by the SQLite overlap-guard triggers (see the AddBookingOverlapGuard migration).</summary>
    public const string OverlapTriggerMessage = "booking_overlap";

    public DbSet<User> Users => Set<User>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLineItem> InvoiceLineItems => Set<InvoiceLineItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Email).HasMaxLength(254);
            e.Property(u => u.FullName).HasMaxLength(100);
            e.Property(u => u.Role).HasMaxLength(20).HasDefaultValue(Roles.Customer);
        });
        b.Entity<Service>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(100);
            e.Property(s => s.Description).HasMaxLength(500);
            e.Property(s => s.Price).HasPrecision(18, 2);
        });
        b.Entity<Booking>(e =>
        {
            e.HasOne(x => x.Service).WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Invoice).WithOne(i => i.Booking).HasForeignKey<Invoice>(i => i.BookingId);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.HasIndex(x => new { x.ServiceId, x.StartTime, x.EndTime });
            e.HasIndex(x => x.CustomerId);
        });
        b.Entity<Invoice>(e =>
        {
            e.HasIndex(i => i.Number).IsUnique();
            e.HasIndex(i => i.CustomerId);
            e.Property(i => i.Number).HasMaxLength(40);
            e.Property(i => i.Total).HasPrecision(18, 2);
            e.HasMany(i => i.LineItems).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<InvoiceLineItem>(e =>
        {
            e.Ignore(l => l.LineTotal);
            e.Property(l => l.UnitPrice).HasPrecision(18, 2);
        });
    }

    /// <summary>Translates database-level constraint failures into application conflict exceptions.</summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 } sqlite)
        {
            if (sqlite.Message.Contains(OverlapTriggerMessage, StringComparison.Ordinal))
                throw new BookingConflictException();
            if (sqlite.Message.Contains("Users.Email", StringComparison.Ordinal))
                throw new ConflictException("Email is already registered.");
            if (sqlite.Message.Contains("Invoices.BookingId", StringComparison.Ordinal))
                throw new ConflictException("Invoice already exists for this booking.");
            throw;
        }
    }
}
