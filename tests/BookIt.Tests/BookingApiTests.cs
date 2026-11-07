using System.Net;
using System.Net.Http.Json;
using BookIt.Tests.Support;

namespace BookIt.Tests;

public class BookingApiTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Api_returns_409_problem_details_for_double_booking()
    {
        var (c, _) = await f.NewUserClientAsync();
        var start = DateTime.UtcNow.Date.AddDays(20).AddHours(9);
        var first = await c.PostAsJsonAsync("/api/bookings", new { serviceId = 2, startTime = start });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // a different user colliding with the same service/time
        var (c2, _) = await f.NewUserClientAsync();
        var clash = await c2.PostAsJsonAsync("/api/bookings", new { serviceId = 2, startTime = start.AddMinutes(30) });
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        Assert.Equal("application/problem+json", clash.Content.Headers.ContentType?.MediaType);
        var problem = (await clash.Content.ReadFromJsonAsync<ProblemResp>())!;
        Assert.Equal(409, problem.Status);
        Assert.Contains("overlaps", problem.Detail);
    }

    [Fact]
    public async Task Full_flow_booking_invoice_mock_payment()
    {
        var (c, _) = await f.NewUserClientAsync();
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
        Assert.False((await c.GetFromJsonAsync<InvoiceResp>($"/api/invoices/{invoice.Id}"))!.IsPaid);

        var paid = await c.PostAsJsonAsync($"/api/invoices/{invoice.Id}/pay", new { succeed = true });
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        var paidBody = (await paid.Content.ReadFromJsonAsync<InvoiceResp>())!;
        Assert.True(paidBody.IsPaid);
        Assert.StartsWith("MOCK-", paidBody.PaymentReference);
    }

    [Fact]
    public async Task Cancelling_frees_slot_and_cancelled_booking_cannot_be_invoiced()
    {
        var (c, _) = await f.NewUserClientAsync();
        var start = DateTime.UtcNow.Date.AddDays(50).AddHours(11);
        var b = (await (await c.PostAsJsonAsync("/api/bookings", new { serviceId = 3, startTime = start })).Content.ReadFromJsonAsync<BookingResp>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/bookings", new { serviceId = 3, startTime = start })).StatusCode);

        var cancel = await c.PostAsync($"/api/bookings/{b.Id}/cancel", null);
        Assert.Equal("Cancelled", (await cancel.Content.ReadFromJsonAsync<BookingResp>())!.Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync($"/api/bookings/{b.Id}/invoice", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/bookings", new { serviceId = 3, startTime = start })).StatusCode);
    }

    [Fact]
    public async Task Paying_twice_is_409_and_unknown_invoice_is_404()
    {
        var (c, _) = await f.NewUserClientAsync();
        var b = (await (await c.PostAsJsonAsync("/api/bookings", new { serviceId = 1, startTime = DateTime.UtcNow.Date.AddDays(60).AddHours(9) })).Content.ReadFromJsonAsync<BookingResp>())!;
        var inv = (await (await c.PostAsync($"/api/bookings/{b.Id}/invoice", null)).Content.ReadFromJsonAsync<InvoiceResp>())!;
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync($"/api/invoices/{inv.Id}/pay", new { succeed = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/invoices/{inv.Id}/pay", new { succeed = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/invoices/99999")).StatusCode);
    }

    [Fact]
    public async Task Users_cannot_see_each_others_bookings_or_invoices()
    {
        var (a, _) = await f.NewUserClientAsync();
        var (b, _) = await f.NewUserClientAsync();
        var res = await a.PostAsJsonAsync("/api/bookings", new { serviceId = 1, startTime = DateTime.UtcNow.Date.AddDays(40).AddHours(8) });
        var booking = (await res.Content.ReadFromJsonAsync<BookingResp>())!;
        var inv = (await (await a.PostAsync($"/api/bookings/{booking.Id}/invoice", null)).Content.ReadFromJsonAsync<InvoiceResp>())!;

        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/bookings/{booking.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/invoices/{inv.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsJsonAsync($"/api/invoices/{inv.Id}/pay", new { succeed = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/api/bookings/{booking.Id}/cancel", null)).StatusCode);
        Assert.Empty((await b.GetFromJsonAsync<List<BookingResp>>("/api/bookings"))!);
    }

    [Fact]
    public async Task Rescheduling_over_http_checks_overlap()
    {
        var (c, _) = await f.NewUserClientAsync();
        var day = DateTime.UtcNow.Date.AddDays(70);
        var first = (await (await c.PostAsJsonAsync("/api/bookings", new { serviceId = 2, startTime = day.AddHours(9) })).Content.ReadFromJsonAsync<BookingResp>())!;
        await c.PostAsJsonAsync("/api/bookings", new { serviceId = 2, startTime = day.AddHours(12) });

        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/api/bookings/{first.Id}", new { startTime = day.AddHours(12).AddMinutes(15) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/api/bookings/{first.Id}", new { startTime = day.AddHours(10) })).StatusCode);
    }
}
