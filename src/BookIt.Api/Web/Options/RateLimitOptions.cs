using System.ComponentModel.DataAnnotations;

namespace BookIt.Api.Web.Options;

/// <summary>Bound from "RateLimiting". Limits brute-force attempts on the auth endpoints, per client IP.</summary>
public class RateLimitOptions
{
    public const string SectionName = "RateLimiting";
    public const string AuthPolicy = "auth";

    [Range(1, int.MaxValue)] public int AuthPermitLimit { get; set; } = 10;
    [Range(1, 3600)] public int AuthWindowSeconds { get; set; } = 60;
}
