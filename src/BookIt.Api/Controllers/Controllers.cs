using System.Security.Claims;
using BookIt.Api.Data;
using BookIt.Api.Domain;
using BookIt.Api.Dtos;
using BookIt.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookIt.Api.Controllers;

public abstract class ApiBase : ControllerBase
{
    protected int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Normalises any incoming timestamp to UTC (unspecified kind is treated as UTC).</summary>
    protected static DateTime ToUtc(DateTime d) =>
        d.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : d.ToUniversalTime();
}

[ApiController]
[Route("api/auth")]
public class AuthController : ApiBase
{
    private readonly BookItDb _db;
    private readonly IPasswordHasher<User> _hasher;
    private readonly ITokenService _tokens;

    public AuthController(BookItDb db, IPasswordHasher<User> hasher, ITokenService tokens)
    { _db = db; _hasher = hasher; _tokens = tokens; }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email == email))
            return Conflict(new { error = "Email is already registered." });

        var user = new User { Email = email, FullName = req.FullName.Trim() };
        user.PasswordHash = _hasher.HashPassword(user, req.Password);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return Ok(new AuthResponse(_tokens.CreateToken(user), user.Id, user.Email, user.FullName));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null || _hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password) == PasswordVerificationResult.Failed)
            return Unauthorized(new { error = "Invalid email or password." });
        return Ok(new AuthResponse(_tokens.CreateToken(user), user.Id, user.Email, user.FullName));
    }
}

[ApiController]
[Route("api/services")]
public class ServicesController : ApiBase
{
    private readonly BookItDb _db;
    public ServicesController(BookItDb db) => _db = db;

    [HttpGet]
    public async Task<IEnumerable<Service>> List([FromQuery] bool includeInactive = false) =>
        await _db.Services.Where(s => includeInactive || s.IsActive).OrderBy(s => s.Id).ToListAsync();

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Service>> Get(int id) =>
        await _db.Services.FindAsync(id) is { } s ? s : NotFound();

    [Authorize, HttpPost]
    public async Task<ActionResult<Service>> Create(ServiceRequest r)
    {
        var s = new Service { Name = r.Name, Description = r.Description, Price = r.Price, DurationMinutes = r.DurationMinutes, IsActive = r.IsActive };
        _db.Services.Add(s);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = s.Id }, s);
    }

    [Authorize, HttpPut("{id:int}")]
    public async Task<ActionResult<Service>> Update(int id, ServiceRequest r)
    {
        var s = await _db.Services.FindAsync(id);
        if (s is null) return NotFound();
        s.Name = r.Name; s.Description = r.Description; s.Price = r.Price; s.DurationMinutes = r.DurationMinutes; s.IsActive = r.IsActive;
        await _db.SaveChangesAsync();
        return s;
    }

    [Authorize, HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var s = await _db.Services.FindAsync(id);
        if (s is null) return NotFound();
        if (await _db.Bookings.AnyAsync(b => b.ServiceId == id))
            return Conflict(new { error = "Service has bookings; deactivate it instead (IsActive=false)." });
        _db.Services.Remove(s);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

[ApiController, Authorize]
[Route("api/bookings")]
public class BookingsController : ApiBase
{
    private readonly BookItDb _db;
    private readonly BookingScheduler _scheduler;
    public BookingsController(BookItDb db, BookingScheduler scheduler) { _db = db; _scheduler = scheduler; }

    private IQueryable<Booking> Mine() =>
        _db.Bookings.Include(b => b.Service).Include(b => b.Invoice).Where(b => b.CustomerId == CurrentUserId);

    [HttpGet]
    public async Task<IEnumerable<BookingDto>> List()
    {
        var items = await Mine().ToListAsync();
        return items.OrderBy(b => b.StartTime).Select(BookingDto.From);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<BookingDto>> Get(int id) =>
        await Mine().FirstOrDefaultAsync(b => b.Id == id) is { } b ? BookingDto.From(b) : NotFound();

    [HttpPost]
    public async Task<ActionResult<BookingDto>> Create(CreateBookingRequest r)
    {
        try
        {
            var b = await _scheduler.CreateAsync(CurrentUserId, r.ServiceId, ToUtc(r.StartTime), r.Notes);
            return CreatedAtAction(nameof(Get), new { id = b.Id }, BookingDto.From(b));
        }
        catch (BookingConflictException e) { return Conflict(new { error = e.Message }); }
        catch (KeyNotFoundException e) { return NotFound(new { error = e.Message }); }
        catch (InvalidOperationException e) { return BadRequest(new { error = e.Message }); }
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<BookingDto>> Reschedule(int id, RescheduleRequest r)
    {
        var b = await Mine().FirstOrDefaultAsync(x => x.Id == id);
        if (b is null) return NotFound();
        if (b.Status is BookingStatus.Cancelled or BookingStatus.Completed)
            return BadRequest(new { error = $"A {b.Status} booking cannot be rescheduled." });
        try { await _scheduler.RescheduleAsync(b, ToUtc(r.StartTime)); }
        catch (BookingConflictException e) { return Conflict(new { error = e.Message }); }
        catch (InvalidOperationException e) { return BadRequest(new { error = e.Message }); }
        return BookingDto.From(b);
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<BookingDto>> Cancel(int id)
    {
        var b = await Mine().FirstOrDefaultAsync(x => x.Id == id);
        if (b is null) return NotFound();
        b.Status = BookingStatus.Cancelled;
        await _db.SaveChangesAsync();
        return BookingDto.From(b);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var b = await Mine().FirstOrDefaultAsync(x => x.Id == id);
        if (b is null) return NotFound();
        if (b.Invoice is not null) return Conflict(new { error = "Booking has an invoice; cancel it instead." });
        _db.Bookings.Remove(b);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:int}/invoice")]
    public async Task<ActionResult<InvoiceDto>> GenerateInvoice(int id)
    {
        var b = await Mine().FirstOrDefaultAsync(x => x.Id == id);
        if (b is null) return NotFound();
        if (b.Status == BookingStatus.Cancelled) return BadRequest(new { error = "Cannot invoice a cancelled booking." });
        if (b.Invoice is not null) return Conflict(new { error = "Invoice already exists for this booking.", invoiceId = b.Invoice.Id });

        var items = InvoiceCalculator.BuildLineItems(b.Service!);
        var invoice = new Invoice
        {
            BookingId = b.Id, CustomerId = b.CustomerId, LineItems = items, Total = InvoiceCalculator.Total(items),
            Number = $"INV-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}"
        };
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();
        return CreatedAtAction("Get", "Invoices", new { id = invoice.Id }, InvoiceDto.From(invoice));
    }
}

[ApiController, Authorize]
[Route("api/invoices")]
public class InvoicesController : ApiBase
{
    private readonly BookItDb _db;
    public InvoicesController(BookItDb db) => _db = db;

    private IQueryable<Invoice> Mine() => _db.Invoices.Include(i => i.LineItems).Where(i => i.CustomerId == CurrentUserId);

    [HttpGet]
    public async Task<IEnumerable<InvoiceDto>> List() =>
        (await Mine().ToListAsync()).OrderByDescending(i => i.IssuedAt).Select(InvoiceDto.From);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<InvoiceDto>> Get(int id) =>
        await Mine().FirstOrDefaultAsync(i => i.Id == id) is { } i ? InvoiceDto.From(i) : NotFound();

    /// <summary>MOCK payment endpoint. No provider, no keys, no card data - it just flips the paid flag.</summary>
    [HttpPost("{id:int}/pay")]
    public async Task<ActionResult<InvoiceDto>> Pay(int id, MockPaymentRequest? req)
    {
        var i = await Mine().FirstOrDefaultAsync(x => x.Id == id);
        if (i is null) return NotFound();
        if (i.IsPaid) return Conflict(new { error = "Invoice is already paid." });
        if (req is { Succeed: false }) return StatusCode(402, new { error = "Mock payment declined." });

        i.IsPaid = true;
        i.PaidAt = DateTime.UtcNow;
        i.PaymentReference = $"MOCK-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}";
        await _db.SaveChangesAsync();
        return InvoiceDto.From(i);
    }
}
