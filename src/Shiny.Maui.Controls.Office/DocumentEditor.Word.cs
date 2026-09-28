using Shiny.Controls.Office.Document;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// Word's keyboard shortcuts, the clipboard and hyperlinks, on the MAUI surface.
/// </summary>
/// <remarks>
/// <para>
/// MAUI has no portable key-down event, so shortcuts arrive through <see cref="HandleShortcut"/> from a
/// host's platform hook, exactly as <see cref="HandleKey"/> does. The mapping from keys to commands is
/// the shared <see cref="WordShortcuts"/> table, so this surface and the Blazor one cannot disagree about
/// what Ctrl+E does.
/// </para>
/// <para>
/// The clipboard goes through <see cref="Clipboard.Default"/> for the plain text and through the
/// controller's own copy for the formatting: a paste whose system text matches the last copy made here
/// comes back formatted; anything else pastes as the text it is.
/// </para>
/// </remarks>
public partial class DocumentEditor
{
    /// <summary>
    /// Raised for a shortcut the surface cannot act on alone — Find, Replace, Link, New Comment and
    /// Word Count all open UI that belongs to the view around the editor.
    /// </summary>
    public event EventHandler<WordCommand>? ShortcutRequested;

    /// <summary>
    /// Raised when an external hyperlink is followed. Left unhandled, the link opens in the system
    /// browser through <see cref="Launcher"/>.
    /// </summary>
    public event EventHandler<DocumentLinkEventArgs>? LinkClicked;

    /// <summary>
    /// Routes a key combination through Word's shortcut table.
    /// </summary>
    /// <param name="key">The key as the platform names it: <c>"b"</c>, <c>"="</c>, <c>"Enter"</c>, <c>"F3"</c>.</param>
    /// <param name="command">Ctrl, or Cmd on Apple platforms.</param>
    /// <param name="shift">Shift.</param>
    /// <param name="alt">Alt / Option.</param>
    /// <returns>True when the key was consumed, including by <see cref="ShortcutRequested"/>.</returns>
    public bool HandleShortcut(string key, bool command, bool shift = false, bool alt = false)
    {
        if (this.controller is null)
            return false;

        var resolved = this.controller.HandleShortcut(key, command, shift, alt, out var handled);

        if (handled)
        {
            this.RaiseDocumentChanged();
            return true;
        }

        switch (resolved)
        {
            case WordCommand.Copy:
                _ = this.CopyAsync();
                return true;

            case WordCommand.Cut:
                _ = this.CutAsync();
                return true;

            case WordCommand.Paste:
                _ = this.PasteAsync();
                return true;

            case WordCommand.PasteText:
                _ = this.PasteAsync(plainText: true);
                return true;

            case WordCommand.None:
                return false;

            default:
                if (!WordShortcuts.IsHostCommand(resolved))
                    return false;

                this.ShortcutRequested?.Invoke(this, resolved);
                return true;
        }
    }

    /// <summary>Copies the selection to the system clipboard, keeping its formatting for a paste back here.</summary>
    public async Task CopyAsync()
    {
        if (this.controller is null)
            return;

        var text = this.controller.Copy();
        if (text.Length > 0)
            await SetClipboardAsync(text);
    }

    /// <summary>Cuts the selection to the system clipboard.</summary>
    public async Task CutAsync()
    {
        if (this.controller is null || this.IsReadOnly)
            return;

        var text = this.controller.Cut();
        if (text.Length > 0)
            await SetClipboardAsync(text);

        this.RaiseDocumentChanged();
    }

    /// <summary>
    /// Pastes from the system clipboard — with formatting when it holds what this editor last copied.
    /// </summary>
    /// <param name="plainText">True for Paste Special ▸ Keep Text Only.</param>
    public async Task PasteAsync(bool plainText = false)
    {
        if (this.controller is null || this.IsReadOnly)
            return;

        string? text = null;

        try
        {
            if (Clipboard.Default.HasText)
                text = await Clipboard.Default.GetTextAsync();
        }
        catch (Exception)
        {
            // A platform that refuses clipboard access still pastes what this editor copied.
        }

        if (plainText)
        {
            text ??= DocumentClipboard.Current?.PlainText;
            if (!string.IsNullOrEmpty(text))
                this.controller.PasteText(text);
        }
        else
        {
            this.controller.Paste(text);
        }

        this.RaiseDocumentChanged();
    }

    static async Task SetClipboardAsync(string text)
    {
        try
        {
            await Clipboard.Default.SetTextAsync(text);
        }
        catch (Exception)
        {
            // The rich copy is still held by the controller; only other apps miss out.
        }
    }

    /// <summary>
    /// Follows the hyperlink the caret is in — the touch counterpart of Ctrl+click, which a touch
    /// screen has no way to express.
    /// </summary>
    /// <returns>True when the caret was in a link.</returns>
    public bool OpenLinkAtCaret()
    {
        if (this.controller?.CurrentHyperlink is not { } link)
            return false;

        this.controller.FollowLink(link);
        return true;
    }

    void OnLinkActivated(object? sender, DocumentLinkEventArgs e)
    {
        if (this.LinkClicked is { } handler)
        {
            handler(this, e);
            return;
        }

        if (Uri.TryCreate(e.Target, UriKind.Absolute, out var uri))
            _ = OpenAsync(uri);

        static async Task OpenAsync(Uri uri)
        {
            try
            {
                await Launcher.Default.OpenAsync(uri);
            }
            catch (Exception)
            {
                // No handler for the scheme; nothing sensible to do but not crash.
            }
        }
    }
}
