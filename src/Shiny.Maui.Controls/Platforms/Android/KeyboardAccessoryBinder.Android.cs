using Android.Views;
using Android.Widget;
using AndroidX.Core.View;
using Microsoft.Maui.Platform;

namespace Shiny.Maui.Controls;

partial class KeyboardAccessoryBinder
{
    // Android has no accessory API at all - the IME is a different process and only an IME app can
    // draw inside it. So the bar is our own view in the activity's content frame, bottom-anchored and
    // pushed up by however much the IME actually overlaps it. It is shown only while the IME is
    // genuinely on screen, so a hardware keyboard (which raises no IME) correctly shows no bar.
    static Android.Views.View? currentAccessoryNative;

    Android.Views.View? accessoryNative;

    partial void ApplyPlatform(KeyboardAccessoryView? bar)
    {
        if (bar is null)
            RemoveAccessory();
        else if (input.IsFocused)
            AddAccessory();
    }

    partial void OnFocusChangedPlatform(bool focused)
    {
        if (focused)
            AddAccessory();
        else
            RemoveAccessory();
    }

    void AddAccessory()
    {
        if (bar is null)
            return;

        var context = input.Handler?.MauiContext;
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (context is null || activity is null)
            return;

        if (activity.FindViewById<ViewGroup>(Android.Resource.Id.Content) is not ViewGroup host)
            return;

        RemoveCurrent();

        var native = bar.ToPlatform(context);
        if (native.Parent is ViewGroup previousParent)
            previousParent.RemoveView(native);

        var heightPx = (int)context.Context.ToPixels(bar.BarHeight);
        host.AddView(native, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, heightPx)
        {
            Gravity = GravityFlags.Bottom
        });

        // Hidden until the insets say the IME is actually up, so the bar never flashes over content
        // on a device with a hardware keyboard.
        native.Visibility = ViewStates.Invisible;

        // BringToFront, never ZIndex: MAUI implements ZIndex by removing and re-adding the native
        // child, which fires ACTION_CANCEL and kills whatever touch is in flight.
        native.BringToFront();

        accessoryNative = native;
        currentAccessoryNative = native;

        ViewCompat.SetOnApplyWindowInsetsListener(native, new AccessoryInsetsListener(this));

        // API 30+ can follow the IME animation frame by frame. Below that the bar snaps into place
        // when the insets land - jumpier, but correct.
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
            ViewCompat.SetWindowInsetsAnimationCallback(native, new AccessoryInsetsAnimationCallback(this));

        ViewCompat.RequestApplyInsets(native);
    }

    void RemoveAccessory()
    {
        if (accessoryNative is null)
            return;

        if (ReferenceEquals(currentAccessoryNative, accessoryNative))
            currentAccessoryNative = null;

        ViewCompat.SetOnApplyWindowInsetsListener(accessoryNative, null);
        // The animation callback holds this binder (and through it the input and its page); clear it
        // with the listener rather than leaving it on a detached native view that outlives both.
        ViewCompat.SetWindowInsetsAnimationCallback(accessoryNative, null);
        (accessoryNative.Parent as ViewGroup)?.RemoveView(accessoryNative);
        accessoryNative = null;
    }

    static void RemoveCurrent()
    {
        if (currentAccessoryNative is null)
            return;

        ViewCompat.SetOnApplyWindowInsetsListener(currentAccessoryNative, null);
        // The animation callback holds this binder (and through it the input and its page); clear it
        // with the listener rather than leaving it on a detached native view that outlives both.
        ViewCompat.SetWindowInsetsAnimationCallback(currentAccessoryNative, null);
        (currentAccessoryNative.Parent as ViewGroup)?.RemoveView(currentAccessoryNative);
        currentAccessoryNative = null;
    }

    void PositionAccessory(Android.Views.View native, WindowInsetsCompat insets)
    {
        var ime = insets.GetInsets(WindowInsetsCompat.Type.Ime()).Bottom;

        if (ime <= 0 || !input.IsFocused)
        {
            native.Visibility = ViewStates.Invisible;
            return;
        }

        // With adjustResize the host has already shrunk to sit above the IME, so the overlap is zero
        // or small; edge-to-edge (forced on API 35+) and adjustPan leave the host full height and the
        // overlap is the whole IME. Measuring it rather than assuming is what makes both correct.
        var overlap = ime;
        if (native.Parent is Android.Views.View host)
        {
            var location = new int[2];
            host.GetLocationInWindow(location);

            var windowHeight = host.RootView?.Height ?? (location[1] + host.Height);
            var below = windowHeight - (location[1] + host.Height);
            overlap = Math.Max(0, ime - below);
        }

        native.TranslationY = -overlap;
        native.Visibility = ViewStates.Visible;
    }

    sealed class AccessoryInsetsListener(KeyboardAccessoryBinder owner) : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat OnApplyWindowInsets(Android.Views.View v, WindowInsetsCompat insets)
        {
            owner.PositionAccessory(v, insets);
            return insets;
        }
    }

    sealed class AccessoryInsetsAnimationCallback(KeyboardAccessoryBinder owner)
        : WindowInsetsAnimationCompat.Callback(DispatchModeStop)
    {
        public override WindowInsetsCompat OnProgress(WindowInsetsCompat insets, IList<WindowInsetsAnimationCompat> runningAnimations)
        {
            if (owner.accessoryNative is Android.Views.View native)
                owner.PositionAccessory(native, insets);

            return insets;
        }
    }
}
