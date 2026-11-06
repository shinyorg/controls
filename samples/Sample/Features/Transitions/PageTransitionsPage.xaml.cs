using Shiny.Maui.Controls;

namespace Sample.Features.Transitions;

public partial class PageTransitionsPage : ContentPage
{
    public PageTransitionsPage()
    {
        this.InitializeComponent();
        SampleSourceCode.Attach(this);

        foreach (var direction in Enum.GetValues<PageTransitionDirection>())
            this.DirectionPicker.Items.Add(direction.ToString());
        this.DirectionPicker.SelectedIndex = 0;

        foreach (var type in Enum.GetValues<PageTransitionType>())
        {
            var button = new Button
            {
                Text = type.ToString(),
                AutomationId = $"TransitionPush{type}",
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(14, 6)
            };
            button.Clicked += (_, _) => this.Open(type);
            this.TransitionButtons.Children.Add(button);
        }

        foreach (var (name, color) in HeroDetailPage.Cards)
        {
            var card = new Border
            {
                WidthRequest = 90,
                HeightRequest = 90,
                BackgroundColor = color,
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
                AutomationId = $"HeroCard{name}",
                Content = new Label { Text = name, TextColor = Colors.White, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
            };
            // The same name on the detail page's header is what pairs them.
            PageTransition.SetHero(card, $"card-{name}");
            card.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => this.Navigation.PushAsync(new HeroDetailPage(name, color)))
            });
            this.HeroCards.Children.Add(card);
        }
    }


    PageTransitionDirection Direction => (PageTransitionDirection)Math.Max(0, this.DirectionPicker.SelectedIndex);


    void Open(PageTransitionType type)
    {
        var page = new TransitionStepPage(1, type, this.Direction, this.ModalSwitch.IsToggled);
        _ = this.ModalSwitch.IsToggled
            ? this.Navigation.PushModalAsync(page)
            : this.Navigation.PushAsync(page);
    }


    // Shell cannot host a NavigationPage, so it is presented modally.
    void OnOpenNavigationPage(object? sender, EventArgs e)
        => _ = this.Navigation.PushModalAsync(new ShinyNavigationPage(new TransitionStepPage(0, PageTransitionType.Default, PageTransitionDirection.Default, false, isNavigationRoot: true)));
}


/// <summary>
/// One step of the demo stack: says how it arrived and offers every transition for the next push.
/// </summary>
public class TransitionStepPage : ContentPage
{
    public TransitionStepPage(int depth, PageTransitionType arrivedWith, PageTransitionDirection direction, bool isModal, bool isNavigationRoot = false)
    {
        this.Title = depth == 0 ? "Transitions" : $"Page {depth}";
        this.BackgroundColor = Palette[depth % Palette.Length];

        // The whole feature, as far as the page is concerned.
        PageTransition.SetPush(this, arrivedWith);
        PageTransition.SetDirection(this, direction);

        var stack = new VerticalStackLayout { Padding = new Thickness(24, 60, 24, 24), Spacing = 8 };
        PageTransition.SetEntrance(stack, PageEntranceAnimation.FadeUp);

        stack.Add(new Label
        {
            Text = depth == 0 ? "Pick a transition" : $"Arrived with {arrivedWith}",
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White
        });

        var buttons = new Microsoft.Maui.Controls.FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach (var type in Enum.GetValues<PageTransitionType>())
        {
            buttons.Children.Add(new Button
            {
                Text = type.ToString(),
                AutomationId = $"TransitionPush{type}",
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(12, 6),
                Command = new Command(() => this.Navigation.PushAsync(new TransitionStepPage(depth + 1, type, direction, false)))
            });
        }
        stack.Add(buttons);

        if (isModal)
            stack.Add(new Button { Text = "Close", AutomationId = "TransitionClose", Command = new Command(() => this.Navigation.PopModalAsync()) });
        else if (isNavigationRoot)
            stack.Add(new Button { Text = "Close", AutomationId = "TransitionClose", Command = new Command(() => this.Navigation.PopModalAsync()) });
        else if (depth > 0)
            stack.Add(new Button { Text = "Pop", AutomationId = "TransitionPop", Command = new Command(() => this.Navigation.PopAsync()) });

        this.Content = new ScrollView { Content = stack };
    }


    static readonly Color[] Palette =
    [
        Color.FromArgb("#334155"),
        Color.FromArgb("#0E7490"),
        Color.FromArgb("#7C3AED"),
        Color.FromArgb("#B45309"),
        Color.FromArgb("#15803D"),
        Color.FromArgb("#BE123C")
    ];
}


/// <summary>The hero demo's detail page: its header shares the tapped card's hero name.</summary>
public class HeroDetailPage : ContentPage
{
    internal static readonly (string Name, Color Color)[] Cards =
    [
        ("Ocean", Color.FromArgb("#0E7490")),
        ("Violet", Color.FromArgb("#7C3AED")),
        ("Amber", Color.FromArgb("#B45309"))
    ];


    public HeroDetailPage(string name, Color color)
    {
        this.Title = name;

        var header = new Border
        {
            HeightRequest = 260,
            BackgroundColor = color,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 0 },
            Content = new Label { Text = name, TextColor = Colors.White, FontSize = 40, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
        };
        PageTransition.SetHero(header, $"card-{name}");

        var body = new VerticalStackLayout { Padding = 24, Spacing = 12 };
        PageTransition.SetEntrance(body, PageEntranceAnimation.FadeUp);
        for (var i = 1; i <= 5; i++)
        {
            body.Add(new Border
            {
                HeightRequest = 56,
                StrokeThickness = 0,
                BackgroundColor = color.WithAlpha(0.15f),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                Content = new Label { Text = $"{name} detail row {i}", Margin = new Thickness(16, 0), VerticalOptions = LayoutOptions.Center }
            });
        }

        this.Content = new ScrollView { Content = new VerticalStackLayout { Children = { header, body } } };
    }
}
