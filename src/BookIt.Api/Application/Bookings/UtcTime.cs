namespace BookIt.Api.Application.Bookings;

internal static class UtcTime
{
    /// <summary>Normalises any incoming timestamp to UTC (unspecified kind is treated as UTC).</summary>
    public static DateTime Normalize(DateTime d) =>
        d.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : d.ToUniversalTime();
}
