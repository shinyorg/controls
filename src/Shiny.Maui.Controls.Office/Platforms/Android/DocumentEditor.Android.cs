using Android.Views;
using AndroidX.Core.View;

namespace Shiny.Maui.Controls.Office;

public partial class DocumentEditor
{
    Android.Views.View? insetHost;
    ViewTreeObserver.IOnGlobalLayoutListener? layoutListener;

    /// <summary>
    /// Measures the keyboard from the window's visible frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not an <c>OnApplyWindowInsetsListener</c> on this control's own view: insets are dispatched
    /// down the tree and stop wherever something consumes them, so a listener on a nested view is
    /// simply never called in a host whose root already handles them — which is what happened here,
    /// and it fails silently.
    /// </para>
    /// <para>
    /// The decor view's visible frame is the one measurement that is true whatever the host has
    /// configured: it shrinks under <c>adjustResize</c> and stays put under <c>adjustPan</c>, and the
    /// IME inset off the root window covers the edge-to-edge case where neither applies.
    /// </para>
    /// </remarks>
    partial void HookKeyboard()
    {
        if (this.Handler?.PlatformView is not Android.Views.View platform)
            return;

        var root = platform.RootView;
        if (root is null)
            return;

        this.UnhookKeyboard();

        this.insetHost = root;
        this.layoutListener = new LayoutListener(this, platform, root);
        root.ViewTreeObserver?.AddOnGlobalLayoutListener(this.layoutListener);
    }

    partial void UnhookKeyboard()
    {
        if (this.layoutListener is { } listener && this.insetHost?.ViewTreeObserver is { IsAlive: true } observer)
            observer.RemoveOnGlobalLayoutListener(listener);

        this.layoutListener = null;
        this.insetHost = null;
        this.ApplyKeyboardInset(0);
    }

    sealed class LayoutListener(DocumentEditor owner, Android.Views.View platform, Android.Views.View root)
        : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
    {
        public void OnGlobalLayout()
        {
            var density = platform.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
            if (density <= 0)
                density = 1f;

            // The root now, not the one captured at hook time: the handler is created before the view is
            // attached, when RootView is the editor itself - its height stood in for the window's, the
            // keyboard's top came out negative, and the "overlap" was the whole editor, so the canvas
            // was padded down to nothing the moment the keyboard opened.
            var window = platform.RootView ?? root;
            var ime = ViewCompat.GetRootWindowInsets(window)?.GetInsets(WindowInsetsCompat.Type.Ime()).Bottom ?? 0;
            if (ime <= 0 || !platform.IsAttachedToWindow)
            {
                owner.ApplyKeyboardInset(0);
                return;
            }

            // Screen coordinates on both sides. The visible frame stops at the keyboard whether the
            // window resizes or pans, and a pan moves this view's on-screen position with it, so what
            // is left is only the part of the editor the keyboard really covers.
            var visible = new Android.Graphics.Rect();
            window.GetWindowVisibleDisplayFrame(visible);

            var location = new int[2];
            platform.GetLocationOnScreen(location);

            // The padding already applied shrinks the canvas, not this view, so the bottom is stable.
            var bottom = location[1] + platform.Height;
            owner.ApplyKeyboardInset(Math.Max(0, (bottom - visible.Bottom) / density));
        }
    }
}
