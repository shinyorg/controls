using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Toast;

sealed class ToastView : ContentView
{
    // M3's neutral snackbar is the inverse surface pair, so the default toast follows the theme
    // instead of a fixed dark grey. Resolved per-toast (these are one-shot, short-lived views).
    static Color DefaultBackground => ThemeColor(ShinyThemeKeys.Color.InverseSurface, Color.FromArgb("#323232"));
    static Color DefaultTextColor => ThemeColor(ShinyThemeKeys.Color.InverseOnSurface, Colors.White);

    static Color ThemeColor(string key, Color fallback)
        => Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c ? c : fallback;
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

        // Resolve theme token keys for this toast type (if typed). Explicit colors on the
        // config (set by the consumer or a ToastTypeStyle) always win; otherwise we bind the
        // matching token via dynamic resources so a runtime theme/appearance switch restyles.
        (string Bg, string Text, string Border)? tokens = null;
        if (config.Type is ToastType type && ToastStyles.TypeTokens.TryGetValue(type, out var t))
            tokens = t;

        // Text color is used by both the label and the spinner. When no explicit color and no
        // token is available, fall back to the hardcoded default.
        var textColor = config.TextColor ?? DefaultTextColor;
        var bgColor = config.BackgroundColor ?? DefaultBackground;

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
            Stroke = config.BorderColor ?? Colors.Transparent,
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

        // Apply theme tokens via dynamic resources for any color the consumer did not set
        // explicitly. This keeps runtime theme/appearance switches live.
        if (tokens is { } tk)
        {
            // Seed per-type hex fallbacks first so non-Application (no theme dictionary) contexts
            // keep the original look; the dynamic resource overrides when the key resolves.
            var (bgHex, textHex, borderHex) = ToastStyles.DefaultColors[config.Type!.Value];

            if (config.BackgroundColor is null)
            {
                border.BackgroundColor = Color.FromArgb(bgHex);
                border.SetDynamicResource(VisualElement.BackgroundColorProperty, tk.Bg);
            }

            if (config.TextColor is null)
            {
                var fallbackText = Color.FromArgb(textHex);
                label.TextColor = fallbackText;
                label.SetDynamicResource(Label.TextColorProperty, tk.Text);
                if (spinner is not null)
                {
                    spinner.Color = fallbackText;
                    spinner.SetDynamicResource(ActivityIndicator.ColorProperty, tk.Text);
                }
            }

            if (config.BorderColor is null && config.BorderThickness > 0)
            {
                // Stroke is a Brush; drive its Color from the token so theme swaps propagate.
                var strokeBrush = new SolidColorBrush(Color.FromArgb(borderHex));
                strokeBrush.SetDynamicResource(SolidColorBrush.ColorProperty, tk.Border);
                border.Stroke = strokeBrush;
            }
        }

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
