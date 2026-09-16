using UIKit;

namespace Shiny.Maui.Controls.Infrastructure;

partial class DragTouchHook
{
    UIView? hooked;
    UILongPressGestureRecognizer? touchDown;
    UIScrollView? lockedScroller;

    /// <remarks>
    /// A zero-duration long press is the only public way to observe touch-down and touch-up without
    /// subclassing the native view, and the simultaneous-recognition delegate lets it sit alongside
    /// MAUI's own recognizers instead of competing with them for the gesture. Once it has recognized
    /// (immediately, at duration zero) movement no longer cancels it - allowableMovement only gates
    /// recognition - so the same recognizer reports the touch-up that ends the drag.
    /// </remarks>
    partial void AttachPlatform()
    {
        var native = this.view.Handler?.PlatformView as UIView;
        if (ReferenceEquals(native, this.hooked))
            return;

        if (this.touchDown is not null)
        {
            this.hooked?.RemoveGestureRecognizer(this.touchDown);
            this.touchDown = null;
        }

        this.hooked = native;
        if (this.hooked is null)
            return;

        // The recognizer is retained by the native view and its callback token is a GC root while
        // retained, so a callback capturing `this` forms a native<->managed cycle the GC can never
        // break (hook -> view -> handler -> native view -> recognizer -> hook). Capture a weak reference.
        var weakSelf = new WeakReference<DragTouchHook>(this);
        this.touchDown = new UILongPressGestureRecognizer(r =>
        {
            if (weakSelf.TryGetTarget(out var hook))
                hook.OnTouch(r);
        })
        {
            MinimumPressDuration = 0,
            CancelsTouchesInView = false,
            DelaysTouchesBegan = false,
            DelaysTouchesEnded = false,
            ShouldRecognizeSimultaneously = (_, _) => true
        };
        this.hooked.AddGestureRecognizer(this.touchDown);
    }


    void OnTouch(UILongPressGestureRecognizer recognizer)
    {
        switch (recognizer.State)
        {
            case UIGestureRecognizerState.Began:
                this.Pressed?.Invoke();
                break;

            case UIGestureRecognizerState.Ended:
            case UIGestureRecognizerState.Cancelled:
            case UIGestureRecognizerState.Failed:
                // The lock is released here rather than left to the owner: this recognizer's
                // lifetime *is* the touch's, so the scroller can never stay disabled.
                this.SetPlatformScrollLock(false);
                this.Released?.Invoke();
                break;
        }
    }


    partial void SetPlatformScrollLock(bool locked)
    {
        if (locked)
        {
            this.lockedScroller ??= FindScroller(this.hooked);
            if (this.lockedScroller is not null)
                this.lockedScroller.ScrollEnabled = false;
        }
        else if (this.lockedScroller is not null)
        {
            this.lockedScroller.ScrollEnabled = true;
            this.lockedScroller = null;
        }
    }


    static partial void SetPlatformRaised(View target, bool raised)
    {
        // Reordering subviews does not disturb UIKit's touch delivery, unlike the remove/re-add
        // that MAUI's ZIndex performs.
        if (raised && target.Handler?.PlatformView is UIView native)
            native.Superview?.BringSubviewToFront(native);
    }


    static UIScrollView? FindScroller(UIView? from)
    {
        while (from is not null)
        {
            if (from is UIScrollView scroller)
                return scroller;

            from = from.Superview;
        }
        return null;
    }
}
