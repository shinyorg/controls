using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

/// <summary>
/// A Dynamic Island / camera-notch style surface that holds any content and morphs its width, height
/// and corner radius to fit it.
/// </summary>
/// <remarks>
/// <para>
/// There are no compact/expanded states to manage: put a view in <see cref="Content"/> and the notch
/// sizes itself to it. Swap the content, or let the content change its own size (a label's text grows,
/// a row becomes visible), and the notch springs to the new size.
/// </para>
/// <para>
/// During a morph the content is laid out once, at its final size, and the surface clips it as it
/// grows or shrinks — the content never re-flows frame by frame, which is what keeps text from
/// wrapping and unwrapping while the notch animates.
/// </para>
/// <para>
/// It positions like any other view (top-centre by default). To sit it over a page, put it in the
/// same <c>Grid</c> cell as the page content. For a notch that lives in the MacBook camera notch,
/// over every application, see <c>INotchWindowService</c> in <c>Shiny.Maui.Controls.Desktop</c>.
/// </para>
/// </remarks>
[ContentProperty(nameof(Content))]
public partial class NotchView : Grid
{
    const string MorphAnimationName = "ShinyNotchMorph";

    /// <summary>
    /// Appended to the notch's <see cref="Element.AutomationId"/> to name the surface: the morphing card
    /// that carries the tap gesture and whose size a test asserts on.
    /// </summary>
    public const string SurfaceAutomationIdSuffix = "_Surface";

    /// <summary>The cap automatic corners stop at, so a tall notch stays a rounded rectangle and not an oval.</summary>
    internal const double AutoCornerRadiusCap = 28d;

    /// <summary>A gentle "back" ease: overshoots by a few percent and settles, rather than bouncing.</summary>
    internal static readonly Easing DefaultEasing = new(t =>
    {
        const double c1 = 0.9;
        const double c3 = c1 + 1;
        var x = t - 1;
        return 1 + (c3 * x * x * x) + (c1 * x * x);
    });

    readonly Border surface;
    readonly RoundRectangle outline;
    readonly ContentView contentHost;
    readonly Shadow shadow;

    double availableWidth = Double.PositiveInfinity;
    bool remeasurePending;
    bool fadeInNext;
    int morphToken;

    public NotchView()
    {
        this.HorizontalOptions = LayoutOptions.Center;
        this.VerticalOptions = LayoutOptions.Start;

        // Top, not centred: the surface grows and shrinks from its top edge, so content pinned to
        // the top stays put while the bottom is revealed or clipped. Centred, anything along the top
        // (a tab strip) slid out of view and back on every resize, which read as the notch
        // collapsing and re-expanding.
        this.contentHost = new ContentView
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Start
        };

        this.outline = new RoundRectangle();

        // One Shadow for life, toggled through its opacity: swapping VisualElement.Shadow on Android
        // tears down and rebuilds the native view, which drops focus from anything typing inside it.
        this.shadow = new Shadow
        {
            Offset = new Point(0, 6),
            Radius = 18,
            Opacity = 0.35f
        };
        ThemeBrush.Apply(this.shadow, Shadow.BrushProperty, ShinyThemeKeys.Brush.Shadow);

        this.surface = new Border
        {
            StrokeShape = this.outline,
            StrokeThickness = 0,
            Padding = 0,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Start,
            WidthRequest = this.MinNotchWidth,
            HeightRequest = this.MinNotchHeight,
            Shadow = this.shadow,
            Content = this.contentHost
        };

        // Command rather than Tapped: Tapped cannot be raised from a test, Command can.
        this.surface.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(this.OnSurfaceTapped) });
        this.Children.Add(this.surface);
        AutomationIds.Link(this, this.surface, SurfaceAutomationIdSuffix);

        // Anything inside the content invalidating its measure bubbles up to here: that is the "the
        // content changed size" signal. Our own resizes land on the surface, above it, so they never
        // trigger a remeasure of their own.
        this.contentHost.MeasureInvalidated += (_, _) => this.ScheduleRefresh();
        this.Loaded += (_, _) => this.ScheduleRefresh();
        this.Unloaded += (_, _) => this.AbortAnimation(MorphAnimationName);

        this.NotchSize = new Size(this.MinNotchWidth, this.MinNotchHeight);
        this.ApplySurface();
        this.ApplyOutline();

        // Last line: replays any styled property that was applied before the children existed.
        StyleGuard.MarkReady(this, typeof(NotchView));
    }

    /// <summary>Raised when the surface (not a button inside the content) is tapped.</summary>
    public event EventHandler? Tapped;

    /// <summary>
    /// Raised as a morph <em>starts</em>, with the size it is heading to. A host that has to make room
    /// for the notch (a native window, say) grows here, before the surface does.
    /// </summary>
    public event EventHandler<NotchSizeChangedEventArgs>? NotchSizeChanged;

    /// <summary>Raised when a morph has finished and the surface is at rest at <see cref="NotchSize"/>.</summary>
    public event EventHandler? MorphCompleted;

    /// <summary>
    /// Re-measures the content and morphs to fit it. The notch already does this whenever the content
    /// reports a size change; call it after a change that does not invalidate measure (a font loaded
    /// late, a native view resized itself).
    /// </summary>
    public void Refresh() => this.Refresh(animate: true);


    // ---------------------------------------------------------------------------------------------
    // Sizing
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The notch's size for a given content size: the content plus padding, clamped between the
    /// minimum and whichever of the maximum and the offered width is smaller.
    /// </summary>
    internal static Size ComputeNotchSize(Size? content, Thickness padding, double minWidth, double minHeight, double maxWidth, double maxHeight, double availableWidth)
    {
        var widthCap = Math.Min(maxWidth, availableWidth);
        if (Double.IsNaN(widthCap) || widthCap <= 0)
            widthCap = Double.PositiveInfinity;

        if (content is not { } c || c.Width <= 0 || c.Height <= 0)
            return new Size(Clamp(minWidth, minWidth, widthCap), Clamp(minHeight, minHeight, maxHeight));

        var w = c.Width + padding.HorizontalThickness;
        var h = c.Height + padding.VerticalThickness;
        return new Size(Clamp(w, minWidth, widthCap), Clamp(h, minHeight, maxHeight));
    }

    /// <summary>The corner radius for a surface of a given size. See <see cref="CornerRadius"/>.</summary>
    internal static double ResolveCornerRadius(double requested, double width, double height)
    {
        var limit = Math.Max(0, Math.Min(width, height) / 2);
        var radius = requested < 0 ? Math.Min(height / 2, AutoCornerRadiusCap) : requested;
        return Math.Clamp(radius, 0, limit);
    }

    static double Clamp(double value, double min, double max)
        => max < min ? min : Math.Clamp(value, min, max);

    /// <summary>
    /// Whether the width the parent offers caps the notch. True in a page. A host that sizes itself
    /// <em>to</em> the notch (the desktop notch window) turns it off, or the notch could never grow
    /// wider than the window it is about to resize.
    /// </summary>
    internal bool FollowsOfferedWidth { get; set; } = true;

    /// <summary>
    /// The surface fills whatever the notch is given instead of animating its own size, and the host
    /// animates itself to <see cref="NotchSize"/>. The desktop notch window sets it: the AppKit head
    /// runs no layout when a realized view changes size, so a managed morph inside a native window would
    /// sit still and then jump — while a native window-frame animation lays out on every step.
    /// </summary>
    internal bool FillsHost
    {
        get => this.fillsHost;
        set
        {
            this.fillsHost = value;
            this.surface.HorizontalOptions = value ? LayoutOptions.Fill : LayoutOptions.Center;
            this.surface.VerticalOptions = value ? LayoutOptions.Fill : LayoutOptions.Start;
            this.HorizontalOptions = value ? LayoutOptions.Fill : LayoutOptions.Center;
            this.VerticalOptions = value ? LayoutOptions.Fill : LayoutOptions.Start;
            if (value)
            {
                this.AbortAnimation(MorphAnimationName);
                this.surface.WidthRequest = -1;
                this.surface.HeightRequest = -1;
            }
            this.Refresh(animate: false);
        }
    }
    bool fillsHost;

    /// <summary>
    /// The first content fades in as well, not only a swapped-in one. The desktop notch window builds a
    /// fresh notch for every content swap, so to it every content is the first; it sets this when there
    /// was something on screen before.
    /// </summary>
    internal bool FadeInFirstContent { get; set; }

    protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
    {
        // The width the parent offers is the cap for content that would otherwise run off the edge.
        // The constraint still includes our own margin (MAUI takes it off later, in ComputeDesiredSize), so
        // take it off here too, or a notch with a side margin is as wide as the parent and runs over it.
        // Only a real change is worth a remeasure: during a morph the parent asks on every frame.
        var offered = widthConstraint - this.Margin.HorizontalThickness;
        if (this.FollowsOfferedWidth && !Double.IsInfinity(offered) && offered > 0 && Math.Abs(offered - this.availableWidth) > 0.5)
        {
            this.availableWidth = offered;
            this.ScheduleRefresh();
        }
        return base.MeasureOverride(widthConstraint, heightConstraint);
    }

    void ScheduleRefresh()
    {
        if (this.remeasurePending)
            return;

        // Coalesced: a content swap invalidates measure several times over in one turn, and the size is
        // only worth reading once it has settled.
        if (this.Dispatcher is not { } dispatcher)
        {
            this.Refresh(animate: true);
            return;
        }

        this.remeasurePending = true;
        dispatcher.Dispatch(() =>
        {
            this.remeasurePending = false;
            this.Refresh(animate: true);
        });
    }

    void Refresh(bool animate)
    {
        var padding = this.ContentPadding;
        var offered = this.FollowsOfferedWidth ? this.availableWidth : Double.PositiveInfinity;
        var widthCap = Math.Min(this.MaxNotchWidth, offered);
        var contentSize = this.MeasureContent(widthCap - padding.HorizontalThickness, this.MaxNotchHeight - padding.VerticalThickness);

        var target = ComputeNotchSize(
            contentSize,
            padding,
            this.MinNotchWidth,
            this.MinNotchHeight,
            this.MaxNotchWidth,
            this.MaxNotchHeight,
            offered
        );

        // The content is pinned at its final size for the whole morph, so it is laid out once and the
        // surface clips it, instead of it re-wrapping on every frame.
        var innerWidth = Math.Max(0, target.Width - padding.HorizontalThickness);
        var innerHeight = Math.Max(0, target.Height - padding.VerticalThickness);
        this.contentHost.WidthRequest = contentSize is { } cs ? Math.Min(cs.Width, innerWidth) : -1;
        this.contentHost.HeightRequest = contentSize is { } ch ? Math.Min(ch.Height, innerHeight) : -1;
        this.contentHost.Margin = padding;

        this.MorphTo(target, animate);
    }

    Size? MeasureContent(double maxWidth, double maxHeight)
    {
        if (this.Content is not IView content || !this.Content.IsVisible)
            return null;

        if (maxWidth <= 0 || Double.IsNaN(maxWidth))
            maxWidth = Double.PositiveInfinity;
        if (maxHeight <= 0 || Double.IsNaN(maxHeight))
            maxHeight = Double.PositiveInfinity;

        // Offer the content no more than it will be laid out at. A layout with a WidthRequest measures
        // its children against the constraint it is given, not its own width, so wrapped text inside
        // would measure as fewer lines than it then wraps to, and the notch would clip the rest.
        maxWidth = CapByRequest(maxWidth, this.Content.WidthRequest, this.Content.MaximumWidthRequest);
        maxHeight = CapByRequest(maxHeight, this.Content.HeightRequest, this.Content.MaximumHeightRequest);

        // The content itself, not its host: the host carries the size we pinned last time, and would
        // just report that back.
        var size = content.Measure(maxWidth, maxHeight);
        var width = Math.Max(size.Width, this.Content.WidthRequest);
        var height = Math.Max(size.Height, this.Content.HeightRequest);
        return width > 0 && height > 0 ? new Size(width, height) : null;
    }

    static double CapByRequest(double constraint, double request, double maximum)
    {
        if (request >= 0)
            constraint = Math.Min(constraint, request);
        if (maximum >= 0)
            constraint = Math.Min(constraint, maximum);
        return constraint;
    }

    void MorphTo(Size target, bool animate)
    {
        if (this.fillsHost)
        {
            // The host morphs, and starts the content's fade as its morph starts (FadeInContent): that
            // can be a while after this, once the new content has been laid out.
            var previous = this.NotchSize;
            this.ApplyCorners(target.Width, target.Height);
            if (!animate)
                this.FadeInContentIfPending(0);
            if (Math.Abs(previous.Width - target.Width) <= 0.5 && Math.Abs(previous.Height - target.Height) <= 0.5)
                return;

            this.NotchSize = target;
            this.NotchSizeChanged?.Invoke(this, new NotchSizeChangedEventArgs(previous, target));
            this.MorphCompleted?.Invoke(this, EventArgs.Empty);
            return;
        }

        var from = new Size(
            this.surface.WidthRequest >= 0 ? this.surface.WidthRequest : target.Width,
            this.surface.HeightRequest >= 0 ? this.surface.HeightRequest : target.Height
        );

        var old = this.NotchSize;
        var changed = Math.Abs(old.Width - target.Width) > 0.5 || Math.Abs(old.Height - target.Height) > 0.5;
        var atRest = Math.Abs(from.Width - target.Width) <= 0.5 && Math.Abs(from.Height - target.Height) <= 0.5;

        // Already there, or already on the way there: pinning the content's size re-raises its
        // MeasureInvalidated, and restarting the morph from that would make it stutter.
        if (!changed && (atRest || this.AnimationIsRunning(MorphAnimationName)))
        {
            this.FadeInContentIfPending(0);
            return;
        }

        var token = ++this.morphToken;
        this.AbortAnimation(MorphAnimationName);
        this.NotchSize = target;
        if (changed)
            this.NotchSizeChanged?.Invoke(this, new NotchSizeChangedEventArgs(old, target));

        var duration = this.MorphDuration;
        if (!animate || duration == 0 || this.Handler == null)
        {
            this.ApplyFrame(target.Width, target.Height);
            this.FadeInContentIfPending(0);
            this.MorphCompleted?.Invoke(this, EventArgs.Empty);
            return;
        }

        var easing = this.MorphEasing ?? DefaultEasing;
        var animation = new Animation(t =>
        {
            this.ApplyFrame(
                from.Width + ((target.Width - from.Width) * t),
                from.Height + ((target.Height - from.Height) * t)
            );
        }, 0, 1, easing);

        animation.Commit(this, MorphAnimationName, 16, duration, finished: (_, cancelled) =>
        {
            if (token != this.morphToken)
                return;

            // Land exactly: an easing that overshoots can finish a hair off the target.
            this.ApplyFrame(target.Width, target.Height);
            if (!cancelled)
                this.MorphCompleted?.Invoke(this, EventArgs.Empty);
        });

        this.FadeInContentIfPending(duration);
    }

    void ApplyFrame(double width, double height)
    {
        width = Math.Max(0, width);
        height = Math.Max(0, height);
        this.surface.WidthRequest = width;
        this.surface.HeightRequest = height;
        this.ApplyCorners(width, height);
    }


    // ---------------------------------------------------------------------------------------------
    // Content
    // ---------------------------------------------------------------------------------------------

    void OnContentChanged(View? oldContent, View? newContent)
    {
        // Fade the new content in as the surface reshapes around it. Not on the very first content:
        // a notch that is there when the page opens should just be there.
        this.fadeInNext = newContent != null && (this.FadeInFirstContent || (oldContent != null && this.Handler != null));
        if (this.fadeInNext)
            this.contentHost.Opacity = 0;

        this.contentHost.Content = newContent;
        this.ScheduleRefresh();
    }

    /// <summary>
    /// Fades in content that is waiting to (see <see cref="FadeInFirstContent"/>), over
    /// <paramref name="duration"/> ms. For a host that fills with the notch and morphs on its own clock.
    /// </summary>
    internal void FadeInContent(uint duration) => this.FadeInContentIfPending(duration);

    void FadeInContentIfPending(uint duration)
    {
        if (!this.fadeInNext)
            return;

        this.fadeInNext = false;
        if (duration == 0)
        {
            this.contentHost.Opacity = 1;
            return;
        }
        _ = this.contentHost.FadeToAsync(1, (uint)(duration * 0.6), Easing.CubicOut);
    }


    // ---------------------------------------------------------------------------------------------
    // Visuals
    // ---------------------------------------------------------------------------------------------

    void ApplySurface()
    {
        this.surface.BackgroundColor = this.SurfaceColor;
        this.surface.Stroke = this.Stroke is { } stroke ? new SolidColorBrush(stroke) : null;
        this.surface.StrokeThickness = this.Stroke != null ? this.StrokeThickness : 0;
        this.shadow.Opacity = this.HasShadow ? 0.35f : 0f;
    }

    void ApplyOutline()
        => this.ApplyCorners(
            this.surface.WidthRequest >= 0 ? this.surface.WidthRequest : this.NotchSize.Width,
            this.surface.HeightRequest >= 0 ? this.surface.HeightRequest : this.NotchSize.Height
        );

    void ApplyCorners(double width, double height)
    {
        var r = ResolveCornerRadius(this.CornerRadius, width, height);
        this.outline.CornerRadius = this.Shape == NotchShape.Notch
            ? new CornerRadius(0, 0, r, r)
            : new CornerRadius(r);
    }

    void OnSurfaceTapped()
    {
        this.Tapped?.Invoke(this, EventArgs.Empty);

        var parameter = this.CommandParameter;
        if (this.Command is { } command && command.CanExecute(parameter))
            command.Execute(parameter);
    }

    /// <summary>The surface's current, possibly mid-morph, size. Exposed for tests.</summary>
    internal Size SurfaceSize => new(this.surface.WidthRequest, this.surface.HeightRequest);

    /// <summary>The surface's current corner radius. Exposed for tests.</summary>
    internal CornerRadius SurfaceCorners => this.outline.CornerRadius;

    /// <summary>The surface's tap command. Exposed for tests.</summary>
    internal System.Windows.Input.ICommand? SurfaceTapCommand
        => (this.surface.GestureRecognizers[0] as TapGestureRecognizer)?.Command;
}
