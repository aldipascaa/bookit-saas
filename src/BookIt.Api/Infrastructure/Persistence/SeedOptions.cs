namespace BookIt.Api.Infrastructure.Persistence;

/// <summary>Bound from the "Seed" configuration section.</summary>
public class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>Seeds the demo customer, demo admin and sample services. Enabled in appsettings.Development.json only.</summary>
    public bool DemoData { get; set; }

    /// <summary>Optional: provision an Admin on first start in any environment (supply the password via env var or secrets).</summary>
    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
}
