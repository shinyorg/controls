using Microsoft.AspNetCore.Components;

namespace Shiny.Blazor.Controls;

/// <summary>
/// A titled box of related commands inside a <see cref="RibbonTab"/> — Clipboard, Font, Paragraph.
/// </summary>
/// <remarks>
/// The group is the unit the ribbon gives up when it runs out of room: a group that does not fit
/// collapses to a single button that opens the whole group in a popup, worst <see cref="Priority"/>
/// first. Items are never dropped individually, because half a group is worse than a closed one.
/// </remarks>
public partial class RibbonGroup : ComponentBase, IDisposable
{
    /// <summary>The ribbon this group is on. Public because a private cascaded parameter is silently skipped.</summary>
    [CascadingParameter] public Ribbon? Ribbon { get; set; }

    /// <summary>The tab this group is on — what the command search says a command is under.</summary>
    [CascadingParameter] public RibbonTab? Tab { get; set; }

    /// <summary>The caption under the group. Also the label when the group collapses to a button.</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>
    /// Collapse order. Groups collapse lowest-priority first, so raise this on the ones that should
    /// survive longest.
    /// </summary>
    [Parameter] public int Priority { get; set; }

    /// <summary>
    /// Whether the group may collapse to a button when space runs short. Set false for a group that has
    /// to stay open — the one holding the control the whole tab is about.
    /// </summary>
    [Parameter] public bool CanCollapse { get; set; } = true;

    /// <summary>Dims and deadens every item in the group without each of them having to be bound.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>
    /// Draws the small arrow in the group's bottom corner — the convention for "there is more of this
    /// than fits here", which opens the full dialog.
    /// </summary>
    [Parameter] public bool ShowDialogLauncher { get; set; }

    /// <summary>Raised when the corner arrow is pressed.</summary>
    [Parameter] public EventCallback DialogLauncherClicked { get; set; }

    /// <summary>Hover text for the launcher arrow. Falls back to "<c>{Title}</c> settings".</summary>
    [Parameter] public string? DialogLauncherTooltip { get; set; }

    /// <summary>The icon on the button this group collapses to. Inline SVG/HTML, an image URL, or a glyph.</summary>
    [Parameter] public string? CollapsedIcon { get; set; }

    /// <summary>Extra classes on the group box.</summary>
    [Parameter] public string? CssClass { get; set; }

    /// <summary>The item children.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }


    /// <summary>
    /// Identifies the group to the measuring pass in <c>ribbon.js</c>, which reports back by id rather
    /// than by position — positions shift as groups collapse, and a report that raced a re-render would
    /// then fold away the wrong group.
    /// </summary>
    internal string Id { get; } = $"rg-{Guid.NewGuid():N}";

    internal bool IsCollapsed => this.Ribbon?.IsGroupCollapsed(this) == true;

    internal bool IsPopupOpen => this.Ribbon?.IsGroupPopupOpen(this) == true;

    bool ShowTitle
        => this.Ribbon?.ShowGroupTitles != false
           && this.Ribbon?.DisplayMode != RibbonDisplayMode.Simplified;

    string LauncherHint => this.DialogLauncherTooltip ?? $"{this.Title} settings";


    string BoxCss(bool inPopup)
        => string.Join(
            ' ',
            new[] { "shiny-ribbon-group", inPopup ? "is-popup" : null, this.Disabled ? "is-disabled" : null, this.CssClass }
                .Where(x => !string.IsNullOrWhiteSpace(x))
        );


    void TogglePopup()
    {
        if (this.IsPopupOpen)
            this.Ribbon?.CloseMenu();
        else
            this.Ribbon?.OpenGroupPopup(this);
    }


    async Task OnLauncherAsync()
    {
        if (this.Disabled)
            return;

        this.Ribbon?.CloseMenu();
        await this.DialogLauncherClicked.InvokeAsync().ConfigureAwait(false);
        this.Ribbon?.NotifyInvoked();
    }


    protected override void OnInitialized() => this.Ribbon?.Register(this);

    public void Dispose() => this.Ribbon?.Unregister(this);
}
