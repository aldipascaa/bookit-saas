using BookIt.Api.Domain;

namespace BookIt.Api.Application.Invoices;

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
