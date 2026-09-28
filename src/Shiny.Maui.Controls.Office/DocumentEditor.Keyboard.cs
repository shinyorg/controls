namespace Shiny.Maui.Controls.Office;

/// <summary>
/// Keeps the editor out from under the soft keyboard.
/// </summary>
/// <remarks>
/// <para>
/// Without this the keyboard simply covers the lower part of the page — measured at roughly 40% of an
/// editor on a phone — and the caret can sit under it while you type into it. Neither platform solves
/// it for us here. On iOS the editor is not inside a scroll container, so the keyboard manager has
/// nothing to scroll and leaves the page where it is. On Android the window pans rather than resizing
/// unless the host activity asks for <c>AdjustResize</c>, and under edge-to-edge even that stops
/// working unless someone consumes the IME inset — which a control library cannot rely on a host
/// having done.
/// </para>
/// <para>
/// So the control measures the overlap itself and pads its own bottom by it. The canvas shrinks, the
/// layout re-runs against the smaller viewport, and the caret stays reachable — the same approach
/// <c>ChatView</c> takes, for the same reason.
/// </para>
/// </remarks>
public partial class DocumentEditor
{
    /// <summary>Shrink to make room for the soft keyboard rather than being covered by it. On by default.</summary>
    public static readonly BindableProperty AdjustForKeyboardProperty = BindableProperty.Create(
        nameof(AdjustForKeyboard),
        typeof(bool),
        typeof(DocumentEditor),
        true);

    /// <inheritdoc cref="AdjustForKeyboardProperty"/>
    public bool AdjustForKeyboard
    {
        get => (bool)this.GetValue(AdjustForKeyboardProperty);
        set => this.SetValue(AdjustForKeyboardProperty, value);
    }

    partial void HookKeyboard();

    partial void UnhookKeyboard();

    /// <summary>
    /// Applies the overlap as bottom padding, or takes it away again.
    /// </summary>
    /// <remarks>
    /// Padding rather than a translation: moving the control would slide it off its own layout slot and
    /// leave whatever is above it — a toolbar, a status line — hanging over nothing. Padding shrinks
    /// the canvas in place, which is what makes the pagination recompute against the room it actually
    /// has.
    /// </remarks>
    void ApplyKeyboardInset(double overlap)
    {
        if (!this.AdjustForKeyboard)
            overlap = 0;

        var target = Math.Max(0, overlap);

        if (Math.Abs(this.Padding.Bottom - target) < 0.5)
            return;

        this.Padding = new Thickness(this.Padding.Left, this.Padding.Top, this.Padding.Right, target);

        // This view has not changed size, so nothing else tells the controller its viewport did.
        if (this.controller is { } controller && this.Width > 0 && this.Height > 0)
        {
            controller.Resize(this.Width, this.ViewportHeight(this.Height));
            if (target > 0 && this.focused)
                controller.RevealCaret();

            this.Invalidate();
        }
    }
}
