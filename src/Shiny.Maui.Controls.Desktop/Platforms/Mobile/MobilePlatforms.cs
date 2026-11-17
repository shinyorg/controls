using Microsoft.Maui.Graphics;
using Shiny.Maui.Controls.QuickEntry;
using MauiWindow = Microsoft.Maui.Controls.Window;

// The android and ios assets of this package. There is no desktop window on a phone, so every
// desktop-only service reports itself unsupported and does nothing - the same answers the net11.0
// (Linux) asset gave on a phone at runtime, without compiling the GTK interop into a mobile app.
//
// These targets exist so an Android/iOS app referencing this package gets *this* build, which
// references core's android/ios asset. Without them the app also received core's net11.0 assembly,
// and on a clean Android build the Java stub scanner read that copy ("Found 0 Java types"): every
// Java.Lang.Object subclass in core - CarouselGallery's RecyclerView adapter, StaggeredGrid's,
// FrostedGlassView's pre-draw listener - then crashed with "no Java peer type found".

namespace Shiny.Maui.Controls.Desktop.FileDrop
{
    static class FileDropPlatform
    {
        public static bool IsSupported => false;

        public static IDisposable? Attach(IFileDropHost host, MauiWindow window, object platformWindow) => null;
    }
}

namespace Shiny.Maui.Controls.Desktop.QuickEntry
{
    static class QuickEntryPlatform
    {
        public static bool IsSupported => false;

        public static void Initialize(IDesktopQuickEntryHost host, object platformWindow, QuickEntryOptions options) { }

        public static void Show(object platformWindow, QuickEntryOptions options, double width, double height) { }

        public static void Hide(object platformWindow) { }

        public static void Resize(object platformWindow, QuickEntryOptions options, double width, double height) { }

        public static void Teardown(object platformWindow) { }

        public static void PolishEntry(object? platformView) { }
    }

    static class ScreenGlowPlatform
    {
        public static bool IsSupported => false;

        public static void Initialize(object platformWindow) { }

        public static void Show(object platformWindow) { }

        public static void Hide(object platformWindow) { }

        public static void Teardown(object platformWindow) { }

        public static Size GetScreenSize() => Size.Zero;
    }
}
