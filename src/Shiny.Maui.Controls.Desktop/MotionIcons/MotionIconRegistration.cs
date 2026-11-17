#if MACOS || MACCATALYST
using System.Runtime.CompilerServices;
using Shiny.Maui.Controls.MotionIcons;

namespace Shiny.Maui.Controls.Desktop.MotionIcons;

/// <summary>
/// Puts <c>MotionIconView</c> plays on Core Animation on macOS and Mac Catalyst.
/// </summary>
/// <remarks>
/// Those heads get core's <c>net11.0</c> asset, which cannot reach Core Animation, so the player is
/// compiled here and plugged into core's seam. A module initializer rather than a builder call:
/// it is not a feature anyone should have to opt into, and any app with this package installed
/// loads it through one of its <c>Use…</c> calls before the first page exists.
/// </remarks>
static class MotionIconRegistration
{
#pragma warning disable CA2255 // Deliberate: see the remarks.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Register() => NativeMotion.Factory ??= CoreAnimationMotionPlayer.TryCreate;
}
#endif
