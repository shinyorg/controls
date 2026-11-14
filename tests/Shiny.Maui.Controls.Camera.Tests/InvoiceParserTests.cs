using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;
using Shiny.Maui.Controls.Analysis.Documents;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Camera.Tests;

public class InvoiceParserTests
{
    static RecognizedText Line(string text, float y) => new(text, new RectF(0.1f, y, 0.8f, 0.04f));

    static readonly RecognizedText[] Receipt =
    [
        Line("INVOICE #INV-1042", 0.05f),
        Line("Date: 03/14/2024", 0.10f),
        Line("2 Widget    5.00   10.00", 0.30f),
        Line("1 Gadget    7.50    7.50", 0.35f),
        Line("Subtotal           17.50", 0.50f),
        Line("Tax                 1.40", 0.55f),
        Line("Total              18.90", 0.60f),
    ];

    [Fact]
    public void Extracts_header_fields()
    {
        new InvoiceParser().TryParse(Receipt, out var invoice, out _).ShouldBeTrue();

        invoice.Number.ShouldBe("INV-1042");
        invoice.Date.ShouldBe(new DateOnly(2024, 3, 14));
        invoice.Total.ShouldBe(18.90m);
    }

    [Fact]
    public void Extracts_order_lines_but_not_summary_rows()
    {
        new InvoiceParser().TryParse(Receipt, out var invoice, out _).ShouldBeTrue();

        invoice.Lines.Count.ShouldBe(2);
        invoice.Lines[0].Quantity.ShouldBe(2m);
        invoice.Lines[0].UnitPrice.ShouldBe(5.00m);
        invoice.Lines[0].Amount.ShouldBe(10.00m);
        invoice.Lines[1].Amount.ShouldBe(7.50m);
    }

    [Fact]
    public void Produces_overlay_boxes()
    {
        new InvoiceParser().TryParse(Receipt, out _, out var boxes).ShouldBeTrue();
        boxes.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Rejects_text_without_invoice_signals()
    {
        RecognizedText[] notInvoice = [Line("hello world", 0.1f), Line("nothing to see", 0.2f)];
        new InvoiceParser().TryParse(notInvoice, out _, out _).ShouldBeFalse();
    }
}
