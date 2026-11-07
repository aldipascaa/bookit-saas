using System.Net;
using System.Net.Http.Json;
using BookIt.Api.Application.Exceptions;
using BookIt.Api.Domain;
using BookIt.Api.Infrastructure.Persistence;
using BookIt.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookIt.Tests;

/// <summary>
/// Races many simultaneous booking requests against a file-backed SQLite database (independent connections,
/// like independent app instances). The in-process semaphore is gone, so only the database can arbitrate.
/// </summary>
public class ConcurrencyTests(FileDbApiFactory f) : IClassFixture<FileDbApiFactory>
{
    private const int Racers = 12;

    private async Task<List<HttpClient>> UsersAsync(int n)
    {
        var clients = new List<HttpClient>();
        for (var i = 0; i < n; i++) clients.Add((await f.NewUserClientAsync()).Client);
        return clients;
    }

    [Fact]
    public async Task Simultaneous_requests_for_the_same_slot_yield_exactly_one_booking()
    {
        var users = await UsersAsync(Racers);
        var start = DateTime.UtcNow.Date.AddDays(100).AddHours(9);
        var gate = new TaskCompletionSource();

        var tasks = users.Select(async c =>
        {
            await gate.Task;
            return await c.PostAsJsonAsync("/api/bookings", new { serviceId = 2, startTime = start });
        }).ToList();
        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(Racers - 1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.DoesNotContain(results, r => (int)r.StatusCode >= 500);
        await AssertNoOverlapsAsync();
    }

    [Fact]
    public async Task Simultaneous_overlapping_but_offset_requests_yield_exactly_one_booking()
    {
        var users = await UsersAsync(Racers);
        var start = DateTime.UtcNow.Date.AddDays(101).AddHours(9); // 60-minute service, offsets of 5 minutes all overlap
        var gate = new TaskCompletionSource();

        var tasks = users.Select(async (c, i) =>
        {
            await gate.Task;
            return await c.PostAsJsonAsync("/api/bookings", new { serviceId = 2, startTime = start.AddMinutes(i * 5) });
        }).ToList();
        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.DoesNotContain(results, r => (int)r.StatusCode >= 500);
        await AssertNoOverlapsAsync();
    }

    [Fact]
    public async Task Simultaneous_requests_for_different_services_all_succeed()
    {
        var users = await UsersAsync(3);
        var start = DateTime.UtcNow.Date.AddDays(102).AddHours(9);
        var results = await Task.WhenAll(users.Select((c, i) => c.PostAsJsonAsync("/api/bookings", new { serviceId = i + 1, startTime = start })));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
    }

    [Fact]
    public async Task Database_rejects_an_overlap_even_when_the_application_precheck_is_bypassed()
    {
        // Two independent DbContexts (separate connections to the same file) insert overlapping rows directly.
        var start = DateTime.UtcNow.Date.AddDays(103).AddHours(9);
        using var s1 = f.Services.CreateScope();
        using var s2 = f.Services.CreateScope();
        var db1 = s1.ServiceProvider.GetRequiredService<BookItDb>();
        var db2 = s2.ServiceProvider.GetRequiredService<BookItDb>();
        var userId = (await db1.Users.FirstAsync()).Id;

        Booking Make(int mins) => new() { ServiceId = 3, CustomerId = userId, StartTime = start.AddMinutes(mins), EndTime = start.AddMinutes(mins + 60), Status = BookingStatus.Confirmed };
        db1.Bookings.Add(Make(0));
        db2.Bookings.Add(Make(30));

        var outcomes = await Task.WhenAll(Attempt(db1), Attempt(db2));
        Assert.Equal(1, outcomes.Count(ok => ok));

        static async Task<bool> Attempt(BookItDb db)
        {
            try { await db.SaveChangesAsync(); return true; }
            catch (BookingConflictException) { return false; }
        }
    }

    [Fact]
    public async Task Concurrent_payments_of_one_invoice_succeed_exactly_once()
    {
        var (c, _) = await f.NewUserClientAsync();
        var b = (await (await c.PostAsJsonAsync("/api/bookings", new { serviceId = 1, startTime = DateTime.UtcNow.Date.AddDays(104).AddHours(9) })).Content.ReadFromJsonAsync<BookingResp>())!;
        var inv = (await (await c.PostAsync($"/api/bookings/{b.Id}/invoice", null)).Content.ReadFromJsonAsync<InvoiceResp>())!;

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => c.PostAsJsonAsync($"/api/invoices/{inv.Id}/pay", new { succeed = true })));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(5, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    private async Task AssertNoOverlapsAsync()
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookItDb>();
        var live = await db.Bookings.AsNoTracking().Where(b => b.Status != BookingStatus.Cancelled).ToListAsync();
        foreach (var g in live.GroupBy(b => b.ServiceId))
        {
            var list = g.OrderBy(b => b.StartTime).ToList();
            for (var i = 1; i < list.Count; i++)
                Assert.True(list[i - 1].EndTime <= list[i].StartTime, $"Overlap on service {g.Key}: {list[i - 1].Id} / {list[i].Id}");
        }
    }
}
