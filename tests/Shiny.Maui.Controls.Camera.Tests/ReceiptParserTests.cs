using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;
using Shiny.Maui.Controls.Analysis.Documents;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Camera.Tests;

public class ReceiptParserTests
{
    static RecognizedText Line(string text, float y) => new(text, new RectF(0.1f, y, 0.8f, 0.04f));

    static readonly RecognizedText[] Sample =
    [
        Line("Bob's Burgers", 0.03f),
        Line("(555) 123-4567", 0.06f),
        Line("Receipt #R-2024-0098", 0.10f),
        Line("Date: 03/14/2024  12:45 PM", 0.13f),
        Line("2 Cheeseburger   8.00   16.00", 0.30f),
        Line("1 Fries          3.50    3.50", 0.34f),
        Line("Subtotal               19.50", 0.50f),
        Line("GST 5%                  0.98", 0.54f),
        Line("PST 7%                  1.37", 0.57f),
        Line("Tip                     3.00", 0.61f),
        Line("Total                  24.85", 0.65f),
        Line("VISA ****1234           24.85", 0.70f),
    ];

    [Fact]
    public void Extracts_merchant_and_header_fields()
    {
        new ReceiptParser().TryParse(Sample, out var receipt, out _).ShouldBeTrue();

        receipt.Merchant.ShouldBe("Bob's Burgers");
        receipt.MerchantPhone.ShouldBe("(555) 123-4567");
        receipt.ReceiptNumber.ShouldBe("R-2024-0098");
        receipt.Date.ShouldBe(new DateOnly(2024, 3, 14));
        receipt.Time.ShouldBe(new TimeOnly(12, 45));
    }

    [Fact]
    public void Extracts_line_items_but_not_summary_rows()
    {
        new ReceiptParser().TryParse(Sample, out var receipt, out _).ShouldBeTrue();

        receipt.Lines.Count.ShouldBe(2);
        receipt.Lines[0].Quantity.ShouldBe(2m);
        receipt.Lines[0].UnitPrice.ShouldBe(8.00m);
        receipt.Lines[0].Amount.ShouldBe(16.00m);
        receipt.Lines[1].Description.ShouldNotBeNull().ShouldContain("Fries");
        receipt.Lines[1].Amount.ShouldBe(3.50m);
    }

    [Fact]
    public void Extracts_tax_breakdown_and_totals()
    {
        new ReceiptParser().TryParse(Sample, out var receipt, out _).ShouldBeTrue();

        receipt.Subtotal.ShouldBe(19.50m);
        receipt.Taxes.Count.ShouldBe(2);
        receipt.Taxes[0].Rate.ShouldBe(5m);
        receipt.Taxes[0].Amount.ShouldBe(0.98m);
        receipt.Tax.ShouldBe(2.35m);   // sum of the breakdown
        receipt.Tip.ShouldBe(3.00m);
        receipt.Total.ShouldBe(24.85m);
    }

    [Fact]
    public void Extracts_payment_method_and_card()
    {
        new ReceiptParser().TryParse(Sample, out var receipt, out _).ShouldBeTrue();

        receipt.PaymentMethod.ShouldBe("Visa");
        receipt.CardLast4.ShouldBe("1234");
    }

    [Fact]
    public void Produces_overlay_boxes()
    {
        new ReceiptParser().TryParse(Sample, out _, out var boxes).ShouldBeTrue();
        boxes.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Rejects_text_without_receipt_signals()
    {
        RecognizedText[] notReceipt = [Line("hello world", 0.1f), Line("nothing to see", 0.2f)];
        new ReceiptParser().TryParse(notReceipt, out _, out _).ShouldBeFalse();
    }
}
