using Microsoft.Maui.Handlers;
using Shiny.Maui.Controls.Themes;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

public class ThemeHandlerHookTests
{
    /// <summary>
    /// An application handler shaped like the maui-labs AppKit and GTK ones: its own mapper, chained to
    /// <see cref="ElementHandler.ElementMapper"/> rather than to MAUI's <c>ApplicationHandler.Mapper</c>.
    /// </summary>
    sealed class BackendApplicationHandler() : ElementHandler<IApplication, object>(BackendMapper)
    {
        static readonly IPropertyMapper<IApplication, BackendApplicationHandler> BackendMapper =
            new PropertyMapper<IApplication, BackendApplicationHandler>(ElementMapper);

        protected override object CreatePlatformElement() => new();
    }

    [Fact]
    public void ABackendWithItsOwnApplicationHandlerStillGetsTheTheme()
    {
        var current = typeof(ShinyThemeManager).GetProperty(nameof(ShinyThemeManager.CurrentTheme))!;
        var previous = current.GetValue(null);

        // A fresh pack, so the manager's cached dictionary from another test cannot match it.
        var theme = new BasicTheme();
        current.SetValue(null, theme);

        try
        {
            ShinyThemeManager.HookHandlers();
            var app = new Application { UserAppTheme = AppTheme.Light };
            app.Resources.MergedDictionaries.ShouldNotContain(theme.Light);

            new BackendApplicationHandler().SetVirtualView(app);

            app.Resources.MergedDictionaries.ShouldContain(theme.Light);
        }
        finally
        {
            current.SetValue(null, previous);
        }
    }
}
