namespace BookIt.Tests.Support;

// Minimal response shapes used by the HTTP-level tests.
public record BookingResp(int Id, int ServiceId, DateTime StartTime, DateTime EndTime, string Status);
public record LineResp(string Description, int Quantity, decimal UnitPrice, decimal LineTotal);
public record InvoiceResp(int Id, string Number, decimal Total, bool IsPaid, string? PaymentReference, List<LineResp> LineItems);
public record ServiceResp(int Id, string Name, string? Description, decimal Price, int DurationMinutes, bool IsActive);
public record ProblemResp(string? Title, int? Status, string? Detail, Dictionary<string, string[]>? Errors);
