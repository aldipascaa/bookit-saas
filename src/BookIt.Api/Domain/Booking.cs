namespace BookIt.Api.Domain;

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
