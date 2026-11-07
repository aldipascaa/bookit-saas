using System.ComponentModel.DataAnnotations;

namespace BookIt.Api.Infrastructure.Security;

/// <summary>
/// Bound from the "Jwt" configuration section and validated at startup (the app refuses to start without a
/// strong signing key). Supply the key via user-secrets (dev) or the Jwt__Key environment variable (prod).
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Prefix of the committed development-only key; rejected outside the Development environment.</summary>
    public const string DevKeyPrefix = "DEV-ONLY";

    [Required(ErrorMessage = "Jwt:Key is not configured. Set the Jwt__Key environment variable (or user-secrets) to a random secret of at least 32 characters.")]
    [MinLength(32, ErrorMessage = "Jwt:Key must be at least 32 characters.")]
    public string Key { get; set; } = "";

    [Required] public string Issuer { get; set; } = "BookIt";
    [Required] public string Audience { get; set; } = "BookIt";
    [Range(1, 1440)] public int ExpiryMinutes { get; set; } = 120;
}
