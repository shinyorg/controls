using Microsoft.Maui.Controls;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// HasFocusGlow = false leaves the outer border without a Shadow, so the platform never wraps it or
/// renders a shadow mask for it. The default keeps today's glow.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class TextEntryFocusGlowTests
{
    static TextEntry Create(Action<TextEntry>? setup = null)
    {
        TestDispatcherProvider.Install();
        new Application();
        var te = new TextEntry();
        setup?.Invoke(te);
        return te;
    }

    [Fact]
    public void Default_BorderHasTheGlowShadow()
    {
        var te = Create();

        te.HasFocusGlow.ShouldBeTrue();
        te.OuterBorder.Shadow.ShouldNotBeNull();
        te.OuterBorder.Shadow.Opacity.ShouldBe(0f);
    }

    [Fact]
    public void Off_BorderHasNoShadow()
    {
        var te = Create(x => x.HasFocusGlow = false);

        te.OuterBorder.Shadow.ShouldBeNull();
    }

    [Fact]
    public void Off_FromStyle_BorderHasNoShadow()
    {
        var style = new Style(typeof(TextEntry));
        style.Setters.Add(new Setter { Property = TextEntry.HasFocusGlowProperty, Value = false });

        var te = Create(x => x.Style = style);

        te.OuterBorder.Shadow.ShouldBeNull();
    }

    [Fact]
    public void Off_ErrorDoesNotBringTheShadowBack()
    {
        var te = Create(x => x.HasFocusGlow = false);

        te.HasError = true;

        te.OuterBorder.Shadow.ShouldBeNull();
    }

    [Fact]
    public void BackOn_WhileInError_ShowsTheGlowAgain()
    {
        var te = Create(x =>
        {
            x.HasFocusGlow = false;
            x.HasError = true;
        });

        te.HasFocusGlow = true;

        te.OuterBorder.Shadow.ShouldNotBeNull();
        te.OuterBorder.Shadow.Opacity.ShouldBeGreaterThan(0f);
    }
}
