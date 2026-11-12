namespace Shiny.Maui.Controls.Foldables;

/// <summary>
/// The built-in posture source: Jetpack WindowManager on Android, <c>UIHingeInteraction</c> plus the
/// window's division reserved region on iOS 27.1+ (iPhone Duo). Everywhere else it reports <see cref="FoldInfo.None"/> forever.
/// </summary>
/// <remarks>
/// <para><see cref="FoldablesMauiAppBuilderExtensions.UseShinyFoldables"/> registers it and wires it to
/// the app lifecycle; you do not normally touch it. It is public so an app can wrap it — a debug
/// "simulate a fold" switch, say — and hand the wrapper to <c>UseFoldPosture</c>, which always wins over
/// the default registration.</para>
/// <para>Posture is reported for the current Activity (Android) or key window (iOS) only.</para>
/// </remarks>
public sealed partial class FoldPostureProvider : IFoldPostureSource
{
    FoldPostureProvider() { }

    /// <summary>The one instance; posture is per window, and the app has one current window.</summary>
    public static FoldPostureProvider Instance { get; } = new();

    /// <inheritdoc />
    public FoldInfo Current { get; private set; } = FoldInfo.None;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>Whether this platform build can report a fold at all.</summary>
    public static bool IsSupported => PlatformIsSupported();

    void Update(FoldInfo next)
    {
        if (next == this.Current)
            return;

        this.Current = next;
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

#if !ANDROID && !SHINY_UIHINGE
    static bool PlatformIsSupported() => false;
#endif
}
