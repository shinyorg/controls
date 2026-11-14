using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;
using Shiny.Maui.Controls.Analysis.Documents;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Camera.Tests;

public class CreditCardTests
{
    static RecognizedText Line(string text, float y) => new(text, new RectF(0.1f, y, 0.8f, 0.06f));

    [Theory]
    [InlineData("4111111111111111", CreditCardType.Visa)]
    [InlineData("5555555555554444", CreditCardType.Mastercard)]
    [InlineData("378282246310005", CreditCardType.Amex)]
    [InlineData("6011111111111117", CreditCardType.Discover)]
    [InlineData("3530111333300000", CreditCardType.JCB)]
    [InlineData("30569309025904", CreditCardType.DinersClub)]
    public void Detects_brand_from_prefix(string number, CreditCardType expected)
        => CreditCards.DetectType(number).ShouldBe(expected);

    [Theory]
    [InlineData("4111111111111111", true)]
    [InlineData("4111111111111112", false)]   // bad checksum
    [InlineData("378282246310005", true)]
    public void Validates_luhn(string number, bool valid)
        => CreditCards.IsValidNumber(number).ShouldBe(valid);

    [Fact]
    public void Parses_a_front_scan()
    {
        RecognizedText[] card =
        [
            Line("4111 1111 1111 1111", 0.30f),
            Line("VALID THRU 12/27", 0.45f),
            Line("JOHN SMITH", 0.60f),
        ];

        new CreditCardParser().TryParse(card, out var cc, out var boxes).ShouldBeTrue();

        cc.Type.ShouldBe(CreditCardType.Visa);
        cc.Number.ShouldBe("4111111111111111");
        cc.Expiry.ShouldBe(new DateOnly(2027, 12, 1));
        cc.FirstName.ShouldBe("JOHN");
        cc.LastName.ShouldBe("SMITH");
        boxes.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Rejects_text_without_a_valid_number()
    {
        RecognizedText[] notCard = [Line("HELLO WORLD", 0.2f), Line("1234 5678", 0.4f)];
        new CreditCardParser().TryParse(notCard, out _, out _).ShouldBeFalse();
    }
}
