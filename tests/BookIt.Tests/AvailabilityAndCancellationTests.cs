using System.Net;
using System.Net.Http.Json;
using BookIt.Tests.Support;

namespace BookIt.Tests;

public class AvailabilityAndCancellationTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private record BusyResp(DateTime StartTime, DateTime EndTime);

    private static string BusyUrl(int serviceId, DateTime from, DateTime to) =>
        $"/api/bookings/busy?serviceId={serviceId}&from={Uri.EscapeDataString(from.ToString("o"))}&to={Uri.EscapeDataString(to.ToString("o"))}";

    private static async Task<BookingResp> Book(HttpClient c, int serviceId, DateTime start)
    {
        var res = await c.PostAsJsonAsync("/api/bookings", new { serviceId, startTime = start });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<BookingResp>())!;
    }

    [Fact]
    public async Task Busy_lists_other_customers_slots_without_identities_and_skips_cancelled()
    {
        var day = DateTime.UtcNow.Date.AddDays(40);
        var (a, _) = await f.NewUserClientAsync();
        var (b, _) = await f.NewUserClientAsync();
        var kept = await Book(a, 1, day.AddHours(9));
        var dropped = await Book(a, 1, day.AddHours(13));
        Assert.Equal(HttpStatusCode.OK, (await a.PostAsync($"/api/bookings/{dropped.Id}/cancel", null)).StatusCode);
        await Book(a, 1, day.AddDays(1).AddHours(9)); // outside the queried day

        var res = await b.GetAsync(BusyUrl(1, day, day.AddDays(1)));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var raw = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("customer", raw, StringComparison.OrdinalIgnoreCase);
        var slots = (await res.Content.ReadFromJsonAsync<List<BusyResp>>())!;
        var only = Assert.Single(slots);
        Assert.Equal(kept.StartTime, only.StartTime);
        Assert.Equal(kept.EndTime, only.EndTime);
    }

    [Fact]
    public async Task Busy_validates_its_input()
    {
        var (c, _) = await f.NewUserClientAsync();
        var day = DateTime.UtcNow.Date.AddDays(41);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(BusyUrl(1, day.AddDays(1), day))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(BusyUrl(1, day, day.AddDays(32)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(BusyUrl(999999, day, day.AddDays(1)))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().GetAsync(BusyUrl(1, day, day.AddDays(1)))).StatusCode);
    }

    [Fact]
    public async Task Cancel_is_idempotent()
    {
        var (c, _) = await f.NewUserClientAsync();
        var booking = await Book(c, 1, DateTime.UtcNow.Date.AddDays(42).AddHours(8));
        for (var i = 0; i < 2; i++)
        {
            var res = await c.PostAsync($"/api/bookings/{booking.Id}/cancel", null);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal("Cancelled", (await res.Content.ReadFromJsonAsync<BookingResp>())!.Status);
        }
    }

    [Fact]
    public async Task Paid_booking_cannot_be_cancelled()
    {
        var (c, _) = await f.NewUserClientAsync();
        var booking = await Book(c, 1, DateTime.UtcNow.Date.AddDays(43).AddHours(8));
        var invoice = (await (await c.PostAsync($"/api/bookings/{booking.Id}/invoice", null)).Content.ReadFromJsonAsync<InvoiceResp>())!;
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync($"/api/invoices/{invoice.Id}/pay", new { succeed = true })).StatusCode);

        var res = await c.PostAsync($"/api/bookings/{booking.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Contains("paid", (await res.Content.ReadFromJsonAsync<ProblemResp>())!.Detail);
        var still = (await c.GetFromJsonAsync<BookingResp>($"/api/bookings/{booking.Id}"))!;
        Assert.Equal("Confirmed", still.Status);
    }

    [Fact]
    public async Task Invoice_of_a_cancelled_booking_cannot_be_paid()
    {
        var (c, _) = await f.NewUserClientAsync();
        var booking = await Book(c, 1, DateTime.UtcNow.Date.AddDays(44).AddHours(8));
        var invoice = (await (await c.PostAsync($"/api/bookings/{booking.Id}/invoice", null)).Content.ReadFromJsonAsync<InvoiceResp>())!;
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync($"/api/bookings/{booking.Id}/cancel", null)).StatusCode);

        var res = await c.PostAsJsonAsync($"/api/invoices/{invoice.Id}/pay", new { succeed = true });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.False((await c.GetFromJsonAsync<InvoiceResp>($"/api/invoices/{invoice.Id}"))!.IsPaid);
    }
}
