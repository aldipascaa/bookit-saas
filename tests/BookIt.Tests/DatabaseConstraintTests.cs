using BookIt.Api.Application.Exceptions;
using BookIt.Api.Domain;
using BookIt.Api.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BookIt.Tests;

/// <summary>
/// Proves the database itself rejects overlapping bookings, independent of any application-level check
/// (this is what protects multiple app instances and racing requests).
/// </summary>
public class DatabaseConstraintTests : IDisposable
{
    private readonly SqliteConnection _conn = new("DataSource=:memory:");
    private readonly DateTime _t0 = new(2030, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    public DatabaseConstraintTests()
    {
        _conn.Open();
        using var db = NewDb();
        db.Database.Migrate();
        db.Users.Add(new User { Email = "u@t.local", FullName = "U", PasswordHash = "x" });
        db.Services.AddRange(new Service { Name = "A", Price = 1, DurationMinutes = 60 }, new Service { Name = "B", Price = 1, DurationMinutes = 60 });
        db.SaveChanges();
    }

    public void Dispose() => _conn.Dispose();

    private BookItDb NewDb() => new(new DbContextOptionsBuilder<BookItDb>().UseSqlite(_conn).Options);

    private static Booking Make(int serviceId, DateTime start, int minutes = 60, BookingStatus status = BookingStatus.Confirmed) =>
        new() { ServiceId = serviceId, CustomerId = 1, StartTime = start, EndTime = start.AddMinutes(minutes), Status = status };

    [Fact]
    public async Task Migrations_apply_cleanly_and_are_recorded()
    {
        using var db = NewDb();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.True((await db.Database.GetAppliedMigrationsAsync()).Count() >= 2);
    }

    [Fact]
    public async Task Insert_that_overlaps_is_rejected_by_the_database_without_any_precheck()
    {
        using var first = NewDb();
        first.Bookings.Add(Make(1, _t0));
        await first.SaveChangesAsync();

        using var second = NewDb(); // no application-level overlap check is performed here
        second.Bookings.Add(Make(1, _t0.AddMinutes(30)));
        await Assert.ThrowsAsync<BookingConflictException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Update_that_creates_an_overlap_is_rejected()
    {
        using var db = NewDb();
        var a = Make(1, _t0);
        var b = Make(1, _t0.AddMinutes(120));
        db.Bookings.AddRange(a, b);
        await db.SaveChangesAsync();

        b.StartTime = _t0.AddMinutes(30);
        b.EndTime = b.StartTime.AddMinutes(60);
        await Assert.ThrowsAsync<BookingConflictException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Un_cancelling_into_an_occupied_slot_is_rejected()
    {
        using var db = NewDb();
        var cancelled = Make(1, _t0, status: BookingStatus.Cancelled);
        db.Bookings.AddRange(Make(1, _t0), cancelled);
        await db.SaveChangesAsync();

        cancelled.Status = BookingStatus.Confirmed;
        await Assert.ThrowsAsync<BookingConflictException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Cancelled_adjacent_and_other_service_bookings_do_not_conflict()
    {
        using var db = NewDb();
        db.Bookings.AddRange(
            Make(1, _t0),
            Make(1, _t0, status: BookingStatus.Cancelled), // cancelled rows never conflict
            Make(1, _t0.AddMinutes(60)),                    // back-to-back
            Make(2, _t0));                                  // other service, same time
        await db.SaveChangesAsync();
        Assert.Equal(4, await db.Bookings.CountAsync());
    }

    [Fact]
    public async Task Duplicate_email_is_translated_to_a_conflict()
    {
        using var db = NewDb();
        db.Users.Add(new User { Email = "u@t.local", FullName = "Dup", PasswordHash = "x" });
        await Assert.ThrowsAsync<ConflictException>(() => db.SaveChangesAsync());
    }
}
