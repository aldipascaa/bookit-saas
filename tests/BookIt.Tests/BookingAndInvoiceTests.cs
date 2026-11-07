using System.Net;
using System.Net.Http.Json;
using BookIt.Api.Data;
using BookIt.Api.Domain;
using BookIt.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BookIt.Tests;

public class InvoiceCalculatorTests
{
    [Fact]
    public void Total_is_service_price_plus_ten_percent_tax()
    {
        var items = InvoiceCalculator.BuildLineItems(new Service { Name = "X", Price = 120m, DurationMinutes = 60 });
        Assert.Equal(2, items.Count);
        Assert.Equal(120m, items[0].LineTotal);
        Assert.Equal(12m, items[1].LineTotal);
        Assert.Equal(132m, InvoiceCalculator.Total(items));
    }

    [Fact]
    public void Tax_is_rounded_to_cents()
    {
        // 33.33 * 10% = 3.333 -> 3.33 ; total 36.66
        var items = InvoiceCalculator.BuildLineItems(new Service { Name = "X", Price = 33.33m, DurationMinutes = 30 });
        Assert.Equal(3.33m, items[1].UnitPrice);
        Assert.Equal(36.66m, InvoiceCalculator.Total(items));
    }

    [Fact]
    public void Free_service_has_no_tax_line_and_zero_total()
    {
        var items = InvoiceCalculator.BuildLineItems(new Service { Name = "Free", Price = 0m, DurationMinutes = 15 });
        Assert.Single(items);
        Assert.Equal(0m, InvoiceCalculator.Total(items));
    }

    [Fact]
    public void Line_total_multiplies_quantity()
    {
        var l = new InvoiceLineItem { Quantity = 3, UnitPrice = 19.99m };
        Assert.Equal(59.97m, l.LineTotal);
    }
}

/// <summary>Scheduler tests against a real (in-memory) SQLite database.</summary>
public class BookingSchedulerTests : IDisposable
{
    private readonly SqliteConnection _conn = new("DataSource=:memory:");
    private readonly BookItDb _db;
    private readonly Service _svc;
    private readonly DateTime _t0 = DateTime.UtcNow.Date.AddDays(5).AddHours(10);

    public BookingSchedulerTests()
    {
        _conn.Open();
        _db = new BookItDb(new DbContextOptionsBuilder<BookItDb>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        _db.Users.Add(new User { Email = "u@t.local", FullName = "U", PasswordHash = "x" });
        _svc = new Service { Name = "Hour", Price = 100m, DurationMinutes = 60 };
        _db.Services.Add(_svc);
        _db.SaveChanges();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    private BookingScheduler Sut => new(_db);

    [Fact]
    public async Task Overlapping_booking_is_rejected()
    {
        await Sut.CreateAsync(1, _svc.Id, _t0, null);
        // starts 30 min into the existing hour
        await Assert.ThrowsAsync<BookingConflictException>(() => Sut.CreateAsync(1, _svc.Id, _t0.AddMinutes(30), null));
        // identical slot
        await Assert.ThrowsAsync<BookingConflictException>(() => Sut.CreateAsync(1, _svc.Id, _t0, null));
        // starts before but runs into it
        await Assert.ThrowsAsync<BookingConflictException>(() => Sut.CreateAsync(1, _svc.Id, _t0.AddMinutes(-30), null));
    }

    [Fact]
    public async Task Back_to_back_bookings_are_allowed()
    {
        await Sut.CreateAsync(1, _svc.Id, _t0, null);
        var next = await Sut.CreateAsync(1, _svc.Id, _t0.AddMinutes(60), null);   // starts exactly when first ends
        var before = await Sut.CreateAsync(1, _svc.Id, _t0.AddMinutes(-60), null); // ends exactly when first starts
        Assert.True(next.Id > 0 && before.Id > 0);
    }

    [Fact]
    public async Task Cancelled_booking_frees_the_slot()
    {
        var b = await Sut.CreateAsync(1, _svc.Id, _t0, null);
        b.Status = BookingStatus.Cancelled;
        await _db.SaveChangesAsync();
        var again = await Sut.CreateAsync(1, _svc.Id, _t0, null);
        Assert.NotEqual(b.Id, again.Id);
    }

    [Fact]
    public async Task Rescheduling_into_another_booking_is_rejected_but_own_slot_is_ok()
    {
        var a = await Sut.CreateAsync(1, _svc.Id, _t0, null);
        await Sut.CreateAsync(1, _svc.Id, _t0.AddMinutes(120), null);
        await Assert.ThrowsAsync<BookingConflictException>(() => Sut.RescheduleAsync(a, _t0.AddMinutes(90)));
        await Sut.RescheduleAsync(a, _t0.AddMinutes(15)); // overlaps only itself
        Assert.Equal(_t0.AddMinutes(15), a.StartTime);
    }

    [Fact]
    public async Task Past_start_time_is_rejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut.CreateAsync(1, _svc.Id, DateTime.UtcNow.AddHours(-2), null));
    }
}

public class BookingApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    public BookingApiTests(ApiFactory f) => _f = f;

    private record BookingResp(int Id, int ServiceId, DateTime StartTime, DateTime EndTime, string Status);
    private record LineResp(string Description, int Quantity, decimal UnitPrice, decimal LineTotal);
    private record InvoiceResp(int Id, string Number, decimal Total, bool IsPaid, string? PaymentReference, List<LineResp> LineItems);

    [Fact]
    public async Task Api_returns_409_for_double_booking()
    {
        var (c, _) = await _f.NewUserClientAsync();
        var start = DateTime.UtcNow.Date.AddDays(20).AddHours(9);
        var first = await c.PostAsJsonAsync("/api/bookings", new { serviceId = 2, startTime = start });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // a different user colliding with the same service/time
        var (c2, _) = await _f.NewUserClientAsync();
        var clash = await c2.PostAsJsonAsync("/api/bookings", new { serviceId = 2, startTime = start.AddMinutes(30) });
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
    }

    [Fact]
    public async Task Full_flow_booking_invoice_mock_payment()
    {
        var (c, _) = await _f.NewUserClientAsync();
        var res = await c.PostAsJsonAsync("/api/bookings", new { serviceId = 1, startTime = DateTime.UtcNow.Date.AddDays(30).AddHours(14) });
        var booking = (await res.Content.ReadFromJsonAsync<BookingResp>())!;
        Assert.Equal("Confirmed", booking.Status);
        Assert.Equal(30, (booking.EndTime - booking.StartTime).TotalMinutes);

        var inv = await c.PostAsync($"/api/bookings/{booking.Id}/invoice", null);
        Assert.Equal(HttpStatusCode.Created, inv.StatusCode);
        var invoice = (await inv.Content.ReadFromJsonAsync<InvoiceResp>())!;
        Assert.Equal(55m, invoice.Total);                       // 50 + 10% tax
        Assert.Equal(invoice.Total, invoice.LineItems.Sum(l => l.LineTotal));
        Assert.False(invoice.IsPaid);

        // second invoice for same booking is rejected
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/api/bookings/{booking.Id}/invoice", null)).StatusCode);

        // declined mock payment leaves it unpaid
        Assert.Equal(HttpStatusCode.PaymentRequired, (await c.PostAsJsonAsync($"/api/invoices/{invoice.Id}/pay", new { succeed = false })).StatusCode);

        var paid = await c.PostAsJsonAsync($"/api/invoices/{invoice.Id}/pay", new { succeed = true });
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        var paidBody = (await paid.Content.ReadFromJsonAsync<InvoiceResp>())!;
        Assert.True(paidBody.IsPaid);
        Assert.StartsWith("MOCK-", paidBody.PaymentReference);
    }

    [Fact]
    public async Task Users_cannot_see_each_others_bookings()
    {
        var (a, _) = await _f.NewUserClientAsync();
        var (b, _) = await _f.NewUserClientAsync();
        var res = await a.PostAsJsonAsync("/api/bookings", new { serviceId = 1, startTime = DateTime.UtcNow.Date.AddDays(40).AddHours(8) });
        var booking = (await res.Content.ReadFromJsonAsync<BookingResp>())!;
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/bookings/{booking.Id}")).StatusCode);
    }

    [Fact]
    public async Task Services_are_listed_publicly_and_crud_requires_auth()
    {
        var anon = _f.CreateClient();
        var list = await anon.GetFromJsonAsync<List<Service>>("/api/services");
        Assert.True(list!.Count >= 3);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.PostAsJsonAsync("/api/services", new { name = "x", price = 1, durationMinutes = 15 })).StatusCode);

        var (c, _) = await _f.NewUserClientAsync();
        var created = await c.PostAsJsonAsync("/api/services", new { name = "Coaching", price = 75, durationMinutes = 45 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var svc = (await created.Content.ReadFromJsonAsync<Service>())!;
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/services/{svc.Id}")).StatusCode);
    }
}
