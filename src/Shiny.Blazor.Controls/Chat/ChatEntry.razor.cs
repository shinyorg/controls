using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls.Chat;

/// <summary>
/// The message composer: a rounded, auto-growing multiline entry with an optional attach button, a
/// markdown formatting toolbar and a send button.
///
/// <para>
/// <see cref="ChatView"/> renders one of these for itself, so most apps never place it directly -
/// they set the pass-through parameters on <see cref="ChatView"/> instead. Use it inside
/// <see cref="ChatView.InputTemplate"/> to reshape the composer, or standalone (an AI prompt box, a
/// comment field) driving <see cref="OnSend"/> yourself.
/// </para>
/// </summary>
public partial class ChatEntry : IAsyncDisposable
{
    IJSObjectReference? module;
    bool disposed;
    ElementReference inputEl;
    int appliedMaxRows = -1;

    /// <summary>The composer text. Use <c>@bind-Text</c> for two-way binding.</summary>
    [Parameter] public string? Text { get; set; }

    /// <summary>Raised on every keystroke with the new text.</summary>
    [Parameter] public EventCallback<string?> TextChanged { get; set; }

    [Parameter] public string Placeholder { get; set; } = "Type a message...";
    [Parameter] public string SendButtonText { get; set; } = "Send";

    /// <summary>When false the entry, attach and send controls are all disabled.</summary>
    [Parameter] public bool IsEnabled { get; set; } = true;

    /// <summary>Shows the attach button; pair it with <see cref="OnAttach"/>.</summary>
    [Parameter] public bool ShowAttach { get; set; }

    /// <summary>Which markdown formatting buttons to offer. <c>None</c> hides the toolbar.</summary>
    [Parameter] public MessageBodyPermissions BodyPermissions { get; set; } = MessageBodyPermissions.None;

    /// <summary>How tall the entry may grow before it scrolls internally, in lines of text.</summary>
    [Parameter] public int MaxRows { get; set; } = 6;

    /// <summary>When true (the default) Enter submits and Shift+Enter inserts a newline.</summary>
    [Parameter] public bool SendOnEnter { get; set; } = true;

    /// <summary>Raised when send is pressed (or Enter, per <see cref="SendOnEnter"/>) with non-empty text.</summary>
    [Parameter] public EventCallback<string> OnSend { get; set; }

    /// <summary>Raised when the user picks a file from the attach button.</summary>
    [Parameter] public EventCallback<InputFileChangeEventArgs> OnAttach { get; set; }

    /// <summary>Raised on every keystroke. Use it to drive a typing indicator.</summary>
    [Parameter] public EventCallback OnTyping { get; set; }

    /// <summary>
    /// Markup appended to the left of the control row beneath the entry, after the attach button.
    /// A segmented control or mode picker goes here.
    /// </summary>
    [Parameter] public RenderFragment? LeftToolbar { get; set; }

    /// <summary>
    /// Markup placed to the right of the control row, immediately before the send button. Model
    /// pickers, a mic button, and the like.
    /// </summary>
    [Parameter] public RenderFragment? RightToolbar { get; set; }


    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var loaded = await JS.InvokeAsync<IJSObjectReference>(
                "import", "./_content/Shiny.Blazor.Controls/chat.js");
            if (disposed) { await loaded.ReleaseLateAsync(); return; }
            module = loaded;
        }

        if (module is null)
            return;

        // The row cap is derived from the live font metrics, so it is re-applied whenever MaxRows
        // changes rather than baked into the stylesheet.
        if (appliedMaxRows != MaxRows)
        {
            appliedMaxRows = MaxRows;
            await module.InvokeVoidAsync("autoGrow", inputEl, MaxRows);
        }
    }


    async Task OnInputAsync(ChangeEventArgs e)
    {
        Text = e.Value?.ToString();
        await TextChanged.InvokeAsync(Text);
        await OnTyping.InvokeAsync();

        if (module is not null)
            await module.InvokeVoidAsync("autoGrow", inputEl, MaxRows);
    }


    async Task OnKeyDownAsync(KeyboardEventArgs e)
    {
        if (SendOnEnter && e.Key == "Enter" && !e.ShiftKey)
            await SubmitAsync();
    }


    /// <summary>Submits the current text as if send had been pressed. No-op when the text is blank.</summary>
    public async Task SubmitAsync()
    {
        var body = Text?.Trim();
        if (string.IsNullOrEmpty(body) || !IsEnabled)
            return;

        Text = string.Empty;
        await TextChanged.InvokeAsync(Text);
        await OnSend.InvokeAsync(body);

        if (module is not null)
            await module.InvokeVoidAsync("autoGrow", inputEl, MaxRows);
    }


    async Task OnFileSelected(InputFileChangeEventArgs e) => await OnAttach.InvokeAsync(e);


    // ---- markdown toolbar ----

    async Task ApplyMarkdownAsync(string before, string after, string placeholder)
    {
        if (module is null)
            return;

        Text = await module.InvokeAsync<string>("wrapSelection", inputEl, before, after, placeholder);
        await TextChanged.InvokeAsync(Text);
        await module.InvokeVoidAsync("autoGrow", inputEl, MaxRows);
    }


    async Task InsertLinkAsync()
    {
        if (module is null)
            return;

        Text = await module.InvokeAsync<string>("insertLink", inputEl);
        await TextChanged.InvokeAsync(Text);
        await module.InvokeVoidAsync("autoGrow", inputEl, MaxRows);
    }


    /// <summary>Moves focus into the entry.</summary>
    public ValueTask FocusAsync() => inputEl.FocusAsync();


    public async ValueTask DisposeAsync()
    {
        disposed = true;
        if (module is not null)
        {
            try { await module.DisposeAsync(); } catch { /* circuit already gone */ }
            module = null;
        }
    }
}
