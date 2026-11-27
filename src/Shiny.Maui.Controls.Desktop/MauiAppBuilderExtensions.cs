using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Shiny.Maui.Controls;
using Shiny.Maui.Controls.Desktop.Docking;
#if !ANDROID && !IOS && !MACCATALYST
using Shiny.Maui.Controls.Desktop.Notch;
#endif
using Shiny.Maui.Controls.Desktop.FileDrop;
using Shiny.Maui.Controls.Desktop.QuickEntry;
using Shiny.Maui.Controls.QuickEntry;
using Shiny.Maui.Controls.Desktop.TrayIcon;

namespace Shiny;

public static class DesktopMauiAppBuilderExtensions
{
    /// <summary>
    /// Adds the desktop features: the tray icon, global hot keys, keyboard shortcuts on macOS, Mac
    /// Catalyst and Linux, docking, and the notch window. Opt into the native quick entry window and
    /// window-level file drop through <paramref name="configure"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// builder
    ///     .UseShinyControls()
    ///     .UseShinyDesktop(cfg => cfg
    ///         .UseQuickEntryWindow()
    ///         .UseFileDrop(o => o.AllowedExtensions.Add(".pdf"))
    ///         .AddDockPanel&lt;ExplorerPanel&gt;("explorer", "Explorer"));
    /// </code>
    /// </example>
    /// <remarks>
    /// <para>
    /// One call because these were six, and forgetting one failed silently: keyboard shortcuts that
    /// never fired on macOS, a dock that loaded nothing. Everything registered by default is inert
    /// until the app uses it - a tray factory nobody asks for, a hot-key service with no keys, a
    /// docking registry with no panels - so there is no cost to having it. The two opt-ins are the
    /// features that change what the app does just by being registered: the quick entry window takes
    /// over from the in-app popup, and file drop makes the window accept drops.
    /// </para>
    /// <para>
    /// Safe to call unconditionally and more than once. On a platform without a feature (a phone has
    /// no tray, and Mac Catalyst no notch window) the service still resolves and reports itself
    /// unsupported, so shared code needs no <c>#if</c>.
    /// </para>
    /// </remarks>
    public static MauiAppBuilder UseShinyDesktop(this MauiAppBuilder builder, Action<ShinyDesktopConfiguration>? configure = null)
    {
        var cfg = new ShinyDesktopConfiguration(builder.Services);
        configure?.Invoke(cfg);

        AddTrayIcon(builder.Services);
        AddGlobalHotKeys(builder.Services);
        AttachKeyboardShortcuts();
        builder.Services.TryAddSingleton<DockableContentRegistry>();
#if !ANDROID && !IOS && !MACCATALYST
        builder.Services.TryAddSingleton(cfg.NotchOptions);
        builder.Services.TryAddSingleton<INotchWindowService>(sp => new DesktopNotchWindowService(
            sp.GetRequiredService<NotchWindowOptions>(),
            sp.GetService<ILogger<DesktopNotchWindowService>>()
        ));
        builder.Services.TryAddSingleton<INotchWindowFactory>(sp => new DesktopNotchWindowFactory(sp.GetRequiredService<NotchWindowOptions>(), sp));
#endif
        return builder;
    }


    static void AddTrayIcon(IServiceCollection services)
    {
#if WINDOWS
        services.TryAddSingleton<ITrayIconFactory, WindowsTrayIconFactory>();
#elif MACCATALYST
        services.TryAddSingleton<ITrayIconFactory, MacCatalystTrayIconFactory>();
#elif MACOS
        services.TryAddSingleton<ITrayIconFactory, MacTrayIconFactory>();
#elif ANDROID || IOS
        // Phones have no tray: the factory throws on use, so guard tray code with a platform check.
        services.TryAddSingleton<ITrayIconFactory, UnsupportedTrayIconFactory>();
#else
        // net11.0 fallback — Linux at runtime, otherwise unsupported.
        services.TryAddSingleton<ITrayIconFactory>(_ =>
        {
            if (OperatingSystem.IsLinux())
                return new LinuxTrayIconFactory();
            return new UnsupportedTrayIconFactory();
        });
#endif
    }


    static void AddGlobalHotKeys(IServiceCollection services)
    {
#if WINDOWS
        services.TryAddSingleton<IGlobalHotKeyService, WindowsGlobalHotKeyService>();
#elif MACCATALYST
        services.TryAddSingleton<IGlobalHotKeyService, CatalystGlobalHotKeyService>();
#elif MACOS
        services.TryAddSingleton<IGlobalHotKeyService, MacGlobalHotKeyService>();
#elif ANDROID || IOS
        // Phones have no global hotkeys.
        services.TryAddSingleton<IGlobalHotKeyService, UnsupportedGlobalHotKeyService>();
#else
        // net11.0 fallback — Linux at runtime, otherwise unsupported.
        services.TryAddSingleton<IGlobalHotKeyService>(sp =>
        {
            if (OperatingSystem.IsLinux())
                return new LinuxGlobalHotKeyService(sp.GetService<ILogger<LinuxGlobalHotKeyService>>());
            return new UnsupportedGlobalHotKeyService();
        });
#endif
    }


    /// <summary>
    /// The native key sources for keyboard shortcuts on macOS (AppKit), Linux (GTK4) and Mac Catalyst.
    /// Core listens for keys on Windows, Android and iOS itself; the AppKit and GTK4 heads ship their
    /// own window types, so their key sources live here with the rest of the desktop interop.
    /// </summary>
    static void AttachKeyboardShortcuts()
    {
#if MACOS || MACCATALYST
        Shiny.Maui.Controls.KeyboardShortcutManager.PlatformAttach = Shiny.Maui.Controls.Desktop.KeyboardShortcuts.KeyboardShortcutPlatform.Attach;
#elif !WINDOWS && !ANDROID && !IOS
        // net11.0 asset — Linux at runtime, otherwise nothing to attach.
        if (OperatingSystem.IsLinux())
            Shiny.Maui.Controls.KeyboardShortcutManager.PlatformAttach = Shiny.Maui.Controls.Desktop.KeyboardShortcuts.KeyboardShortcutPlatform.Attach;
#endif
    }
}


/// <summary>
/// The options for <see cref="DesktopMauiAppBuilderExtensions.UseShinyDesktop"/>.
/// </summary>
public class ShinyDesktopConfiguration(IServiceCollection services)
{
#if !ANDROID && !IOS && !MACCATALYST
    internal NotchWindowOptions NotchOptions { get; } = new();

    /// <summary>
    /// Configures the notch window - a <see cref="NotchView"/> at the top-centre of the screen, above
    /// every other application, that on a MacBook lives in the camera notch and grows out of it.
    /// </summary>
    /// <remarks>
    /// macOS AppKit, Windows and Linux (GTK4) only; it does not exist on Android, iOS or Mac Catalyst, so
    /// call it inside <c>#if !ANDROID &amp;&amp; !IOS &amp;&amp; !MACCATALYST</c>. The service itself is always
    /// registered on those platforms; this only changes its options.
    /// </remarks>
    public ShinyDesktopConfiguration ConfigureNotch(Action<NotchWindowOptions> configure)
    {
        configure(this.NotchOptions);
        return this;
    }
#endif


    /// <summary>
    /// Opens Shiny's quick entry popup as a borderless, always-on-top OS window over <em>other
    /// applications</em>, with the screen-edge glow drawn across the whole display, and claims
    /// <see cref="QuickEntryOptions.HotKey"/> system-wide at startup.
    /// </summary>
    /// <remarks>
    /// The popup, <c>PromptView</c> and the in-app presentation live in <c>Shiny.Maui.Controls</c> and
    /// work without this. Opt-in because it changes what the app does: with
    /// <see cref="QuickEntryPresentation.Auto"/> (the default) the core service picks this window over
    /// the in-app overlay. On Mac Catalyst, and anywhere this package's <c>net11.0</c> asset runs
    /// without being a desktop, the presenters report themselves unsupported and the overlay stays.
    /// </remarks>
    public ShinyDesktopConfiguration UseQuickEntryWindow()
    {
#if WINDOWS
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IScreenGlowPresenter, WindowsScreenGlow>(sp => new WindowsScreenGlow(sp.GetService<ILogger<WindowsScreenGlow>>())));
#else
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IScreenGlowPresenter, DesktopScreenGlowPresenter>(sp => new DesktopScreenGlowPresenter(sp.GetService<ILogger<DesktopScreenGlowPresenter>>())));
#endif
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IQuickEntryPresenter, DesktopQuickEntryPresenter>(sp => new DesktopQuickEntryPresenter(sp.GetService<ILogger<DesktopQuickEntryPresenter>>())));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMauiInitializeService, DesktopQuickEntryInitializer>());

        // The prompt's text field is a BorderlessEntry, polished by BorderlessEntryRegistration -
        // which this package's module initializer installs whether or not this is called.
        return this;
    }


    /// <summary>
    /// Makes the app window accept files dragged from Finder / Explorer / Files, including over a
    /// <c>BlazorWebView</c> or other hosted web content, and reports them through
    /// <see cref="IFileDropService"/>.
    /// </summary>
    /// <remarks>
    /// On a platform without window-level file drop (iOS, Android, and any host that reaches this
    /// package's <c>net11.0</c> asset without being Linux) the service still resolves,
    /// <see cref="IFileDropService.IsSupported"/> is false and nothing fires.
    /// </remarks>
    public ShinyDesktopConfiguration UseFileDrop(Action<FileDropOptions>? configure = null)
    {
        var options = new FileDropOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IFileDropService>(sp => new FileDropService(
            sp.GetRequiredService<FileDropOptions>(),
            sp.GetService<IFileDropDelegate>(),
            sp.GetService<ILogger<FileDropService>>()
        ));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMauiInitializeService, FileDropInitializer>());
        return this;
    }


    /// <summary>
    /// The same, plus an app-wide <see cref="IFileDropDelegate"/> that handles drops wherever they land
    /// and whatever page is showing. It runs before <see cref="IFileDropService.Dropped"/>, which it can
    /// suppress by setting <see cref="FileDropContext.Handled"/>.
    /// </summary>
    public ShinyDesktopConfiguration UseFileDrop<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TDelegate>(
        Action<FileDropOptions>? configure = null
    )
        where TDelegate : class, IFileDropDelegate
    {
        services.TryAddSingleton<IFileDropDelegate, TDelegate>();
        return this.UseFileDrop(configure);
    }


    /// <summary>
    /// Registers a panel type so dock layouts referencing <paramref name="panelTypeId"/> can resolve
    /// and instantiate the view. The view is resolved through DI per request.
    /// </summary>
    /// <typeparam name="TView">The panel content view type.</typeparam>
    /// <param name="panelTypeId">Stable string ID stored in persisted layouts.</param>
    public ShinyDesktopConfiguration AddDockPanel<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TView>(string panelTypeId, string? displayName = null, string? icon = null)
        where TView : View
    {
        services.AddTransient<TView>();
        services.AddSingleton<IDockableContentFactory>(sp =>
            new ServiceProviderPanelFactory<TView>(panelTypeId, displayName, icon, sp));
        return this;
    }


    sealed class ServiceProviderPanelFactory<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TView> : IDockableContentFactory where TView : View
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
