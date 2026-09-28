using Shiny.Controls.Office.Icons;
using Shiny.Controls.Office.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls.Office;

/// <summary>
/// The Office application window — title bar, ribbon, rulers, side panes, editor, status bar and the
/// File backstage — as one container an editor drops itself into.
/// </summary>
/// <example>
/// <code>
/// &lt;OfficeShell App="OfficeApp.Word" @bind-IsBackstageOpen="backstage" @bind-IsFocusMode="focus"&gt;
///     &lt;TitleBar&gt;&lt;OfficeTitleBar @bind-DocumentName="name" CommandIndex="commands" /&gt;&lt;/TitleBar&gt;
///     &lt;Ribbon&gt;&lt;Ribbon ApplicationButtonText="File" ApplicationButtonClicked="() =&gt; backstage = true"&gt;…&lt;/Ribbon&gt;&lt;/Ribbon&gt;
///     &lt;ChildContent&gt;&lt;DocumentEditor … /&gt;&lt;/ChildContent&gt;
///     &lt;StatusBar&gt;&lt;OfficeStatusBar Items="status" @bind-Zoom="zoom" /&gt;&lt;/StatusBar&gt;
///     &lt;Backstage&gt;&lt;OfficeBackstage Templates="templates" SaveRequested="SaveAsync" /&gt;&lt;/Backstage&gt;
/// &lt;/OfficeShell&gt;
/// </code>
/// </example>
/// <remarks>
/// <para>
/// The shell cascades itself, so the shell parts inside it (<see cref="OfficeTitleBar"/>,
/// <see cref="OfficeStatusBar"/>, <see cref="OfficeBackstage"/> …) take its <see cref="App"/>, its accent
/// and its compact layout without being told, and the status bar's Focus button and the backstage's
/// Back button drive the shell's modes directly.
/// </para>
/// <para>
/// Responsive through a width the browser reports (<c>officeShell.js</c>): below
/// <see cref="OfficeShellLayout.CompactWidth"/> the title bar's search becomes an icon, rulers and side
/// panes hide, and <see cref="ShellLayout"/> says the ribbon should go simplified — bind the ribbon's
/// <c>DisplayMode</c> to <c>ShellLayout.SimplifiedRibbon</c>, since the ribbon's display mode is the host's.
/// Without the script everything stays at the desktop layout.
/// </para>
/// </remarks>
public partial class OfficeShell : ComponentBase, IAsyncDisposable
{
    [Inject] IJSRuntime Js { get; set; } = default!;

    ElementReference root;
    IJSObjectReference? module;
    DotNetObjectReference<OfficeShell>? selfReference;
    bool disposed;

    /// <summary>Which app the shell is dressed as: accent, formats, view modes.</summary>
    [Parameter] public OfficeApp App { get; set; } = OfficeApp.Word;

    /// <summary>Overrides the app's accent. Any CSS colour.</summary>
    [Parameter] public string? AccentColor { get; set; }

    [Parameter] public RenderFragment? TitleBar { get; set; }

    [Parameter] public RenderFragment? Ribbon { get; set; }

    /// <summary>The horizontal ruler, above the editor. Hidden in the compact layout and in focus mode.</summary>
    [Parameter] public RenderFragment? Ruler { get; set; }

    /// <summary>The vertical ruler, left of the editor.</summary>
    [Parameter] public RenderFragment? VerticalRuler { get; set; }

    /// <summary>The left pane — the navigation pane, the slide rail.</summary>
    [Parameter] public RenderFragment? LeftPane { get; set; }

    /// <summary>The editor.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>The right pane — comments, the reviewing pane.</summary>
    [Parameter] public RenderFragment? RightPane { get; set; }

    [Parameter] public RenderFragment? StatusBar { get; set; }

    /// <summary>The File backstage, shown over everything while <see cref="IsBackstageOpen"/>.</summary>
    [Parameter] public RenderFragment? Backstage { get; set; }

    /// <summary>Anything else to float over the shell — the host's own dialogs.</summary>
    [Parameter] public RenderFragment? Overlay { get; set; }

    [Parameter] public bool IsLeftPaneOpen { get; set; }

    [Parameter] public EventCallback<bool> IsLeftPaneOpenChanged { get; set; }

    [Parameter] public bool IsRightPaneOpen { get; set; }

    [Parameter] public EventCallback<bool> IsRightPaneOpenChanged { get; set; }

    /// <summary>Width of the left pane in pixels. Default 280.</summary>
    [Parameter] public double LeftPaneWidth { get; set; } = 280;

    /// <summary>Width of the right pane in pixels. Default 320.</summary>
    [Parameter] public double RightPaneWidth { get; set; } = 320;

    /// <summary>Whether the File backstage covers the shell. Two-way bindable.</summary>
    [Parameter] public bool IsBackstageOpen { get; set; }

    [Parameter] public EventCallback<bool> IsBackstageOpenChanged { get; set; }

    /// <summary>
    /// Focus mode: the title bar, ribbon, rulers, panes and status bar step away and a floating Exit
    /// Focus button (or Escape) brings them back. Two-way bindable.
    /// </summary>
    [Parameter] public bool IsFocusMode { get; set; }

    [Parameter] public EventCallback<bool> IsFocusModeChanged { get; set; }

    /// <summary>Raised when the width crosses a breakpoint and the layout changes.</summary>
    [Parameter] public EventCallback<OfficeShellLayout> ShellLayoutChanged { get; set; }

    [Parameter] public string? CssClass { get; set; }

    [Parameter] public string? Style { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }


    /// <summary>What fits at the current width.</summary>
    public OfficeShellLayout ShellLayout { get; private set; } = OfficeShellLayout.For(0);

    /// <summary>The app's descriptor, with the accent the shell is wearing.</summary>
    public OfficeAppInfo AppInfo => OfficeAppInfo.For(this.App);

    /// <summary>The CSS custom properties that carry the accent — shared with parts rendered outside a shell.</summary>
    internal string AccentVariables => OfficeShellStyle.AccentVariables(this.AppInfo, this.AccentColor);


    public Task OpenBackstageAsync() => this.SetBackstageOpenAsync(true);

    public Task CloseBackstageAsync() => this.SetBackstageOpenAsync(false);

    public async Task SetBackstageOpenAsync(bool open)
    {
        if (this.IsBackstageOpen == open)
            return;

        this.IsBackstageOpen = open;
        await this.IsBackstageOpenChanged.InvokeAsync(open);
        this.StateHasChanged();
    }

    public Task ToggleFocusModeAsync() => this.SetFocusModeAsync(!this.IsFocusMode);

    public async Task SetFocusModeAsync(bool focus)
    {
        if (this.IsFocusMode == focus)
            return;

        this.IsFocusMode = focus;
        await this.IsFocusModeChanged.InvokeAsync(focus);
        this.StateHasChanged();
    }

    public async Task SetRightPaneOpenAsync(bool open)
    {
        if (this.IsRightPaneOpen == open)
            return;

        this.IsRightPaneOpen = open;
        await this.IsRightPaneOpenChanged.InvokeAsync(open);
        this.StateHasChanged();
    }

    public async Task SetLeftPaneOpenAsync(bool open)
    {
        if (this.IsLeftPaneOpen == open)
            return;

        this.IsLeftPaneOpen = open;
        await this.IsLeftPaneOpenChanged.InvokeAsync(open);
        this.StateHasChanged();
    }


    /// <summary>Called from <c>officeShell.js</c> with the shell's width.</summary>
    [JSInvokable]
    public async Task OnShellResized(double width)
    {
        if (!this.ApplyWidth(width))
            return;

        await this.ShellLayoutChanged.InvokeAsync(this.ShellLayout);
        this.StateHasChanged();
    }


    /// <summary>Takes a new width. True when the layout it implies is different.</summary>
    internal bool ApplyWidth(double width)
    {
        var next = OfficeShellLayout.For(width);
        if (next == this.ShellLayout)
            return false;

        this.ShellLayout = next;
        return true;
    }


    async Task OnKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key != "Escape")
            return;

        if (this.IsBackstageOpen)
            await this.SetBackstageOpenAsync(false);
        else if (this.IsFocusMode)
            await this.SetFocusModeAsync(false);
    }


    string RootCss
        => string.Join(' ', new[]
        {
            "office-shell",
            this.ShellLayout.IsCompact ? "is-compact" : null,
            this.IsFocusMode ? "is-focus" : null,
            this.IsBackstageOpen ? "has-backstage" : null,
            this.CssClass
        }.Where(x => x is not null));

    string RootStyle => this.AccentVariables + this.Style;


    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        try
        {
            var loaded = await this.Js.InvokeAsync<IJSObjectReference>("import", "./_content/Shiny.Blazor.Controls.Office/officeShell.js");
            if (this.disposed)
            {
                await loaded.DisposeAsync();
                return;
            }

            this.module = loaded;
            this.selfReference = DotNetObjectReference.Create(this);
            await this.module.InvokeVoidAsync("observe", this.root, this.selfReference);
        }
        catch (JSDisconnectedException) { }
        catch (InvalidOperationException) { }   // prerendering: no JS yet, stay on the desktop layout
        catch (JSException ex)
        {
            Console.Error.WriteLine($"[Shiny.Office] The shell cannot track its width: {ex.Message}");
        }
    }


    public async ValueTask DisposeAsync()
    {
        this.disposed = true;

        if (this.module is not null)
        {
            try
            {
                await this.module.InvokeVoidAsync("unobserve", this.root);
                await this.module.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
            catch (ObjectDisposedException) { }
        }

        this.selfReference?.Dispose();
    }
}


/// <summary>The accent custom properties every shell part carries.</summary>
static class OfficeShellStyle
{
    public static string AccentVariables(OfficeAppInfo info, string? accentOverride = null)
    {
        if (!string.IsNullOrWhiteSpace(accentOverride))
            return $"--office-accent:{accentOverride};--office-accent-ink:#FFFFFF;--office-accent-dark:{accentOverride};";

        return $"--office-accent:{info.AccentHex};--office-accent-ink:{info.AccentInkHex};--office-accent-dark:{info.AccentDarkHex};";
    }
}
