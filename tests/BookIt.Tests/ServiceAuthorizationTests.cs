using System.Net;
using System.Net.Http.Json;
using BookIt.Tests.Support;

namespace BookIt.Tests;

public class ServiceAuthorizationTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private static readonly object NewService = new { name = "Coaching", description = "1:1", price = 75, durationMinutes = 45 };

    [Fact]
    public async Task Services_are_listed_publicly()
    {
        var list = await f.CreateClient().GetFromJsonAsync<List<ServiceResp>>("/api/services");
        Assert.True(list!.Count >= 3);
    }

    [Fact]
    public async Task Anonymous_cannot_manage_services_401()
    {
        var anon = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/services", NewService)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PutAsJsonAsync("/api/services/1", NewService)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.DeleteAsync("/api/services/1")).StatusCode);
    }

    [Fact]
    public async Task Regular_user_cannot_manage_services_403()
    {
        var (c, _) = await f.NewUserClientAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/services", NewService)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsJsonAsync("/api/services/1", NewService)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.DeleteAsync("/api/services/1")).StatusCode);

        // and nothing changed
        var svc = (await c.GetFromJsonAsync<ServiceResp>("/api/services/1"))!;
        Assert.Equal("Initial Consultation", svc.Name);
    }

    [Fact]
    public async Task Regular_user_can_still_book()
    {
        var (c, _) = await f.NewUserClientAsync();
        var res = await c.PostAsJsonAsync("/api/bookings", new { serviceId = 1, startTime = DateTime.UtcNow.Date.AddDays(90).AddHours(9) });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }

    [Fact]
    public async Task Admin_can_create_update_and_delete_services()
    {
        var (admin, _) = await f.AdminClientAsync();
        var created = await admin.PostAsJsonAsync("/api/services", NewService);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var svc = (await created.Content.ReadFromJsonAsync<ServiceResp>())!;

        var updated = await admin.PutAsJsonAsync($"/api/services/{svc.Id}", new { name = "Coaching Plus", price = 90, durationMinutes = 60, isActive = true });
        Assert.Equal("Coaching Plus", (await updated.Content.ReadFromJsonAsync<ServiceResp>())!.Name);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/services/{svc.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/services/{svc.Id}")).StatusCode);
    }

    [Fact]
    public async Task Inactive_services_are_hidden_from_everyone_except_admins()
    {
        var (admin, _) = await f.AdminClientAsync();
        var svc = (await (await admin.PostAsJsonAsync("/api/services", new { name = "Hidden", price = 10, durationMinutes = 15, isActive = false })).Content.ReadFromJsonAsync<ServiceResp>())!;

        var (user, _) = await f.NewUserClientAsync();
        Assert.DoesNotContain(await user.GetFromJsonAsync<List<ServiceResp>>("/api/services?includeInactive=true") ?? [], s => s.Id == svc.Id);
        Assert.Contains((await admin.GetFromJsonAsync<List<ServiceResp>>("/api/services?includeInactive=true"))!, s => s.Id == svc.Id);

        // and an inactive service cannot be booked
        var book = await user.PostAsJsonAsync("/api/bookings", new { serviceId = svc.Id, startTime = DateTime.UtcNow.Date.AddDays(91).AddHours(9) });
        Assert.Equal(HttpStatusCode.BadRequest, book.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_service_with_bookings_is_409()
    {
        var (user, _) = await f.NewUserClientAsync();
        await user.PostAsJsonAsync("/api/bookings", new { serviceId = 3, startTime = DateTime.UtcNow.Date.AddDays(95).AddHours(9) });
        var (admin, _) = await f.AdminClientAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync("/api/services/3")).StatusCode);
    }
}
