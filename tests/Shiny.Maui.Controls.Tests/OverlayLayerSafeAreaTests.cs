using Microsoft.Maui.Controls;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Ribbons;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// The overlay root is edge-to-edge, so a layer that places things at a target's position in the root
/// must be too - a layout's default safe-area inset moved every such popup down by the status bar on
/// a page that reaches under it. A layer whose content docks to an edge keeps the default instead,
/// because that inset is the only thing holding it off the status bar and home indicator.
/// </summary>
public class OverlayLayerSafeAreaTests
{
    [Fact]
    public void TheTooltipLayerDoesNotApplyTheSafeAreaAgain()
        => new PageOverlay.TooltipLayer().SafeAreaEdges.ShouldBe(SafeAreaEdges.None);

    [Fact]
    public void TheFloatingToolbarLayersDoNotApplyTheSafeAreaAgain()
    {
        new PageOverlay.FloatingToolbarLayer().SafeAreaEdges.ShouldBe(SafeAreaEdges.None);
        new PageOverlay.FloatingToolbarMenuLayer().SafeAreaEdges.ShouldBe(SafeAreaEdges.None);
    }

    [Fact]
    public void TheRibbonMenuLayerDoesNotApplyTheSafeAreaAgain()
        => new Ribbon.RibbonMenuLayer().SafeAreaEdges.ShouldBe(SafeAreaEdges.None);

    /// <summary>
    /// The toast layer's inset is now the only one a toast gets: the manager stopped adding the iOS
    /// window's on top of it. Edge-to-edge here would put a top toast under the status bar.
    /// </summary>
    [Fact]
    public void TheToastLayerKeepsTheSafeArea()
        => new PageOverlay.ToastLayer().SafeAreaEdges.ShouldNotBe(SafeAreaEdges.None);
}
