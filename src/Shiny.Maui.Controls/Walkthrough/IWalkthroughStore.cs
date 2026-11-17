using System.Collections.Concurrent;

namespace Shiny.Maui.Controls;

/// <summary>
/// Remembers which walkthroughs a user has already been through, so a tour with a
/// <see cref="Walkthrough.RememberRunKey"/> runs once and then stays out of the way.
/// </summary>
/// <remarks>
/// Replaceable so the flag can live wherever the rest of your user state does — a server profile, a
/// synced settings store — rather than being stranded on one device. Assign
/// <see cref="Walkthrough.Store"/> during startup.
/// </remarks>
public interface IWalkthroughStore
{
    /// <summary>Whether the walkthrough with this key has already completed.</summary>
    bool HasRun(string key);

    /// <summary>Records (or clears) that it has.</summary>
    void SetHasRun(string key, bool value);
}


/// <summary>
/// The default store, backed by MAUI's <see cref="Microsoft.Maui.Storage.Preferences"/>.
/// </summary>
/// <remarks>
/// Preferences is a platform API, so on the plain <c>net11.0</c> build — the one the AppKit and GTK4
/// heads use — it throws <c>NotImplementedInReferenceAssemblyException</c>. That is caught once and the
/// store falls back to memory for the rest of the process: the walkthrough still runs once per launch
/// rather than repeating within a session, and an app on those heads that wants real persistence
/// supplies its own <see cref="IWalkthroughStore"/>.
/// </remarks>
public sealed class PreferencesWalkthroughStore : IWalkthroughStore
{
    const string Prefix = "shiny.walkthrough.";

    static readonly ConcurrentDictionary<string, bool> Fallback = new();
    static bool preferencesUnavailable;


    public bool HasRun(string key)
    {
        if (!preferencesUnavailable)
        {
            try
            {
                return Microsoft.Maui.Storage.Preferences.Default.Get(Prefix + key, false);
            }
            catch
            {
                preferencesUnavailable = true;
            }
        }
        return Fallback.TryGetValue(key, out var value) && value;
    }


    public void SetHasRun(string key, bool value)
    {
        if (!preferencesUnavailable)
        {
            try
            {
                Microsoft.Maui.Storage.Preferences.Default.Set(Prefix + key, value);
                return;
            }
            catch
            {
                preferencesUnavailable = true;
            }
        }
        Fallback[key] = value;
    }
}


/// <summary>A store that never remembers anything — useful in tests and while designing a tour.</summary>
public sealed class InMemoryWalkthroughStore : IWalkthroughStore
{
    readonly ConcurrentDictionary<string, bool> values = new();

    public bool HasRun(string key) => this.values.TryGetValue(key, out var value) && value;

    public void SetHasRun(string key, bool value) => this.values[key] = value;
}
