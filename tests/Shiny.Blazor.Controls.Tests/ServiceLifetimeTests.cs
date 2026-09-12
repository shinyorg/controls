using Microsoft.Extensions.DependencyInjection;
using Shiny.Blazor.Controls.Dialogs;
using Shiny.Blazor.Controls.Docking;
using Shiny.Blazor.Controls.OnScreenKeyboard;
using Shiny.Blazor.Controls.Splash;
using Shiny.Blazor.Controls.Toast;
using Shouldly;
using Xunit;

using Shiny.Blazor.Controls.Theming;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// Under WebAssembly scoped and singleton behave identically, so a control service registered as a
/// singleton looks perfectly fine right up until the app runs on Blazor Server — where one user's
/// toast, dialog or keyboard appears on every connected user's screen. Nothing catches that at
/// build time and no WASM test can reproduce it, so the lifetimes are asserted directly.
/// </summary>
public class ServiceLifetimeTests
{
    public static TheoryData<Type> PerUserServices =>
    [
        typeof(IToastService), typeof(ToastService),
        typeof(IDialogService), typeof(DialogService), typeof(DialogOptions),
        typeof(IOnScreenKeyboardService), typeof(OnScreenKeyboardService), typeof(OnScreenKeyboardOptions),
        typeof(ISplashScreen),
        typeof(IWalkthroughStore),
        typeof(IShinyThemeService), typeof(ShinyThemeService)
    ];

    [Theory]
    [MemberData(nameof(PerUserServices))]
    public void PerUserStateIsScoped(Type serviceType)
        => Registrations().Single(x => x.ServiceType == serviceType)
            .Lifetime.ShouldBe(ServiceLifetime.Scoped);

    /// <summary>
    /// The one deliberate singleton: an immutable lookup over the registered panel factories, with
    /// no per-user state in it. Pinned so "fix the inconsistency" does not quietly make it scoped.
    /// </summary>
    [Fact]
    public void TheDockRegistryStaysSingleton()
        => Registrations().Single(x => x.ServiceType == typeof(DockableContentRegistry))
            .Lifetime.ShouldBe(ServiceLifetime.Singleton);

    [Fact]
    public void TheUmbrellaAndTheIndividualCallsCompose()
    {
        // Either order, and both together, must leave exactly one registration each — otherwise the
        // second call quietly wins and a SetCustom* replacement gets undone.
        var services = new ServiceCollection();
        services.AddShinyToast();
        services.AddShinyControls();
        services.AddShinyOnScreenKeyboard();

        foreach (var type in new[] { typeof(IToastService), typeof(IDialogService), typeof(IOnScreenKeyboardService) })
            services.Count(x => x.ServiceType == type).ShouldBe(1);
    }

    [Fact]
    public void ARegistrationMadeFirstWins()
    {
        var services = new ServiceCollection();
        services.AddScoped<IToastService, FakeToastService>();
        services.AddShinyControls();

        var descriptor = services.Single(x => x.ServiceType == typeof(IToastService));
        descriptor.ImplementationType.ShouldBe(typeof(FakeToastService));
    }

    [Fact]
    public void SetCustomReplacesTheDefault()
    {
        var services = new ServiceCollection();
        services.AddShinyControls(cfg => cfg.SetCustomToaster<FakeToastService>());

        var descriptor = services.Single(x => x.ServiceType == typeof(IToastService));
        descriptor.ImplementationType.ShouldBe(typeof(FakeToastService));
    }

    /// <summary>
    /// The options object is reachable from DI and documented as live, so each scope has to get its
    /// own — otherwise one user turning the keyboard's auto-show off turns it off for everyone.
    /// </summary>
    [Fact]
    public void EachScopeGetsItsOwnKeyboardOptions()
    {
        var provider = new ServiceCollection()
            .AddShinyControls(cfg => cfg.ConfigureKeyboard(x => x.HeightPx = 320))
            .BuildServiceProvider();

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var a = first.ServiceProvider.GetRequiredService<OnScreenKeyboardOptions>();
        var b = second.ServiceProvider.GetRequiredService<OnScreenKeyboardOptions>();

        a.HeightPx.ShouldBe(320);
        b.HeightPx.ShouldBe(320);
        a.ShouldNotBeSameAs(b);

        a.HeightPx = 200;
        b.HeightPx.ShouldBe(320);
    }

    [Fact]
    public void EachScopeGetsItsOwnKeyboardVisibility()
    {
        var provider = new ServiceCollection().AddShinyControls().BuildServiceProvider();

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        first.ServiceProvider.GetRequiredService<IOnScreenKeyboardService>().Show();

        second.ServiceProvider.GetRequiredService<IOnScreenKeyboardService>().IsVisible.ShouldBeFalse();
    }

    static IServiceCollection Registrations()
    {
        var services = new ServiceCollection();
        services.AddShinyControls();
        return services;
    }

    sealed class FakeToastService : IToastService
    {
        public event Action? OnChanged { add { } remove { } }
        public IReadOnlyList<ToastEntry> ActiveToasts => [];

        public Task<IDisposable> ShowAsync(string text, Action<ToastConfig>? configure = null) => throw new NotSupportedException();
        public Task<IDisposable> InfoAsync(string text, Action<ToastConfig>? configure = null) => throw new NotSupportedException();
        public Task<IDisposable> SuccessAsync(string text, Action<ToastConfig>? configure = null) => throw new NotSupportedException();
        public Task<IDisposable> WarningAsync(string text, Action<ToastConfig>? configure = null) => throw new NotSupportedException();
        public Task<IDisposable> DangerAsync(string text, Action<ToastConfig>? configure = null) => throw new NotSupportedException();
        public Task<IDisposable> CriticalAsync(string text, Action<ToastConfig>? configure = null) => throw new NotSupportedException();
    }
}
