namespace Shiny.Maui.Controls.Agents;

/// <summary>
/// Draws A2UI v1.0 surfaces - agent-described UI - with native MAUI views: the 18 basic catalog
/// components plus the Shiny catalog (DataGrid, Gauge, ChipGroup, DateRangePicker and more).
/// </summary>
/// <remarks>
/// <para>
/// Give it one <see cref="Surface"/>, or a <see cref="Host"/> (optionally narrowed with
/// <see cref="SurfaceId"/>) to draw the host's surfaces in creation order as they arrive. Inputs
/// write to the surface's data model, buttons dispatch their actions through the engine, and a data
/// change redraws only the views that read the changed path.
/// </para>
/// <para>
/// Standalone, the view owns vertical scrolling (<see cref="IsScrollEnabled"/>), so don't put it in a
/// <c>ScrollView</c>. Inside an <c>AgentView</c> message it sizes to its content and the conversation
/// scrolls instead.
/// </para>
/// </remarks>
public class A2UISurfaceView : ContentView
{
    readonly VerticalStackLayout stack = new() { Spacing = 12 };
    readonly List<A2UISurfaceRenderer> renderers = [];
    A2UISurfaceHost? subscribedHost;
    ScrollView? scroller;

    /// <summary>Creates an empty surface view.</summary>
    public A2UISurfaceView()
    {
        this.Padding = 0;
        this.ApplyScroll();
    }

    /// <summary>The surface to draw. Takes precedence over <see cref="Host"/>.</summary>
    public static readonly BindableProperty SurfaceProperty = BindableProperty.Create(
        nameof(Surface), typeof(A2UISurface), typeof(A2UISurfaceView), null,
        propertyChanged: (b, _, _) => ((A2UISurfaceView)b).Reload());

    /// <inheritdoc cref="SurfaceProperty"/>
    public A2UISurface? Surface
    {
        get => (A2UISurface?)this.GetValue(SurfaceProperty);
        set => this.SetValue(SurfaceProperty, value);
    }

    /// <summary>A surface host to draw from when <see cref="Surface"/> isn't set. Surfaces appear as the agent creates them.</summary>
    public static readonly BindableProperty HostProperty = BindableProperty.Create(
        nameof(Host), typeof(A2UISurfaceHost), typeof(A2UISurfaceView), null,
        propertyChanged: (b, _, _) => ((A2UISurfaceView)b).Reload());

    /// <inheritdoc cref="HostProperty"/>
    public A2UISurfaceHost? Host
    {
        get => (A2UISurfaceHost?)this.GetValue(HostProperty);
        set => this.SetValue(HostProperty, value);
    }

    /// <summary>With <see cref="Host"/>: the one surface to draw. Null draws every surface on the host in creation order.</summary>
    public static readonly BindableProperty SurfaceIdProperty = BindableProperty.Create(
        nameof(SurfaceId), typeof(string), typeof(A2UISurfaceView), null,
        propertyChanged: (b, _, _) => ((A2UISurfaceView)b).Reload());

    /// <inheritdoc cref="SurfaceIdProperty"/>
    public string? SurfaceId
    {
        get => (string?)this.GetValue(SurfaceIdProperty);
        set => this.SetValue(SurfaceIdProperty, value);
    }

    /// <summary>Component type to renderer. Defaults to <see cref="A2UIRendererRegistry.CreateDefault"/> (basic + Shiny).</summary>
    public static readonly BindableProperty RenderersProperty = BindableProperty.Create(
        nameof(Renderers), typeof(A2UIRendererRegistry), typeof(A2UISurfaceView), null,
        defaultValueCreator: _ => A2UIRendererRegistry.CreateDefault(),
        propertyChanged: (b, _, _) => ((A2UISurfaceView)b).Reload());

    /// <inheritdoc cref="RenderersProperty"/>
    public A2UIRendererRegistry Renderers
    {
        get => (A2UIRendererRegistry)this.GetValue(RenderersProperty);
        set => this.SetValue(RenderersProperty, value);
    }

    /// <summary>
    /// True (the default) when the view owns vertical scrolling - standalone on a page. False sizes it
    /// to its content, for hosts that scroll themselves (an <c>AgentView</c> message).
    /// </summary>
    public static readonly BindableProperty IsScrollEnabledProperty = BindableProperty.Create(
        nameof(IsScrollEnabled), typeof(bool), typeof(A2UISurfaceView), true,
        propertyChanged: (b, _, _) => ((A2UISurfaceView)b).ApplyScroll());

    /// <inheritdoc cref="IsScrollEnabledProperty"/>
    public bool IsScrollEnabled
    {
        get => (bool)this.GetValue(IsScrollEnabledProperty);
        set => this.SetValue(IsScrollEnabledProperty, value);
    }

    /// <summary>
    /// Whether an outermost Card draws its edge and fill (default true). Turn it off when the host
    /// already draws one - <c>AgentView</c> does - so there is no card-in-bubble double border.
    /// </summary>
    public static readonly BindableProperty ShowBorderProperty = BindableProperty.Create(
        nameof(ShowBorder), typeof(bool), typeof(A2UISurfaceView), true,
        propertyChanged: (b, _, _) => ((A2UISurfaceView)b).Reload());

    /// <inheritdoc cref="ShowBorderProperty"/>
    public bool ShowBorder
    {
        get => (bool)this.GetValue(ShowBorderProperty);
        set => this.SetValue(ShowBorderProperty, value);
    }

    void ApplyScroll()
    {
        if (this.IsScrollEnabled)
        {
            this.Content = null;
            this.scroller = new ScrollView { Orientation = ScrollOrientation.Vertical, Content = this.stack };
            this.Content = this.scroller;
        }
        else
        {
            if (this.scroller is not null)
                this.scroller.Content = null;
            this.scroller = null;
            this.Content = this.stack;
        }
    }

    void Reload()
    {
        foreach (var renderer in this.renderers)
            renderer.Dispose();
        this.renderers.Clear();
        this.stack.Children.Clear();

        if (this.subscribedHost is not null)
        {
            this.subscribedHost.SurfaceCreated -= this.OnSurfaceCreated;
            this.subscribedHost.SurfaceDeleted -= this.OnSurfaceDeleted;
            this.subscribedHost = null;
        }

        if (this.Surface is { } surface)
        {
            this.Add(surface);
            return;
        }

        if (this.Host is not { } host)
            return;

        host.SurfaceCreated += this.OnSurfaceCreated;
        host.SurfaceDeleted += this.OnSurfaceDeleted;
        this.subscribedHost = host;

        foreach (var s in host.Surfaces.Values)
        {
            if (this.Wants(s))
                this.Add(s);
        }
    }

    bool Wants(A2UISurface surface) => this.SurfaceId is null || surface.SurfaceId == this.SurfaceId;

    void Add(A2UISurface surface)
    {
        if (surface.IsDeleted)
            return;

        // openUrl needs somewhere to go; the engine leaves that to the host app
        surface.Host.OpenUrl ??= uri => _ = Launcher.Default.OpenAsync(uri);

        var renderer = new A2UISurfaceRenderer(this, surface, this.Renderers);
        this.renderers.Add(renderer);
        this.stack.Children.Add(renderer.Container);
    }

    void OnSurfaceCreated(object? sender, A2UISurface surface)
    {
        if (this.Wants(surface))
            this.Add(surface);
    }

    void OnSurfaceDeleted(object? sender, A2UISurface surface)
    {
        var renderer = this.renderers.FirstOrDefault(r => r.Surface == surface);
        if (renderer is null)
            return;
        renderer.Dispose();
        this.renderers.Remove(renderer);
        this.stack.Children.Remove(renderer.Container);
    }
}
