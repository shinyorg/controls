using Foundation;
using UIKit;

namespace Shiny.Maui.Controls;

public partial class ShinyNavigationPage
{
    // UIKit refers to its delegates weakly, so the one handed to the recognizer has to be kept alive
    // here or it is collected and the gesture silently reverts to UIKit's own (disabled) behaviour.
    readonly List<NSObject> gestureDelegates = new();

    /// <summary>
    /// Puts iOS's edge-swipe pop back. <c>UINavigationController</c> disables
    /// <c>interactivePopGestureRecognizer</c> whenever its bar is hidden — which is exactly what this
    /// page does to make room for the drawn bar — so a stock <c>NavigationPage</c>'s swipe-back would
    /// otherwise be lost simply by adopting it.
    /// </summary>
    /// <remarks>
    /// The recognizer's delegate is replaced rather than nulled. Nulling it is the widely-copied
    /// trick and it does re-enable the gesture, but it also lets a swipe start on the root page,
    /// where UIKit pops nothing and leaves the controller unable to respond to touches at all. The
    /// replacement answers the one question the default delegate exists to answer — is there anything
    /// to pop — and nothing else.
    /// </remarks>
    partial void ApplySwipeBack()
    {
        if (!this.EnableSwipeBackGesture)
            return;

        if (this.Handler?.PlatformView is not UIResponder responder)
            return;

        var controller = FindNavigationController(responder);
        if (controller?.InteractivePopGestureRecognizer is not { } recognizer)
            return;

        if (recognizer.Delegate is PopGestureDelegate)
        {
            recognizer.Enabled = true;
            return;
        }

        var behaviour = new PopGestureDelegate(controller);
        this.gestureDelegates.Add(behaviour);
        recognizer.Delegate = behaviour;
        recognizer.Enabled = true;
    }


    /// <summary>
    /// Points the status bar's clock and icons at <paramref name="style"/>.
    /// </summary>
    /// <remarks>
    /// <para>There is no background to set. iOS has never had a status bar background — what shows
    /// behind the clock is whatever the app draws there, which is the bar's own surface extended
    /// through the top safe-area inset. So <paramref name="background"/> is not used here: it has
    /// already done its work by the time this runs, and only the foreground is left.</para>
    /// <para><c>SetStatusBarStyle</c> is deprecated and takes effect only when the app's
    /// <c>Info.plist</c> sets <c>UIViewControllerBasedStatusBarAppearance</c> to <c>false</c>. It is
    /// used anyway because it is the same call MAUI itself makes for <c>BarTextColor</c> — the
    /// alternative is <c>preferredStatusBarStyle</c>, which can only be answered by overriding a view
    /// controller, and every controller in the chain here belongs to MAUI. Without the key the status
    /// bar keeps the system's appearance and only the colour behind it follows the bar, which is why
    /// nothing about this is allowed to throw.</para>
    /// </remarks>
    partial void ApplyStatusBar(Color? background, StatusBarStyle style)
    {
        var app = UIApplication.SharedApplication;
        if (app is null)
            return;

        var resolved = style == StatusBarStyle.LightContent
            ? UIStatusBarStyle.LightContent
            : UIStatusBarStyle.DarkContent;

#pragma warning disable CA1422 // see the remarks: MAUI's own status bar handling calls this too
        app.SetStatusBarStyle(resolved, false);
#pragma warning restore CA1422
    }


    static UINavigationController? FindNavigationController(UIResponder? responder)
    {
        // Walked rather than cast: which type MAUI's iOS navigation handler exposes as its platform
        // view has changed across releases, and the responder chain has not.
        while (responder is not null)
        {
            if (responder is UINavigationController controller)
                return controller;

            responder = responder.NextResponder;
        }
        return null;
    }


    sealed class PopGestureDelegate(UINavigationController controller) : UIGestureRecognizerDelegate
    {
        public override bool ShouldBegin(UIGestureRecognizer recognizer)
            => controller.ViewControllers is { Length: > 1 };

        /// <summary>
        /// Never alongside another gesture. Letting the edge swipe run with a scroll view's pan is how
        /// a horizontal flick inside a carousel ends up popping the page.
        /// </summary>
        public override bool ShouldRecognizeSimultaneously(UIGestureRecognizer recognizer, UIGestureRecognizer other)
            => false;
    }
}
