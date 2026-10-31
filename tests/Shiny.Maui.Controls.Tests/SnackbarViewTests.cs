using Microsoft.Maui.Controls;
using Shiny.Maui.Controls.Themes;
using Shiny.Maui.Controls.Toast;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// What a snackbar adds to the toast view: the action button and its separate target, the loading
/// line, the reason it closed with, and the hover pause.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class SnackbarViewTests
{
    readonly Application app;
    readonly BasicTheme theme = new();

    public SnackbarViewTests()
    {
        TestDispatcherProvider.Install();
        this.app = new Application { UserAppTheme = AppTheme.Light };
        this.app.Resources.MergedDictionaries.Add(this.theme.Light);
    }

    ToastView Host(ToastConfig config)
    {
        var view = new ToastView(config);
        new ContentPage { Content = view }.Parent = this.app;
        return view;
    }


    [Fact]
    public void SnackbarDefaults()
    {
        var config = new SnackbarConfig();

        config.DisplayMode.ShouldBe(ToastDisplayMode.Snackbar);
        config.QueueMode.ShouldBe(ToastQueueMode.Replace);
        config.TextOverflow.ShouldBe(ToastTextOverflow.MultiLine);
        config.Duration.ShouldBe(SnackbarConfig.DefaultDuration);
        config.PauseOnHover.ShouldBeTrue();
        new ToastConfig().PauseOnHover.ShouldBeFalse();
    }


    [Fact]
    public void NoActionTextMeansNoButton()
        => Host(new SnackbarConfig { Text = "Saved" }).ActionButton.ShouldBeNull();


    [Fact]
    public void TheActionButtonFollowsTheThemeAndIsNamedForTests()
    {
        var view = Host(new SnackbarConfig { Text = "Deleted", ActionText = "Undo", AutomationId = "Snack" });

        view.ActionButton.ShouldNotBeNull();
        view.ActionButton.Text.ShouldBe("Undo");
        view.ActionButton.TextColor.ShouldBe((Color)this.theme.Light[ShinyThemeKeys.Color.InversePrimary]);
        view.ActionButton.AutomationId.ShouldBe("Snack" + SnackbarConfig.ActionAutomationIdSuffix);
        view.Chrome.MaximumWidthRequest.ShouldBe(600);
    }


    [Fact]
    public void TheBodyTapIsNotOnTheBorderWhenThereIsAnAction()
    {
        // On the border it would sit over the button too, and a tap on the button could run both.
        var view = Host(new SnackbarConfig { Text = "Deleted", ActionText = "Undo" });

        view.Chrome.GestureRecognizers.OfType<TapGestureRecognizer>().ShouldBeEmpty();
    }


    [Fact]
    public void PressingTheActionRunsItsCommandNotTheBodysAndClosesAsActionInvoked()
    {
        var action = 0;
        var tap = 0;
        var view = Host(new SnackbarConfig
        {
            Text = "Deleted",
            ActionText = "Undo",
            ActionCommand = new Command(() => action++),
            TapCommand = new Command(() => tap++)
        });

        ((IButtonController)view.ActionButton!).SendClicked();

        // Headless there is no animation ticker, so the slide-out never finishes; the reason is
        // recorded when it starts.
        view.ClosingReason.ShouldBe(SnackbarResult.ActionInvoked);
        action.ShouldBe(1);
        tap.ShouldBe(0);
    }


    [Fact]
    public void TheLoadingLineIsIndeterminateUntilItGetsAValue()
    {
        var view = Host(new SnackbarConfig { Text = "Uploading", IsLoading = true, Duration = TimeSpan.Zero });

        view.LoadingBar.ShouldNotBeNull();
        view.LoadingBar.IsIndeterminate.ShouldBeTrue();

        view.UpdateProgress(0.4);
        view.LoadingBar.IsIndeterminate.ShouldBeFalse();
        view.LoadingBar.Value.ShouldBe(0.4);

        view.UpdateProgress(null);
        view.LoadingBar.IsIndeterminate.ShouldBeTrue();
    }


    [Fact]
    public void TextUpdatesInPlace()
    {
        var view = Host(new SnackbarConfig { Text = "Uploading", IsLoading = true });

        view.UpdateText("Almost there");

        view.TextLabel.Text.ShouldBe("Almost there");
        AutomationProperties.GetName(view).ShouldBe("Almost there");
    }


    [Fact]
    public void PausingWithNoCountdownRunningIsHarmless()
    {
        var view = Host(new SnackbarConfig { Text = "x", Duration = TimeSpan.Zero });

        view.PauseCountdown();
        view.IsCountdownPaused.ShouldBeTrue();
        view.ResumeCountdown();
        view.IsCountdownPaused.ShouldBeFalse();
    }
}
