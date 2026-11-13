using Microsoft.Maui.Controls;
using Shiny.Maui.Controls.SignaturePad;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>The inline signature surface, and the pad that hosts it in a floating panel.</summary>
[Collection(ApplicationResourcesCollection.Name)]
public class SignatureViewTests
{
    public SignatureViewTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
    }

    [Fact]
    public void AnEmptySurfaceHasNoSignatureAndNoClearButton()
    {
        var view = new SignatureView();

        view.HasSignature.ShouldBeFalse();
        view.ClearButton.IsVisible.ShouldBeFalse();
    }

    [Fact]
    public void AnUnsizedSurfaceStillAsksForADrawableHeight()
    {
        var view = new SignatureView();

        var size = view.Measure(300, double.PositiveInfinity);

        size.Height.ShouldBe(SignatureView.DefaultHeight);
    }

    [Fact]
    public void ThePadDrawsNoBorderOfItsOwnAroundTheSurface()
    {
        var pad = new Shiny.Maui.Controls.SignaturePad.SignaturePad();

        pad.SignatureView.ShowBorder.ShouldBeFalse();
    }

    [Fact]
    public void ThePadForwardsInkSettingsToTheSurface()
    {
        var pad = new Shiny.Maui.Controls.SignaturePad.SignaturePad
        {
            StrokeColor = Microsoft.Maui.Graphics.Colors.Blue,
            StrokeWidth = 6
        };

        pad.SignatureView.StrokeColor.ShouldBe(Microsoft.Maui.Graphics.Colors.Blue);
        pad.SignatureView.StrokeWidth.ShouldBe(6);
    }

    [Fact]
    public void ThePadNamesTheSurfaceItHosts()
    {
        var pad = new Shiny.Maui.Controls.SignaturePad.SignaturePad { AutomationId = "Sig" };

        pad.SignatureView.Surface.AutomationId.ShouldBe("Sig" + Shiny.Maui.Controls.SignaturePad.SignaturePad.CanvasAutomationIdSuffix);
        pad.SignatureView.ClearButton.AutomationId.ShouldBe("Sig" + Shiny.Maui.Controls.SignaturePad.SignaturePad.ClearAutomationIdSuffix);
    }
}
