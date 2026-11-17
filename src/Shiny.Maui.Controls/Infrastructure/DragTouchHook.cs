using Microsoft.Maui.Controls;

namespace Shiny.Maui.Controls.Infrastructure;

/// <summary>
/// The native touch plumbing a view needs to start a drag from inside a scroller.
/// </summary>
/// <remarks>
/// Two problems, one hook.
///
/// (1) A <see cref="PanGestureRecognizer"/> nested in a <see cref="ScrollView"/> is not reliably
/// delivered. Android's scroller takes the gesture away from its children the moment the touch
/// crosses the touch slop, and <c>ScrollOrientation.Neither</c> does not help because MauiScrollView
/// only tests it in OnTouchEvent, never in OnInterceptTouchEvent. UIScrollView cancels the touches
/// it has already delivered to a child as soon as its own pan recognizer begins. Either way the pan
/// dies mid-gesture - or never starts at all.
///
/// (2) A pan cannot time a long press. It does not start until the finger has already moved, so
/// "hold still, then drag" has to be measured from the raw touch-down instead.
///
/// <see cref="DragSortRow"/> solves the first half the same way for TableView's row reorder.
/// </remarks>
sealed partial class DragTouchHook
{
    readonly View view;

    public DragTouchHook(View view)
    {
        this.view = view;
        view.HandlerChanged += (_, _) => this.AttachPlatform();
        this.AttachPlatform();
    }

    /// <summary>The native touch went down on the view - before any pan has begun.</summary>
    public Action? Pressed { get; set; }

    /// <summary>That touch ended or was cancelled. The scroller stealing it counts as cancelled.</summary>
    public Action? Released { get; set; }

    /// <summary>
    /// Keeps the enclosing scroller off the gesture that is already in flight. No-op on the plain
    /// net11.0 and Windows builds, where the scroller does not fight the child for it.
    /// </summary>
    public void LockScroller(bool locked) => this.SetPlatformScrollLock(locked);

    /// <summary>
    /// Paints <paramref name="target"/> over its siblings.
    /// </summary>
    /// <remarks>
    /// Never through <see cref="VisualElement.ZIndex"/> on Android or iOS: MAUI implements ZIndex by
    /// removing the native child and re-adding it at the new position, and removing a view
    /// mid-gesture dispatches ACTION_CANCEL to it - which is to say, raising a view when a drag
    /// starts kills that very drag on its first frame.
    /// </remarks>
    public static void Raise(View target, bool raised)
    {
#if ANDROID || IOS
        SetPlatformRaised(target, raised);
#else
        target.ZIndex = raised ? 1 : 0;
#endif
    }

    partial void AttachPlatform();
    partial void SetPlatformScrollLock(bool locked);
    static partial void SetPlatformRaised(View target, bool raised);
}
