using Shiny.Maui.Controls.Analysis.Documents;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Camera.Tests;

public class MrzTests
{
    // The canonical ICAO 9303 TD3 specimen.
    const string Line1 = "P<UTOERIKSSON<<ANNA<MARIA<<<<<<<<<<<<<<<<<<<";
    const string Line2 = "L898902C36UTO7408122F1204159ZE184226B<<<<<10";

    [Fact]
    public void Parses_the_td3_specimen()
    {
        Mrz.TryParseTd3(Line1, Line2, out var p).ShouldBeTrue();

        p.Number.ShouldBe("L898902C3");
        p.Surname.ShouldBe("ERIKSSON");
        p.GivenNames.ShouldBe("ANNA MARIA");
        p.Nationality.ShouldBe("UTO");
        p.IssuingCountry.ShouldBe("UTO");
        p.DateOfBirth.ShouldBe(new DateOnly(1974, 8, 12));
        p.Expiry.ShouldBe(new DateOnly(2012, 4, 15));
        p.Sex.ShouldBe(PassportSex.Female);
    }

    [Fact]
    public void Tolerates_spaces_and_lowercase_from_ocr()
    {
        Mrz.TryParseTd3(Line1.ToLowerInvariant(), "  " + Line2 + "  ", out var p).ShouldBeTrue();
        p.Number.ShouldBe("L898902C3");
        p.Surname.ShouldBe("ERIKSSON");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("not a passport", "neither is this")]
    public void Rejects_non_mrz(string? l1, string? l2)
        => Mrz.TryParseTd3(l1, l2, out _).ShouldBeFalse();
}
