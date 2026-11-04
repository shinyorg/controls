using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// Issue #65: the app's implicit styles for the primitives a button is built from must not repaint
/// those parts per visual state. The MAUI template's Label style sets a Gray TextColor in Disabled, and
/// a visual-state setter beats the button's local value, so a disabled button ignored its TextColor.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class ShinyButtonImplicitStyleTests
{
    readonly Application app;

    public ShinyButtonImplicitStyleTests()
    {
        TestDispatcherProvider.Install();
        TestDispatcherProvider.Instance.Timers.Clear();

        // Fresh Application per test: these implicit styles must not leak into other tests.
        this.app = new Application();
        this.app.Resources.Add(DisabledStyle<Label>(Label.TextColorProperty, Colors.Gray));
        this.app.Resources.Add(DisabledStyle<Border>(VisualElement.BackgroundColorProperty, Colors.Gray));
        this.app.Resources.Add(DisabledStyle<Image>(VisualElement.OpacityProperty, 0.1));
        this.app.Resources.Add(DisabledStyle<ActivityIndicator>(ActivityIndicator.ColorProperty, Colors.Gray));
    }

    /// <summary>The shape of the template's Styles.xaml: an implicit style with a Disabled visual state.</summary>
    static Style DisabledStyle<T>(BindableProperty property, object value)
    {
        var group = new VisualStateGroup { Name = "CommonStates" };
        group.States.Add(new VisualState { Name = "Normal" });
        group.States.Add(new VisualState
        {
            Name = "Disabled",
            Setters = { new Setter { Property = property, Value = value } }
        });

        var style = new Style(typeof(T));
        style.Setters.Add(new Setter { Property = VisualStateManager.VisualStateGroupsProperty, Value = new VisualStateGroupList { group } });
        return style;
    }

    /// <summary>The button's own style from the issue: different colours per state.</summary>
    static Style IssueButtonStyle()
    {
        var group = new VisualStateGroup { Name = "CommonStates" };
        group.States.Add(new VisualState
        {
            Name = "Normal",
            Setters =
            {
                new Setter { Property = ShinyButton.TextColorProperty, Value = Colors.Blue },
                new Setter { Property = ShinyButton.ButtonBackgroundColorProperty, Value = Colors.Magenta }
            }
        });
        group.States.Add(new VisualState
        {
            Name = "Disabled",
            Setters =
            {
                new Setter { Property = ShinyButton.TextColorProperty, Value = Colors.Green },
                new Setter { Property = ShinyButton.ButtonBackgroundColorProperty, Value = Colors.Orange }
            }
        });
        group.States.Add(new VisualState { Name = "PointerOver" });

        var style = new Style(typeof(ShinyButton));
        style.Setters.Add(new Setter { Property = ShinyButton.TextColorProperty, Value = Colors.Yellow });
        style.Setters.Add(new Setter { Property = VisualStateManager.VisualStateGroupsProperty, Value = new VisualStateGroupList { group } });
        return style;
    }

    ShinyButton Host(ShinyButton button)
    {
        this.app.MainPage = new ContentPage { Content = button };
        return button;
    }

    static IEnumerable<T> Parts<T>(Element root)
        => root.GetVisualTreeDescendants().OfType<T>();

    static Label TextLabel(ShinyButton button) => Parts<Label>(button).Single();


    [Fact]
    public void DisabledVisualState_TextColor_ReachesTheLabel()
    {
        this.app.Resources.Add(IssueButtonStyle());
        var button = this.Host(new ShinyButton { Text = "Go", IsEnabled = false });

        TextLabel(button).TextColor.ShouldBe(Colors.Green);
        ((Border)button.Content!).BackgroundColor.ShouldBe(Colors.Orange);

        button.IsEnabled = true;
        TextLabel(button).TextColor.ShouldBe(Colors.Blue);
        ((Border)button.Content!).BackgroundColor.ShouldBe(Colors.Magenta);

        button.IsEnabled = false;
        TextLabel(button).TextColor.ShouldBe(Colors.Green);
        ((Border)button.Content!).BackgroundColor.ShouldBe(Colors.Orange);
    }

    [Fact]
    public void Disabled_ExplicitColours_SurviveImplicitStyles()
    {
        var button = this.Host(new ShinyButton
        {
            Text = "Go",
            TextColor = Colors.Red,
            ButtonBackgroundColor = Colors.Navy
        });
        button.IsEnabled = false;

        TextLabel(button).TextColor.ShouldBe(Colors.Red);
        ((Border)button.Content!).BackgroundColor.ShouldBe(Colors.Navy);

        // The button expresses Disabled itself, once, on the border - the parts add no dim of their own.
        ((Border)button.Content!).Opacity.ShouldBe(button.DisabledOpacity);
        TextLabel(button).Opacity.ShouldBe(1d);
    }

    [Fact]
    public void Disabled_ImageIcon_IsNotRestyled()
    {
        var button = this.Host(new ShinyButton
        {
            Text = "Go",
            LeftIcon = ImageSource.FromFile("icon.png")
        });
        button.IsEnabled = false;

        Parts<Image>(button).Single().Opacity.ShouldBe(1d);
    }

    [Fact]
    public void Disabled_PlatformBusyIndicator_KeepsTheButtonsColour()
    {
        var button = this.Host(new ShinyButton
        {
            Text = "Go",
            TextColor = Colors.Red,
            BusyMotionIcon = null,
            DisableWhileBusy = true
        });
        button.IsBusy = true;

        button.IsEnabled.ShouldBeFalse();
        Parts<ActivityIndicator>(button).Single().Color.ShouldBe(Colors.Red);
    }

    [Fact]
    public void ButtonGroupText_DisabledLabel_IsNotRestyled()
    {
        var segment = new ButtonGroupText { Text = "USD" };
        this.app.MainPage = new ContentPage { Content = segment };
        segment.IsEnabled = false;

        Parts<Label>(segment).Single().TextColor.ShouldNotBe(Colors.Gray);
        Parts<Border>(segment).Single().BackgroundColor.ShouldNotBe(Colors.Gray);
    }
}
