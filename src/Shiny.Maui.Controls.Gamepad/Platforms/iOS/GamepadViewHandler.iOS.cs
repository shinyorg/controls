using CoreGraphics;
using Foundation;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using UIKit;

namespace Shiny.Maui.Controls.Gaming;


/// <summary>The stock GraphicsView handler with a multi-touch platform view swapped in.</summary>
public class GamepadViewHandler : GraphicsViewHandler
{
    protected override PlatformTouchGraphicsView CreatePlatformView() => new GamepadPlatformView();


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
/// Reads every finger by its UITouch, and declines touches that land on no element so they reach the
/// view beneath.
/// </summary>
/// <remarks>
/// <para>The base view reports touches to MAUI as one anonymous array of points, with no way to tell
/// which finger is which when a second one lands. A stick needs to know its thumb is still its thumb
/// while the other presses a button, so the touch overrides here replace the base's rather than
/// extending them.</para>
/// <para>The owner is held weakly: UIKit retains this view, and a strong reference back to the MAUI
/// view would keep the whole page alive through the native side.</para>
/// </remarks>
class GamepadPlatformView : PlatformTouchGraphicsView
{
    public GamepadPlatformView()
    {
        this.MultipleTouchEnabled = true;
        this.ExclusiveTouch = false;
    }


    public WeakReference<GamepadView>? Owner { get; set; }

    GamepadView? View => this.Owner != null && this.Owner.TryGetTarget(out var view) ? view : null;


    // a UITouch is the same object for the whole life of one finger - its native handle is the id
    static long IdOf(UITouch touch) => touch.Handle.Handle.ToInt64();


    public override bool PointInside(CGPoint point, UIEvent? uievent)
    {
        var view = this.View;
        if (view == null)
            return base.PointInside(point, uievent);

        return base.PointInside(point, uievent) && view.ShouldReceive((float)point.X, (float)point.Y);
    }


    public override void TouchesBegan(NSSet touches, UIEvent? evt)
    {
        var view = this.View;
        if (view == null)
            return;

        foreach (var touch in touches.Cast<UITouch>())
        {
            var p = touch.LocationInView(this);
            view.OnPointerDown(IdOf(touch), (float)p.X, (float)p.Y);
        }
    }


    public override void TouchesMoved(NSSet touches, UIEvent? evt)
    {
        var view = this.View;
        if (view == null)
            return;

        foreach (var touch in touches.Cast<UITouch>())
        {
            var p = touch.LocationInView(this);
            view.OnPointerMove(IdOf(touch), (float)p.X, (float)p.Y);
        }
    }


    public override void TouchesEnded(NSSet touches, UIEvent? evt)
    {
        var view = this.View;
        if (view == null)
            return;

        foreach (var touch in touches.Cast<UITouch>())
            view.OnPointerUp(IdOf(touch));
    }


    public override void TouchesCancelled(NSSet touches, UIEvent? evt)
    {
        var view = this.View;
        if (view == null)
            return;

        foreach (var touch in touches.Cast<UITouch>())
            view.OnPointerCancel(IdOf(touch));
    }
}
