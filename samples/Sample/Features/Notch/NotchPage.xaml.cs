using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls;
#if !ANDROID && !IOS && !MACCATALYST
using Shiny.Maui.Controls.Desktop.Notch;
#endif

namespace Sample.Features.Notch;

public partial class NotchPage : ContentPage
{
    static readonly string[] Phrases =
    [
        "Hi",
        "Hello there",
        "Hello there, this is getting longer",
        "Hello there, this is getting longer and will wrap once it runs out of room across the page"
    ];

    int phrase;
    Label? growingLabel;

    public NotchPage()
    {
        InitializeComponent();
        SampleSourceCode.Attach(this);
        this.OnRecording(this, EventArgs.Empty);

        // The notch window exists on macOS, Windows and Linux (this file's net11.0 build is the GTK head).
#if !ANDROID && !IOS && !MACCATALYST
        this.DesktopSection.IsVisible = true;
#endif
    }

    void OnEmpty(object? sender, EventArgs e) => this.Notch.Content = null;

    void OnRecording(object? sender, EventArgs e) => this.Notch.Content = Recording();

    void OnPlaying(object? sender, EventArgs e) => this.Notch.Content = NowPlaying();

    void OnMessage(object? sender, EventArgs e) => this.Notch.Content = Message();

    void OnLonger(object? sender, EventArgs e) => this.SetPhrase(Math.Min(this.phrase + 1, Phrases.Length - 1));

    void OnShorter(object? sender, EventArgs e) => this.SetPhrase(Math.Max(this.phrase - 1, 0));

    void SetPhrase(int index)
    {
        this.phrase = index;
        if (this.growingLabel == null || this.Notch.Content != this.growingLabel)
        {
            this.growingLabel = WhiteLabel(Phrases[index]);
            this.Notch.Content = this.growingLabel;
        }
        else
        {
            this.growingLabel.Text = Phrases[index];
        }
    }

    void OnShapeToggled(object? sender, ToggledEventArgs e)
        => this.Notch.Shape = e.Value ? NotchShape.Notch : NotchShape.Island;

    void OnNotchTapped(object? sender, EventArgs e)
        => this.TapStatus.Text = $"Tapped at {DateTime.Now:T}";

    static Label WhiteLabel(string text, double size = 14, FontAttributes attributes = FontAttributes.None) => new()
    {
        Text = text,
        TextColor = Colors.White,
        FontSize = size,
        FontAttributes = attributes,
        VerticalTextAlignment = TextAlignment.Center
    };

    static View Recording() => new HorizontalStackLayout
    {
        Spacing = 8,
        Children =
        {
            new Ellipse { Fill = Colors.Red, WidthRequest = 10, HeightRequest = 10, VerticalOptions = LayoutOptions.Center },
            WhiteLabel("0:42", 14, FontAttributes.Bold)
        }
    };

    static View NowPlaying() => new Grid
    {
        ColumnDefinitions = { new ColumnDefinition(56), new ColumnDefinition(GridLength.Auto) },
        RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
        ColumnSpacing = 12,
        RowSpacing = 4,
        WidthRequest = 300,
        Children =
        {
            // BoxView with Color rather than a gradient Border: the AppKit head paints neither a
            // Border's gradient fill nor a BoxView's Background, only BoxView.Color.
            new BoxView { Color = Color.FromArgb("#7C3AED"), CornerRadius = 10, HeightRequest = 56 }.At(row: 0, rowSpan: 2),
            WhiteLabel("Midnight Drive", 16, FontAttributes.Bold).At(row: 0, column: 1),
            new Label { Text = "The Shiny Band", TextColor = Color.FromArgb("#A1A1AA"), FontSize = 13 }.At(row: 1, column: 1),
            new Grid
            {
                Margin = new Thickness(0, 8, 0, 0),
                ColumnDefinitions = { new ColumnDefinition(new GridLength(2, GridUnitType.Star)), new ColumnDefinition(new GridLength(3, GridUnitType.Star)) },
                Children =
                {
                    new BoxView { Color = Color.FromArgb("#3F3F46"), CornerRadius = 2, HeightRequest = 4 }.At(columnSpan: 2),
                    new BoxView { Color = Colors.White, CornerRadius = 2, HeightRequest = 4 }
                }
            }.At(row: 2, columnSpan: 2)
        }
    };

    static View Message() => new VerticalStackLayout
    {
        Spacing = 4,
        WidthRequest = 280,
        Children =
        {
            WhiteLabel("Sam", 15, FontAttributes.Bold),
            new Label
            {
                Text = "Can we get the notch control into the next release? It just needs to size to whatever I put in it.",
                TextColor = Color.FromArgb("#D4D4D8"),
                FontSize = 13
            }
        }
    };

#if !ANDROID && !IOS && !MACCATALYST
    int desktopIndex;

    static INotchWindowService? DesktopNotch
        => IPlatformApplication.Current?.Services.GetService<INotchWindowService>();

    async void OnDesktopShow(object? sender, EventArgs e)
    {
        if (DesktopNotch is not { } service)
            return;

        this.DesktopStatus.Text = service.HasHardwareNotch
            ? "This screen has a camera notch: the empty notch hides inside it, content grows out of it."
            : "No camera notch on this screen, so it floats at the top of it.";
        await service.ShowAsync(Recording());
    }

    async void OnDesktopNext(object? sender, EventArgs e)
    {
        if (DesktopNotch is not { } service)
            return;

        this.desktopIndex = (this.desktopIndex + 1) % 4;
        await service.SetContentAsync(this.desktopIndex switch
        {
            0 => Recording(),
            1 => NowPlaying(),
            2 => Message(),
            _ => null
        });
    }

    void OnDesktopHide(object? sender, EventArgs e) => DesktopNotch?.Hide();
#else
    void OnDesktopShow(object? sender, EventArgs e) { }

    void OnDesktopNext(object? sender, EventArgs e) { }

    void OnDesktopHide(object? sender, EventArgs e) { }
#endif
}

static class GridPlacement
{
    public static T At<T>(this T view, int row = 0, int column = 0, int rowSpan = 1, int columnSpan = 1) where T : View
    {
        Grid.SetRow(view, row);
        Grid.SetColumn(view, column);
        Grid.SetRowSpan(view, rowSpan);
        Grid.SetColumnSpan(view, columnSpan);
        return view;
    }
}
