using SkiaSharp.Views.Maui.Controls.Hosting;

namespace Shiny.Maui.Controls.Office;

public static class MauiAppBuilderExtensions
{
    /// <summary>
    /// Registers everything the Office controls need: the SkiaSharp views they are built on, and — on
    /// the macOS AppKit head — the Skia canvas SkiaSharp itself does not ship.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call this instead of <c>UseSkiaSharp()</c>; it calls that itself. On iOS, Android, Mac Catalyst
    /// and Windows the two are equivalent, so an app already calling <c>UseSkiaSharp()</c> loses
    /// nothing by switching. On <c>net10.0-macos</c> it is the difference between the spreadsheet,
    /// document and slide controls rendering and a blank page: SkiaSharp has no AppKit target, and its
    /// fallback handler throws <see cref="NotImplementedException"/> where the platform view should be.
    /// See <c>MacOSSKCanvasViewHandler</c>.
    /// </para>
    /// <para>
    /// Spell checking needs no registration — the platform checker installs itself through a module
    /// initializer. See <c>PlatformSpellChecker</c>.
    /// </para>
    /// </remarks>
    public static MauiAppBuilder UseShinyOffice(this MauiAppBuilder builder)
    {
        builder.UseSkiaSharp();

#if MACOS
        // After UseSkiaSharp, deliberately: the handler collection keeps the last registration for a
        // type, so this replaces the stock SKCanvasViewHandler rather than racing it.
        builder.ConfigureMauiHandlers(handlers =>
            handlers.AddHandler<SkiaSharp.Views.Maui.Controls.SKCanvasView, MacOSSKCanvasViewHandler>());
#endif

#if IOS || MACCATALYST
        // The editors' hidden entry: UIKit drops a Backspace in an empty field, which is where the
        // editors keep it. See OfficeTextInput.
        builder.ConfigureMauiHandlers(handlers =>
            handlers.AddHandler<OfficeTextInput, OfficeTextInputHandler>());
#endif

        return builder;
    }
}
