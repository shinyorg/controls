using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Shiny.Maui.Controls.Cells;
using Shiny.Maui.Controls.Toast;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// Cover for the trim-safety pass (issue #33): the library's own bindings moved from string paths to
/// source-generated typed bindings, and consumer-supplied member paths moved behind one helper. These
/// pin the behaviour that a typed binding could plausibly have changed.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class TrimSafeBindingTests
{
    public TrimSafeBindingTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
    }

    [Fact]
    public void SetCustomToasterRegistersTheCustomType()
    {
        // It used to register the built-in Toaster whatever T was, so a custom toaster never ran.
        var services = new ServiceCollection();
        new ShinyControlConfiguration(services).SetCustomToaster<StubToaster>();

        services.Single(x => x.ServiceType == typeof(IToaster)).ImplementationType.ShouldBe(typeof(StubToaster));
    }

    [Fact]
    public void DefaultGroupHeaderStillShowsTheKeyText()
    {
        // The self binding is now typed object -> object onto a string property; it must still
        // convert the key to its text the way the old "." binding did.
        var grid = new VirtualizedGrid.VirtualizedGrid();
        var header = grid.CreateGroupHeaderView(42).ShouldBeOfType<Label>();

        header.Text.ShouldBe("42");
    }

    [Fact]
    public void PickerDisplayMemberReadsTheNamedProperty()
    {
        var cell = new PickerCell { DisplayMember = nameof(Fruit.Name), SubDisplayMember = nameof(Fruit.Colour) };
        var item = new Fruit("Apple", "Red");

        cell.GetDisplayText(item).ShouldBe("Apple");
        cell.GetSubDisplayText(item).ShouldBe("Red");
    }

    [Fact]
    public void PickerDisplayMemberThatDoesNotExistFallsBack()
    {
        var cell = new PickerCell { DisplayMember = "Nope", SubDisplayMember = "Nope" };
        var item = new Fruit("Apple", "Red");

        cell.GetDisplayText(item).ShouldBe(item.ToString());
        cell.GetSubDisplayText(item).ShouldBe(string.Empty);
    }

    public record Fruit(string Name, string Colour);

    sealed class StubToaster : IToaster
    {
        public Task<IDisposable> ShowAsync(string text) => throw new NotSupportedException();
        public Task<IDisposable> ShowAsync(string text, Action<ToastConfig> configure) => throw new NotSupportedException();
        public Task<IDisposable> InfoAsync(string text, Action<ToastConfig>? configure = null) => throw new NotSupportedException();
        public Task<IDisposable> SuccessAsync(string text, Action<ToastConfig>? configure = null) => throw new NotSupportedException();
        public Task<IDisposable> WarningAsync(string text, Action<ToastConfig>? configure = null) => throw new NotSupportedException();
        public Task<IDisposable> DangerAsync(string text, Action<ToastConfig>? configure = null) => throw new NotSupportedException();
        public Task<IDisposable> CriticalAsync(string text, Action<ToastConfig>? configure = null) => throw new NotSupportedException();
        public Task<SnackbarResult> SnackbarAsync(string text, string? actionText = null, Action<SnackbarConfig>? configure = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ILoadingSnackbar> LoadingSnackbarAsync(string text, Action<SnackbarConfig>? configure = null) => throw new NotSupportedException();
    }
}
