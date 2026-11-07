using BookIt.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BookIt.Api.Data;

public static class DbSeeder
{
    // Demo/test values only.
    public const string DemoEmail = "demo@bookit.test";
    public const string DemoPassword = "Demo#12345";

    public static async Task InitializeAsync(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookItDb>();
        // Creates the schema on first run (keeps the project free of EF CLI tooling).
        await db.Database.EnsureCreatedAsync();

        if (!await db.Users.AnyAsync())
        {
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
            var demo = new User { Email = DemoEmail, FullName = "Demo Customer" };
            demo.PasswordHash = hasher.HashPassword(demo, DemoPassword);
            db.Users.Add(demo);
        }
        if (!await db.Services.AnyAsync())
        {
            db.Services.AddRange(
                new Service { Name = "Initial Consultation", Description = "30 minute intro call", Price = 50m, DurationMinutes = 30 },
                new Service { Name = "Strategy Session", Description = "Deep-dive working session", Price = 120m, DurationMinutes = 60 },
                new Service { Name = "Full-day Workshop", Description = "On-site team workshop", Price = 800m, DurationMinutes = 480 });
        }
        await db.SaveChangesAsync();
    }
}
