using System.Collections;
using System.Collections.Specialized;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.MotionIcons;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

/// <summary>
/// Scrolls a set of items endlessly - horizontally, vertically, or along any angle - in the style of
/// Magic UI's marquee.
/// </summary>
/// <example>
/// <code>
/// &lt;shiny:Marquee ItemsSource="{Binding Logos}" Gap="24" FadeEdges="True" PauseOnHover="True"&gt;
///     &lt;shiny:Marquee.ItemTemplate&gt;
///         &lt;DataTemplate&gt;&lt;Image Source="{Binding}" HeightRequest="32" /&gt;&lt;/DataTemplate&gt;
///     &lt;/shiny:Marquee.ItemTemplate&gt;
/// &lt;/shiny:Marquee&gt;
/// </code>
/// </example>
/// <remarks>
/// <para>
/// The items are realized from <see cref="ItemTemplate"/> once per copy and laid end to end along the
/// axis at <see cref="Angle"/>; enough copies are made to keep the viewport covered for a whole loop,
/// so the run wraps without a seam. Views cannot be cloned, which is why content comes from a template:
/// with no <see cref="ItemsSource"/> the template is realized once per copy and inherits the
/// marquee's binding context.
/// </para>
/// <para>
/// Movement runs on the window's shared frame ticker - one timer for every marquee and motion icon on
/// the window - and stops while the marquee is unloaded, paused, or the device asks for reduced motion.
/// </para>
/// </remarks>
public class Marquee : ContentView
{
    readonly MarqueePanel panel;
    readonly MarqueeTrack track;
    readonly BoxView fade;
    readonly BoxView fadeProbe;
    readonly List<Size> itemSizes = [];

    INotifyCollectionChanged? observed;
    MotionTicker? ticker;
    int groupSize;
    int copies;
    double period;
    double largestExtent;
    double offset;
    bool hovering;
    bool pressing;
    bool reducedMotion;
    bool rebuildQueued;
    Size lastViewport;

    public Marquee()
    {
        this.track = new MarqueeTrack { IsClippedToBounds = false };
        this.fade = new BoxView { InputTransparent = true, IsVisible = false };
        this.fadeProbe = new BoxView { IsVisible = false, WidthRequest = 0, HeightRequest = 0, InputTransparent = true };
        this.fadeProbe.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == BoxView.ColorProperty.PropertyName)
                this.UpdateFade();
        };

        this.panel = new MarqueePanel(this)
        {
            Children = { this.track, this.fade, this.fadeProbe }
        };

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => this.SetHovering(true);
        pointer.PointerExited += (_, _) => this.SetHovering(false);
        pointer.PointerPressed += (_, _) => this.SetPressing(true);
        pointer.PointerReleased += (_, _) => this.SetPressing(false);
        this.panel.GestureRecognizers.Add(pointer);

        this.Content = this.panel;
        this.Loaded += (_, _) =>
        {
            this.reducedMotion = this.RespectReducedMotion && IsReduceMotionEnabled();
            this.UpdateRunning();
        };
        this.Unloaded += (_, _) => this.StopTicker();

        ThemeProbe.Tint(this.fadeProbe, BoxView.ColorProperty, this.FadeColor, ShinyThemeKeys.Color.Surface);
        StyleGuard.MarkReady(this, typeof(Marquee));
        this.Rebuild();
    }


    // ---------------------------------------------------------------------------------------------
    // Properties
    // ---------------------------------------------------------------------------------------------

    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource), typeof(IEnumerable), typeof(Marquee), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Marquee), () => ((Marquee)b).OnItemsSourceChanged()));

    /// <summary>The items to scroll. Null realizes <see cref="ItemTemplate"/> once per copy against the marquee's binding context.</summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)this.GetValue(ItemsSourceProperty);
        set => this.SetValue(ItemsSourceProperty, value);
    }

    public static readonly BindableProperty ItemTemplateProperty = BindableProperty.Create(
        nameof(ItemTemplate), typeof(DataTemplate), typeof(Marquee), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Marquee), () => ((Marquee)b).Rebuild()));

    /// <summary>How each item is drawn. A <see cref="DataTemplateSelector"/> is honoured.</summary>
    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)this.GetValue(ItemTemplateProperty);
        set => this.SetValue(ItemTemplateProperty, value);
    }

    public static readonly BindableProperty AngleProperty = BindableProperty.Create(
        nameof(Angle), typeof(double), typeof(Marquee), 0d,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Marquee), () => ((Marquee)b).OnGeometryChanged()));

    /// <summary>
    /// The direction of travel, in degrees. 0 scrolls leftward, 90 upward, 180 rightward, 270 downward;
    /// anything in between travels along that angle (-15 drifts left and slightly down).
    /// </summary>
    public double Angle
    {
        get => (double)this.GetValue(AngleProperty);
        set => this.SetValue(AngleProperty, value);
    }

    public static readonly BindableProperty ReverseProperty = BindableProperty.Create(
        nameof(Reverse), typeof(bool), typeof(Marquee), false);

    /// <summary>Runs the other way along the same axis.</summary>
    public bool Reverse
    {
        get => (bool)this.GetValue(ReverseProperty);
        set => this.SetValue(ReverseProperty, value);
    }

    public static readonly BindableProperty DurationProperty = BindableProperty.Create(
        nameof(Duration), typeof(TimeSpan), typeof(Marquee), TimeSpan.FromSeconds(40));

    /// <summary>How long one full pass of the item set takes. Ignored when <see cref="Speed"/> is set.</summary>
    public TimeSpan Duration
    {
        get => (TimeSpan)this.GetValue(DurationProperty);
        set => this.SetValue(DurationProperty, value);
    }

    public static readonly BindableProperty SpeedProperty = BindableProperty.Create(
        nameof(Speed), typeof(double), typeof(Marquee), 0d);

    /// <summary>
    /// Travel speed in device-independent units per second. When positive it replaces
    /// <see cref="Duration"/>, so the pace stays the same however many items there are.
    /// </summary>
    public double Speed
    {
        get => (double)this.GetValue(SpeedProperty);
        set => this.SetValue(SpeedProperty, value);
    }

    public static readonly BindableProperty GapProperty = BindableProperty.Create(
        nameof(Gap), typeof(double), typeof(Marquee), 16d,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Marquee), () => ((Marquee)b).OnGeometryChanged()));

    /// <summary>Space between items, and between the last item and the next copy's first.</summary>
    public double Gap
    {
        get => (double)this.GetValue(GapProperty);
        set => this.SetValue(GapProperty, value);
    }

    public static readonly BindableProperty RepeatProperty = BindableProperty.Create(
        nameof(Repeat), typeof(int), typeof(Marquee), 4,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Marquee), () => ((Marquee)b).Rebuild()));

    /// <summary>The fewest copies of the item set to lay out. More are added when it takes more to fill the viewport.</summary>
    public int Repeat
    {
        get => (int)this.GetValue(RepeatProperty);
        set => this.SetValue(RepeatProperty, value);
    }

    public static readonly BindableProperty PauseOnHoverProperty = BindableProperty.Create(
        nameof(PauseOnHover), typeof(bool), typeof(Marquee), false,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Marquee), () => ((Marquee)b).UpdateRunning()));

    /// <summary>Stops while a mouse or pen is over it (desktop, iPad pointer).</summary>
    public bool PauseOnHover
    {
        get => (bool)this.GetValue(PauseOnHoverProperty);
        set => this.SetValue(PauseOnHoverProperty, value);
    }

    public static readonly BindableProperty PauseOnPressProperty = BindableProperty.Create(
        nameof(PauseOnPress), typeof(bool), typeof(Marquee), false,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Marquee), () => ((Marquee)b).UpdateRunning()));

    /// <summary>Stops while a finger (or button) is held down on it.</summary>
    public bool PauseOnPress
    {
        get => (bool)this.GetValue(PauseOnPressProperty);
        set => this.SetValue(PauseOnPressProperty, value);
    }

    public static readonly BindableProperty IsRunningProperty = BindableProperty.Create(
        nameof(IsRunning), typeof(bool), typeof(Marquee), true, BindingMode.TwoWay,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Marquee), () => ((Marquee)b).UpdateRunning()));

    /// <summary>Whether it moves at all. False holds it where it is.</summary>
    public bool IsRunning
    {
        get => (bool)this.GetValue(IsRunningProperty);
        set => this.SetValue(IsRunningProperty, value);
    }

    static readonly BindablePropertyKey IsPausedPropertyKey = BindableProperty.CreateReadOnly(
        nameof(IsPaused), typeof(bool), typeof(Marquee), false);

    public static readonly BindableProperty IsPausedProperty = IsPausedPropertyKey.BindableProperty;

    /// <summary>True whenever it is standing still - stopped, hovered, pressed, or held by reduced motion.</summary>
    public bool IsPaused => (bool)this.GetValue(IsPausedProperty);

    public static readonly BindableProperty FadeEdgesProperty = BindableProperty.Create(
        nameof(FadeEdges), typeof(bool), typeof(Marquee), false,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Marquee), () => ((Marquee)b).UpdateFade()));

    /// <summary>Fades the content out at both ends of the axis.</summary>
    public bool FadeEdges
    {
        get => (bool)this.GetValue(FadeEdgesProperty);
        set => this.SetValue(FadeEdgesProperty, value);
    }

    public static readonly BindableProperty FadeLengthProperty = BindableProperty.Create(
        nameof(FadeLength), typeof(double), typeof(Marquee), 48d,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Marquee), () => ((Marquee)b).UpdateFade()));

    /// <summary>How long each fade is, in device-independent units.</summary>
    public double FadeLength
    {
        get => (double)this.GetValue(FadeLengthProperty);
        set => this.SetValue(FadeLengthProperty, value);
    }

    public static readonly BindableProperty FadeColorProperty = BindableProperty.Create(
        nameof(FadeColor), typeof(Color), typeof(Marquee), null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(Marquee), () =>
            ThemeProbe.Tint(((Marquee)b).fadeProbe, BoxView.ColorProperty, (Color?)n, ShinyThemeKeys.Color.Surface)));

    /// <summary>
    /// What the edges fade into - set it to whatever is behind the marquee. Defaults to the theme's
    /// Surface color. MAUI has no alpha mask, so the fade is painted over the content in this color.
    /// </summary>
    public Color? FadeColor
    {
        get => (Color?)this.GetValue(FadeColorProperty);
        set => this.SetValue(FadeColorProperty, value);
    }

    public static readonly BindableProperty KeepContentUprightProperty = BindableProperty.Create(
        nameof(KeepContentUpright), typeof(bool), typeof(Marquee), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Marquee), () => ((Marquee)b).OnGeometryChanged()));

    /// <summary>
    /// At an angle, whether items stay level (the default) or tilt with the direction of travel like a
    /// ribbon. Has no effect at 0°, 90°, 180° or 270° - other than turning items on their side at 90°
    /// and 270° when off.
    /// </summary>
    public bool KeepContentUpright
    {
        get => (bool)this.GetValue(KeepContentUprightProperty);
        set => this.SetValue(KeepContentUprightProperty, value);
    }

    public static readonly BindableProperty RespectReducedMotionProperty = BindableProperty.Create(
        nameof(RespectReducedMotion), typeof(bool), typeof(Marquee), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Marquee), () =>
        {
            var m = (Marquee)b;
            m.reducedMotion = m.RespectReducedMotion && IsReduceMotionEnabled();
            m.UpdateRunning();
        }));

    /// <summary>Holds still when the OS asks for reduced motion. On by default.</summary>
    public bool RespectReducedMotion
    {
        get => (bool)this.GetValue(RespectReducedMotionProperty);
        set => this.SetValue(RespectReducedMotionProperty, value);
    }


    // ---------------------------------------------------------------------------------------------
    // Test seams
    // ---------------------------------------------------------------------------------------------

    internal double Offset => this.offset;
    internal double Period => this.period;
    internal int Copies => this.copies;
    internal int GroupSize => this.groupSize;
    internal MarqueeTrack Track => this.track;
    internal bool IsTicking => this.ticker is not null;


    /// <summary>Moves the run on by real time. The ticker's callback, and the seam tests drive.</summary>
    internal void Advance(TimeSpan elapsed)
    {
        if (this.IsPaused)
            return;

        this.offset = MarqueeGeometry.Advance(this.offset, this.period, elapsed, this.Duration, this.Speed, this.Reverse);
        this.ApplyTranslation();
    }


    internal void SetHovering(bool value)
    {
        this.hovering = value;
        this.UpdateRunning();
    }


    internal void SetPressing(bool value)
    {
        this.pressing = value;
        this.UpdateRunning();
    }


    // ---------------------------------------------------------------------------------------------
    // Items
    // ---------------------------------------------------------------------------------------------

    void OnItemsSourceChanged()
    {
        if (this.observed is not null)
            this.observed.CollectionChanged -= this.OnCollectionChanged;

        this.observed = this.ItemsSource as INotifyCollectionChanged;
        if (this.observed is not null)
            this.observed.CollectionChanged += this.OnCollectionChanged;

        this.Rebuild();
    }


    void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => this.Rebuild();


    void Rebuild() => this.Rebuild(Math.Max(1, this.Repeat));


    void Rebuild(int copyCount)
    {
        this.rebuildQueued = false;
        this.track.Children.Clear();
        this.groupSize = 0;
        this.copies = 0;

        var template = this.ItemTemplate;
        if (template is not null)
        {
            var hasSource = this.ItemsSource is not null;
            var items = hasSource ? this.ItemsSource!.Cast<object?>().ToList() : [null];

            for (var copy = 0; copy < copyCount; copy++)
            {
                foreach (var item in items)
                {
                    if (this.Realize(template, item, hasSource) is not { } view)
                        continue;

                    // Every copy after the first is decoration; a screen reader reads the set once.
                    if (copy > 0)
                        AutomationProperties.SetIsInAccessibleTree(view, false);

                    this.track.Children.Add(view);
                }
            }

            this.groupSize = items.Count;
            this.copies = copyCount;
        }

        this.ApplyItemRotation();
        ((IView)this.panel).InvalidateMeasure();
        this.UpdateRunning();
    }


    View? Realize(DataTemplate template, object? item, bool hasSource)
    {
        var chosen = template is DataTemplateSelector selector ? selector.SelectTemplate(item, this) : template;
        var content = chosen?.CreateContent();
        var view = content as View ?? (content as ViewCell)?.View;

        if (view is not null && hasSource)
            view.BindingContext = item;

        return view;
    }


    void ApplyItemRotation()
    {
        var rotation = this.KeepContentUpright ? 0 : this.Angle;
        foreach (var child in this.track.Children)
        {
            if (child is VisualElement ve)
                ve.Rotation = rotation;
        }
    }


    void OnGeometryChanged()
    {
        this.ApplyItemRotation();
        ((IView)this.panel).InvalidateMeasure();
        this.UpdateFade();
    }


    // ---------------------------------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------------------------------

    internal Size MeasurePanel(double widthConstraint, double heightConstraint)
    {
        if (this.WidthRequest >= 0)
            widthConstraint = this.WidthRequest;
        if (this.HeightRequest >= 0)
            heightConstraint = this.HeightRequest;

        this.itemSizes.Clear();
        foreach (var child in this.track.Children)
            this.itemSizes.Add(child.Measure(double.PositiveInfinity, double.PositiveInfinity));

        var angle = this.Angle;
        var upright = this.KeepContentUpright;

        // One copy of the set is the period; it repeats exactly, so the first is representative.
        this.period = 0;
        this.largestExtent = 0;
        double band = 0;
        for (var i = 0; i < this.groupSize && i < this.itemSizes.Count; i++)
        {
            var along = MarqueeGeometry.ExtentAlong(this.itemSizes[i], angle, upright);
            this.period += along + this.Gap;
            this.largestExtent = Math.Max(this.largestExtent, along);
            band = Math.Max(band, MarqueeGeometry.ExtentAcross(this.itemSizes[i], angle, upright));
        }
        this.offset = MarqueeGeometry.Wrap(this.offset, this.period);

        var (x, y) = MarqueeGeometry.Axis(angle);
        var width = double.IsFinite(widthConstraint) ? widthConstraint : (Math.Abs(x) * this.period) + (Math.Abs(y) * band);
        var height = double.IsFinite(heightConstraint) ? heightConstraint : (Math.Abs(y) * this.period) + (Math.Abs(x) * band);

        ((IView)this.fade).Measure(width, height);
        ((IView)this.fadeProbe).Measure(0, 0);
        return new Size(width, height);
    }


    internal Size ArrangePanel(Rect bounds)
    {
        var size = bounds.Size;
        ((IView)this.fade).Arrange(new Rect(Point.Zero, size));
        ((IView)this.fadeProbe).Arrange(Rect.Zero);

        var count = Math.Min(this.track.Children.Count, this.itemSizes.Count);
        if (count == 0 || this.period <= 0)
        {
            this.track.Placements.Clear();
            ((IView)this.track).Arrange(Rect.Zero);
            return size;
        }

        var angle = this.Angle;
        var upright = this.KeepContentUpright;
        var viewport = MarqueeGeometry.ViewportExtent(size, angle);

        // More copies are needed to cover a viewport this size. Children cannot be added mid-arrange,
        // so the rebuild is queued and this pass lays out what exists.
        var needed = MarqueeGeometry.Copies(this.period, viewport, this.largestExtent, this.Repeat);
        if (needed > this.copies && !this.rebuildQueued && this.groupSize > 0)
        {
            this.rebuildQueued = true;
            var dispatcher = this.Dispatcher ?? Application.Current?.Dispatcher;
            if (dispatcher is null)
                this.rebuildQueued = false;
            else
                dispatcher.Dispatch(() => this.Rebuild(needed));
        }

        // Lay the run along the axis through the centre of the viewport, starting far enough back that
        // it still covers the near edge once shifted forward by a whole period.
        var (x, y) = MarqueeGeometry.Axis(angle);
        var centre = new Point(size.Width / 2, size.Height / 2);
        var s = -(viewport / 2) - this.largestExtent;

        var rects = new Rect[count];
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        for (var i = 0; i < count; i++)
        {
            var item = this.itemSizes[i];
            var along = MarqueeGeometry.ExtentAlong(item, angle, upright);
            var cx = centre.X + ((s + (along / 2)) * x);
            var cy = centre.Y + ((s + (along / 2)) * y);
            var rect = new Rect(cx - (item.Width / 2), cy - (item.Height / 2), item.Width, item.Height);
            rects[i] = rect;

            left = Math.Min(left, rect.Left);
            top = Math.Min(top, rect.Top);
            right = Math.Max(right, rect.Right);
            bottom = Math.Max(bottom, rect.Bottom);
            s += along + this.Gap;
        }

        this.track.Placements.Clear();
        foreach (var rect in rects)
            this.track.Placements.Add(rect.Offset(-left, -top));

        this.track.Extent = new Size(right - left, bottom - top);
        ((IView)this.track).Arrange(new Rect(left, top, right - left, bottom - top));

        if (size != this.lastViewport)
        {
            this.lastViewport = size;
            this.UpdateFade();
        }

        this.ApplyTranslation();
        this.UpdateRunning();
        return size;
    }


    void ApplyTranslation()
    {
        var (x, y) = MarqueeGeometry.Axis(this.Angle);
        this.track.TranslationX = -this.offset * x;
        this.track.TranslationY = -this.offset * y;
    }


    void UpdateFade()
    {
        if (!this.FadeEdges || this.lastViewport.Width <= 0 || this.lastViewport.Height <= 0)
        {
            this.fade.IsVisible = false;
            return;
        }

        var color = this.fadeProbe.Color ?? Colors.White;
        var (start, end) = MarqueeGeometry.GradientPoints(this.Angle);

        // The gradient runs between the two points in the box's own pixels; the fade is a fraction of that.
        var length = Math.Sqrt(
            Math.Pow((end.X - start.X) * this.lastViewport.Width, 2) +
            Math.Pow((end.Y - start.Y) * this.lastViewport.Height, 2));
        var f = (float)Math.Clamp(this.FadeLength / Math.Max(1, length), 0, 0.45);
        var clear = color.WithAlpha(0);

        this.fade.Background = new LinearGradientBrush(
            [
                new GradientStop(color, 0),
                new GradientStop(clear, f),
                new GradientStop(clear, 1 - f),
                new GradientStop(color, 1)
            ],
            start,
            end
        );
        this.fade.IsVisible = true;
    }


    // ---------------------------------------------------------------------------------------------
    // Running
    // ---------------------------------------------------------------------------------------------

    void UpdateRunning()
    {
        var paused = !this.IsRunning
            || (this.PauseOnHover && this.hovering)
            || (this.PauseOnPress && this.pressing)
            || this.reducedMotion;

        this.SetValue(IsPausedPropertyKey, paused);

        if (!paused && this.IsLoaded && this.period > 0)
            this.StartTicker();
        else
            this.StopTicker();
    }


    void StartTicker()
    {
        if (this.ticker is not null)
            return;

        this.ticker = MotionTicker.For(this);
        if (this.ticker is not null)
            this.ticker.Tick += this.Advance;
    }


    void StopTicker()
    {
        if (this.ticker is null)
            return;

        this.ticker.Tick -= this.Advance;
        this.ticker = null;
    }


    static bool IsReduceMotionEnabled()
    {
        try
        {
#if IOS || MACCATALYST
            return UIKit.UIAccessibility.IsReduceMotionEnabled;
#elif ANDROID
            // Android has no reduce-motion switch; "Remove animations" zeroes the animator scale.
            var resolver = Android.App.Application.Context.ContentResolver;
            return Android.Provider.Settings.Global.GetFloat(resolver, Android.Provider.Settings.Global.AnimatorDurationScale, 1f) == 0f;
#elif WINDOWS
            return !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
#else
            return false;
#endif
        }
        catch
        {
            return false;
        }
    }
}
