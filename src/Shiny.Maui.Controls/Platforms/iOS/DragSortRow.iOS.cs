using UIKit;

namespace Shiny.Maui.Controls.Infrastructure;

partial class DragSortRow
{
    UIView? hookedHandle;
    UILongPressGestureRecognizer? touchDown;
    UIScrollView? lockedScroller;

    /// <remarks>
    /// UIScrollView cancels the touches it has delivered to a child as soon as its own pan
    /// recognizer starts, which kills the handle's pan mid-gesture. Disabling the scroller the
    /// instant the handle is touched keeps the pan alive. A zero-duration long press is the only
    /// public way to observe that touch-down without subclassing the native view, and the
    /// simultaneous-recognition delegate lets it sit alongside MAUI's recognizers instead of
    /// competing with them for the gesture.
    /// </remarks>
    partial void HookPlatformScrollLock()
    {
        this.DragHandle.HandlerChanged += (_, _) => AttachTouchHook();
        AttachTouchHook();
    }


    partial void SetPlatformRaised(bool raised)
    {
        // Reordering subviews doesn't disturb UIKit's touch delivery, unlike the remove/re-add
        // that MAUI's ZIndex performs on Android.
        if (raised && this.Handler?.PlatformView is UIView native)
            native.Superview?.BringSubviewToFront(native);
    }


    void AttachTouchHook()
    {
        var native = this.DragHandle.Handler?.PlatformView as UIView;
        if (ReferenceEquals(native, this.hookedHandle))
            return;

        if (this.touchDown != null)
        {
            this.hookedHandle?.RemoveGestureRecognizer(this.touchDown);
            this.touchDown = null;
        }

        this.hookedHandle = native;
        if (this.hookedHandle == null)
            return;

        // The recognizer is retained by the native view and its callback token is a GC root while
        // retained, so a callback capturing `this` forms a native<->managed cycle the GC can never
        // break: the row, its handle's native view and the page leak. Capture a weak reference.
        var weakSelf = new WeakReference<DragSortRow>(this);
        this.touchDown = new UILongPressGestureRecognizer(r =>
        {
            if (weakSelf.TryGetTarget(out var row))
                row.OnHandleTouch(r);
        })
        {
            MinimumPressDuration = 0,
            CancelsTouchesInView = false,
            DelaysTouchesBegan = false,
            DelaysTouchesEnded = false,
            ShouldRecognizeSimultaneously = (_, _) => true
        };
        this.hookedHandle.AddGestureRecognizer(this.touchDown);
    }


    void OnHandleTouch(UILongPressGestureRecognizer recognizer)
    {
        switch (recognizer.State)
        {
            case UIGestureRecognizerState.Began:
                this.lockedScroller = FindScroller(recognizer.View);
                if (this.lockedScroller != null)
                    this.lockedScroller.ScrollEnabled = false;
                break;

            case UIGestureRecognizerState.Ended:
            case UIGestureRecognizerState.Cancelled:
            case UIGestureRecognizerState.Failed:
                // Ends with the touch, which is also when the pan ends - so this owns the
                // lock for its whole lifetime and the controller never touches the scroller.
                if (this.lockedScroller != null)
                {
                    this.lockedScroller.ScrollEnabled = true;
                    this.lockedScroller = null;
                }
                break;
        }
    }


    static UIScrollView? FindScroller(UIView? view)
    {
        while (view != null)
        {
            if (view is UIScrollView scroller)
                return scroller;

            view = view.Superview;
        }
        return null;
    }
}
