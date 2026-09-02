using Microsoft.Maui.Controls.Shapes;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// The modal page a <see cref="SlideView"/> pushes to present a deck: the slide edge to edge on black,
/// with a control bar that fades out and comes back on a touch.
/// </summary>
/// <remarks>
/// A modal page rather than re-parenting the viewer, for the reason every full-screen control in this
/// repo uses one: moving a view in the tree rebuilds its platform view, and lifting the viewer out of
/// whatever layout the consumer put it in means putting it back exactly right afterwards. The page
/// carries its own <see cref="SlideView"/> over the same deck instead.
/// </remarks>
class SlideShowPage : ContentPage
{
    static readonly Color Scrim = Color.FromArgb("#CC000000");
    static readonly TimeSpan AutoHideDelay = TimeSpan.FromSeconds(3);

    readonly SlideView owner;
    readonly SlideView surface;
    readonly Border chrome;
    readonly Border notesPanel;
    readonly Label notesLabel;
    readonly Label counter;
    readonly Button previous;
    readonly Button next;

    IDispatcherTimer? autoHide;
    bool notesShown;
    bool screenKeptOn;

    public SlideShowPage(SlideView owner)
    {
        this.owner = owner;

        this.BackgroundColor = Colors.Black;
        this.Padding = 0;

        Shell.SetNavBarIsVisible(this, false);
        Shell.SetTabBarIsVisible(this, false);
        NavigationPage.SetHasNavigationBar(this, false);

        this.surface = new SlideView(owner);
        this.surface.SlideChanged += this.OnSlideChanged;
        this.surface.Interacted += this.OnInteracted;

        this.previous = ChromeButton("‹", 22);
        this.previous.Clicked += (_, _) => this.Navigate(forward: false);

        this.next = ChromeButton("›", 22);
        this.next.Clicked += (_, _) => this.Navigate(forward: true);

        this.counter = new Label
        {
            TextColor = Colors.White,
            FontSize = 14,
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            WidthRequest = 72
        };

        var notesButton = ChromeButton("Notes", 13);
        notesButton.Clicked += (_, _) => this.ToggleNotes();
        notesButton.IsVisible = owner.Deck?.Slides.Any(x => !string.IsNullOrWhiteSpace(x.Notes)) == true;

        var exit = ChromeButton("Exit", 13);
        exit.Clicked += (_, _) => this.owner.StopPresenting();

        this.chrome = ChromePanel(22);
        this.chrome.Padding = new Thickness(8, 2);
        this.chrome.HorizontalOptions = LayoutOptions.Center;
        this.chrome.IsVisible = owner.ShowPresenterControls;
        this.chrome.Content = new HorizontalStackLayout
        {
            Spacing = 2,
            Children = { this.previous, this.counter, this.next, notesButton, exit }
        };

        this.notesLabel = new Label
        {
            TextColor = Colors.White,
            FontSize = 14,
            LineBreakMode = LineBreakMode.WordWrap
        };

        this.notesPanel = ChromePanel(10);
        this.notesPanel.Padding = 12;
        this.notesPanel.Margin = new Thickness(24, 0, 24, 8);
        this.notesPanel.IsVisible = false;
        this.notesPanel.MaximumHeightRequest = 180;
        this.notesPanel.Content = new ScrollView { Content = this.notesLabel };

        this.Content = new Grid
        {
            Children =
            {
                this.surface,
                new VerticalStackLayout
                {
                    Spacing = 0,
                    VerticalOptions = LayoutOptions.End,
                    Margin = new Thickness(0, 0, 0, 20),
                    Children = { this.notesPanel, this.chrome }
                }
            }
        };

        this.Update();
    }

    static Button ChromeButton(string text, double fontSize)
    {
        var button = new Button
        {
            // An explicit Style, empty on purpose: an app's implicit Button style would otherwise
            // repaint the presenter bar in the app's own colours.
            Style = new Style(typeof(Button)),
            Text = text,
            FontSize = fontSize,
            TextColor = Colors.White,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            Padding = new Thickness(10, 0),
            MinimumWidthRequest = 0,
            HeightRequest = 36
        };

        // The states are declared rather than left to whatever style is in scope, because a visual
        // state setter beats a local value: dropping the style is not enough to stop a Disabled state
        // painting its own background, which on iOS put a grey pill around the arrow at the end of a
        // deck — on a bar that is meant to be nothing but text on black. Declaring them also defines
        // the disabled look here: an arrow that is spent should read as dim, not as missing.
        VisualStateManager.SetVisualStateGroups(button, new VisualStateGroupList
        {
            new VisualStateGroup
            {
                Name = nameof(VisualStateManager.CommonStates),
                States =
                {
                    ChromeState(VisualStateManager.CommonStates.Normal, 1),
                    ChromeState(VisualStateManager.CommonStates.Disabled, 0.35),
                    ChromeState("Pressed", 0.6),
                    ChromeState("PointerOver", 1)
                }
            }
        });

        return button;
    }

    static VisualState ChromeState(string name, double opacity) => new()
    {
        Name = name,
        Setters =
        {
            new Setter { Property = VisualElement.BackgroundColorProperty, Value = Colors.Transparent },
            new Setter { Property = Button.TextColorProperty, Value = Colors.White },
            new Setter { Property = VisualElement.OpacityProperty, Value = opacity }
        }
    };

    /// <inheritdoc cref="ChromeButton"/>
    static Border ChromePanel(int cornerRadius) => new()
    {
        Style = new Style(typeof(Border)),
        BackgroundColor = Scrim,
        Stroke = Colors.Transparent,
        StrokeThickness = 0,
        StrokeShape = new RoundRectangle { CornerRadius = cornerRadius }
    };

    protected override void OnAppearing()
    {
        base.OnAppearing();
        this.KeepScreenOn(true);
        this.RestartAutoHide();
    }

    // The platform back gesture collapses the show rather than popping the page out from under the
    // owner: routing it through IsPresenting keeps the property, the event and the page in step.
    protected override bool OnBackButtonPressed()
    {
        this.owner.StopPresenting();
        return true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        this.autoHide?.Stop();
        this.KeepScreenOn(false);

        this.surface.SlideChanged -= this.OnSlideChanged;
        this.surface.Interacted -= this.OnInteracted;

        // The inline viewer is left on the slide the show ended on, not the one it started from.
        this.owner.AdoptSlideIndex(this.surface.SlideIndex);
        this.surface.Dispose();

        this.owner.OnShowPageDismissed();
    }

    void Navigate(bool forward)
    {
        if (forward)
            this.surface.Next();
        else
            this.surface.Previous();

        this.WakeChrome();
    }

    void OnSlideChanged(object? sender, int index)
    {
        this.owner.AdoptSlideIndex(index);
        this.Update();
    }

    void OnInteracted(object? sender, EventArgs e) => this.WakeChrome();

    void ToggleNotes()
    {
        this.notesShown = !this.notesShown;
        this.Update();
        this.WakeChrome();
    }

    void Update()
    {
        var controller = this.surface.Controller;
        var count = controller?.Count ?? 0;
        var index = controller?.Index ?? 0;

        this.counter.Text = count == 0 ? string.Empty : $"{index + 1} / {count}";

        // The dim that goes with this is a visual state on the button itself; see ChromeButton.
        this.previous.IsEnabled = controller?.CanGoPrevious == true;
        this.next.IsEnabled = controller?.CanGoNext == true;

        var notes = controller?.Current?.Notes;
        this.notesLabel.Text = string.IsNullOrWhiteSpace(notes) ? "No notes on this slide." : notes;
        this.notesPanel.IsVisible = this.notesShown && this.owner.ShowPresenterControls;
    }

    /// <summary>Bring the chrome back and push the fade-out deadline out again.</summary>
    void WakeChrome()
    {
        if (!this.owner.ShowPresenterControls)
            return;

        this.chrome.IsVisible = true;
        this.chrome.Opacity = 1;
        this.RestartAutoHide();
    }

    void RestartAutoHide()
    {
        if (!this.owner.ShowPresenterControls)
            return;

        if (this.autoHide is null)
        {
            var timer = this.Dispatcher?.CreateTimer();
            if (timer is null)
                return;

            timer.IsRepeating = false;
            timer.Interval = AutoHideDelay;
            timer.Tick += (_, _) => this.HideChrome();
            this.autoHide = timer;
        }

        this.autoHide.Stop();
        this.autoHide.Interval = AutoHideDelay;
        this.autoHide.Start();
    }

    async void HideChrome()
    {
        try
        {
            // The bar goes; the notes panel stays. Notes are opened deliberately and read while the
            // presenter is talking rather than touching the screen - fading them on the same timer
            // means tapping to finish a sentence.
            //
            // Faded *and* hidden: a bar left at zero opacity still swallows the tap that was meant to
            // advance the slide underneath it.
            await this.chrome.FadeTo(0, 200);
            this.chrome.IsVisible = false;
        }
        catch (Exception)
        {
            // The page went away mid-fade; nothing to keep.
        }
    }

    void KeepScreenOn(bool on)
    {
        if (!this.owner.KeepScreenOnWhilePresenting || on == this.screenKeptOn)
            return;

        try
        {
            DeviceDisplay.Current.KeepScreenOn = on;
            this.screenKeptOn = on;
        }
        catch (Exception)
        {
            // Not every head has a display to keep awake (the plain net10.0 build, a headless test).
        }
    }
}
