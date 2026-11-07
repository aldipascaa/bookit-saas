using BookIt.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BookIt.Api.Infrastructure.Persistence;

/// <summary>Applies EF Core migrations and seeds data at startup.</summary>
public static class DbInitializer
{
    // Demo/test values only - seeded when Seed:DemoData is true (Development).
    public const string DemoEmail = "demo@bookit.test";
    public const string DemoPassword = "Demo#12345";
    public const string DemoAdminEmail = "admin@bookit.test";
    public const string DemoAdminPassword = "Admin#12345";

    public static async Task InitializeAsync(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookItDb>();
        var seed = scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value;
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DbInitializer));

        await db.Database.MigrateAsync();

        if (seed.DemoData)
        {
            await EnsureUserAsync(db, hasher, DemoEmail, "Demo Customer", DemoPassword, Roles.Customer);
            await EnsureUserAsync(db, hasher, DemoAdminEmail, "Demo Admin", DemoAdminPassword, Roles.Admin);
            if (!await db.Services.AnyAsync())
            {
                db.Services.AddRange(
                    new Service { Name = "Initial Consultation", Description = "30 minute intro call", Price = 50m, DurationMinutes = 30 },
                    new Service { Name = "Strategy Session", Description = "Deep-dive working session", Price = 120m, DurationMinutes = 60 },
                    new Service { Name = "Full-day Workshop", Description = "On-site team workshop", Price = 800m, DurationMinutes = 480 });
            }
        }

        if (!string.IsNullOrWhiteSpace(seed.AdminEmail) && !string.IsNullOrWhiteSpace(seed.AdminPassword))
            await EnsureUserAsync(db, hasher, seed.AdminEmail, "Administrator", seed.AdminPassword, Roles.Admin);

        await db.SaveChangesAsync();
        log.LogInformation("Database migrated (demo data: {DemoData})", seed.DemoData);
    }

    private static async Task EnsureUserAsync(BookItDb db, IPasswordHasher<User> hasher, string email, string fullName, string password, string role)
    {
        email = email.Trim().ToLowerInvariant();
        var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (existing is not null)
        {
            if (existing.Role != role && role == Roles.Admin) existing.Role = role; // promote a pre-existing account to the configured admin
            return;
        }
        var user = new User { Email = email, FullName = fullName, Role = role };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }
}
