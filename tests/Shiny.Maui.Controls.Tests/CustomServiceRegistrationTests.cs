using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using Shiny.Maui.Controls.Toast;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

// SetCustomToaster<T>() used to register the built-in Toaster whatever T was (#29)
[Collection(ApplicationResourcesCollection.Name)]
public class CustomServiceRegistrationTests
{
    [Fact]
    public void SetCustomToaster_registers_the_custom_toaster()
    {
        using var _ = ThemeManagerState.Preserve();
        var builder = MauiApp.CreateBuilder();
        builder.UseShinyControls(c => c.SetCustomToaster<FakeToaster>());

        using var services = builder.Services.BuildServiceProvider();

        services.GetService<IToaster>().ShouldBeOfType<FakeToaster>();
        builder.Services.Count(x => x.ServiceType == typeof(IToaster)).ShouldBe(1);
    }

    [Fact]
    public void Default_toaster_is_registered_without_a_custom_one()
    {
        using var _ = ThemeManagerState.Preserve();
        var builder = MauiApp.CreateBuilder();
        builder.UseShinyControls();

        builder.Services.Single(x => x.ServiceType == typeof(IToaster)).ImplementationType.ShouldBe(typeof(Toaster));
    }

    sealed class FakeToaster : IToaster
    {
        public Task<IDisposable> ShowAsync(string text) => Done();
        public Task<IDisposable> ShowAsync(string text, Action<ToastConfig> configure) => Done();
        public Task<IDisposable> InfoAsync(string text, Action<ToastConfig>? configure = null) => Done();
        public Task<IDisposable> SuccessAsync(string text, Action<ToastConfig>? configure = null) => Done();
        public Task<IDisposable> WarningAsync(string text, Action<ToastConfig>? configure = null) => Done();
        public Task<IDisposable> DangerAsync(string text, Action<ToastConfig>? configure = null) => Done();
        public Task<IDisposable> CriticalAsync(string text, Action<ToastConfig>? configure = null) => Done();
        public Task<SnackbarResult> SnackbarAsync(string text, string? actionText = null, Action<SnackbarConfig>? configure = null, CancellationToken cancellationToken = default) => Task.FromResult(SnackbarResult.Dismissed);
        public Task<ILoadingSnackbar> LoadingSnackbarAsync(string text, Action<SnackbarConfig>? configure = null) => throw new NotSupportedException();

        static Task<IDisposable> Done() => Task.FromResult<IDisposable>(new Disposable());

        sealed class Disposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
