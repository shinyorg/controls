using Android.Content;
using Android.Views;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace Shiny.Maui.Controls.Gaming;


/// <summary>The stock GraphicsView handler with a multi-touch platform view swapped in.</summary>
public class GamepadViewHandler : GraphicsViewHandler
{
    protected override PlatformTouchGraphicsView CreatePlatformView() => new GamepadPlatformView(this.Context);


    protected override void ConnectHandler(PlatformTouchGraphicsView platformView)
    {
        base.ConnectHandler(platformView);
        if (platformView is GamepadPlatformView gamepad)
            gamepad.Owner = new WeakReference<GamepadView>((GamepadView)this.VirtualView);
    }


    protected override void DisconnectHandler(PlatformTouchGraphicsView platformView)
    {
        if (platformView is GamepadPlatformView gamepad)
            gamepad.Owner = null;

        base.DisconnectHandler(platformView);
    }
}


/// <summary>
/// Reads every pointer of a multi-touch MotionEvent by its pointer id, and declines a gesture that starts
/// on no element so it reaches the view beneath.
/// </summary>
/// <remarks>
/// <para>Declining is Android's own pass-through: a view that returns false for ACTION_DOWN is skipped,
/// and its parent offers the event to the next child down the z-order - the game under the overlay.
/// With split motion events (the default for every layout) a later finger that lands on a button still
/// arrives here as its own ACTION_DOWN even while the first finger is on the game.</para>
/// <para>Coordinates arrive in physical pixels and the engine works in the same device-independent
/// units the canvas draws in, hence the division by density.</para>
/// </remarks>
class GamepadPlatformView(Context context) : PlatformTouchGraphicsView(context)
{
    public WeakReference<GamepadView>? Owner { get; set; }

    GamepadView? View => this.Owner != null && this.Owner.TryGetTarget(out var view) ? view : null;


    public override bool OnTouchEvent(MotionEvent? e)
    {
        var view = this.View;
        if (view == null || e == null)
            return base.OnTouchEvent(e);

        var density = this.Resources?.DisplayMetrics?.Density ?? 1f;

        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
            case MotionEventActions.PointerDown:
            {
                var index = e.ActionIndex;
                var x = e.GetX(index) / density;
                var y = e.GetY(index) / density;

                if (e.ActionMasked == MotionEventActions.Down)
                {
                    if (!view.ShouldReceive(x, y))
                        return false;

                    // a stick drag must never be taken over by a scroller or a drawer somewhere above
                    this.Parent?.RequestDisallowInterceptTouchEvent(true);
                }

                view.OnPointerDown(e.GetPointerId(index), x, y);
                return true;
            }

            case MotionEventActions.Move:
                for (var i = 0; i < e.PointerCount; i++)
                    view.OnPointerMove(e.GetPointerId(i), e.GetX(i) / density, e.GetY(i) / density);
                return true;

            case MotionEventActions.Up:
            case MotionEventActions.PointerUp:
                view.OnPointerUp(e.GetPointerId(e.ActionIndex));
                return true;

            case MotionEventActions.Cancel:
                for (var i = 0; i < e.PointerCount; i++)
                    view.OnPointerCancel(e.GetPointerId(i));
                return true;
        }
        return true;
    }
}
