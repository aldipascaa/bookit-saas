namespace BookIt.Api.Domain;

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string FullName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Service
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int DurationMinutes { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum BookingStatus { Pending = 0, Confirmed = 1, Cancelled = 2, Completed = 3 }

public class Booking
{
    public int Id { get; set; }
    public int ServiceId { get; set; }
    public Service? Service { get; set; }
    public int CustomerId { get; set; }
    public User? Customer { get; set; }
    /// <summary>UTC start.</summary>
    public DateTime StartTime { get; set; }
    /// <summary>UTC end (start + service duration).</summary>
    public DateTime EndTime { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public string? Notes { get; set; }
    public Invoice? Invoice { get; set; }
}

public class Invoice
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public int BookingId { get; set; }
    public Booking? Booking { get; set; }
    public int CustomerId { get; set; }
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public List<InvoiceLineItem> LineItems { get; set; } = new();
    public decimal Total { get; set; }
    public bool IsPaid { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? PaymentReference { get; set; }
}

public class InvoiceLineItem
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public string Description { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal LineTotal => Math.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);
}
