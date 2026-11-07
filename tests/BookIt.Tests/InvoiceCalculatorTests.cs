using BookIt.Api.Application.Invoices;
using BookIt.Api.Domain;

namespace BookIt.Tests;

public class InvoiceCalculatorTests
{
    [Fact]
    public void Total_is_service_price_plus_ten_percent_tax()
    {
        var items = InvoiceCalculator.BuildLineItems(new Service { Name = "X", Price = 120m, DurationMinutes = 60 });
        Assert.Equal(2, items.Count);
        Assert.Equal(120m, items[0].LineTotal);
        Assert.Equal(12m, items[1].LineTotal);
        Assert.Equal(132m, InvoiceCalculator.Total(items));
    }

    [Fact]
    public void Tax_is_rounded_to_cents()
    {
        // 33.33 * 10% = 3.333 -> 3.33 ; total 36.66
        var items = InvoiceCalculator.BuildLineItems(new Service { Name = "X", Price = 33.33m, DurationMinutes = 30 });
        Assert.Equal(3.33m, items[1].UnitPrice);
        Assert.Equal(36.66m, InvoiceCalculator.Total(items));
    }

    [Fact]
    public void Free_service_has_no_tax_line_and_zero_total()
    {
        var items = InvoiceCalculator.BuildLineItems(new Service { Name = "Free", Price = 0m, DurationMinutes = 15 });
        Assert.Single(items);
        Assert.Equal(0m, InvoiceCalculator.Total(items));
    }

    [Fact]
    public void Line_total_multiplies_quantity()
    {
        var l = new InvoiceLineItem { Quantity = 3, UnitPrice = 19.99m };
        Assert.Equal(59.97m, l.LineTotal);
    }
}
