using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Gantt.Internal;

/// <summary>
/// Resolves theme tokens into concrete <see cref="Color"/> values for the parts of the Gantt that are
/// <i>drawn</i> rather than composed from views.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="IDrawable"/> gets a canvas, not a resource dictionary, so <c>SetDynamicResource</c>
/// is no use to it — it needs a colour it can hand to <c>canvas.FillColor</c> right now. The trick is
/// the one <c>ThemeProbe</c> already uses for brushes: a hidden, zero-sized <see cref="BoxView"/> is a
/// real element in the visual tree, so a dynamic resource on its <see cref="BoxView.Color"/> resolves
/// and keeps resolving across a live theme swap. Reading that property gives the drawable a real
/// colour, and watching it gives the control something to invalidate on.
/// </para>
/// <para>
/// Every fallback lives here rather than at each call site. That is what keeps the drawables free of
/// colour literals entirely: they ask for <c>palette.OnSurface</c> and get a themed colour, so there
/// is nowhere for a hardcoded grey to creep back in.
/// </para>
/// </remarks>
sealed class GanttPalette : IDisposable
{
    // Only ever used before the theme dictionary has merged - which does happen, because the
    // alternate app heads merge theirs after the control's constructor has already run once.
    static readonly Color FallbackSurface = Colors.White;
    static readonly Color FallbackSurfaceContainer = Color.FromArgb("#F3F4F6");
    static readonly Color FallbackSurfaceContainerHigh = Color.FromArgb("#E9EAEE");
    static readonly Color FallbackOnSurface = Colors.Black;
    static readonly Color FallbackOnSurfaceVariant = Colors.Gray;
    static readonly Color FallbackOutline = Colors.Gray;
    static readonly Color FallbackOutlineVariant = Color.FromArgb("#E5E7EB");
    static readonly Color FallbackPrimary = Colors.SlateBlue;
    static readonly Color FallbackOnPrimary = Colors.White;
    static readonly Color FallbackSecondary = Colors.SteelBlue;
    static readonly Color FallbackTertiary = Colors.DarkSlateBlue;
    static readonly Color FallbackError = Colors.IndianRed;
    static readonly Color FallbackWarning = Colors.Goldenrod;
    static readonly Color FallbackSuccess = Colors.SeaGreen;

    readonly List<BoxView> probes = [];
    readonly Layout host;

    readonly BoxView surface;
    readonly BoxView surfaceContainer;
    readonly BoxView surfaceContainerHigh;
    readonly BoxView onSurface;
    readonly BoxView onSurfaceVariant;
    readonly BoxView outline;
    readonly BoxView outlineVariant;
    readonly BoxView primary;
    readonly BoxView onPrimary;
    readonly BoxView secondary;
    readonly BoxView tertiary;
    readonly BoxView error;
    readonly BoxView warning;
    readonly BoxView success;

    public GanttPalette(Layout host)
    {
        this.host = host;

        this.surface = this.Probe(ShinyThemeKeys.Color.Surface);
        this.surfaceContainer = this.Probe(ShinyThemeKeys.Color.SurfaceContainer);
        this.surfaceContainerHigh = this.Probe(ShinyThemeKeys.Color.SurfaceContainerHigh);
        this.onSurface = this.Probe(ShinyThemeKeys.Color.OnSurface);
        this.onSurfaceVariant = this.Probe(ShinyThemeKeys.Color.OnSurfaceVariant);
        this.outline = this.Probe(ShinyThemeKeys.Color.Outline);
        this.outlineVariant = this.Probe(ShinyThemeKeys.Color.OutlineVariant);
        this.primary = this.Probe(ShinyThemeKeys.Color.Primary);
        this.onPrimary = this.Probe(ShinyThemeKeys.Color.OnPrimary);
        this.secondary = this.Probe(ShinyThemeKeys.Color.Secondary);
        this.tertiary = this.Probe(ShinyThemeKeys.Color.Tertiary);
        this.error = this.Probe(ShinyThemeKeys.Color.Error);
        this.warning = this.Probe(ShinyThemeKeys.Color.Warning);
        this.success = this.Probe(ShinyThemeKeys.Color.Success);
    }

    public Color Surface => Resolve(this.surface, FallbackSurface);
    public Color SurfaceContainer => Resolve(this.surfaceContainer, FallbackSurfaceContainer);
    public Color SurfaceContainerHigh => Resolve(this.surfaceContainerHigh, FallbackSurfaceContainerHigh);
    public Color OnSurface => Resolve(this.onSurface, FallbackOnSurface);
    public Color OnSurfaceVariant => Resolve(this.onSurfaceVariant, FallbackOnSurfaceVariant);
    public Color Outline => Resolve(this.outline, FallbackOutline);
    public Color OutlineVariant => Resolve(this.outlineVariant, FallbackOutlineVariant);
    public Color Primary => Resolve(this.primary, FallbackPrimary);
    public Color OnPrimary => Resolve(this.onPrimary, FallbackOnPrimary);
    public Color Secondary => Resolve(this.secondary, FallbackSecondary);
    public Color Tertiary => Resolve(this.tertiary, FallbackTertiary);
    public Color Error => Resolve(this.error, FallbackError);
    public Color Warning => Resolve(this.warning, FallbackWarning);
    public Color Success => Resolve(this.success, FallbackSuccess);

    /// <summary>Fires when the theme changes any of the resolved colours.</summary>
    public event EventHandler? Changed;


    /// <summary>
    /// A probe's colour, or its fallback. Transparent counts as unresolved: it is the seed value, and
    /// a theme pack would never deliberately paint chrome with it.
    /// </summary>
    static Color Resolve(BoxView probe, Color fallback) =>
        probe.Color is { } value && value != Colors.Transparent ? value : fallback;


    BoxView Probe(string token)
    {
        var probe = new BoxView
        {
            // Never measured, never painted. It exists only to own a resolvable resource chain.
            IsVisible = false,
            WidthRequest = 0,
            HeightRequest = 0,
            InputTransparent = true,
            Color = Colors.Transparent
        };
        probe.SetDynamicResource(BoxView.ColorProperty, token);
        probe.PropertyChanged += this.OnProbeChanged;

        this.probes.Add(probe);
        this.host.Add(probe);
        return probe;
    }


    void OnProbeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BoxView.Color))
            this.Changed?.Invoke(this, EventArgs.Empty);
    }


    public void Dispose()
    {
        foreach (var probe in this.probes)
            probe.PropertyChanged -= this.OnProbeChanged;

        this.probes.Clear();
    }
}


/// <summary>Colour helpers the drawables share.</summary>
static class GanttColors
{
    /// <summary>Black or white, whichever reads on the given fill.</summary>
    public static Color InkFor(Color background)
    {
        // Rec. 601 luma. Good enough for picking between two inks, and far cheaper than a full
        // contrast-ratio computation run once per bar per frame.
        var luma = (0.299 * background.Red) + (0.587 * background.Green) + (0.114 * background.Blue);
        return luma > 0.6 ? Colors.Black : Colors.White;
    }

    /// <summary>Parses a task's colour string, falling back when it is null or unparseable.</summary>
    public static Color Parse(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        return Color.TryParse(value, out var parsed) ? parsed : fallback;
    }
}
