using Shiny.Controls.Camera;
using Shiny.Maui.Controls.Analysis.Documents;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Camera.Tests;

// Exercises the per-parser Merge/IsComplete that the document analyzer uses to build one payload across frames
// instead of emitting a stream of partials. (The analyzer's OCR gate needs platform recognition, so it isn't
// unit-tested here — these cover the pure merge rules it relies on.)
public class DocumentParserMergeTests
{
    static IReadOnlyList<DocumentField> Fields(params (string Label, string? Value)[] f)
        => f.Select(x => new DocumentField(x.Label, x.Value)).ToList();

    [Fact]
    public void CreditCard_merge_fills_nulls_and_keeps_first_brand()
    {
        var p = new CreditCardParser();
        var frame1 = new CreditCard(CreditCardType.Visa, "4111111111111111", null, null, null, null, null,
            Fields(("Number", "4111111111111111")));
        var frame2 = new CreditCard(CreditCardType.Unknown, "4111111111111111", new DateOnly(2027, 12, 1), "ALLAN", "RITCHIE", null, null,
            Fields(("Number", "4111111111111111"), ("Expiry", "12/2027"), ("First Name", "ALLAN"), ("Last Name", "RITCHIE")));

        p.IsComplete(frame1).ShouldBeFalse();   // no expiry yet

        var merged = p.Merge(frame1, frame2);
        merged.Number.ShouldBe("4111111111111111");
        merged.Expiry.ShouldBe(new DateOnly(2027, 12, 1));
        merged.FirstName.ShouldBe("ALLAN");
        merged.Type.ShouldBe(CreditCardType.Visa);          // kept from the accumulated (non-Unknown) read
        merged.Fields.Count.ShouldBe(4);                    // richer field list wins
        p.IsComplete(merged).ShouldBeTrue();
    }

    [Fact]
    public void CreditCard_different_number_replaces_accumulation()
    {
        var p = new CreditCardParser();
        var a = new CreditCard(CreditCardType.Visa, "4111111111111111", null, null, null, null, null, Fields());
        var b = new CreditCard(CreditCardType.Mastercard, "5500000000000004", new DateOnly(2028, 1, 1), null, null, null, null, Fields());

        p.Merge(a, b).Number.ShouldBe("5500000000000004");   // a new card resets, doesn't merge
    }

    [Fact]
    public void Passport_merge_completes_from_partial_reads()
    {
        var p = new PassportParser();
        var a = new Passport("X1234", "RITCHIE", null, "CAN", "CAN", null, null, PassportSex.Unspecified, Fields());
        var b = new Passport("X1234", "RITCHIE", "ALLAN", "CAN", "CAN", new DateOnly(1980, 1, 1), new DateOnly(2030, 1, 1), PassportSex.Male, Fields());

        p.IsComplete(a).ShouldBeFalse();

        var merged = p.Merge(a, b);
        merged.GivenNames.ShouldBe("ALLAN");
        merged.DateOfBirth.ShouldBe(new DateOnly(1980, 1, 1));
        merged.Sex.ShouldBe(PassportSex.Male);
        p.IsComplete(merged).ShouldBeTrue();
    }

    [Fact]
    public void Invoice_merge_keeps_longer_line_list_and_completes()
    {
        var p = new InvoiceParser();
        var a = new Invoice("INV-1", null, null, new List<InvoiceLine>(), Fields());
        var b = new Invoice("INV-1", new DateOnly(2026, 6, 1), 100m,
            new List<InvoiceLine> { new("Widget", 2, 25m, 50m) }, Fields());

        p.IsComplete(a).ShouldBeFalse();

        var merged = p.Merge(a, b);
        merged.Total.ShouldBe(100m);
        merged.Lines.Count.ShouldBe(1);
        p.IsComplete(merged).ShouldBeTrue();
    }

    [Fact]
    public void Receipt_merge_fills_totals_and_completes()
    {
        var p = new ReceiptParser();
        var a = new Receipt("Acme Cafe", null, null, null, null, new List<ReceiptLine>(), null,
            new List<ReceiptTax>(), null, null, null, null, null, null, null, Fields());
        var b = new Receipt("Acme Cafe", null, null, new DateOnly(2026, 6, 1), null,
            new List<ReceiptLine> { new("Coffee", 1, 4m, 4m) }, 4m,
            new List<ReceiptTax> { new("GST", 0.05m, 0.20m) }, 0.20m, null, null, 4.20m, "CAD", null, null, Fields());

        p.IsComplete(a).ShouldBeFalse();   // no total yet

        var merged = p.Merge(a, b);
        merged.Total.ShouldBe(4.20m);
        merged.Lines.Count.ShouldBe(1);
        merged.Taxes.Count.ShouldBe(1);
        p.IsComplete(merged).ShouldBeTrue();
    }

    [Fact]
    public void HealthCard_merge_fills_nulls_and_completes_on_number()
    {
        var p = new HealthCardParser();
        var a = new HealthCard(null, "ALLAN RITCHIE", null, null, "ON", Fields());
        var b = new HealthCard("1234-567-890", "ALLAN RITCHIE", new DateOnly(2029, 5, 1), "OHIP", "ON", Fields());

        p.IsComplete(a).ShouldBeFalse();

        var merged = p.Merge(a, b);
        merged.Number.ShouldBe("1234-567-890");
        merged.Expiry.ShouldBe(new DateOnly(2029, 5, 1));
        merged.Name.ShouldBe("ALLAN RITCHIE");
        p.IsComplete(merged).ShouldBeTrue();
    }
}
