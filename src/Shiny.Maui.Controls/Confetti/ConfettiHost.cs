using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

/// <summary>
/// Puts bursts on a page's confetti canvas. Shared by <see cref="IConfettiService"/> and the
/// <see cref="Confetti"/> attached properties, so neither needs the other.
/// </summary>
static class ConfettiHost
{
    /// <summary>
    /// Installs the page's confetti layer ahead of the first tap. Installing the overlay root
    /// re-parents the page's content, which rebuilds its native views - fine while the page is
    /// loading, a visible flash if it happens on the tap that is supposed to be celebrating.
    /// </summary>
    public static void Prepare(Element anchor)
    {
        var page = PageOverlay.FindPage(anchor);
        if (page is not null)
            CanvasFor(page);
    }


    public static Task FireAsync(Element? anchor, IReadOnlyList<ConfettiShot> shots)
    {
        if (ReducedMotion.IsEnabled)
            shots = [.. shots.Where(x => !x.Options.DisableForReducedMotion)];

        if (shots.Count == 0)
            return Task.CompletedTask;

        var page = (anchor is null ? null : PageOverlay.FindPage(anchor)) ?? PageOverlay.CurrentPage();
        if (page is null)
            return Task.CompletedTask;

        return CanvasFor(page).Fire(shots);
    }


    public static void Clear()
    {
        var page = PageOverlay.CurrentPage();
        if (page?.Content is PageOverlay.ShinyOverlayRoot root &&
            root.Children.OfType<PageOverlay.ConfettiLayer>().FirstOrDefault() is { } layer)
        {
            foreach (var canvas in layer.Children.OfType<ConfettiCanvas>())
                canvas.Clear();
        }
    }


    /// <summary>
    /// The centre of <paramref name="element"/> as a fraction of its page, which is the unit every
    /// origin is in. Null when it is not on a page or has not been laid out.
    /// </summary>
    public static Point? NormalizedCenter(VisualElement element)
    {
        var root = PageOverlay.GetOrCreateRoot(element);
        if (root is null || root.Width <= 0 || root.Height <= 0)
            return null;

        var bounds = ViewGeometry.BoundsIn(element, root);
        if (bounds is null)
            return null;

        return new Point(bounds.Value.Center.X / root.Width, bounds.Value.Center.Y / root.Height);
    }


    /// <summary>A point in <paramref name="element"/>'s page, as a fraction of that page.</summary>
    public static Point? Normalize(VisualElement element, Func<Element, Point?> positionIn)
    {
        var root = PageOverlay.GetOrCreateRoot(element);
        if (root is null || root.Width <= 0 || root.Height <= 0)
            return null;

        var point = positionIn(root);
        return point is null ? null : new Point(point.Value.X / root.Width, point.Value.Y / root.Height);
    }


    /// <summary>The bursts for a preset or a custom recipe, launched from <paramref name="origin"/>.</summary>
    public static IReadOnlyList<ConfettiShot> Shots(ConfettiPreset preset, ConfettiOptions? options, Point? origin)
    {
        if (options is null)
            return ConfettiPresets.Build(preset, origin, Random.Shared);

        var copy = options.Clone();
        if (origin is { } o)
        {
            copy.OriginX = o.X;
            copy.OriginY = o.Y;
        }
        return [new ConfettiShot(TimeSpan.Zero, copy)];
    }


    static ConfettiCanvas CanvasFor(ContentPage page)
    {
        var layer = PageOverlay.GetOrCreateLayer<PageOverlay.ConfettiLayer>(page, PageOverlay.Layers.Confetti)!;
        if (layer.Children.OfType<ConfettiCanvas>().FirstOrDefault() is { } existing)
            return existing;

        var canvas = new ConfettiCanvas();
        layer.Children.Add(canvas);
        return canvas;
    }
}
