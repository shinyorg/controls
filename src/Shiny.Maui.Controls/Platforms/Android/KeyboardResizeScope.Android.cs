using Android.Views;
using AWindow = Android.Views.Window;

namespace Shiny.Maui.Controls.Infrastructure;

/// <summary>
/// Switches the activity window to <c>adjustResize</c> for as long as a composer that has to stay above
/// the keyboard (ChatView, AgentView) is on screen, and puts the app's own mode back after the last one
/// leaves.
/// </summary>
/// <remarks>
/// MAUI's Android default is <c>adjustPan</c>: the system slides the whole window up until the focused
/// editor clears the keyboard. A composer is more than its editor - toolbar above, send button and
/// actions beside and below - so the pan stops with the editor's top line peeking over the keyboard and
/// the rest of the composer, the send button included, underneath it, while the app bar slides off the
/// top. Under <c>adjustResize</c> the window stays put and MAUI hands the keyboard inset to views whose
/// <see cref="SafeAreaEdges"/> bottom includes <see cref="SafeAreaRegions.SoftInput"/>, which lifts the
/// composer clear with the keyboard's own animation.
/// </remarks>
sealed class KeyboardResizeScope : IDisposable
{
    static int activeCount;
    static AWindow? window;
    static SoftInput previousAdjust;

    bool disposed;

    KeyboardResizeScope() { }

    public static KeyboardResizeScope Enter()
    {
        if (activeCount++ == 0)
        {
            window = Platform.CurrentActivity?.Window;
            if (window?.Attributes is { } attrs)
            {
                previousAdjust = attrs.SoftInputMode & SoftInput.MaskAdjust;
                if (previousAdjust != SoftInput.AdjustResize)
                    window.SetSoftInputMode((attrs.SoftInputMode & ~SoftInput.MaskAdjust) | SoftInput.AdjustResize);
            }
        }
        return new KeyboardResizeScope();
    }

    public void Dispose()
    {
        if (this.disposed)
            return;

        this.disposed = true;
        if (--activeCount > 0)
            return;

        activeCount = 0;
        if (window?.Attributes is { } attrs && previousAdjust != SoftInput.AdjustResize)
            window.SetSoftInputMode((attrs.SoftInputMode & ~SoftInput.MaskAdjust) | previousAdjust);
        window = null;
    }
}
