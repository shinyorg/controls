using AppKit;
using AVFoundation;
using CoreAnimation;
using CoreGraphics;

namespace Shiny.Maui.Controls.Media;

/// <summary>The macOS AppKit video surface: a layer-backed <see cref="NSView"/> hosting an <see cref="AVPlayerLayer"/>.</summary>
/// <remarks>
/// <para>
/// ⚠️ <b>The player layer is a sublayer, not the backing layer.</b> Making an <see cref="AVPlayerLayer"/> the view's
/// backing layer through <c>MakeBackingLayer</c> (the AppKit stand-in for UIKit's <c>layerClass</c>) builds and plays —
/// position advances, audio plays, the video size is reported — and shows nothing but black: a backing layer
/// belongs to AppKit, which owns and redraws its contents. Hosted as a sublayer of an ordinary layer-backed view,
/// the player layer draws.
/// </para>
/// <para>
/// ⚠️ <b>The handler has to place this view itself.</b> MAUI's generic <c>ViewHandler</c> arranges nothing on
/// <c>net11.0-macos</c>: the platform view keeps a zero frame while MAUI's own layout says 1028x220, so the video drew
/// into a 0x0 rectangle — playing, ready for display, and invisible. <see cref="ApplyArrangedFrame"/> is called from
/// each handler's <c>PlatformArrange</c> (the CameraView's macOS handler works around the same thing).
/// </para>
/// <para>
/// The player layer follows the view's size through its autoresizing mask, and <see cref="Layout"/> sets it outright
/// so a frame change never shows the video at the old size for a pass.
/// </para>
/// </remarks>
public class MediaPlayerNSView : NSView, IApplePlayerOutput
{
    public MediaPlayerNSView()
    {
        this.WantsLayer = true;
        this.PlayerLayer = new AVPlayerLayer
        {
            AutoresizingMask = CAAutoresizingMask.WidthSizable | CAAutoresizingMask.HeightSizable
        };
        this.Layer!.AddSublayer(this.PlayerLayer);
    }

    /// <inheritdoc />
    public AVPlayerLayer PlayerLayer { get; }

    /// <summary>
    /// Give the view the frame MAUI arranged it at. The AppKit host's containers are flipped, so MAUI's top-left
    /// rectangle is the frame as-is. Every arrange, not only the first: the first is rarely the final one.
    /// </summary>
    internal void ApplyArrangedFrame(Rect frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0)
            return;

        var wanted = new CGRect(frame.X, frame.Y, frame.Width, frame.Height);
        if (Math.Abs(this.Frame.X - wanted.X) >= 0.5 || Math.Abs(this.Frame.Y - wanted.Y) >= 0.5
            || Math.Abs(this.Frame.Width - wanted.Width) >= 0.5 || Math.Abs(this.Frame.Height - wanted.Height) >= 0.5)
        {
            this.Frame = wanted;
        }
        this.NeedsLayout = true;
    }

    /// <inheritdoc />
    public override void Layout()
    {
        base.Layout();

        // A layer-backed NSView can be handed a fresh backing layer; a player layer left on the old one still plays,
        // into a layer that is no longer on screen.
        if (this.Layer is { } root && !ReferenceEquals(this.PlayerLayer.SuperLayer, root))
            root.InsertSublayer(this.PlayerLayer, 0);

        // no implicit animation: the video would otherwise ease to its new size behind the resized view
        CATransaction.Begin();
        CATransaction.DisableActions = true;
        this.PlayerLayer.Frame = this.Bounds;
        CATransaction.Commit();
    }
}
