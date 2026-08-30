using System.Runtime.Versioning;
using AVFoundation;
using CoreMedia;

namespace Shiny.Maui.Controls.Camera;

/// <summary>
/// Translates <see cref="PhotoQuality"/> into AVFoundation's two-part still-capture configuration, for iOS,
/// MacCatalyst and macOS alike.
/// </summary>
/// <remarks>
/// <para>
/// Two parts because AVFoundation splits the decision in two, and gets unhappy if they disagree. The
/// <c>AVCapturePhotoOutput</c> declares a <i>ceiling</i> — the largest photo and the most expensive
/// prioritization it will ever be asked for — and each <c>AVCapturePhotoSettings</c> then picks something at
/// or below it. Settings that exceed the output's ceiling throw at capture time rather than degrading.
/// </para>
/// <para>
/// The ceiling is therefore always raised to the maximum, whatever the current <see cref="PhotoQuality"/> is,
/// and the per-shot settings do the actual choosing. That is what makes the property live-settable: a view
/// that starts at <see cref="PhotoQuality.Session"/> and is later moved to <see cref="PhotoQuality.Highest"/>
/// over the wire needs no session reconfiguration, because the headroom was already declared.
/// </para>
/// <para>
/// Note what this deliberately does <i>not</i> do: touch the session preset. The preset sizes the preview and
/// the video outputs too, so raising it for the sake of stills would shrink-and-blink the preview and
/// renegotiate the format under any running recording. Asking the photo output for full-sensor dimensions
/// gets the same pixels without disturbing anything else — it is the same mechanism that lets a phone take a
/// 12MP still in the middle of recording 1080p video.
/// </para>
/// <para>
/// The version guards are written out inline at every call site rather than hidden behind a helper because
/// the platform-compatibility analyzer only understands them there. <c>MaxPhotoDimensions</c> arrived in iOS
/// 16 / macOS 13 and this package's floor is iOS 15, so the pre-16 path is the deprecated pair of booleans.
/// </para>
/// </remarks>
static class ApplePhotoQuality
{
    /// <summary>
    /// Declares the output's ceiling. Call inside the session configuration block, and again after anything
    /// that changes the active format (a preset change, a lens swap) — the supported dimensions are a
    /// property of the format, so they move when it does.
    /// </summary>
    public static void ConfigureOutput(AVCapturePhotoOutput output, AVCaptureDevice? device)
    {
        // Always the most expensive rung, regardless of the current PhotoQuality — see the class remarks.
        // Apple's guidance is to declare this before the session runs, so it is the ceiling that has to be
        // generous, not the per-shot setting.
        try
        {
            output.MaxPhotoQualityPrioritization = AVCapturePhotoQualityPrioritization.Quality;
        }
        catch
        {
            // Some capture devices (external/UVC on macOS) reject the prioritization outright. The capture
            // still works, it just will not fuse — better than refusing to configure the session.
        }

        if (OperatingSystem.IsIOSVersionAtLeast(16) ||
            OperatingSystem.IsMacCatalystVersionAtLeast(16) ||
            OperatingSystem.IsMacOSVersionAtLeast(13))
        {
            if (MaxDimensions(device) is { } dims)
            {
                try
                {
                    output.MaxPhotoDimensions = dims;
                }
                catch
                {
                    // Same bargain: a format that will not accept its own reported maximum falls back to the
                    // default dimensions rather than taking the session down with it.
                }
            }
            return;
        }

#pragma warning disable CA1416, CA1422
        // Pre-iOS 16 there is no MaxPhotoDimensions; the equivalent is a bool on the output, paired with one
        // on each settings object. Deprecated, and still the only lever below the 16.0 floor.
        output.IsHighResolutionCaptureEnabled = true;
#pragma warning restore CA1416, CA1422
    }


    /// <summary>
    /// Builds the settings for one capture at the requested quality.
    /// </summary>
    public static AVCapturePhotoSettings CreateSettings(
        AVCapturePhotoOutput output,
        AVCaptureDevice? device,
        PhotoQuality quality
    )
    {
        var settings = AVCapturePhotoSettings.Create();
        if (quality == PhotoQuality.Session)
            return settings; // whatever the session is sized for, which is the pre-PhotoQuality behaviour

        var wanted = quality == PhotoQuality.Highest
            ? AVCapturePhotoQualityPrioritization.Quality
            : AVCapturePhotoQualityPrioritization.Balanced;

        // Never above the ceiling declared in ConfigureOutput, or the capture throws. Normally identical;
        // this matters when the output refused the ceiling above and quietly kept a lower one.
        settings.PhotoQualityPrioritization = (AVCapturePhotoQualityPrioritization)Math.Min(
            (long)wanted,
            (long)output.MaxPhotoQualityPrioritization
        );

        if (OperatingSystem.IsIOSVersionAtLeast(16) ||
            OperatingSystem.IsMacCatalystVersionAtLeast(16) ||
            OperatingSystem.IsMacOSVersionAtLeast(13))
        {
            if (MaxDimensions(device) is { } dims && Fits(dims, output.MaxPhotoDimensions))
                settings.MaxPhotoDimensions = dims;

            return settings;
        }

#pragma warning disable CA1416, CA1422
        settings.IsHighResolutionPhotoEnabled = output.IsHighResolutionCaptureEnabled;
#pragma warning restore CA1416, CA1422
        return settings;
    }


    /// <summary>
    /// The largest still the device's active format will produce, or null where the format reports nothing.
    /// </summary>
    /// <remarks>
    /// Largest by area rather than by width: the reported list is not ordered, and on devices that offer both
    /// a 4:3 and a 16:9 still the wider one is not the bigger one. Area is what "full sensor" means here.
    /// </remarks>
    [SupportedOSPlatform("ios16.0")]
    [SupportedOSPlatform("maccatalyst16.0")]
    [SupportedOSPlatform("macos13.0")]
    static CMVideoDimensions? MaxDimensions(AVCaptureDevice? device)
    {
        var supported = device?.ActiveFormat?.SupportedMaxPhotoDimensions;
        if (supported == null || supported.Length == 0)
            return null;

        var best = supported[0];
        foreach (var candidate in supported)
        {
            if ((long)candidate.Width * candidate.Height > (long)best.Width * best.Height)
                best = candidate;
        }
        return best;
    }


    [SupportedOSPlatform("ios16.0")]
    [SupportedOSPlatform("maccatalyst16.0")]
    [SupportedOSPlatform("macos13.0")]
    static bool Fits(CMVideoDimensions wanted, CMVideoDimensions ceiling)
        => ceiling.Width > 0 && ceiling.Height > 0 &&
           wanted.Width <= ceiling.Width && wanted.Height <= ceiling.Height;
}
