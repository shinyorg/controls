using Shiny.Maui.Controls;
using Shiny.Controls.Keyframe;
using Shiny.Maui.Controls.Keyframe;

namespace Sample.Features.Transitions;

public partial class PageTransitionsPage : ContentPage
{
    // The GoToAsync overload demo navigates by route.
    static PageTransitionsPage() => Routing.RegisterRoute(nameof(TransitionRoutePage), new TransitionRouteFactory());

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


    // Per-call overloads: the transition is for this one navigation; the pages set nothing.
    void OnPerCallSharedAxisZ(object? sender, EventArgs e)
        => _ = this.Navigation.PushAsync(new TransitionStepPage(1, PageTransitionType.Default, PageTransitionDirection.Default, false, label: "PushAsync(page, SharedAxisZ)"), PageTransitionType.SharedAxisZ);

    void OnPerCallCover(object? sender, EventArgs e)
        => _ = this.Navigation.PushAsync(new TransitionStepPage(2, PageTransitionType.Default, PageTransitionDirection.Default, false, label: "PushAsync(page, Cover, FromBottom)"), PageTransitionType.Cover, PageTransitionDirection.FromBottom);

    void OnPerCallModal(object? sender, EventArgs e)
        => _ = this.Navigation.PushModalAsync(new TransitionStepPage(3, PageTransitionType.Default, PageTransitionDirection.Default, true, label: "PushModalAsync(page, Zoom)"), PageTransitionType.Zoom);

    async void OnPerCallGoTo(object? sender, EventArgs e)
    {
        if (Shell.Current is { } shell)
            await shell.GoToAsync(nameof(TransitionRoutePage), PageTransitionType.FadeThrough);
    }

    // A custom animator, per call and set on the page itself.
    void OnCustomAnimatorPush(object? sender, EventArgs e)
        => _ = this.Navigation.PushAsync(new TransitionStepPage(4, PageTransitionType.Default, PageTransitionDirection.Default, false, label: "PushAsync(page, swing)"), new SwingAnimator());

    void OnCustomAnimatorPage(object? sender, EventArgs e)
    {
        var page = new TransitionStepPage(5, PageTransitionType.Default, PageTransitionDirection.Default, false, label: "PageTransition.Animator");
        PageTransition.SetAnimator(page, new SwingAnimator());
        _ = this.Navigation.PushAsync(page);
    }


    // A transition written as Keyframes: the page drops in and springs; the one underneath fades back.
    // Popping plays it backwards, because no PopEnter/PopExit are set.
    static readonly KeyframePageTransition Drop = new()
    {
        Enter = Keys(TimeSpan.FromMilliseconds(500),
            new Track { Property = "TranslationY", Keys = { new Key { Offset = 0, Value = "-700", Easing = EasingCatalog.Get("spring(0.55, 11)") }, new Key { Offset = 1, Value = "0" } } }),
        Exit = Keys(TimeSpan.FromMilliseconds(500),
            new Track { Property = "Opacity", Keys = { new Key { Offset = 0, Value = "1" }, new Key { Offset = 1, Value = "0.3" } } },
            new Track { Property = "Scale", Keys = { new Key { Offset = 0, Value = "1", Easing = Easings.CubicOut }, new Key { Offset = 1, Value = "0.92" } } })
    };

    static Keyframes Keys(TimeSpan duration, params Track[] tracks)
    {
        var keyframes = new Keyframes { Duration = duration };
        foreach (var track in tracks)
            keyframes.Tracks.Add(track);
        return keyframes;
    }

    void OnKeyframeAnimatorPush(object? sender, EventArgs e)
        => _ = this.Navigation.PushAsync(new TransitionStepPage(6, PageTransitionType.Default, PageTransitionDirection.Default, false, label: "Keyframes drop"), Drop);

    // Shell cannot host a NavigationPage, so it is presented modally.
    void OnOpenNavigationPage(object? sender, EventArgs e)
        => _ = this.Navigation.PushModalAsync(new ShinyNavigationPage(new TransitionStepPage(0, PageTransitionType.Default, PageTransitionDirection.Default, false, isNavigationRoot: true)));
}


/// <summary>
/// One step of the demo stack: says how it arrived and offers every transition for the next push.
/// </summary>
public class TransitionStepPage : ContentPage
{
    public TransitionStepPage(int depth, PageTransitionType arrivedWith, PageTransitionDirection direction, bool isModal, bool isNavigationRoot = false, string? label = null)
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
            Text = label ?? (depth == 0 ? "Pick a transition" : $"Arrived with {arrivedWith}"),
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
        {
            stack.Add(new Button { Text = "Close", AutomationId = "TransitionClose", Command = new Command(() => this.Navigation.PopModalAsync()) });
            stack.Add(new Button { Text = "PopModalAsync(Fade)", AutomationId = "TransitionCloseFade", Command = new Command(() => this.Navigation.PopModalAsync(PageTransitionType.Fade)) });
        }
        else if (isNavigationRoot)
            stack.Add(new Button { Text = "Close", AutomationId = "TransitionClose", Command = new Command(() => this.Navigation.PopModalAsync()) });
        else if (depth > 0)
        {
            stack.Add(new Button { Text = "Pop", AutomationId = "TransitionPop", Command = new Command(() => this.Navigation.PopAsync()) });
            stack.Add(new Button { Text = "PopAsync(Fade)", AutomationId = "TransitionPopFade", Command = new Command(() => this.Navigation.PopAsync(PageTransitionType.Fade)) });
        }

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


/// <summary>The page the GoToAsync overload demo navigates to by route.</summary>
public class TransitionRoutePage : TransitionStepPage
{
    public TransitionRoutePage() : base(2, PageTransitionType.Default, PageTransitionDirection.Default, false, label: "GoToAsync(route, FadeThrough)") { }
}


sealed class TransitionRouteFactory : RouteFactory
{
    public override Element GetOrCreate() => new TransitionRoutePage();
    public override Element GetOrCreate(IServiceProvider services) => new TransitionRoutePage();
}


/// <summary>
/// A trivial custom transition: the incoming page swings in from a slight tilt and scale while the
/// page underneath recedes, and the reverse on a pop. It animates the context's surfaces (the page,
/// or its content on Linux) and leaves them wherever it ends; the host resets them afterwards.
/// </summary>
public sealed class SwingAnimator : IPageTransitionAnimator
{
    public async Task AnimateAsync(PageTransitionContext context, CancellationToken cancellationToken)
    {
        var length = (uint)Math.Max(1, context.Duration.TotalMilliseconds * 1.4);
        var incoming = context.ToSurface;
        var outgoing = context.FromSurface;
        using var _ = cancellationToken.Register(() =>
        {
            if (incoming is not null)
                incoming.CancelAnimations();
            if (outgoing is not null)
                outgoing.CancelAnimations();
        });

        var tasks = new List<Task>();
        if (!context.IsPop)
        {
            if (incoming is not null)
            {
                incoming.Opacity = 0;
                incoming.Scale = 0.86;
                incoming.Rotation = -6;
                incoming.TranslationY = 40;
                tasks.Add(incoming.FadeToAsync(1, length / 2, Easing.CubicOut));
                tasks.Add(incoming.ScaleToAsync(1, length, Easing.SpringOut));
                tasks.Add(incoming.RotateToAsync(0, length, Easing.SpringOut));
                tasks.Add(incoming.TranslateToAsync(0, 0, length, Easing.SpringOut));
            }
            if (outgoing is not null)
            {
                tasks.Add(outgoing.ScaleToAsync(0.94, length, Easing.CubicOut));
                tasks.Add(outgoing.FadeToAsync(0.4, length, Easing.CubicOut));
            }
        }
        else
        {
            if (outgoing is not null)
            {
                tasks.Add(outgoing.FadeToAsync(0, length / 2, Easing.CubicIn));
                tasks.Add(outgoing.ScaleToAsync(0.86, length / 2, Easing.CubicIn));
                tasks.Add(outgoing.RotateToAsync(6, length / 2, Easing.CubicIn));
            }
            if (incoming is not null)
            {
                incoming.Scale = 0.94;
                incoming.Opacity = 0.4;
                tasks.Add(incoming.ScaleToAsync(1, length / 2, Easing.CubicOut));
                tasks.Add(incoming.FadeToAsync(1, length / 2, Easing.CubicOut));
            }
        }
        await Task.WhenAll(tasks);
    }
}
