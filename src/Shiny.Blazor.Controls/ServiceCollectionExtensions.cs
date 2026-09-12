using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shiny.Blazor.Controls.Theming;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shiny.Blazor.Controls.Captchas;
using Shiny.Blazor.Controls.Dialogs;
using Shiny.Blazor.Controls.Docking;
using Shiny.Blazor.Controls.FileDrop;
using Shiny.Blazor.Controls.Images;
using Shiny.Blazor.Controls.OnScreenKeyboard;
using Shiny.Blazor.Controls.QuickEntry;
using Shiny.Blazor.Controls.Splash;
using Shiny.Blazor.Controls.Toast;

namespace Shiny.Blazor.Controls;

public static class ShinyControlsServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything the host components need — Toast, the progress line, Dialogs, the splash
    /// screen, the walkthrough store, docking, file drop and the on-screen keyboard — in one call, mirroring
    /// MAUI's <c>UseShinyControls</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Most controls need no registration at all: a component plus its scoped CSS and its JS module
    /// works from the package reference alone. This exists for the handful that are driven by a
    /// service, where the failure mode was placing <c>&lt;ToastHost /&gt;</c> in a layout, forgetting
    /// the matching <c>AddShinyToast()</c>, and getting a DI resolution exception at render time
    /// instead of a working toast.
    /// </para>
    /// <para>
    /// Every individual <c>AddShiny*</c> call still exists and every registration here is a
    /// <c>TryAdd</c>, so the two compose in either order and an app that wants to keep its WASM
    /// payload tight can keep registering à la carte. To replace an implementation, either use the
    /// <c>SetCustom*</c> methods on <see cref="ShinyControlConfiguration"/> or register your own
    /// before calling this — first registration wins.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddShinyControls(
        this IServiceCollection services,
        Action<ShinyControlConfiguration>? configure = null
    )
    {
        // Configure first, exactly as MAUI does: SetCustom* registers outright, and the TryAdds below
        // then step aside for it.
        var cfg = new ShinyControlConfiguration(services);
        configure?.Invoke(cfg);

        services.AddShinyTheme();
        services.AddShinyToast();
        services.AddShinyProgressLine();
        services.AddShinyDialogs(cfg.DialogConfigure);
        services.AddShinySplashScreen();
        services.AddShinyWalkthrough();
        services.AddShinyDocking();
        services.AddShinyOnScreenKeyboard(cfg.KeyboardConfigure);
        services.AddShinyQuickEntry(cfg.QuickEntryConfigure);
        services.AddShinyFileDrop(cfg.FileDropConfigure);

        return services;
    }
}


/// <summary>
/// The configuration surface for <see cref="ShinyControlsServiceCollectionExtensions.AddShinyControls"/>.
/// Deliberately shaped like MAUI's <c>ShinyControlConfiguration</c> so the two hosts read the same.
/// </summary>
public class ShinyControlConfiguration(IServiceCollection services)
{
    internal Action<DialogOptions>? DialogConfigure { get; private set; }
    internal Action<OnScreenKeyboardOptions>? KeyboardConfigure { get; private set; }
    internal Action<QuickEntryOptions>? QuickEntryConfigure { get; private set; }
    internal Action<FileDropOptions>? FileDropConfigure { get; private set; }

    /// <summary>
    /// App-wide dialog defaults — the default animation, and a <see cref="DialogOptions.ConfigureDefaults"/>
    /// hook that runs against every dialog's config before the per-call one.
    /// </summary>
    public ShinyControlConfiguration ConfigureDialogs(Action<DialogOptions> configure)
    {
        this.DialogConfigure = configure;
        return this;
    }

    /// <summary>
    /// Quick entry defaults — placement, sizing, dismissal, and the screen-edge glow's trigger and
    /// appearance. The popup is registered whether or not this is called; this only changes its
    /// settings.
    /// </summary>
    public ShinyControlConfiguration ConfigureQuickEntry(Action<QuickEntryOptions> configure)
    {
        this.QuickEntryConfigure = configure;
        return this;
    }

    /// <summary>
    /// File drop filters — which extensions to accept, size and count ceilings. The service is
    /// registered whether or not this is called; this only changes its settings.
    /// </summary>
    public ShinyControlConfiguration ConfigureFileDrop(Action<FileDropOptions> configure)
    {
        this.FileDropConfigure = configure;
        return this;
    }

    /// <summary>
    /// On-screen keyboard defaults — auto-show/hide policy, height, push-vs-overlay, theme and
    /// autorepeat timing.
    /// </summary>
    public ShinyControlConfiguration ConfigureKeyboard(Action<OnScreenKeyboardOptions> configure)
    {
        this.KeyboardConfigure = configure;
        return this;
    }

    /// <summary>Replace the default <see cref="IDialogService"/>.</summary>
    public ShinyControlConfiguration SetCustomDialogs<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class, IDialogService
    {
        services.AddScoped<IDialogService, T>();
        return this;
    }

    /// <summary>Replace the default <see cref="IToastService"/>.</summary>
    public ShinyControlConfiguration SetCustomToaster<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class, IToastService
    {
        services.AddScoped<IToastService, T>();
        return this;
    }

    /// <summary>Replace the default <see cref="IOnScreenKeyboardService"/>.</summary>
    public ShinyControlConfiguration SetCustomOnScreenKeyboard<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class, IOnScreenKeyboardService
    {
        services.AddScoped<IOnScreenKeyboardService, T>();
        return this;
    }

    /// <summary>
    /// Keep "has this user seen the tour" somewhere other than <c>localStorage</c> — a server
    /// profile, a synced settings store.
    /// </summary>
    public ShinyControlConfiguration SetCustomWalkthroughStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class, IWalkthroughStore
    {
        services.AddScoped<IWalkthroughStore, T>();
        return this;
    }

    /// <summary>
    /// Route <see cref="ShinyImage"/> downloads through the registered <see cref="HttpClient"/>, so a
    /// base address or an auth handler applies to images too.
    /// </summary>
    /// <remarks>
    /// Off by default and genuinely optional: <see cref="ShinyImage"/> streams through <c>fetch</c>
    /// with nothing registered. This is for images that have to come through C#, which in practice
    /// means authenticated ones.
    /// </remarks>
    public ShinyControlConfiguration UseHttpImageDownloader()
    {
        services.AddShinyImages();
        return this;
    }

    /// <summary>
    /// Register captcha providers and defaults — the local challenge, reCAPTCHA, hCaptcha,
    /// Turnstile, or your own.
    /// </summary>
    /// <remarks>
    /// Optional: <c>&lt;Captcha /&gt;</c> renders the local challenge with nothing registered. This is
    /// for picking a hosted provider, setting its site key, or changing the app-wide defaults.
    /// </remarks>
    public ShinyControlConfiguration ConfigureCaptcha(Action<CaptchaConfiguration> configure)
    {
        services.AddShinyCaptcha(configure);
        return this;
    }


    /// <summary>
    /// Replace how <see cref="PasswordStrength"/> scores passwords — zxcvbn, a Have I Been Pwned
    /// range query, or your server's own policy endpoint.
    /// </summary>
    /// <remarks>
    /// Entirely optional: with nothing registered the component uses
    /// <see cref="DefaultPasswordStrengthEvaluator"/>, which needs no network and no data files.
    /// Read <see cref="IPasswordStrengthEvaluator"/>'s remarks before writing one that goes to the
    /// wire — the password itself must never leave the browser.
    /// </remarks>
    public ShinyControlConfiguration SetCustomPasswordStrengthEvaluator<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, IPasswordStrengthEvaluator
    {
        services.AddScoped<IPasswordStrengthEvaluator, T>();
        return this;
    }


    /// <summary>Route <see cref="ShinyImage"/> downloads through your own <see cref="IImageDownloader"/>.</summary>
    public ShinyControlConfiguration SetCustomImageDownloader<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class, IImageDownloader
    {
        services.AddShinyImages<T>();
        return this;
    }

    /// <summary>
    /// Register a Razor component as a dock panel. Docking itself is registered by
    /// <c>AddShinyControls</c>; the panels can only come from the app.
    /// </summary>
    public ShinyControlConfiguration AddDockPanel<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>(
        string panelTypeId,
        string? displayName = null,
        string? icon = null
    )
        where TComponent : ComponentBase
    {
        services.AddDockPanel<TComponent>(panelTypeId, displayName, icon);
        return this;
    }
}
