using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Diagram.Internal;

/// <summary>
/// Resolves theme tokens into concrete <see cref="Color"/> values for the parts of the diagram that
/// are drawn rather than composed from views - which is all of them, unless a node template is set.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="IDrawable"/> gets a canvas, not a resource dictionary, so <c>SetDynamicResource</c>
/// is no use to it: it needs a colour it can hand to <c>canvas.FillColor</c> right now. The trick is
/// the one <c>ThemeProbe</c> and <c>GanttPalette</c> already use - a hidden, zero-sized
/// <see cref="BoxView"/> is a real element in the visual tree, so a dynamic resource on its
/// <see cref="BoxView.Color"/> resolves and keeps resolving across a live theme swap. Reading it
/// gives the drawable a real colour, and watching it gives the control something to invalidate on.
/// </para>
/// <para>
/// Every fallback lives here rather than at each call site, which is what keeps the drawables free of
/// colour literals entirely: they ask for <c>palette.OnSurface</c> and get a themed colour, so there
/// is nowhere for a hardcoded grey to creep back in.
/// </para>
/// </remarks>
sealed class DiagramPalette : IDisposable
{
    // Only ever used before the theme dictionary has merged - which does happen, because the
    // alternate app heads merge theirs after the control's constructor has already run once.
    static readonly Color FallbackSurface = Colors.White;
    static readonly Color FallbackSurfaceContainer = Color.FromArgb("#F3F4F6");
    static readonly Color FallbackOnSurface = Colors.Black;
    static readonly Color FallbackOnSurfaceVariant = Colors.Gray;
    static readonly Color FallbackOutline = Color.FromArgb("#9CA3AF");
    static readonly Color FallbackOutlineVariant = Color.FromArgb("#E5E7EB");
    static readonly Color FallbackPrimary = Colors.SlateBlue;

    readonly List<BoxView> probes = [];
    readonly Layout host;

    readonly BoxView surface;
    readonly BoxView surfaceContainer;
    readonly BoxView onSurface;
    readonly BoxView onSurfaceVariant;
    readonly BoxView outline;
    readonly BoxView outlineVariant;
    readonly BoxView primary;

    /// <summary>Creates the probes and parents them into the control's own layout.</summary>
    /// <param name="host">The layout to hold the probes. They are invisible and zero-sized.</param>
    public DiagramPalette(Layout host)
    {
        this.host = host;

        this.surface = this.Probe(ShinyThemeKeys.Color.Surface);
        this.surfaceContainer = this.Probe(ShinyThemeKeys.Color.SurfaceContainer);
        this.onSurface = this.Probe(ShinyThemeKeys.Color.OnSurface);
        this.onSurfaceVariant = this.Probe(ShinyThemeKeys.Color.OnSurfaceVariant);
        this.outline = this.Probe(ShinyThemeKeys.Color.Outline);
        this.outlineVariant = this.Probe(ShinyThemeKeys.Color.OutlineVariant);
        this.primary = this.Probe(ShinyThemeKeys.Color.Primary);
    }

    /// <summary>The diagram's background.</summary>
    public Color Surface => Resolve(this.surface, FallbackSurface);

    /// <summary>A node's default fill.</summary>
    public Color SurfaceContainer => Resolve(this.surfaceContainer, FallbackSurfaceContainer);

    /// <summary>A node label's colour.</summary>
    public Color OnSurface => Resolve(this.onSurface, FallbackOnSurface);

    /// <summary>A connection label's colour.</summary>
    public Color OnSurfaceVariant => Resolve(this.onSurfaceVariant, FallbackOnSurfaceVariant);

    /// <summary>A node outline and a connection line.</summary>
    public Color Outline => Resolve(this.outline, FallbackOutline);

    /// <summary>The background grid.</summary>
    public Color OutlineVariant => Resolve(this.outlineVariant, FallbackOutlineVariant);

    /// <summary>Selection, the marquee, connector handles and the line being drawn.</summary>
    public Color Primary => Resolve(this.primary, FallbackPrimary);

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

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var probe in this.probes)
            probe.PropertyChanged -= this.OnProbeChanged;

        this.probes.Clear();
    }

    /// <summary>Parses a node or connection's colour string, falling back when it is null or unparseable.</summary>
    /// <param name="value">The hex string from the model.</param>
    /// <param name="fallback">The themed colour to use instead.</param>
    public static Color Parse(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        return Color.TryParse(value, out var parsed) ? parsed : fallback;
    }
}
