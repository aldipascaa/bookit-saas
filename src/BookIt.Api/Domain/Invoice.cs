namespace BookIt.Api.Domain;

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
