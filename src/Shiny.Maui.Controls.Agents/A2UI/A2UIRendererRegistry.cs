namespace Shiny.Maui.Controls.Agents;

/// <summary>
/// Maps an A2UI component type (its <c>component</c> name) to the function that draws it. An
/// <see cref="A2UISurfaceView"/> looks every component up here; a type with no renderer, or one its
/// catalog doesn't define, draws an "unsupported component" placeholder - never a guess.
/// </summary>
/// <remarks>
/// <see cref="CreateDefault"/> covers the 18 basic catalog components and the Shiny catalog. Apps
/// (and add-ons) <see cref="Register"/> their own components, or replace a built-in one, with the
/// same call; a custom component also needs a catalog that defines it, registered on the
/// <see cref="A2UISurfaceHost"/>.
/// </remarks>
public sealed class A2UIRendererRegistry
{
    readonly Dictionary<string, Func<A2UIRenderContext, View>> renderers = new(StringComparer.Ordinal);

    /// <summary>Every component type with a renderer.</summary>
    public IReadOnlyCollection<string> Types => this.renderers.Keys;

    /// <summary>Adds or replaces the renderer for a component type.</summary>
    /// <param name="componentType">The component name as the agent sends it, e.g. <c>Gauge</c>.</param>
    /// <param name="renderer">Builds the view. Use <see cref="A2UIRenderContext.Watch"/> for anything bound to data.</param>
    public A2UIRendererRegistry Register(string componentType, Func<A2UIRenderContext, View> renderer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentType);
        ArgumentNullException.ThrowIfNull(renderer);
        this.renderers[componentType] = renderer;
        return this;
    }

    /// <summary>Removes a component type's renderer, so it draws as unsupported.</summary>
    public bool Remove(string componentType) => this.renderers.Remove(componentType);

    /// <summary>The renderer for a component type.</summary>
    public bool TryGet(string componentType, out Func<A2UIRenderContext, View> renderer)
        => this.renderers.TryGetValue(componentType, out renderer!);

    /// <summary>The 18 basic catalog renderers only.</summary>
    public static A2UIRendererRegistry CreateBasic() => A2UIBasicRenderers.AddTo(new A2UIRendererRegistry());

    /// <summary>The basic catalog plus the Shiny catalog (DataGrid, Gauge, ChipGroup, DateRangePicker, ...).</summary>
    public static A2UIRendererRegistry CreateDefault() => A2UIShinyRenderers.AddTo(CreateBasic());
}
