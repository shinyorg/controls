using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.Chat;

public partial class ChatView
{
    KeyboardResizeScope? keyboardScope;

    // Android lifts the composer through MAUI's keyboard inset, which only arrives with the window in
    // adjustResize (see KeyboardResizeScope). iOS pads itself in ChatView.iOS.cs and must not opt in
    // as well, or the lift would be doubled.
    partial void HookKeyboard()
    {
        if (!this.AdjustForKeyboard)
            return;

        this.SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.None, SafeAreaRegions.None, SafeAreaRegions.None, SafeAreaRegions.SoftInput);
        this.keyboardScope ??= KeyboardResizeScope.Enter();
    }

    partial void UnhookKeyboard()
    {
        this.keyboardScope?.Dispose();
        this.keyboardScope = null;
        this.ClearValue(SafeAreaEdgesProperty);
    }
}
