using Microsoft.Maui.LifecycleEvents;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Handlers;
using Shiny.Maui.Controls;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Dialogs;
using Shiny.Maui.Controls.Flyout;
using Shiny.Maui.Controls.Images;
using Shiny.Maui.Controls.Images.Svg;
using Shiny.Maui.Controls.QuickEntry;
using Shiny.Maui.Controls.Themes;
using Shiny.Maui.Controls.Toast;
#if ANDROID || IOS || MACCATALYST || WINDOWS
using Shiny.Maui.Controls.CarouselGallery;
using Shiny.Maui.Controls.StaggeredGrid;
using Shiny.Maui.Controls.VirtualizedGrid;
#endif

namespace Shiny;

public static class ControlsMauiAppBuilderExtensions
{
    public static MauiAppBuilder UseShinyControls(
        this MauiAppBuilder builder, 
        Action<ShinyControlConfiguration>? configure = null
    )
    {
        var cfg = new ShinyControlConfiguration(builder.Services);
        configure?.Invoke(cfg);

        // Always have a theme so token resources resolve; default to Basic if none chosen.
        if (ShinyThemeManager.CurrentTheme is null)
            cfg.UseBasicTheme();

        builder.Services.TryAddSingleton<IFeedbackService, HapticFeedbackService>();

        // Quick entry is always registered, in its in-app form. The Desktop add-on adds a second
        // presenter for the native-window form and the service picks between them at open time, so
        // an app that ships to phones and desktops configures this once and nothing else.
        builder.Services.TryAddSingleton(cfg.QuickEntryOptions);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IQuickEntryPresenter, InAppQuickEntryPresenter>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IScreenGlowPresenter, InAppScreenGlowPresenter>());
        builder.Services.TryAddSingleton<QuickEntryService>();
        builder.Services.TryAddSingleton<IQuickEntryService>(sp => sp.GetRequiredService<QuickEntryService>());
        builder.Services.TryAddSingleton<IToaster, Toaster>();

        // The page-edge progress line. Singleton because the line is one piece of window chrome that
        // follows navigation, not per-page state - two instances would mean two lines stacked on the
        // same edge the moment a second operation started.
        builder.Services.TryAddSingleton<IProgressLineService, ProgressLineService>();

        // Stateless: the particles live on each page's canvas, not in the service.
        builder.Services.TryAddSingleton<IConfettiService, ConfettiService>();

        // Lets a view model drive whatever flyout is on the page that is showing, without the page
        // having to hand its FlyoutView over. The registry it reads is weak, so this holds nothing alive.
        builder.Services.TryAddSingleton<IFlyoutService, FlyoutService>();
        builder.Services.TryAddSingleton(cfg.DialogOptions);
        builder.Services.TryAddSingleton<IDialogService, DialogService>();

        // Keyboard shortcuts run on one static engine (XAML-declared shortcuts have no route to DI);
        // the service is a facade over it. The initializer gets app-wide shortcuts registered during
        // startup a key source once the first window exists.
        builder.Services.TryAddSingleton<IKeyboardShortcutService, KeyboardShortcutService>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IMauiInitializeService, KeyboardShortcutInitializer>());

        // ShinyImage's stack. The downloader is registered separately from the service so an app can
        // swap in its own HttpClient (auth headers, pinning) without also taking over caching,
        // queueing and de-duplication - which is what almost every "custom image loading" need
        // actually is.
        builder.Services.TryAddSingleton(cfg.ImageOptions);
        builder.Services.TryAddSingleton<IImageDownloader>(sp => new HttpImageDownloader(
            sp.GetRequiredService<ImageOptions>(),
            sp.GetService<HttpClient>(),
            sp.GetService<TimeProvider>()
        ));
        builder.Services.TryAddSingleton<IImageService>(sp => new ImageService(
            sp.GetRequiredService<ImageOptions>(),
            sp.GetRequiredService<IImageDownloader>(),
            sp.GetService<TimeProvider>()
        ));

        // Parsed SVG documents are immutable and shared, so one cache for the whole app is right:
        // an icon used on six screens is parsed once no matter which of them is showing.
        builder.Services.TryAddSingleton(sp => new SvgCache(sp.GetRequiredService<ImageOptions>().SvgCacheEntryLimit));

        // Application.Current is not available during builder configuration, so the theme is applied
        // when the app or its first window gets a handler - the earliest point it exists, and before
        // any page realizes its visual tree.
        ShinyThemeManager.HookHandlers();

        // Every page gets its overlay root before it renders, so the first toast, dialog or confetti
        // burst on a page finds it in place instead of moving the page's content - see PageOverlay.
        if (cfg.InstallPageOverlayBeforeRender)
            PageOverlay.WrapBeforeRender();

#if ANDROID || IOS || MACCATALYST || WINDOWS
        builder.ConfigureMauiHandlers(handlers =>
        {
            handlers.AddHandler<Shiny.Maui.Controls.CarouselGallery.CarouselGallery, CarouselGalleryHandler>();
            handlers.AddHandler<Shiny.Maui.Controls.StaggeredGrid.StaggeredGrid, StaggeredGridHandler>();
            handlers.AddHandler<Shiny.Maui.Controls.VirtualizedGrid.VirtualizedGrid, VirtualizedGridHandler>();
        });
#endif

#if ANDROID || IOS || WINDOWS
        // Button semantics for the controls that are buttons drawn from a tap gesture - see ITapTarget.
        // Not on the net10.0 build, whose AppKit/GTK4 heads bring their own content-view handler.
        builder.ConfigureMauiHandlers(handlers =>
        {
            handlers.AddHandler<ShinyButton, TapTargetHandler>();
            handlers.AddHandler<Fab, TapTargetHandler>();
            handlers.AddHandler<FabMenuItem, TapTargetHandler>();
            handlers.AddHandler<IconTextTool, TapTargetHandler>();
            handlers.AddHandler<ChipView, TapTargetHandler>();
        });
#endif

#if ANDROID
        // In-page overlays (ribbon dropdowns, the Office backstage and dialogs) take the back button
        // while they are open - see AndroidBackButton.
        builder.ConfigureLifecycleEvents(events => events.AddAndroid(android => android
            .OnBackPressed(_ => AndroidBackButton.HandleBack())
            // Hardware-keyboard shortcuts. The activity only sees keys the focused view declined.
            .OnKeyDown((activity, keyCode, e) => AndroidKeySource.OnKey(activity, keyCode, e, isUp: false))
            .OnKeyUp((activity, keyCode, e) => AndroidKeySource.OnKey(activity, keyCode, e, isUp: true))));
#endif

#if ANDROID
        // Material 3 (<UseMaterial3>true</UseMaterial3>) maps Entry and Editor to EntryHandler2 and
        // EditorHandler2: separate, *internal* handlers whose platform view is a TextInputLayout with
        // its own filled box, underline and floating hint. Their mappers are not EntryHandler.Mapper /
        // EditorHandler.Mapper, so none of the mappings below ran - the box came back inside TextEntry,
        // TagEntry, AutoCompleteEntry and every EntryCell, the autofill opt-out silently stopped
        // working, and the native floating hint fought TextEntry's own placeholder. These two are
        // internal parts of controls that draw their own chrome, so they keep the classic handlers
        // whatever the app opted into. An exact-type registration wins over MAUI's Entry/Editor one.
        builder.ConfigureMauiHandlers(handlers =>
        {
            handlers.AddHandler<BorderlessEntry, EntryHandler>();
            handlers.AddHandler<BorderlessEditor, EditorHandler>();
        });
#endif

        EntryHandler.Mapper.AppendToMapping("ShinyBorderless", (handler, view) =>
        {
            if (view is not BorderlessEntry)
                return;

#if ANDROID
            handler.PlatformView.Background = null;
#elif IOS || MACCATALYST
            handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
#elif WINDOWS
            // The Editor below always had this; the Entry did not, so every EntryCell on Windows drew
            // a full TextBox - border, fill and padding - inside its row.
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            handler.PlatformView.Background = null;
            handler.PlatformView.Padding = new Microsoft.UI.Xaml.Thickness(0);
            StripWindowsInteractionChrome(handler.PlatformView);
#endif
        });

        // Autofill / autocorrect / prediction opt-out. MAUI exposes spell-check and text-prediction but
        // nothing for autofill, which is the one that silently replaces a half-typed serial with a saved
        // address. The mapping is registered under several keys because the platform switches all live on
        // the same native state that MAUI rewrites when the keyboard or password mode changes - re-running
        // after those puts our flags back.
        foreach (var key in new[]
                 {
                     BorderlessEntry.AutoCompleteMapperKey,
                     nameof(InputView.Keyboard),
                     nameof(Entry.IsPassword),
                     nameof(Entry.IsTextPredictionEnabled),
                     nameof(InputView.IsSpellCheckEnabled)
                 })
        {
            EntryHandler.Mapper.AppendToMapping(key, (handler, view) =>
            {
                if (view is BorderlessEntry borderless)
                    ApplyAutoComplete(handler, borderless);
            });
        }

        // The multiline twin of the above. UITextView also carries a default text-container inset that
        // an Entry does not, so it is zeroed here - otherwise the editor's text sits several points in
        // from any single-line control sharing the same rounded container.
        EditorHandler.Mapper.AppendToMapping("ShinyBorderless", (handler, view) =>
        {
            if (view is not BorderlessEditor)
                return;

#if ANDROID
            handler.PlatformView.Background = null;
            handler.PlatformView.SetPadding(0, 0, 0, 0);
#elif IOS || MACCATALYST
            handler.PlatformView.BackgroundColor = UIKit.UIColor.Clear;
            handler.PlatformView.TextContainerInset = UIKit.UIEdgeInsets.Zero;
            handler.PlatformView.TextContainer.LineFragmentPadding = 0;
#elif WINDOWS
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            handler.PlatformView.Background = null;
            handler.PlatformView.Padding = new Microsoft.UI.Xaml.Thickness(0);
            StripWindowsInteractionChrome(handler.PlatformView);
#endif
        });

        return builder;
    }

#if WINDOWS
    // The local BorderThickness/Background above only cover the resting state. The TextBox template's
    // PointerOver, Focused and Disabled visual states set the border element's thickness, brush and
    // fill from theme resources, which beat the template-bound local values - so a borderless entry
    // grew the accent underline and a filled box the moment it was hovered or focused. Overriding
    // those resources on the control itself is WinUI's own "lightweight styling" route, and unlike
    // replacing the Style it leaves the template (and anything the app styles on TextBox) intact.
    static void StripWindowsInteractionChrome(Microsoft.UI.Xaml.Controls.TextBox textBox)
    {
        var clear = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var none = new Microsoft.UI.Xaml.Thickness(0);
        var resources = textBox.Resources;

        resources["TextControlBorderThemeThickness"] = none;
        resources["TextControlBorderThemeThicknessFocused"] = none;

        foreach (var key in WindowsChromeBrushKeys)
            resources[key] = clear;
    }

    static readonly string[] WindowsChromeBrushKeys =
    [
        "TextControlBackgroundPointerOver",
        "TextControlBackgroundFocused",
        "TextControlBackgroundDisabled",
        "TextControlBorderBrush",
        "TextControlBorderBrushPointerOver",
        "TextControlBorderBrushFocused",
        "TextControlBorderBrushDisabled"
    ];
#endif

    static void ApplyAutoComplete(IEntryHandler handler, BorderlessEntry entry)
    {
        var enabled = entry.IsAutoCompleteEnabled;
#if !ANDROID && !IOS && !MACCATALYST && !WINDOWS
        _ = enabled; // no soft-input assistance to switch off on this head
#endif
#if ANDROID
        var native = handler.PlatformView;

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            native.ImportantForAutofill = enabled
                ? Android.Views.ImportantForAutofill.Auto
                : Android.Views.ImportantForAutofill.NoExcludeDescendants;
        }

        // InputType is what carries the "no suggestions" flag, and assigning it resets the
        // transformation method - which is the password mask. Leave password fields alone (autofill
        // there is wanted anyway) and restore the transformation for everything else.
        if (entry.IsPassword)
            return;

        var target = enabled
            ? native.InputType & ~Android.Text.InputTypes.TextFlagNoSuggestions
            : native.InputType | Android.Text.InputTypes.TextFlagNoSuggestions;

        if (native.InputType != target)
        {
            var transformation = native.TransformationMethod;
            var selection = native.SelectionStart;
            native.InputType = target;
            native.TransformationMethod = transformation;
            if (selection >= 0 && selection <= (native.Text?.Length ?? 0))
                native.SetSelection(selection);
        }
#elif IOS || MACCATALYST
        var native = handler.PlatformView;
        native.AutocorrectionType = enabled ? UIKit.UITextAutocorrectionType.Default : UIKit.UITextAutocorrectionType.No;
        native.SpellCheckingType = enabled ? UIKit.UITextSpellCheckingType.Default : UIKit.UITextSpellCheckingType.No;

        // An empty content type is the documented opt-out from AutoFill and the strong-password sheet.
        //
        // Never assign null here. UITextField.TextContentType is bound WITHOUT [NullAllowed] (the type
        // carries NullableContext=1 and the property has no NullableAttribute, unlike Text/Placeholder),
        // so the setter takes the null straight into objc_msgSend and the app dies with EXC_BAD_ACCESS -
        // not a catchable managed exception. Since this mapping runs for every BorderlessEntry, a null on
        // the *enabled* path - the default - took down every page holding one: EntryCell, and therefore
        // every TableView with a text row. Clearing the property means writing nil, which only KVC can do.
        if (!enabled)
        {
            native.TextContentType = new Foundation.NSString(string.Empty);
        }
        else if (native.TextContentType is { Length: 0 })
        {
            // Only ours gets cleared - an empty content type is not something anything else sets, so a
            // real one assigned by the app or by MAUI is left alone.
            native.SetValueForKey(Foundation.NSNull.Null, new Foundation.NSString("textContentType"));
        }
#elif WINDOWS
        handler.PlatformView.IsSpellCheckEnabled = enabled && entry.IsSpellCheckEnabled;
#endif
    }
}

public class ShinyControlConfiguration(IServiceCollection services)
{
    /// <summary>
    /// Wrap every <see cref="ContentPage"/>'s content in the overlay root that toasts, dialogs,
    /// confetti and the other in-page overlays draw into, as the page's handler connects. On by default.
    /// </summary>
    /// <remarks>
    /// Without it, the first overlay on a page installs the root then, which moves the page's content
    /// while it is on screen: its native views are detached and laid out again and its bindings see a
    /// null context and back, so bound lists reload - a visible hitch on the first toast. The cost of
    /// leaving it on is that a rendered page's <c>Content</c> is that root (a <see cref="Grid"/>), not
    /// the view it was given. Turn it off if your code reads <c>page.Content</c> back and expects its
    /// own view; the overlays still work, they just install the root lazily as before.
    /// </remarks>
    public bool InstallPageOverlayBeforeRender { get; set; } = true;

    internal DialogOptions DialogOptions { get; } = new();

    internal ImageOptions ImageOptions { get; } = new();

    internal QuickEntryOptions QuickEntryOptions { get; } = new();

    /// <summary>
    /// Configure the quick entry popup and its screen-edge glow — placement, sizing, dismissal, and
    /// whether it opens as an in-app overlay or a native desktop window.
    /// </summary>
    /// <remarks>
    /// The popup is registered whether or not this is called; this only changes its settings.
    /// <see cref="QuickEntryOptions.Presentation"/> defaults to
    /// <see cref="QuickEntryPresentation.Auto"/>, so the same configuration gives a real OS window on
    /// desktop (with the <c>Shiny.Maui.Controls.Desktop</c> add-on registered) and an overlay on
    /// phones, with no platform check at the call site.
    /// </remarks>
    public ShinyControlConfiguration ConfigureQuickEntry(Action<QuickEntryOptions> configure)
    {
        configure(this.QuickEntryOptions);
        return this;
    }

    /// <summary>
    /// Configure <see cref="ShinyImage"/>'s loading: download concurrency, cache expiry, and the
    /// size ceilings for the memory and disk tiers.
    /// </summary>
    public ShinyControlConfiguration ConfigureImages(Action<ImageOptions> configure)
    {
        configure(this.ImageOptions);
        return this;
    }

    /// <summary>
    /// Replace how image bytes are fetched while keeping the built-in caching, download queue and
    /// request de-duplication. This is the hook for authenticated images - your implementation gets
    /// to build the request, so headers, cookies, a custom handler and certificate pinning are all
    /// yours.
    /// </summary>
    public ShinyControlConfiguration SetCustomImageDownloader<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class, IImageDownloader
    {
        services.AddSingleton<IImageDownloader, T>();
        return this;
    }

    /// <summary>
    /// Replace the whole image pipeline - caching included. Prefer
    /// <see cref="SetCustomImageDownloader{T}"/> unless you genuinely need your own cache.
    /// </summary>
    public ShinyControlConfiguration SetCustomImageService<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class, IImageService
    {
        services.AddSingleton<IImageService, T>();
        return this;
    }

    /// <summary>
    /// Configure global defaults for the <see cref="IDialogService"/> service — the default animation,
    /// app-wide styling (via <see cref="DialogOptions.ConfigureDefaults"/>), and an optional
    /// <see cref="DialogOptions.ContentTemplate"/> that fully replaces the default dialog card.
    /// </summary>
    public ShinyControlConfiguration ConfigureDialogs(Action<DialogOptions> configure)
    {
        configure(this.DialogOptions);
        return this;
    }

    /// <summary>
    /// Replace the default <see cref="IDialogService"/> implementation with your own.
    /// </summary>
    public ShinyControlConfiguration SetCustomDialogs<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class, IDialogService
    {
        services.AddSingleton<IDialogService, T>();
        return this;
    }

    /// <summary>
    /// Set a custom feedback service implementation. Note that the default implementation is designed to work with various controls, so if you use a custom implementation, you may need to ensure it integrates properly with the Blazor component or provides its own mechanism for providing feedback (e.g., haptic feedback, sound, etc.).
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public ShinyControlConfiguration SetCustomFeedback<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class, IFeedbackService
    {
        services.TryAddSingleton<IFeedbackService, T>();
        return this;
    }

    /// <summary>
    /// Set a custom toaster implementation. Note that the default implementation is designed to work with the Shiny Blazor Toast component, so if you use a custom implementation, you may need to ensure it integrates properly with the Blazor component or provides its own mechanism for displaying toasts.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public ShinyControlConfiguration SetCustomToaster<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class, IToaster
    {
        services.TryAddSingleton<IToaster, T>();
        return this;
    }


    /// <summary>
    /// Replace how <see cref="PasswordStrength"/> scores passwords — zxcvbn, a Have I Been Pwned
    /// range query, or your server's own policy endpoint.
    /// </summary>
    /// <remarks>
    /// Entirely optional: with nothing registered the control uses
    /// <see cref="DefaultPasswordStrengthEvaluator"/>, which needs no network and no data files.
    /// Read <see cref="IPasswordStrengthEvaluator"/>'s remarks before writing one that goes to the
    /// wire — the password itself must never leave the device.
    /// </remarks>
    public ShinyControlConfiguration SetCustomPasswordStrengthEvaluator<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, IPasswordStrengthEvaluator
    {
        services.AddSingleton<IPasswordStrengthEvaluator, T>();
        return this;
    }


    /// <summary>
    /// Integrate feedback for standard MAUI controls. Calls AddDefaults() to register all built-in hooks
    /// (Button, Entry, Slider, Switch, etc.) then applies any additional configuration.
    /// </summary>
    public ShinyControlConfiguration AddDefaultMauiControlFeedback(Action<MauiControlFeedbackBuilder>? configure = null)
    {
        var builder = new MauiControlFeedbackBuilder();
        builder.AddDefaults();
        configure?.Invoke(builder);

        services.AddSingleton<IReadOnlyList<IControlFeedbackHook>>(builder.Hooks);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMauiInitializeService, MauiControlFeedbackIntegrator>());
        return this;
    }

    /// <summary>
    /// Integrate feedback for standard MAUI controls with only the hooks you configure — no defaults.
    /// </summary>
    public ShinyControlConfiguration AddMauiControlFeedback(Action<MauiControlFeedbackBuilder> configure)
    {
        var builder = new MauiControlFeedbackBuilder();
        configure(builder);

        services.AddSingleton<IReadOnlyList<IControlFeedbackHook>>(builder.Hooks);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMauiInitializeService, MauiControlFeedbackIntegrator>());
        return this;
    }

    /// <summary>
    /// Disable all feedback (haptic, sound, etc.) for the controls. This will replace the default feedback service with a no-op implementation, effectively silencing any feedback that would normally be triggered by user interactions with the controls.
    /// </summary>
    /// <returns></returns>
    public ShinyControlConfiguration DisableFeedback()
    {
        services.AddSingleton<IFeedbackService, NoFeedbackService>();
        return this;
    }

    /// <summary>
    /// Apply a Shiny theme to the app. The theme's token resources are merged into the
    /// application and kept in sync with the OS light/dark appearance. Controls pick the colors
    /// up automatically. Call <see cref="ShinyThemeManager.SetTheme"/> to switch at runtime.
    /// </summary>
    public ShinyControlConfiguration UseTheme(IShinyTheme theme)
    {
        ShinyThemeManager.SetTheme(theme);
        return this;
    }

    /// <summary>Apply the built-in <see cref="BasicTheme"/> (the default).</summary>
    public ShinyControlConfiguration UseBasicTheme() => this.UseTheme(new BasicTheme());

    /// <summary>
    /// Make the controls fold-aware: a <see cref="Shiny.Maui.Controls.Flyout.FlyoutView"/> snaps its panel to a vertical fold,
    /// dialogs, overlays and toasts move off it, and a <see cref="Shiny.Maui.Controls.FloatingPanel.FloatingPanel"/> gains a
    /// detent on a tabletop fold. Without a source every control behaves exactly as it does on a phone.
    /// </summary>
    /// <remarks>
    /// The <c>Shiny.Maui.Controls.Foldables</c> package's <c>UseShinyFoldables()</c> registers the
    /// built-in Android (Jetpack WindowManager) and iOS 27.1 (iPhone Duo) sources for you; use this to
    /// supply your own — an adapter over Shiny.Core's <c>IScreenPosture</c>, or a simulated fold for
    /// testing on a device that does not fold. A source set here always wins over the package's
    /// built-in one, whichever is registered first. Pass null to turn fold handling off.
    /// </remarks>
    public ShinyControlConfiguration UseFoldPosture(IFoldPostureSource? source)
    {
        FoldPosture.SetSource(source);
        return this;
    }
}