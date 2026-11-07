using BookIt.Api.Application.Bookings;
using BookIt.Api.Application.Contracts;
using BookIt.Api.Application.Exceptions;
using BookIt.Api.Domain;
using BookIt.Api.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookIt.Tests;

/// <summary>Application-service tests against a real (in-memory) SQLite database built from the real migrations.</summary>
public class BookingServiceTests : IDisposable
{
    private readonly SqliteConnection _conn = new("DataSource=:memory:");
    private readonly BookItDb _db;
    private readonly Service _svc;
    private readonly DateTime _t0 = DateTime.UtcNow.Date.AddDays(5).AddHours(10);

    public BookingServiceTests()
    {
        _conn.Open();
        _db = new BookItDb(new DbContextOptionsBuilder<BookItDb>().UseSqlite(_conn).Options);
        _db.Database.Migrate();
        _db.Users.Add(new User { Email = "u@t.local", FullName = "U", PasswordHash = "x" });
        _svc = new Service { Name = "Hour", Price = 100m, DurationMinutes = 60 };
        _db.Services.Add(_svc);
        _db.SaveChanges();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    private BookingService Sut => new(_db, TimeProvider.System, NullLogger<BookingService>.Instance);
    private Task<BookingDto> Book(DateTime start) => Sut.CreateAsync(1, new CreateBookingRequest(_svc.Id, start, null));

    [Fact]
    public async Task Overlapping_booking_is_rejected()
    {
        await Book(_t0);
        await Assert.ThrowsAsync<BookingConflictException>(() => Book(_t0.AddMinutes(30)));  // starts inside
        await Assert.ThrowsAsync<BookingConflictException>(() => Book(_t0));                 // identical
        await Assert.ThrowsAsync<BookingConflictException>(() => Book(_t0.AddMinutes(-30))); // runs into it
    }

    [Fact]
    public async Task Back_to_back_bookings_are_allowed()
    {
        await Book(_t0);
        var next = await Book(_t0.AddMinutes(60));    // starts exactly when first ends
        var before = await Book(_t0.AddMinutes(-60)); // ends exactly when first starts
        Assert.True(next.Id > 0 && before.Id > 0);
    }

    [Fact]
    public async Task Cancelled_booking_frees_the_slot()
    {
        var b = await Book(_t0);
        await Sut.CancelAsync(1, b.Id);
        var again = await Book(_t0);
        Assert.NotEqual(b.Id, again.Id);
    }

    [Fact]
    public async Task Rescheduling_into_another_booking_is_rejected_but_own_slot_is_ok()
    {
        var a = await Book(_t0);
        await Book(_t0.AddMinutes(120));
        await Assert.ThrowsAsync<BookingConflictException>(() => Sut.RescheduleAsync(1, a.Id, new RescheduleRequest(_t0.AddMinutes(90))));
        var moved = await Sut.RescheduleAsync(1, a.Id, new RescheduleRequest(_t0.AddMinutes(15))); // overlaps only itself
        Assert.Equal(_t0.AddMinutes(15), moved.StartTime);
    }

    [Fact]
    public async Task Past_start_time_is_rejected()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Book(DateTime.UtcNow.AddHours(-2)));
    }

    [Fact]
    public async Task Unknown_service_is_not_found_and_inactive_service_is_a_rule_violation()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Sut.CreateAsync(1, new CreateBookingRequest(999, _t0, null)));
        _svc.IsActive = false;
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(() => Book(_t0));
    }

    [Fact]
    public async Task Bookings_of_other_customers_are_invisible()
    {
        var b = await Book(_t0);
        await Assert.ThrowsAsync<NotFoundException>(() => Sut.GetAsync(2, b.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => Sut.CancelAsync(2, b.Id));
    }
}
