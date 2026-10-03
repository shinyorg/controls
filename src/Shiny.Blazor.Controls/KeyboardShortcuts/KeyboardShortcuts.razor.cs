using Microsoft.AspNetCore.Components;
using Shiny.Controls.Keyboard;

namespace Shiny.Blazor.Controls;

/// <summary>Where a <see cref="KeyboardShortcuts"/> group listens.</summary>
public enum KeyboardShortcutScopeKind
{
    /// <summary>The whole page, for as long as the component is rendered.</summary>
    Document,

    /// <summary>Only while keyboard focus is inside the component's content — an editor, a grid, a canvas.</summary>
    Element
}

/// <summary>
/// A group of <see cref="KeyboardShortcut"/>s that switch on and off together.
/// </summary>
/// <example>
/// <code>
/// &lt;KeyboardShortcuts&gt;
///     &lt;KeyboardShortcut Gesture="Primary+S" OnPressed="Save" Description="Save" /&gt;
///     &lt;KeyboardShortcut Key="F5" OnPressed="Refresh" /&gt;
///     &lt;KeyboardShortcut Gesture="Ctrl+K, Ctrl+C" OnPressed="Comment" /&gt;
/// &lt;/KeyboardShortcuts&gt;
///
/// &lt;KeyboardShortcuts Scope="KeyboardShortcutScopeKind.Element"&gt;
///     &lt;KeyboardShortcut Gesture="Primary+B" OnPressed="Bold" /&gt;
///     &lt;textarea /&gt;
/// &lt;/KeyboardShortcuts&gt;
/// </code>
/// </example>
/// <remarks>
/// <para>
/// Precedence follows the markup: a nested group beats the group around it, an unrelated group
/// rendered later beats one rendered earlier, and an <see cref="IsModal"/> group — a dialog's —
/// silences every group outside it, app-wide registrations included.
/// </para>
/// <para>
/// Shortcuts and ordinary content can be mixed in <see cref="ChildContent"/>; the shortcuts render
/// nothing.
/// </para>
/// </remarks>
public partial class KeyboardShortcuts : ComponentBase, IDisposable
{
    ElementReference element;
    bool isElementScope;
    KeyboardShortcutService? service;

    [Inject] IKeyboardShortcutService Service { get; set; } = null!;

    /// <summary>The enclosing group, if any. Public because a private cascading parameter is silently never set.</summary>
    [CascadingParameter] public KeyboardShortcuts? ParentGroup { get; set; }

    /// <summary>The shortcuts, and optionally the content they apply to.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// <see cref="KeyboardShortcutScopeKind.Document"/> (the default) listens everywhere on the page;
    /// <see cref="KeyboardShortcutScopeKind.Element"/> only while focus is inside <see cref="ChildContent"/>.
    /// Read once, when the component is created.
    /// </summary>
    [Parameter] public KeyboardShortcutScopeKind Scope { get; set; }

    /// <summary>While this group is live, no group outside it can fire. For dialogs and modal panels.</summary>
    [Parameter] public bool IsModal { get; set; }

    /// <summary>Switch the whole group off without removing it. On by default.</summary>
    [Parameter] public bool IsEnabled { get; set; } = true;

    /// <summary>A label for debugging and cheat sheets.</summary>
    [Parameter] public string? Name { get; set; }

    internal KeyboardShortcutScope EngineScope { get; private set; } = null!;

    internal KeyboardShortcutService Owner => this.service!;

    protected override void OnInitialized()
    {
        this.service = this.Service as KeyboardShortcutService
            ?? throw new InvalidOperationException(
                $"<KeyboardShortcuts> needs the built-in {nameof(KeyboardShortcutService)}; register it with AddShinyKeyboardShortcuts() or AddShinyControls().");

        this.isElementScope = this.Scope == KeyboardShortcutScopeKind.Element;
        this.EngineScope = this.service.CreateScope(this.Name, this.ParentGroup?.EngineScope, this.IsModal, this.isElementScope);
    }

    protected override void OnParametersSet()
        => this.service!.UpdateScope(this.EngineScope, this.IsModal, this.IsEnabled);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        if (this.isElementScope)
            await this.service!.SetScopeElementAsync(this.EngineScope, this.element);

        await this.service!.StartAsync();
    }

    public void Dispose() => this.service?.RemoveScope(this.EngineScope);
}
