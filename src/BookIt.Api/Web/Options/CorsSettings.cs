namespace BookIt.Api.Web.Options;

/// <summary>
/// Bound from "Cors". The bundled frontend is same-origin, so CORS is disabled unless origins are listed
/// (e.g. Cors__AllowedOrigins__0=https://app.example.com for a separately hosted frontend).
/// </summary>
public class CorsSettings
{
    public const string SectionName = "Cors";
    public const string PolicyName = "configured-origins";

    public string[] AllowedOrigins { get; set; } = [];
}
