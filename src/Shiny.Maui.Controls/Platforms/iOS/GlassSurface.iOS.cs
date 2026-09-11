using System.Runtime.CompilerServices;
using Microsoft.Maui.Platform;
using UIKit;

namespace Shiny.Maui.Controls.Infrastructure;

static partial class GlassSurface
{
    /// <summary>
    /// The pane attached to each native view, so a re-apply updates the one that is there instead of
    /// stacking a second one behind it. Keyed weakly: MAUI disposes a platform view whenever its
    /// handler is disconnected, and a strong table would hold every bar the app has ever shown.
    /// </summary>
    static readonly ConditionalWeakTable<UIView, UIVisualEffectView> panes = new();


    /// <summary>
    /// iOS 26 is where <c>UIGlassEffect</c> arrives. The check is a runtime one rather than a
    /// compile-time <c>#if</c>: the package's minimum is iOS 15, so the same binary has to run on
    /// both and simply paint its fill on the older one.
    /// </summary>
    static bool PlatformIsSupported()
        => OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26);


    static void PlatformApply(VisualElement host, GlassSurfaceOptions options)
    {
        // Spelled out here rather than deferred to PlatformIsSupported, even though Apply has already
        // asked it: the platform-compatibility analyzer only recognises the version check when it can
        // see it in the same method, and the alternative is CA1416 suppressed on every line below.
        if (!OperatingSystem.IsIOSVersionAtLeast(26) && !OperatingSystem.IsMacCatalystVersionAtLeast(26))
            return;

        // No handler yet - the caller re-applies on HandlerChanged, which is the same path a
        // re-parented page takes when MAUI rebuilds its platform views.
        if (host.Handler?.PlatformView is not UIView native)
            return;

        if (!panes.TryGetValue(native, out var pane))
        {
            pane = new UIVisualEffectView
            {
                AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight,

                // The glass is scenery. Every gesture in the bar belongs to the MAUI views above it,
                // and a UIVisualEffectView that took touches would swallow the tap on whichever tab
                // happened to be over it.
                UserInteractionEnabled = false
            };

            panes.Add(native, pane);
        }

        // Re-inserted rather than left where it was: MAUI adds and removes subviews of its own as
        // content changes, and the glass has to stay behind all of them. Inserting a view that is
        // already a subview moves it, so this is also the no-op case.
        pane.Frame = native.Bounds;
        native.InsertSubview(pane, 0);

        // A fresh effect each time. UIGlassEffect is configuration rather than a live object - the
        // style is fixed at construction, and mutating TintColor on the instance the view is already
        // holding is not something UIKit promises to notice.
        var effect = UIGlassEffect.Create(options.Clear ? UIGlassEffectStyle.Clear : UIGlassEffectStyle.Regular);
        if (options.Tint is { } tint)
            effect.TintColor = tint.ToPlatform();

        pane.Effect = effect;

        // The shape goes on the pane, not on a mask: UIKit's corner configuration is what gives the
        // glass its correctly-curved edge highlight, and a capsule tracks the height on its own
        // rather than needing the radius recomputed every time the bar resizes.
        pane.CornerConfiguration = options.Capsule
            ? UICornerConfiguration.CreateCapsule()
            : UICornerConfiguration.CreateUniformCorners(UICornerRadius.CreateFixed((nfloat)Math.Max(0, options.CornerRadius)));
    }


    static void PlatformRemove(VisualElement host)
    {
        if (host.Handler?.PlatformView is not UIView native)
            return;

        if (!panes.TryGetValue(native, out var pane))
            return;

        panes.Remove(native);
        pane.RemoveFromSuperview();
        pane.Dispose();
    }
}
