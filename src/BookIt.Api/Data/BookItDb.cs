using BookIt.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace BookIt.Api.Data;

public class BookItDb : DbContext
{
    public BookItDb(DbContextOptions<BookItDb> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLineItem> InvoiceLineItems => Set<InvoiceLineItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(u => u.Email).IsUnique();
        b.Entity<Booking>().HasOne(x => x.Service).WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Booking>().HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Booking>().HasOne(x => x.Invoice).WithOne(i => i.Booking).HasForeignKey<Invoice>(i => i.BookingId);
        b.Entity<Invoice>().HasIndex(i => i.Number).IsUnique();
        b.Entity<Invoice>().HasMany(i => i.LineItems).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<InvoiceLineItem>().Ignore(l => l.LineTotal);
    }
}
