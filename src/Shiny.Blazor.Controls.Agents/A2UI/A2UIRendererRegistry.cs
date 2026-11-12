using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Shiny.Blazor.Controls.Agents.Renderers;

namespace Shiny.Blazor.Controls.Agents;

/// <summary>
/// Maps an A2UI component type (<c>Text</c>, <c>DataGrid</c>, an app's own <c>OrderCard</c>) to what
/// draws it. <see cref="CreateDefault"/> covers the basic catalog and the Shiny catalog; apps and
/// add-ons <see cref="Register(string, RenderFragment{A2UIRenderContext})"/> their own.
/// </summary>
/// <remarks>
/// A renderer is a <see cref="RenderFragment{TValue}"/> over <see cref="A2UIRenderContext"/>, or a
/// component with a <c>Context</c> parameter of that type. Both are trim and AOT safe: nothing is
/// discovered by reflection. A type with no renderer draws an "unsupported component" placeholder; the
/// engine has already reported it to the agent.
/// </remarks>
public sealed class A2UIRendererRegistry
{
    readonly Dictionary<string, RenderFragment<A2UIRenderContext>> renderers = new(StringComparer.Ordinal);

    /// <summary>The registered component types.</summary>
    public IReadOnlyCollection<string> Types => renderers.Keys;

    /// <summary>Adds (or replaces) the renderer for a component type.</summary>
    public A2UIRendererRegistry Register(string componentType, RenderFragment<A2UIRenderContext> renderer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentType);
        renderers[componentType] = renderer ?? throw new ArgumentNullException(nameof(renderer));
        return this;
    }

    /// <summary>
    /// Adds (or replaces) a component renderer: <typeparamref name="TComponent"/> gets the
    /// <see cref="A2UIRenderContext"/> in its <c>Context</c> parameter.
    /// </summary>
    public A2UIRendererRegistry Register<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>(string componentType)
        where TComponent : IComponent
        => Register(componentType, ctx => builder =>
        {
            builder.OpenComponent<TComponent>(0);
            builder.AddAttribute(1, "Context", ctx);
            builder.CloseComponent();
        });

    /// <summary>Removes a renderer; returns false when there was none.</summary>
    public bool Remove(string componentType) => renderers.Remove(componentType);

    /// <summary>Looks a renderer up by component type.</summary>
    public bool TryGet(string componentType, [NotNullWhen(true)] out RenderFragment<A2UIRenderContext>? renderer)
        => renderers.TryGetValue(componentType, out renderer);

    /// <summary>A copy, so an app can extend the defaults without touching a shared registry.</summary>
    public A2UIRendererRegistry Clone()
    {
        var copy = new A2UIRendererRegistry();
        foreach (var (k, v) in renderers)
            copy.renderers[k] = v;
        return copy;
    }

    static A2UIRendererRegistry? shared;

    /// <summary>The default registry, shared and read-only by convention. Clone it to add to it.</summary>
    internal static A2UIRendererRegistry Default => shared ??= CreateDefault();

    /// <summary>A registry with every basic-catalog and Shiny-catalog component.</summary>
    public static A2UIRendererRegistry CreateDefault() => new A2UIRendererRegistry()
        // basic catalog: layout and display
        .Register("Text", A2UIBasicRenderers.Text)
        .Register("Image", A2UIBasicRenderers.Image)
        .Register("Icon", A2UIBasicRenderers.Icon)
        .Register("Video", A2UIBasicRenderers.Video)
        .Register("AudioPlayer", A2UIBasicRenderers.AudioPlayer)
        .Register("Row", A2UIBasicRenderers.Row)
        .Register("Column", A2UIBasicRenderers.Column)
        .Register("List", A2UIBasicRenderers.List)
        .Register("Card", A2UIBasicRenderers.Card)
        .Register("Divider", A2UIBasicRenderers.Divider)
        .Register<A2UITabs>("Tabs")
        .Register<A2UIModal>("Modal")
        // basic catalog: inputs
        .Register<A2UIButton>("Button")
        .Register<A2UITextField>("TextField")
        .Register<A2UICheckBox>("CheckBox")
        .Register<A2UIChoicePicker>("ChoicePicker")
        .Register<A2UISlider>("Slider")
        .Register<A2UIDateTimeInput>("DateTimeInput")
        // Shiny catalog
        .Register<A2UIDataGrid>("DataGrid")
        .Register("Gauge", A2UIShinyRenderers.Gauge)
        .Register("ProgressBar", A2UIShinyRenderers.ProgressBar)
        .Register("Pill", A2UIShinyRenderers.Pill)
        .Register("Avatar", A2UIShinyRenderers.Avatar)
        .Register<A2UIChipGroup>("ChipGroup")
        .Register<A2UITagEntry>("TagEntry")
        .Register<A2UIDateRangePicker>("DateRangePicker")
        .Register<A2UIRangeSlider>("RangeSlider")
        .Register("Timeline", A2UIShinyRenderers.Timeline)
        .Register<A2UIExpander>("Expander")
        .Register("Markdown", A2UIShinyRenderers.Markdown);
}
