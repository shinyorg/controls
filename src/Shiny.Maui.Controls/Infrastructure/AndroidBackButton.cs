namespace Shiny.Maui.Controls.Infrastructure;

/// <summary>
/// Lets an in-page overlay (a ribbon dropdown, an Office backstage or dialog, focus mode) take Android's
/// back button while it is showing, the way a real popup would.
/// </summary>
/// <remarks>
/// <para>
/// Without it the back gesture goes straight past the overlay to the activity, which closes the page -
/// or the app - with the dropdown still open. The most recently activated overlay answers first, so a
/// dialog opened over the backstage closes before the backstage does.
/// </para>
/// <para>
/// Routed through MAUI's <c>OnBackPressed</c> lifecycle event (registered by <c>UseShinyControls</c>)
/// rather than an <c>OnBackPressedCallback</c> subclass: the app build generates no Java peer for a
/// Java subclass in this assembly, so constructing one threw "no Java peer type found" and crashed the
/// app the moment a dropdown opened. Other heads have no back button, so this is inert there.
/// </para>
/// </remarks>
sealed class AndroidBackButton
{
    static readonly List<AndroidBackButton> active = [];
    readonly Action onBack;

    public AndroidBackButton(Action onBack) => this.onBack = onBack;


    /// <summary>Takes the back button while <paramref name="value"/>, gives it back otherwise.</summary>
    public void SetActive(bool value)
    {
        lock (active)
        {
            active.Remove(this);
            if (value)
                active.Add(this);
        }
    }


    /// <summary>Hands a back press to the most recent overlay. True when one took it.</summary>
    internal static bool HandleBack()
    {
        AndroidBackButton? top;
        lock (active)
        {
            if (active.Count == 0)
                return false;

            top = active[^1];

            // Out before it runs: the overlay's own close normally says so too, and one that forgets
            // must not swallow every back press after it.
            active.RemoveAt(active.Count - 1);
        }

        top.onBack();
        return true;
    }
}
