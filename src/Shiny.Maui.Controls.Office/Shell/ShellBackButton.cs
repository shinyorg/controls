namespace Shiny.Maui.Controls.Office;

/// <summary>
/// Lets an in-page overlay (the backstage, a dialog, focus mode) take Android's back button while it
/// is showing, the way a real dialog would.
/// </summary>
/// <remarks>
/// Without it the back gesture goes straight past the overlay to the activity, which closes the page -
/// or the app - with the backstage still open. A callback is added each time the overlay opens, not
/// once up front, because the dispatcher asks the most recently added callback first: a dialog opened
/// over the backstage has to close before the backstage does. Other heads have no back button, so this
/// is a no-op there.
/// </remarks>
sealed class ShellBackButton
{
    readonly Action onBack;

#if ANDROID
    Callback? callback;

    sealed class Callback : AndroidX.Activity.OnBackPressedCallback
    {
        readonly Action onBack;

        public Callback(Action onBack) : base(true) => this.onBack = onBack;

        public override void HandleOnBackPressed() => this.onBack();
    }
#endif

    public ShellBackButton(Action onBack) => this.onBack = onBack;


    /// <summary>Takes the back button while <paramref name="active"/>, gives it back otherwise.</summary>
    public void SetActive(bool active)
    {
#if ANDROID
        if (active)
        {
            if (this.callback is not null)
                return;

            if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity is not AndroidX.Activity.ComponentActivity activity)
                return;

            this.callback = new Callback(this.onBack);
            activity.OnBackPressedDispatcher.AddCallback(this.callback);
        }
        else if (this.callback is { } existing)
        {
            this.callback = null;
            existing.Remove();
            existing.Dispose();
        }
#else
        _ = active;
        _ = this.onBack;
#endif
    }
}
