using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;
using Shiny.Maui.Controls.Analysis.Documents;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Camera.Tests;

public class HealthCardParserTests
{
    static RecognizedText Line(string text, float y) => new(text, new RectF(0.1f, y, 0.8f, 0.05f));

    [Fact]
    public void Extracts_number_name_expiry_issuer()
    {
        RecognizedText[] card =
        [
            Line("ONTARIO HEALTH", 0.05f),
            Line("SAMPLE, JOHN", 0.20f),
            Line("1234 567 890", 0.35f),
            Line("Expiry 2026-05-31", 0.55f),
        ];

        new HealthCardParser().TryParse(card, out var hc, out var boxes).ShouldBeTrue();

        hc.Number.ShouldBe("1234567890");
        hc.Name.ShouldBe("SAMPLE, JOHN");
        hc.Expiry.ShouldBe(new DateOnly(2026, 5, 31));
        hc.Issuer.ShouldNotBeNull().ShouldContain("HEALTH");
        boxes.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Rejects_card_without_a_number()
    {
        RecognizedText[] card = [Line("ONTARIO HEALTH", 0.05f), Line("SAMPLE, JOHN", 0.20f)];
        new HealthCardParser().TryParse(card, out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void Parses_ontario_number_with_version_code()
    {
        RecognizedText[] card =
        [
            Line("ServiceOntario", 0.05f),
            Line("DOE, JANE", 0.20f),
            Line("1234-567-890 XY", 0.35f),
            Line("2027-06-30", 0.55f),
        ];

        new HealthCardParser().TryParse(card, out var hc, out _).ShouldBeTrue();

        hc.Number.ShouldBe("1234567890");
        hc.Province.ShouldBe("Ontario");
        hc.Fields.ShouldContain(f => f.Label == "Version code" && f.Value == "XY");
        hc.Fields.ShouldContain(f => f.Label == "Plan" && f.Value == "OHIP");
    }

    [Fact]
    public void Parses_quebec_ramq_alphanumeric_number()
    {
        RecognizedText[] card =
        [
            Line("Régie de l'assurance maladie du Québec", 0.05f),
            Line("TREMBLAY, MARIE", 0.20f),
            Line("TREM 1234 5678", 0.35f),
        ];

        new HealthCardParser().TryParse(card, out var hc, out _).ShouldBeTrue();

        hc.Number.ShouldBe("TREM 1234 5678");
        hc.Province.ShouldBe("Quebec");
        hc.Fields.ShouldContain(f => f.Label == "Plan" && f.Value == "RAMQ");
    }

    [Fact]
    public void Parses_bc_personal_health_number()
    {
        RecognizedText[] card =
        [
            Line("BC Services Card", 0.05f),
            Line("9999 999 998", 0.30f),
        ];

        new HealthCardParser().TryParse(card, out var hc, out _).ShouldBeTrue();

        hc.Number.ShouldBe("9999999998");
        hc.Province.ShouldBe("British Columbia");
    }

    [Fact]
    public void Parses_alberta_nine_digit_number()
    {
        RecognizedText[] card =
        [
            Line("Alberta Health Care Insurance Plan", 0.05f),
            Line("DOE, JOHN", 0.20f),
            Line("12345 6789", 0.35f),
        ];

        new HealthCardParser().TryParse(card, out var hc, out _).ShouldBeTrue();

        hc.Number.ShouldBe("123456789");
        hc.Province.ShouldBe("Alberta");
    }
}
