using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Shiny.Maui.Controls.Desktop.Docking;
using Shiny.Maui.Controls.Desktop.QuickEntry;
using Shiny.Maui.Controls.QuickEntry;
using Shiny.Maui.Controls.Desktop.TrayIcon;

namespace Shiny;

public static class DesktopMauiAppBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="ITrayIconFactory"/> for the current platform. On unsupported
    /// platforms (Android, iOS, tvOS) this is a no-op factory that throws on use, so
    /// guard your tray code with platform checks.
    /// </summary>
    public static MauiAppBuilder UseTrayIcon(this MauiAppBuilder builder)
    {
#if WINDOWS
        builder.Services.TryAddSingleton<ITrayIconFactory, WindowsTrayIconFactory>();
#elif MACCATALYST
        builder.Services.TryAddSingleton<ITrayIconFactory, MacCatalystTrayIconFactory>();
#elif MACOS
        builder.Services.TryAddSingleton<ITrayIconFactory, MacTrayIconFactory>();
#else
        // net10.0 fallback — Linux at runtime, otherwise unsupported.
        builder.Services.TryAddSingleton<ITrayIconFactory>(_ =>
        {
            if (OperatingSystem.IsLinux())
                return new LinuxTrayIconFactory();
            return new UnsupportedTrayIconFactory();
        });
#endif
        return builder;
    }

    /// <summary>
    /// Adds the desktop half of Shiny's quick entry popup: a borderless, always-on-top OS window that
    /// opens over <em>other applications</em>, the screen-edge glow drawn across the whole display,
    /// and <see cref="IGlobalHotKeyService"/> for system-wide shortcuts.
    /// </summary>
    /// <example>
    /// <code>
    /// builder
    ///     .UseShinyControls(cfg => cfg.ConfigureQuickEntry(o =>
    ///     {
    ///         o.HotKey = OperatingSystem.IsMacOS() ? "Cmd+Opt+Space" : "Ctrl+Alt+Space";
    ///         o.ScreenGlow = ScreenGlowTrigger.WhileBusy;
    ///     }))
    ///     .UseDesktopQuickEntry();
    /// </code>
    /// </example>
    /// <remarks>
    /// <para>
    /// The popup itself, the <c>PromptView</c> control and the in-app presentation all live in
    /// <c>Shiny.Maui.Controls</c> and are registered by <c>UseShinyControls()</c> — so a shared
    /// codebase already has a working popup on every platform. This adds a second presentation the
    /// core service will choose when <see cref="QuickEntryOptions.Presentation"/> allows it, which
    /// <see cref="QuickEntryPresentation.Auto"/> (the default) does.
    /// </para>
    /// <para>
    /// Safe to call unconditionally. On MacCatalyst — and on any platform that reaches this package's
    /// <c>net10.0</c> asset without being a desktop — the presenters report themselves unsupported
    /// and the core service quietly stays with the overlay.
    /// </para>
    /// </remarks>
    public static MauiAppBuilder UseDesktopQuickEntry(this MauiAppBuilder builder)
    {
#if WINDOWS
        builder.Services.TryAddSingleton<IGlobalHotKeyService, WindowsGlobalHotKeyService>();
        builder.Services.AddSingleton<IScreenGlowPresenter>(sp => new WindowsScreenGlow(sp.GetService<ILogger<WindowsScreenGlow>>()));
#elif MACCATALYST
        builder.Services.TryAddSingleton<IGlobalHotKeyService, CatalystGlobalHotKeyService>();
        builder.Services.AddSingleton<IScreenGlowPresenter>(sp => new DesktopScreenGlowPresenter(sp.GetService<ILogger<DesktopScreenGlowPresenter>>()));
#elif MACOS
        builder.Services.TryAddSingleton<IGlobalHotKeyService, MacGlobalHotKeyService>();
        builder.Services.AddSingleton<IScreenGlowPresenter>(sp => new DesktopScreenGlowPresenter(sp.GetService<ILogger<DesktopScreenGlowPresenter>>()));
#else
        // net10.0 fallback — Linux at runtime, otherwise unsupported.
        builder.Services.TryAddSingleton<IGlobalHotKeyService>(sp =>
        {
            if (OperatingSystem.IsLinux())
                return new LinuxGlobalHotKeyService(sp.GetService<ILogger<LinuxGlobalHotKeyService>>());
            return new UnsupportedGlobalHotKeyService();
        });
        builder.Services.AddSingleton<IScreenGlowPresenter>(sp => new DesktopScreenGlowPresenter(sp.GetService<ILogger<DesktopScreenGlowPresenter>>()));
#endif

        builder.Services.AddSingleton<IQuickEntryPresenter>(sp => new DesktopQuickEntryPresenter(sp.GetService<ILogger<DesktopQuickEntryPresenter>>()));
        builder.Services.AddSingleton<IMauiInitializeService, DesktopQuickEntryInitializer>();

        // BorderlessEntry's chrome-stripping runs through handler mappers that the AppKit and GTK4
        // heads never execute — they ship their own handler types. Core exposes a hook for exactly
        // this rather than taking a dependency on a package that only exists on desktop.
        PromptEntryPolish.Handler = QuickEntryPlatform.PolishEntry;
        return builder;
    }

    /// <summary>
    /// Supplies the native key sources that make Shiny's keyboard shortcuts work on macOS (AppKit),
    /// Linux (GTK4) and Mac Catalyst.
    /// </summary>
    /// <example>
    /// <code>
    /// builder
    ///     .UseShinyControls()
    ///     .UseDesktopKeyboardShortcuts();
    /// </code>
    /// </example>
    /// <remarks>
    /// <para>
    /// <c>KeyboardShortcut</c>, <c>KeyboardShortcuts.Shortcuts</c> and <c>IKeyboardShortcutService</c>
    /// live in <c>Shiny.Maui.Controls</c>, which already listens for keys on Windows, Android and iOS.
    /// The AppKit and GTK4 heads ship their own window types, so their key sources live here with the
    /// rest of the desktop interop; without this call, shortcuts there are declared but never fire.
    /// </para>
    /// <para>
    /// Safe to call unconditionally: on Windows, Android and iOS core's own source stays in place.
    /// These are in-app shortcuts that fire while your window has focus — for system-wide hotkeys see
    /// <see cref="IGlobalHotKeyService"/>.
    /// </para>
    /// </remarks>
    public static MauiAppBuilder UseDesktopKeyboardShortcuts(this MauiAppBuilder builder)
    {
#if MACOS || MACCATALYST
        Shiny.Maui.Controls.KeyboardShortcutManager.PlatformAttach = Shiny.Maui.Controls.Desktop.KeyboardShortcuts.KeyboardShortcutPlatform.Attach;
#elif !WINDOWS
        // net10.0 asset — Linux at runtime, otherwise nothing to attach.
        if (OperatingSystem.IsLinux())
            Shiny.Maui.Controls.KeyboardShortcutManager.PlatformAttach = Shiny.Maui.Controls.Desktop.KeyboardShortcuts.KeyboardShortcutPlatform.Attach;
#endif
        return builder;
    }

    /// <summary>
    /// Registers the docking infrastructure. Call once during MAUI app startup, then
    /// register each panel with <see cref="AddDockPanel{TView}"/>.
    /// </summary>
    public static MauiAppBuilder UseShinyDocking(this MauiAppBuilder builder)
    {
        builder.Services.TryAddSingleton<DockableContentRegistry>();
        return builder;
    }

    /// <summary>
    /// Registers a panel type so dock layouts referencing <paramref name="panelTypeId"/>
    /// can resolve and instantiate the View. The View is resolved through DI per request.
    /// </summary>
    /// <typeparam name="TView">The panel content view type.</typeparam>
    /// <param name="builder">The MAUI app builder.</param>
    /// <param name="panelTypeId">Stable string ID stored in persisted layouts.</param>
    public static MauiAppBuilder AddDockPanel<TView>(this MauiAppBuilder builder, string panelTypeId, string? displayName = null, string? icon = null)
        where TView : View
    {
        builder.Services.AddTransient<TView>();
        builder.Services.AddSingleton<IDockableContentFactory>(sp =>
            new ServiceProviderPanelFactory<TView>(panelTypeId, displayName, icon, sp));
        return builder;
    }

    sealed class ServiceProviderPanelFactory<TView> : IDockableContentFactory where TView : View
    {
        readonly IServiceProvider sp;

        public ServiceProviderPanelFactory(string panelTypeId, string? displayName, string? icon, IServiceProvider sp)
        {
            PanelTypeId = panelTypeId;
            DisplayName = displayName ?? panelTypeId;
            Icon = icon;
            this.sp = sp;
        }

        public string PanelTypeId { get; }
        public string DisplayName { get; }
        public string? Icon { get; }

        public Task<View> CreateAsync(string instanceId, CancellationToken ct = default)
            => Task.FromResult<View>(sp.GetRequiredService<TView>());
    }
}

/// <summary>
/// Claims the configured global hotkey at startup.
/// </summary>
/// <remarks>
/// <see cref="IMauiInitializeService"/> runs as the MauiApp is built, on every host including the
/// AppKit and GTK4 heads that ship their own handler types and so never run a mapper hook this could
/// otherwise key off. Claiming a hotkey needs nothing from the visual tree, so this is the earliest
/// safe point.
/// </remarks>
sealed class DesktopQuickEntryInitializer : IMauiInitializeService
{
    public void Initialize(IServiceProvider services)
    {
        var options = services.GetRequiredService<QuickEntryOptions>();
        if (String.IsNullOrWhiteSpace(options.HotKey))
            return;

        var hotKeys = services.GetRequiredService<IGlobalHotKeyService>();
        var quickEntry = services.GetRequiredService<IQuickEntryService>();

        if (hotKeys.Register(options.HotKey, quickEntry.Toggle) == null)
        {
            services
                .GetService<ILogger<DesktopQuickEntryInitializer>>()?
                .LogWarning(
                    "The quick entry hotkey '{HotKey}' could not be registered. The popup can still be opened from a tray icon or IQuickEntryService.Show().",
                    options.HotKey
                );
        }
    }
}

sealed class UnsupportedGlobalHotKeyService : IGlobalHotKeyService
{
    public bool IsSupported => false;

    public IDisposable? Register(string accelerator, Action pressed) => null;
}

sealed class UnsupportedTrayIconFactory : ITrayIconFactory
{
    public ITrayIcon Create()
        => throw new PlatformNotSupportedException("System tray icons are not supported on this platform.");
}
