using BookIt.Api.Application.Abstractions;
using BookIt.Api.Application.Contracts;
using BookIt.Api.Application.Exceptions;
using BookIt.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookIt.Api.Application.Bookings;

/// <summary>
/// Booking use cases. The overlap check here is a fast, friendly pre-check; the authoritative guard is the
/// database (see the AddBookingOverlapGuard migration), which makes check-then-insert race-free across
/// requests and across application instances. A violation surfaces from SaveChangesAsync as
/// <see cref="BookingConflictException"/>.
/// </summary>
public class BookingService(IAppDbContext db, TimeProvider clock, ILogger<BookingService> log) : IBookingService
{
    private IQueryable<Booking> Mine(int customerId) =>
        db.Bookings.Include(b => b.Service).Include(b => b.Invoice).Where(b => b.CustomerId == customerId);

    public async Task<IReadOnlyList<BookingDto>> ListAsync(int customerId, CancellationToken ct = default) =>
        (await Mine(customerId).AsNoTracking().OrderBy(b => b.StartTime).ToListAsync(ct)).Select(BookingDto.From).ToList();

    public async Task<BookingDto> GetAsync(int customerId, int id, CancellationToken ct = default) =>
        BookingDto.From(await Find(customerId, id, ct));

    public async Task<BookingDto> CreateAsync(int customerId, CreateBookingRequest request, CancellationToken ct = default)
    {
        var service = await db.Services.FirstOrDefaultAsync(s => s.Id == request.ServiceId, ct)
            ?? throw new NotFoundException("Service not found.");
        if (!service.IsActive) throw new BusinessRuleException("Service is not available.");

        var start = UtcTime.Normalize(request.StartTime);
        EnsureFuture(start);
        var end = start.AddMinutes(service.DurationMinutes);

        if (await HasConflictAsync(service.Id, start, end, null, ct)) throw Conflict(service.Id, start);

        var booking = new Booking
        {
            ServiceId = service.Id, CustomerId = customerId, StartTime = start, EndTime = end,
            Status = BookingStatus.Confirmed, Notes = request.Notes?.Trim()
        };
        db.Bookings.Add(booking);
        try { await db.SaveChangesAsync(ct); }
        catch (BookingConflictException) { throw Conflict(service.Id, start); }
        booking.Service = service;
        log.LogInformation("Booking {BookingId} created for service {ServiceId} at {Start:u}", booking.Id, service.Id, start);
        return BookingDto.From(booking);
    }

    public async Task<BookingDto> RescheduleAsync(int customerId, int id, RescheduleRequest request, CancellationToken ct = default)
    {
        var b = await Find(customerId, id, ct);
        if (b.Status is BookingStatus.Cancelled or BookingStatus.Completed)
            throw new BusinessRuleException($"A {b.Status} booking cannot be rescheduled.");

        var start = UtcTime.Normalize(request.StartTime);
        EnsureFuture(start);
        var end = start.AddMinutes(b.Service!.DurationMinutes);

        if (await HasConflictAsync(b.ServiceId, start, end, b.Id, ct)) throw Conflict(b.ServiceId, start);
        b.StartTime = start;
        b.EndTime = end;
        try { await db.SaveChangesAsync(ct); }
        catch (BookingConflictException) { throw Conflict(b.ServiceId, start); }
        log.LogInformation("Booking {BookingId} rescheduled to {Start:u}", b.Id, start);
        return BookingDto.From(b);
    }

    public async Task<BookingDto> CancelAsync(int customerId, int id, CancellationToken ct = default)
    {
        var b = await Find(customerId, id, ct);
        if (b.Status == BookingStatus.Cancelled) return BookingDto.From(b); // idempotent
        if (b.Status == BookingStatus.Completed) throw new BusinessRuleException("A Completed booking cannot be cancelled.");
        if (b.Invoice is { IsPaid: true })
            throw new ConflictException("This booking is already paid and cannot be cancelled (refunds are not supported).");
        b.Status = BookingStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        log.LogInformation("Booking {BookingId} cancelled", b.Id);
        return BookingDto.From(b);
    }

    /// <summary>
    /// Taken time ranges of a service inside [from, to), so a client can show availability before booking.
    /// Only start/end are exposed - never who booked.
    /// </summary>
    public async Task<IReadOnlyList<BusySlotDto>> BusyAsync(int serviceId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var start = UtcTime.Normalize(from);
        var end = UtcTime.Normalize(to);
        if (end <= start) throw new BusinessRuleException("'to' must be after 'from'.");
        if (end - start > MaxBusyRange) throw new BusinessRuleException($"The range may span at most {MaxBusyRange.TotalDays:0} days.");
        if (!await db.Services.AnyAsync(s => s.Id == serviceId, ct)) throw new NotFoundException("Service not found.");

        var rows = await db.Bookings.AsNoTracking()
            .Where(b => b.ServiceId == serviceId && b.Status != BookingStatus.Cancelled && b.StartTime < end && start < b.EndTime)
            .OrderBy(b => b.StartTime)
            .Select(b => new { b.StartTime, b.EndTime })
            .ToListAsync(ct);
        return rows.Select(r => new BusySlotDto(DateTime.SpecifyKind(r.StartTime, DateTimeKind.Utc),
            DateTime.SpecifyKind(r.EndTime, DateTimeKind.Utc))).ToList();
    }

    private static readonly TimeSpan MaxBusyRange = TimeSpan.FromDays(31);

    public async Task DeleteAsync(int customerId, int id, CancellationToken ct = default)
    {
        var b = await Find(customerId, id, ct);
        if (b.Invoice is not null) throw new ConflictException("Booking has an invoice; cancel it instead.");
        db.Bookings.Remove(b);
        await db.SaveChangesAsync(ct);
    }

    private async Task<Booking> Find(int customerId, int id, CancellationToken ct) =>
        await Mine(customerId).FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("Booking not found.");

    private void EnsureFuture(DateTime startUtc)
    {
        if (startUtc <= clock.GetUtcNow().UtcDateTime) throw new BusinessRuleException("Start time must be in the future.");
    }

    /// <summary>True when [start,end) overlaps a non-cancelled booking for the same service.</summary>
    private Task<bool> HasConflictAsync(int serviceId, DateTime start, DateTime end, int? ignoreBookingId, CancellationToken ct) =>
        db.Bookings.AnyAsync(b => b.ServiceId == serviceId
            && b.Status != BookingStatus.Cancelled
            && (ignoreBookingId == null || b.Id != ignoreBookingId)
            && b.StartTime < end && start < b.EndTime, ct);

    private BookingConflictException Conflict(int serviceId, DateTime start)
    {
        log.LogInformation("Booking conflict for service {ServiceId} at {Start:u}", serviceId, start);
        return new BookingConflictException();
    }
}
