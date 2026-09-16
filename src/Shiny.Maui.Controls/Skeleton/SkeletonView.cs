using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Themes;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

/// <summary>
/// Wraps a content area and, while <see cref="IsBusy"/> is true, replaces it with animated
/// shimmer placeholders — conceptually similar to <c>RefreshView</c>. Provide a custom
/// placeholder layout via <see cref="SkeletonTemplate"/> or rely on the built-in line placeholders.
/// </summary>
[ContentProperty(nameof(Content))]
public partial class SkeletonView : Grid, IDisposable
{
    const string ShimmerAnimationName = "ShinySkeletonShimmer";

    readonly ContentView realContentHost;
    readonly Grid skeletonHost;
    readonly ContentView placeholderHost;
    readonly Border templateShimmerBand;
    readonly Grid templateClipHost;
    readonly List<View> shimmerBands = new();

    bool isAnimating;
    double containerWidth;

    public SkeletonView()
    {
        this.realContentHost = new ContentView();
        this.placeholderHost = new ContentView();

        // The band is drawn entirely by its gradient Background, which constrains what it can be.
        // Not a BoxView: one with no Color of its own picks up whatever the host app's implicit
        // Style TargetType="BoxView" says, and an opaque fill over the gradient shows up as a solid
        // bar sweeping across the placeholders. Not a bare layout either: a Grid maps to WinUI's
        // LayoutPanel, which only paints a solid BackgroundColor - a gradient Background on it is
        // silently dropped, so the shimmer is invisible on Windows. Border is drawn on every
        // platform; the stroke properties are set explicitly so an implicit Border style cannot
        // outline the band.
        this.templateShimmerBand = new Border
        {
            IsVisible = false,
            InputTransparent = true,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Fill,
            Stroke = Brush.Transparent,
            StrokeThickness = 0,
            Padding = 0
        };

        // The band translates, so the mask that keeps it inside the template's shapes cannot live on
        // the band itself — a Clip travels with the element it is set on. It goes on this fixed host
        // instead, which stays anchored to the placeholders while the band slides underneath it.
        this.templateClipHost = new Grid { InputTransparent = true };
        this.templateClipHost.Children.Add(this.templateShimmerBand);

        this.skeletonHost = new Grid
        {
            IsVisible = false,
            InputTransparent = true,
            IsClippedToBounds = true
        };
        this.skeletonHost.Children.Add(this.placeholderHost);
        this.skeletonHost.Children.Add(this.templateClipHost);

        // Both layers occupy the single (0,0) cell so the skeleton overlays the same area.
        this.Children.Add(this.realContentHost);
        this.Children.Add(this.skeletonHost);

        // MAUI never disposes views: a busy skeleton removed from the tree would otherwise keep its
        // shimmer loop (and the Task.Delay timers it awaits) running forever, rooting the page.
        this.Loaded += (_, _) =>
        {
            if (this.IsBusy)
                this.StartShimmer();
        };
        this.Unloaded += (_, _) => this.StopShimmer();

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(SkeletonView));
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width > 0 && Math.Abs(width - this.containerWidth) > 0.5)
        {
            this.containerWidth = width;
            this.ConfigureShimmerBands();
        }
    }

    protected override Size ArrangeOverride(Rect bounds)
    {
        var size = base.ArrangeOverride(bounds);

        // Only valid once the placeholders have actually been arranged, which is exactly here.
        if (this.IsBusy && this.SkeletonTemplate != null)
            this.UpdateTemplateClip();

        return size;
    }

    void OnContentChanged() => this.realContentHost.Content = this.Content;

    void OnIsBusyChanged(bool busy)
    {
        if (busy)
        {
            this.RebuildSkeleton();
            this.realContentHost.IsVisible = false;
            this.skeletonHost.IsVisible = true;
            this.StartShimmer();
        }
        else
        {
            this.StopShimmer();
            this.skeletonHost.IsVisible = false;
            this.realContentHost.IsVisible = true;
        }
    }

    void OnSkeletonAppearanceChanged()
    {
        if (this.IsBusy)
            this.RebuildSkeleton();
    }

    void RebuildSkeleton()
    {
        this.shimmerBands.Clear();

        if (this.SkeletonTemplate != null)
        {
            // A custom template can be any shape, so the only thing we can do generically is sweep
            // one sheen across the whole placeholder area.
            this.placeholderHost.Content = this.SkeletonTemplate.CreateContent() as View;
            this.templateShimmerBand.IsVisible = this.ShimmerEnabled;
            this.shimmerBands.Add(this.templateShimmerBand);
        }
        else
        {
            // The built-in placeholders each clip their own sheen, so the sweep lights up the bars
            // themselves and never the gaps between them (which is what makes a single full-height
            // band read as a solid box sliding over the control rather than a shimmer).
            this.templateShimmerBand.IsVisible = false;
            this.placeholderHost.Content = this.BuildDefaultSkeleton();
        }

        this.ConfigureShimmerBands();
    }

    View BuildDefaultSkeleton()
    {
        var count = Math.Max(this.ItemCount, 1);
        var stack = new VerticalStackLayout { Spacing = this.ItemSpacing };
        for (var i = 0; i < count; i++)
        {
            // Shorten the last line for a more natural paragraph look.
            var fraction = i == count - 1 && count > 1 ? 0.6 : 1.0;
            stack.Children.Add(this.BuildLine(fraction));
        }
        return stack;
    }

    View BuildLine(double widthFraction)
    {
        var band = this.CreateShimmerBand();
        this.shimmerBands.Add(band);

        // Border (rather than BoxView) so the sheen is masked to the rounded placeholder shape.
        var bar = new Border
        {
            HeightRequest = this.ItemHeight,
            Padding = 0,
            Stroke = null,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(this.CornerRadius) },
            HorizontalOptions = LayoutOptions.Fill,
            InputTransparent = true,
            Content = band
        };
        // Theme default — overridden if the consumer sets BaseColor explicitly. Left dynamic so the
        // bars keep tracking a live light/dark switch.
        if (this.BaseColor is Color baseColor)
            bar.BackgroundColor = baseColor;
        else
            bar.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHigh);

        if (widthFraction >= 1.0)
            return bar;

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(widthFraction, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(1 - widthFraction, GridUnitType.Star))
            }
        };
        grid.Children.Add(bar);
        return grid;
    }

    /// <summary>
    /// Masks the template band to the silhouettes of the placeholder shapes, so the sweep lights up
    /// the shapes and not the empty box around them. The built-in placeholders do not need this —
    /// each line owns and clips its own band.
    /// </summary>
    void UpdateTemplateClip()
    {
        if (this.placeholderHost.Content is not View content)
        {
            this.templateClipHost.Clip = null;
            return;
        }

        var group = new GeometryGroup();
        CollectShapeGeometry(content, this.placeholderHost.Bounds.Location, group);

        // Nothing recognizable (or nothing arranged yet) — sweep the whole area rather than clipping
        // the shimmer away entirely.
        this.templateClipHost.Clip = group.Children.Count == 0 ? null : group;
    }

    static void CollectShapeGeometry(View element, Point offset, GeometryGroup group)
    {
        if (!element.IsVisible)
            return;

        var origin = new Point(offset.X + element.Bounds.X, offset.Y + element.Bounds.Y);

        // Containers contribute nothing themselves — the gaps they introduce are exactly what must
        // not shimmer — so recurse and let the leaves describe the silhouette.
        switch (element)
        {
            case Layout layout:
                foreach (var child in layout.Children)
                {
                    if (child is View childView)
                        CollectShapeGeometry(childView, origin, group);
                }
                return;

            case ContentView { Content: View contentViewChild }:
                CollectShapeGeometry(contentViewChild, origin, group);
                return;

            case Border { Content: View borderChild }:
                CollectShapeGeometry(borderChild, origin, group);
                return;

            case ScrollView { Content: View scrollChild }:
                CollectShapeGeometry(scrollChild, origin, group);
                return;
        }

        if (element.Width <= 0 || element.Height <= 0)
            return;

        var radius = element switch
        {
            BoxView box => box.CornerRadius.TopLeft,
            Border { StrokeShape: RoundRectangle rounded } => rounded.CornerRadius.TopLeft,
            _ => 0d
        };

        group.Children.Add(new RoundRectangleGeometry(
            new CornerRadius(radius),
            new Rect(origin.X, origin.Y, element.Width, element.Height)
        ));
    }

    // Border, not a layout: WinUI's LayoutPanel only paints a solid BackgroundColor, so a gradient
    // Background on a Grid is silently dropped and the sheen never appears on Windows.
    Border CreateShimmerBand() => new()
    {
        IsVisible = this.ShimmerEnabled,
        InputTransparent = true,
        HorizontalOptions = LayoutOptions.Start,
        VerticalOptions = LayoutOptions.Fill,
        Stroke = Brush.Transparent,
        StrokeThickness = 0,
        Padding = 0
    };

    double BandWidth => Math.Max(this.containerWidth * 0.4, 40);

    void ConfigureShimmerBands()
    {
        if (this.containerWidth <= 0 || this.shimmerBands.Count == 0)
            return;

        var bandWidth = this.BandWidth;
        var brush = this.CreateSheenBrush();

        foreach (var band in this.shimmerBands)
        {
            band.WidthRequest = bandWidth;
            band.Background = brush;
        }
    }

    Brush CreateSheenBrush()
    {
        var highlight = this.ResolveShimmerColor();

        // iOS interpolates gradient stops per channel without premultiplying alpha, so fading out to
        // Colors.Transparent (#00000000) drags the sweep through black and the band reads as a dark
        // box crossing the placeholders. Fading to the highlight's own zero-alpha variant keeps the
        // hue constant across the whole gradient on every platform.
        var edge = highlight.WithAlpha(0f);

        return new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
            GradientStops =
            {
                new GradientStop(edge, 0f),
                new GradientStop(highlight, 0.5f),
                new GradientStop(edge, 1f)
            }
        };
    }

    Color ResolveShimmerColor()
    {
        if (this.ShimmerColor is Color shimmerColor)
            return shimmerColor;

        // The surface tokens are ordered by elevation, not luminance — SurfaceContainerHighest is
        // *darker* than SurfaceContainerHigh in every light theme — so a sheen taken straight from a
        // token sweeps a dark band over the placeholders. Derive it from the base fill instead so the
        // highlight is always a step brighter, in both light and dark themes.
        var baseColor = this.ResolveBaseColor();
        return baseColor.GetLuminosity() >= 0.98f
            ? baseColor.AddLuminosity(-0.06f)
            : baseColor.AddLuminosity(0.1f);
    }

    Color ResolveBaseColor()
    {
        if (this.BaseColor is Color baseColor)
            return baseColor;

        return Application.Current?.Resources.TryGetValue(ShinyThemeKeys.Color.SurfaceContainerHigh, out var v) == true && v is Color c
            ? c
            : Color.FromArgb("#E2E9F2");
    }

    void SetBandsVisible(bool visible)
    {
        foreach (var band in this.shimmerBands)
            band.IsVisible = visible;
    }

    void SetBandOffset(double x)
    {
        foreach (var band in this.shimmerBands)
            band.TranslationX = x;
    }

    async void StartShimmer()
    {
        if (!this.ShimmerEnabled)
        {
            this.SetBandsVisible(false);
            return;
        }
        if (this.isAnimating)
            return;

        this.isAnimating = true;
        var run = ++this.shimmerRun;
        this.SetBandsVisible(true);
        this.ConfigureShimmerBands();

        while (this.isAnimating && run == this.shimmerRun && this.IsBusy)
        {
            if (this.containerWidth <= 0)
            {
                await Task.Delay(50);
                continue;
            }

            var bandWidth = this.BandWidth;
            var sweep = new TaskCompletionSource();

            // One animation driving every band keeps the per-placeholder sheens in lockstep, so the
            // sweep reads as a single highlight crossing the control.
            new Animation(this.SetBandOffset, -bandWidth, this.containerWidth, Easing.Linear)
                .Commit(this, ShimmerAnimationName, length: this.AnimationDuration, finished: (_, _) => sweep.TrySetResult());

            // The view can be detached mid-sweep (e.g. navigation) and never report finished; the
            // timeout lets the loop guard tear down instead of hanging on the continuation.
            await Task.WhenAny(sweep.Task, Task.Delay((int)this.AnimationDuration + 250));
        }

        if (run == this.shimmerRun)
            this.SetBandOffset(0);
    }

    // Distinguishes a stale loop (still awaiting its last sweep) from the one a restart just began.
    int shimmerRun;

    void StopShimmer()
    {
        this.isAnimating = false;
        this.shimmerRun++;
        this.AbortAnimation(ShimmerAnimationName);
        this.SetBandsVisible(false);
        this.SetBandOffset(0);
    }

    void OnShimmerEnabledChanged()
    {
        if (!this.IsBusy)
            return;

        if (this.ShimmerEnabled)
            this.StartShimmer();
        else
            this.StopShimmer();
    }

    void OnShimmerColorChanged()
    {
        if (this.isAnimating)
            this.ConfigureShimmerBands();
    }

    public void Dispose()
    {
        this.StopShimmer();
        GC.SuppressFinalize(this);
    }
}
