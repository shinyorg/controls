using Microsoft.AspNetCore.Components;

namespace Shiny.Blazor.Controls;

/// <summary>
/// What every item on a ribbon has: an icon, a label, a size and the states that dim it.
/// </summary>
/// <remarks>
/// Unlike the MAUI side, these are components that render themselves rather than descriptors the bar
/// reads. A group is a CSS grid, so the flow into columns falls out of auto-placement and there is
/// nothing for a parent to compute — which means an item can just be markup.
/// </remarks>
public abstract class RibbonItemBase : ComponentBase
{
    /// <summary>
    /// The group the item is in — null for an item in the quick access row, which is on the bar but
    /// not in a group. Public because a private cascaded parameter is silently skipped.
    /// </summary>
    [CascadingParameter] public RibbonGroup? Group { get; set; }

    /// <summary>The ribbon the item is on. Cascaded over the whole bar, so it is set either way.</summary>
    [CascadingParameter] public Ribbon? Ribbon { get; set; }

    /// <summary>The label under (or beside) the icon.</summary>
    [Parameter] public string? Text { get; set; }

    /// <summary>Inline SVG/HTML markup, an image URL, or a glyph/emoji.</summary>
    [Parameter] public string? Icon { get; set; }

    /// <summary>Hover text. Falls back to <see cref="Text"/>.</summary>
    [Parameter] public string? Tooltip { get; set; }

    /// <summary>A second line under the tooltip's title, for saying what the command actually does.</summary>
    [Parameter] public string? Description { get; set; }

    /// <summary>
    /// The keyboard shortcut, as text — "Ctrl+B". Appended to the tooltip in brackets the way Office shows
    /// it, and carried into <see cref="Ribbon.GetCommands"/> so a command search can show it too. The
    /// ribbon does not bind the key; the editor that owns the command does.
    /// </summary>
    [Parameter] public string? Shortcut { get; set; }

    /// <summary>How much room the item asks for. See <see cref="RibbonItemSize"/>.</summary>
    [Parameter] public RibbonItemSize Size { get; set; } = RibbonItemSize.Large;

    /// <summary>
    /// A disabled item is drawn dimmed and does not respond. Prefer this over removing the item: a
    /// command that disappears when it cannot run makes the bar move under the pointer.
    /// </summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Extra classes on the item's root element.</summary>
    [Parameter] public string? CssClass { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }


    /// <summary>The size actually drawn: the simplified ribbon is one row, so everything in it is small.</summary>
    protected RibbonItemSize EffectiveSize
        => this.Ribbon?.DisplayMode == RibbonDisplayMode.Simplified
            ? RibbonItemSize.Small
            : this.Size;

    /// <summary>A group can dim every item in it without each of them having to be bound.</summary>
    protected bool IsDisabled => this.Disabled || this.Group?.Disabled == true;

    /// <summary>The hover text actually used.</summary>
    protected string? EffectiveTooltip => this.Tooltip ?? this.Text;

    /// <summary>
    /// The native tooltip text, title and description folded into one string.
    /// </summary>
    /// <remarks>
    /// Ribbon buttons use the browser's own tooltip rather than the <c>Tooltip</c> control, unlike the
    /// Office toolbars. A wrapper element around each button would become the grid item, and the whole
    /// column flow is placed on the buttons themselves — the group would lay out one item per column.
    /// </remarks>
    protected string? TitleText
        => string.IsNullOrWhiteSpace(this.Description)
            ? this.TooltipWithShortcut
            : $"{this.TooltipWithShortcut}\n{this.Description}";

    /// <summary>The tooltip's title line: "Bold (Ctrl+B)".</summary>
    protected string? TooltipWithShortcut
        => string.IsNullOrWhiteSpace(this.Shortcut) || string.IsNullOrWhiteSpace(this.EffectiveTooltip)
           || this.EffectiveTooltip!.Contains(this.Shortcut!, StringComparison.OrdinalIgnoreCase)
            ? this.EffectiveTooltip
            : $"{this.EffectiveTooltip} ({this.Shortcut})";

    protected string SizeClass => this.EffectiveSize == RibbonItemSize.Large ? "is-large" : "is-small";

    protected string RootClass(string kind)
        => string.Join(
            ' ',
            new[] { "shiny-ribbon-item", kind, this.SizeClass, this.IsDisabled ? "is-disabled" : null, this.CssClass }
                .Where(x => !string.IsNullOrWhiteSpace(x))
        );


    /// <summary>
    /// How the command search runs this item, or null for an item that is not a command (hosted
    /// content, a separator). Overridden by the button kinds.
    /// </summary>
    internal virtual Func<Task>? CommandInvoker => null;

    /// <summary>The lines of a dropdown this item carries, which the command search indexes too.</summary>
    internal virtual IReadOnlyList<RibbonMenuEntry>? CommandMenu => null;

    /// <summary>Named choices the item offers — a gallery's entries — indexed as "Label › Choice".</summary>
    internal virtual IReadOnlyList<(string Text, Func<Task> Run)>? CommandChoices => null;


    /// <summary>
    /// Keeps the ribbon's command index current. Every render rather than once, because the label,
    /// the shortcut and whether it is enabled all change under a live editor.
    /// </summary>
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        this.Ribbon?.IndexCommand(this);
    }


    /// <summary>
    /// Whether an icon string is a URL to load rather than markup to inline.
    /// </summary>
    /// <remarks>Same rule the toolbar uses, so one icon string works on either control.</remarks>
    internal static bool IsImageUrl(string s)
        => s.StartsWith("http", StringComparison.OrdinalIgnoreCase)
           || s.StartsWith('/')
           || s.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
           || s.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
           || s.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
           || s.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);
}
