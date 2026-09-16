using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Toast;

sealed class ToastView : ContentView
{
    // M3's neutral snackbar is the inverse surface pair, so the default toast follows the theme
    // instead of a fixed dark grey. These hex values are only what shows before a theme resolves.
    static readonly (string Bg, string Text, string Border) DefaultTokens =
        (ShinyThemeKeys.Color.InverseSurface, ShinyThemeKeys.Color.InverseOnSurface, ShinyThemeKeys.Color.Outline);
    static readonly Color DefaultBackground = Color.FromArgb("#323232");
    static readonly Color DefaultTextColor = Colors.White;

    // The theme colours, resolved through this view rather than copied out of the application's
    // resources. A toast can stay up (a spinner toast for a long operation) across a light/dark flip,
    // and it lives inside the page, so a palette scoped over the page must win over the app's.
    //
    // Deliberately not SetDynamicResource straight onto the child properties: those are seeded with
    // a hex fallback first, and a seeded local value outranks a dynamic resource - the token would
    // resolve and silently lose, forever. A Brush-typed Stroke cannot take a Color token at all.
    // So the token lands on these, which have no local value, and the callbacks push it down.
    static readonly BindableProperty ThemeBackgroundColorProperty = BindableProperty.Create(
        "ThemeBackgroundColor", typeof(Color), typeof(ToastView), null,
        propertyChanged: static (b, _, n) => ((ToastView)b).OnThemeBackground(n as Color));

    static readonly BindableProperty ThemeTextColorProperty = BindableProperty.Create(
        "ThemeTextColor", typeof(Color), typeof(ToastView), null,
        propertyChanged: static (b, _, n) => ((ToastView)b).OnThemeText(n as Color));

    static readonly BindableProperty ThemeBorderColorProperty = BindableProperty.Create(
        "ThemeBorderColor", typeof(Color), typeof(ToastView), null,
        propertyChanged: static (b, _, n) => ((ToastView)b).OnThemeBorder(n as Color));

    Color fallbackBackground = DefaultBackground;
    Color fallbackText = DefaultTextColor;
    Color? fallbackBorder;

    static readonly Color ProgressBarColor = Color.FromArgb("#FFFFFF");

    readonly ToastConfig config;
    readonly Border border;
    readonly Label label;
    readonly Microsoft.Maui.Controls.ProgressBar? progressBar;
    readonly ActivityIndicator? spinner;
    readonly Image? icon;
    CancellationTokenSource? autoDismissCts;
    Action? onDismissed;
    bool isDismissing;

    public ToastView(ToastConfig config)
    {
        this.config = config;
        InputTransparent = false;

        // Resolve theme token keys for this toast type, or the neutral inverse-surface pair for an
        // untyped toast. Explicit colors on the config (set by the consumer or a ToastTypeStyle)
        // always win; otherwise the token is followed live - see ThemeBackgroundColorProperty.
        var tokens = DefaultTokens;
        if (config.Type is ToastType type && ToastStyles.TypeTokens.TryGetValue(type, out var t))
        {
            tokens = t;
            var (bgHex, textHex, borderHex) = ToastStyles.DefaultColors[type];
            fallbackBackground = Color.FromArgb(bgHex);
            fallbackText = Color.FromArgb(textHex);
            fallbackBorder = Color.FromArgb(borderHex);
        }

        // Text color is used by both the label and the spinner. Seeded with the fallback; the token
        // replaces it once this view is in a tree that resolves it.
        var textColor = config.TextColor ?? fallbackText;
        var bgColor = config.BackgroundColor ?? fallbackBackground;

        // Icon
        if (config.Icon is not null)
        {
            icon = new Image
            {
                Source = config.Icon,
                HeightRequest = 20,
                WidthRequest = 20,
                VerticalOptions = LayoutOptions.Center
            };
        }

        // Spinner
        if (config.Spinner != ToastSpinnerPosition.None)
        {
            spinner = new ActivityIndicator
            {
                IsRunning = true,
                Color = textColor,
                HeightRequest = 20,
                WidthRequest = 20,
                VerticalOptions = LayoutOptions.Center
            };
        }

        // Label
        label = new Label
        {
            Text = config.Text,
            TextColor = textColor,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
            LineBreakMode = config.TextOverflow switch
            {
                ToastTextOverflow.MultiLine => LineBreakMode.WordWrap,
                _ => LineBreakMode.TailTruncation
            },
            MaxLines = config.TextOverflow == ToastTextOverflow.MultiLine ? int.MaxValue : 1
        }.WithFontSize(ShinyThemeKeys.Type.BodyMediumSize);

        // Content layout — use Grid so the label column is width-constrained
        // (HorizontalStackLayout gives unlimited width, breaking truncation/wrap)
        var contentLayout = new Grid
        {
            ColumnSpacing = 10,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill
        };

        var col = 0;

        // Add spinner on left
        if (config.Spinner == ToastSpinnerPosition.Left && spinner is not null)
        {
            contentLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            contentLayout.Children.Add(spinner);
            Grid.SetColumn(spinner, col++);
        }

        // Add icon
        if (icon is not null)
        {
            contentLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            contentLayout.Children.Add(icon);
            Grid.SetColumn(icon, col++);
        }

        // Label gets star column so it's constrained to remaining width
        contentLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        // For marquee, wrap label in a clipped container
        if (config.TextOverflow == ToastTextOverflow.Marquee)
        {
            label.LineBreakMode = LineBreakMode.NoWrap;
            label.MaxLines = 1;
            label.HorizontalOptions = LayoutOptions.Start;

            var marqueeContainer = new Grid
            {
                IsClippedToBounds = true,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Center
            };
            marqueeContainer.Children.Add(label);
            contentLayout.Children.Add(marqueeContainer);
            Grid.SetColumn(marqueeContainer, col++);
        }
        else
        {
            contentLayout.Children.Add(label);
            Grid.SetColumn(label, col++);
        }

        // Add spinner on right
        if (config.Spinner == ToastSpinnerPosition.Right && spinner is not null)
        {
            contentLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            contentLayout.Children.Add(spinner);
            Grid.SetColumn(spinner, col++);
        }

        // Main container
        View innerContent;
        if (config.ShowProgressBar && config.Duration > TimeSpan.Zero)
        {
            progressBar = new Microsoft.Maui.Controls.ProgressBar
            {
                Progress = 1.0,
                ProgressColor = ProgressBarColor,
                HeightRequest = 2,
                VerticalOptions = LayoutOptions.End,
                Margin = new Thickness(0)
            };

            var grid = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto)
                }
            };
            grid.Children.Add(contentLayout);
            Grid.SetRow(contentLayout, 0);
            grid.Children.Add(progressBar);
            Grid.SetRow(progressBar, 1);

            innerContent = grid;
        }
        else
        {
            innerContent = contentLayout;
        }

        // Border (pill or fill)
        var isPill = config.DisplayMode == ToastDisplayMode.Pill;
        border = new Border
        {
            Content = innerContent,
            BackgroundColor = bgColor,
            Padding = new Thickness(16, 10),
            StrokeThickness = config.BorderThickness,
            Stroke = config.BorderColor ?? (config.BorderThickness > 0 && fallbackBorder is { } fb ? fb : Colors.Transparent),
            HorizontalOptions = isPill ? LayoutOptions.Center : LayoutOptions.Fill,
            MaximumWidthRequest = isPill ? 400 : double.PositiveInfinity,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            {
                CornerRadius = isPill ? config.CornerRadius : 0
            },
        };

        // A pill floats above the page, an edge-anchored bar does not.
        if (isPill)
            border.SetDynamicResource(VisualElement.ShadowProperty, ShinyThemeKeys.Elevation.Level2);

        // Follow the theme for any color the consumer did not set explicitly. The untyped border
        // stays transparent unless the consumer asks for one, as it always has.
        if (config.BackgroundColor is null)
            this.SetDynamicResource(ThemeBackgroundColorProperty, tokens.Bg);

        if (config.TextColor is null)
            this.SetDynamicResource(ThemeTextColorProperty, tokens.Text);

        if (config.BorderColor is null && config.BorderThickness > 0 && fallbackBorder is not null)
            this.SetDynamicResource(ThemeBorderColorProperty, tokens.Border);

        // Tap gesture
        if (config.DismissOnTap || config.TapCommand is not null)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += OnTapped;
            border.GestureRecognizers.Add(tap);
        }

        Content = border;

        // Accessibility
        AutomationProperties.SetName(this, config.Text);
    }

    void OnThemeBackground(Color? color)
    {
        if (border is not null)
            border.BackgroundColor = color ?? fallbackBackground;
    }

    void OnThemeText(Color? color)
    {
        var resolved = color ?? fallbackText;
        if (label is not null)
            label.TextColor = resolved;
        if (spinner is not null)
            spinner.Color = resolved;
    }

    void OnThemeBorder(Color? color)
    {
        if (border is not null && fallbackBorder is not null)
            border.Stroke = color ?? fallbackBorder;
    }

    internal Border Chrome => border;
    internal Label TextLabel => label;
    internal ActivityIndicator? Spinner => spinner;

    public void SetOnDismissed(Action callback) => onDismissed = callback;

    public async Task AnimateInAsync()
    {
        var translateY = config.Position == ToastPosition.Bottom ? 80 : -80;
        TranslationY = translateY;
        Opacity = 0;
        IsVisible = true;

        await Task.WhenAll(
            this.TranslateToAsync(0, 0, 250, Easing.CubicOut),
            this.FadeToAsync(1, 250, Easing.CubicOut)
        );

        if (config.AnnounceToScreenReader)
        {
            try
            {
                SemanticScreenReader.Announce(config.Text);
            }
            catch
            {
                // May not be available on all platforms
            }
        }

        StartAutoDismiss();
        StartProgressBar();
        StartMarquee();
    }

    public async Task AnimateOutAsync()
    {
        if (isDismissing)
            return;
        isDismissing = true;

        autoDismissCts?.Cancel();

        var translateY = config.Position == ToastPosition.Bottom ? 80 : -80;

        await Task.WhenAll(
            this.TranslateToAsync(0, translateY, 200, Easing.CubicIn),
            this.FadeToAsync(0, 200, Easing.CubicIn)
        );

        IsVisible = false;
        onDismissed?.Invoke();
    }

    public async Task AnimatePositionAsync(double newY)
    {
        await this.TranslateToAsync(TranslationX, newY - Bounds.Y, 150, Easing.CubicOut);
    }

    void OnTapped(object? sender, TappedEventArgs e)
    {
        config.TapCommand?.Execute(null);

        if (config.DismissOnTap)
            _ = AnimateOutAsync();
    }

    void StartAutoDismiss()
    {
        if (config.Duration <= TimeSpan.Zero)
            return;

        autoDismissCts = new CancellationTokenSource();
        var token = autoDismissCts.Token;

        Dispatcher.DispatchDelayed(config.Duration, () =>
        {
            if (!token.IsCancellationRequested)
                _ = AnimateOutAsync();
        });
    }

    void StartProgressBar()
    {
        if (progressBar is null || config.Duration <= TimeSpan.Zero)
            return;

        var animation = new Animation(v => progressBar.Progress = v, 1.0, 0.0);
        animation.Commit(
            this,
            "ProgressCountdown",
            length: (uint)config.Duration.TotalMilliseconds,
            easing: Easing.Linear
        );
    }

    void StartMarquee()
    {
        if (config.TextOverflow != ToastTextOverflow.Marquee)
            return;

        // Wait for layout so we know the container width
        label.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(100), () =>
        {
            var containerWidth = label.Parent is View parent ? parent.Width : 0;
            if (containerWidth <= 0)
                containerWidth = 200;

            // Measure desired label width
            var measured = label.Measure(double.PositiveInfinity, double.PositiveInfinity);
            var textWidth = measured.Width;

            // Only scroll if text is wider than container
            if (textWidth <= containerWidth)
                return;

            var speed = config.MarqueeSpeedPixelsPerSecond > 0 ? config.MarqueeSpeedPixelsPerSecond : 40;
            var totalDistance = textWidth + containerWidth;
            var onePassMs = (uint)(totalDistance / speed * 1000);

            var loopCount = 0;
            var loops = config.MarqueeLoops;

            // When MarqueeLoops > 0, override auto-dismiss to match scroll time
            if (loops > 0)
            {
                autoDismissCts?.Cancel();

                var totalMs = onePassMs * (uint)loops;
                autoDismissCts = new CancellationTokenSource();
                var token = autoDismissCts.Token;

                Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(totalMs), () =>
                {
                    if (!token.IsCancellationRequested)
                        _ = AnimateOutAsync();
                });

                // Re-run progress bar against marquee total time
                if (progressBar is not null)
                {
                    this.AbortAnimation("ProgressCountdown");
                    var progressAnim = new Animation(v => progressBar.Progress = v, 1.0, 0.0);
                    progressAnim.Commit(this, "ProgressCountdown",
                        length: totalMs, easing: Easing.Linear);
                }
            }

            // Start at right edge of container, scroll until fully off left
            var animation = new Animation(
                v => label.TranslationX = v,
                containerWidth,
                -textWidth
            );
            animation.Commit(
                this,
                "MarqueeScroll",
                length: onePassMs,
                easing: Easing.Linear,
                repeat: () => loops <= 0 || ++loopCount < loops
            );
        });
    }
}
