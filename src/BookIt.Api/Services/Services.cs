using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BookIt.Api.Data;
using BookIt.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BookIt.Api.Services;

public class JwtOptions
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "BookIt";
    public string Audience { get; set; } = "BookIt";
    public int ExpiryMinutes { get; set; } = 120;
}

public interface ITokenService { string CreateToken(User user); }

public class TokenService : ITokenService
{
    private readonly JwtOptions _o;
    public TokenService(IOptions<JwtOptions> o) => _o = o.Value;

    public string CreateToken(User user)
    {
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_o.Key)), SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Name, user.FullName),
        };
        var token = new JwtSecurityToken(_o.Issuer, _o.Audience, claims,
            expires: DateTime.UtcNow.AddMinutes(_o.ExpiryMinutes), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>Pure invoice math, kept separate so it is trivially unit-testable.</summary>
public static class InvoiceCalculator
{
    public const decimal TaxRate = 0.10m;

    public static List<InvoiceLineItem> BuildLineItems(Service service, decimal taxRate = TaxRate)
    {
        var items = new List<InvoiceLineItem>
        {
            new() { Description = $"{service.Name} ({service.DurationMinutes} min)", Quantity = 1, UnitPrice = service.Price }
        };
        var tax = Math.Round(service.Price * taxRate, 2, MidpointRounding.AwayFromZero);
        if (tax > 0)
            items.Add(new() { Description = $"Tax ({taxRate:P0})", Quantity = 1, UnitPrice = tax });
        return items;
    }

    public static decimal Total(IEnumerable<InvoiceLineItem> items) => items.Sum(i => i.LineTotal);
}

public class BookingConflictException : Exception
{
    public BookingConflictException(string message) : base(message) { }
}

public class BookingScheduler
{
    // Serialises check-then-insert so concurrent requests cannot both pass the overlap check.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly BookItDb _db;
    public BookingScheduler(BookItDb db) => _db = db;

    /// <summary>True when [start,end) overlaps a non-cancelled booking for the same service.</summary>
    public Task<bool> HasConflictAsync(int serviceId, DateTime start, DateTime end, int? ignoreBookingId = null) =>
        _db.Bookings.AnyAsync(b => b.ServiceId == serviceId
            && b.Status != BookingStatus.Cancelled
            && (ignoreBookingId == null || b.Id != ignoreBookingId)
            && b.StartTime < end && start < b.EndTime);

    public async Task<Booking> CreateAsync(int customerId, int serviceId, DateTime startUtc, string? notes)
    {
        var service = await _db.Services.FindAsync(serviceId)
            ?? throw new KeyNotFoundException("Service not found.");
        if (!service.IsActive) throw new InvalidOperationException("Service is not available.");
        if (startUtc <= DateTime.UtcNow) throw new InvalidOperationException("Start time must be in the future.");

        var end = startUtc.AddMinutes(service.DurationMinutes);
        await Gate.WaitAsync();
        try
        {
            if (await HasConflictAsync(serviceId, startUtc, end))
                throw new BookingConflictException("That time slot overlaps an existing booking.");

            var booking = new Booking
            {
                ServiceId = serviceId, CustomerId = customerId, StartTime = startUtc, EndTime = end,
                Status = BookingStatus.Confirmed, Notes = notes
            };
            _db.Bookings.Add(booking);
            await _db.SaveChangesAsync();
            booking.Service = service;
            return booking;
        }
        finally { Gate.Release(); }
    }

    public async Task<Booking> RescheduleAsync(Booking booking, DateTime newStartUtc)
    {
        var service = booking.Service ?? await _db.Services.FindAsync(booking.ServiceId)
            ?? throw new KeyNotFoundException("Service not found.");
        if (newStartUtc <= DateTime.UtcNow) throw new InvalidOperationException("Start time must be in the future.");
        var end = newStartUtc.AddMinutes(service.DurationMinutes);
        await Gate.WaitAsync();
        try
        {
            if (booking.Status != BookingStatus.Cancelled && await HasConflictAsync(booking.ServiceId, newStartUtc, end, booking.Id))
                throw new BookingConflictException("That time slot overlaps an existing booking.");
            booking.StartTime = newStartUtc;
            booking.EndTime = end;
            await _db.SaveChangesAsync();
            return booking;
        }
        finally { Gate.Release(); }
    }
}
