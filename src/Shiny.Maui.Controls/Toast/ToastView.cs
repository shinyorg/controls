using Shiny.Maui.Controls.Infrastructure;
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
    readonly ProgressBar? loadingBar;
    readonly Button? actionButton;
    readonly ActivityIndicator? spinner;
    readonly Image? icon;
    CancellationTokenSource? autoDismissCts;
    Action<SnackbarResult>? onDismissed;
    bool isDismissing;

    // The countdown, kept as "time left" so a hover can stop it and pick it up again.
    TimeSpan remaining;
    DateTime armedAt;
    bool timerArmed;
    bool paused;

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

        var snackbar = config as SnackbarConfig;
        var isSnackbar = config.DisplayMode == ToastDisplayMode.Snackbar;
        var isPill = config.DisplayMode == ToastDisplayMode.Pill;

        // The action sits beside the content rather than inside it, so the body's tap target never
        // covers the button: tapping the message is the body action, tapping the button is the action.
        View body = contentLayout;
        if (!String.IsNullOrEmpty(snackbar?.ActionText))
        {
            actionButton = new Button
            {
                Text = snackbar.ActionText,
                Background = Brush.Transparent,
                BackgroundColor = Colors.Transparent,
                BorderWidth = 0,
                CornerRadius = 4,
                Padding = new Thickness(12, 0),
                MinimumHeightRequest = 40,
                MinimumWidthRequest = 48,
                FontAttributes = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center
            }.WithFontSize(ShinyThemeKeys.Type.LabelLargeSize);

            if (snackbar.ActionTextColor is { } actionColor)
                actionButton.TextColor = actionColor;
            else
                actionButton.SetDynamicResource(Button.TextColorProperty, ShinyThemeKeys.Color.InversePrimary);

            // Clicked, not a gesture recognizer: a Button ignores those.
            actionButton.Clicked += OnActionClicked;

            var withAction = new Grid
            {
                ColumnSpacing = 8,
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
            };
            withAction.Children.Add(contentLayout);
            withAction.Children.Add(actionButton);
            Grid.SetColumn(actionButton, 1);
            body = withAction;
        }

        // Snackbar padding: 48 tall on one line, and the button's own padding stands in on the right.
        var padding = isSnackbar
            ? (actionButton is null ? new Thickness(16, 14) : new Thickness(16, 4, 8, 4))
            : new Thickness(16, 10);

        if (snackbar?.IsLoading == true)
        {
            loadingBar = new ProgressBar
            {
                ShowText = false,
                Minimum = 0,
                Maximum = 1,
                Value = snackbar.Progress ?? 0,
                IsIndeterminate = snackbar.Progress is null,
                TrackHeight = 3,
                CornerRadius = 0,
                TrackColor = Colors.Transparent,
                InputTransparent = true,
                VerticalOptions = LayoutOptions.Start,
                HorizontalOptions = LayoutOptions.Fill
            };
            loadingBar.SetDynamicResource(ProgressBar.BarColorProperty, ShinyThemeKeys.Color.InversePrimary);
        }

        // Main container
        View innerContent;
        if (loadingBar is not null || (isSnackbar && config.ShowProgressBar && config.Duration > TimeSpan.Zero))
        {
            // A line along the top edge runs edge to edge, so the padding moves off the border and
            // onto the body under it.
            View topLine = (View?)loadingBar ?? (progressBar = new Microsoft.Maui.Controls.ProgressBar
            {
                Progress = 1.0,
                ProgressColor = ProgressBarColor,
                HeightRequest = 2,
                VerticalOptions = LayoutOptions.Start
            });

            body.Margin = padding;
            padding = new Thickness(0);

            // One cell: the line lies over the top padding instead of pushing the content down.
            var grid = new Grid();
            grid.Children.Add(body);
            grid.Children.Add(topLine);

            innerContent = grid;
        }
        else if (config.ShowProgressBar && config.Duration > TimeSpan.Zero)
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
            grid.Children.Add(body);
            Grid.SetRow(body, 0);
            grid.Children.Add(progressBar);
            Grid.SetRow(progressBar, 1);

            innerContent = grid;
        }
        else
        {
            innerContent = body;
        }

        // Border (pill, fill or snackbar). A snackbar fills the width up to 600, and a fill-aligned view
        // with a maximum width is centred in the space it would have filled.
        border = new Border
        {
            Content = innerContent,
            BackgroundColor = bgColor,
            Padding = padding,
            StrokeThickness = config.BorderThickness,
            Stroke = config.BorderColor ?? (config.BorderThickness > 0 && fallbackBorder is { } fb ? fb : Colors.Transparent),
            HorizontalOptions = isPill ? LayoutOptions.Center : LayoutOptions.Fill,
            MaximumWidthRequest = isPill ? 400 : isSnackbar ? 600 : double.PositiveInfinity,
            MinimumHeightRequest = isSnackbar ? 48 : -1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            {
                CornerRadius = isPill || isSnackbar ? config.CornerRadius : 0
            },
        };

        // A pill or a snackbar floats above the page, an edge-anchored bar does not.
        if (isPill || isSnackbar)
            border.SetDynamicResource(VisualElement.ShadowProperty, ShinyThemeKeys.Elevation.Level2);

        // Follow the theme for any color the consumer did not set explicitly. The untyped border
        // stays transparent unless the consumer asks for one, as it always has.
        if (config.BackgroundColor is null)
            this.SetDynamicResource(ThemeBackgroundColorProperty, tokens.Bg);

        if (config.TextColor is null)
            this.SetDynamicResource(ThemeTextColorProperty, tokens.Text);

        if (config.BorderColor is null && config.BorderThickness > 0 && fallbackBorder is not null)
            this.SetDynamicResource(ThemeBorderColorProperty, tokens.Border);

        // Tap gesture. With an action button it goes on the content beside the button, not the border
        // around both.
        if (config.DismissOnTap || config.TapCommand is not null)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += OnTapped;
            (actionButton is null ? (View)border : contentLayout).GestureRecognizers.Add(tap);
        }

        // Hover only exists on desktop, and a pointer recognizer on a phone eats real taps.
        if (config.PauseOnHover && DeviceInfo.Idiom == DeviceIdiom.Desktop)
        {
            var pointer = new PointerGestureRecognizer();
            pointer.PointerEntered += (_, _) => PauseCountdown();
            pointer.PointerExited += (_, _) => ResumeCountdown();
            border.GestureRecognizers.Add(pointer);
        }

        Content = border;

        // Accessibility
        AutomationProperties.SetName(this, config.Text);

        // The id goes on the border rather than this view: the tap gesture lives there, and a pill
        // only covers the middle of this full-width view.
        if (!String.IsNullOrEmpty(config.AutomationId))
        {
            AutomationIds.Set(border, config.AutomationId);
            AutomationIds.Set(label, config.AutomationId + ToastConfig.TextAutomationIdSuffix);
            if (actionButton is not null)
                AutomationIds.Set(actionButton, config.AutomationId + SnackbarConfig.ActionAutomationIdSuffix);
        }
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
    internal Button? ActionButton => actionButton;
    internal ProgressBar? LoadingBar => loadingBar;
    internal bool IsCountdownPaused => paused;

    /// <summary>Why it is closing, set the moment it starts to slide out.</summary>
    internal SnackbarResult? ClosingReason { get; private set; }

    public void SetOnDismissed(Action<SnackbarResult> callback) => onDismissed = callback;

    internal void UpdateText(string text)
    {
        label.Text = text;
        AutomationProperties.SetName(this, text);
    }

    internal void UpdateProgress(double? progress)
    {
        if (loadingBar is null)
            return;

        loadingBar.IsIndeterminate = progress is null;
        loadingBar.Value = progress ?? 0;
    }

    string Announcement => actionButton is null ? config.Text : $"{config.Text}. {actionButton.Text}";

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

        // Replaced while it was still sliding in: nothing left to start.
        if (isDismissing)
            return;

        if (config.AnnounceToScreenReader)
        {
            try
            {
                SemanticScreenReader.Announce(Announcement);
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

    public async Task AnimateOutAsync(SnackbarResult reason = SnackbarResult.Dismissed)
    {
        if (isDismissing)
            return;
        isDismissing = true;
        ClosingReason = reason;

        autoDismissCts?.Cancel();
        timerArmed = false;

        var translateY = config.Position == ToastPosition.Bottom ? 80 : -80;

        await Task.WhenAll(
            this.TranslateToAsync(0, translateY, 200, Easing.CubicIn),
            this.FadeToAsync(0, 200, Easing.CubicIn)
        );

        IsVisible = false;
        onDismissed?.Invoke(reason);
    }

    public async Task AnimatePositionAsync(double newY)
    {
        await this.TranslateToAsync(TranslationX, newY - Bounds.Y, 150, Easing.CubicOut);
    }

    void OnTapped(object? sender, TappedEventArgs e)
    {
        if (isDismissing)
            return;

        config.TapCommand?.Execute(null);

        if (config.DismissOnTap)
            _ = AnimateOutAsync(SnackbarResult.Dismissed);
    }

    void OnActionClicked(object? sender, EventArgs e)
    {
        if (isDismissing)
            return;

        (config as SnackbarConfig)?.ActionCommand?.Execute(null);
        _ = AnimateOutAsync(SnackbarResult.ActionInvoked);
    }

    void StartAutoDismiss()
    {
        if (config.Duration <= TimeSpan.Zero)
            return;

        ArmCountdown(config.Duration);
    }

    void ArmCountdown(TimeSpan duration)
    {
        autoDismissCts?.Cancel();
        autoDismissCts = new CancellationTokenSource();
        var token = autoDismissCts.Token;

        remaining = duration;
        timerArmed = true;
        if (paused)
            return;

        armedAt = DateTime.UtcNow;
        Dispatcher.DispatchDelayed(duration, () =>
        {
            if (!token.IsCancellationRequested)
                _ = AnimateOutAsync(SnackbarResult.TimedOut);
        });
    }

    internal void PauseCountdown()
    {
        if (paused || isDismissing)
            return;

        paused = true;
        if (!timerArmed)
            return;

        autoDismissCts?.Cancel();
        remaining -= DateTime.UtcNow - armedAt;
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;

        this.AbortAnimation("ProgressCountdown");
    }

    internal void ResumeCountdown()
    {
        if (!paused)
            return;

        paused = false;
        if (!timerArmed || isDismissing)
            return;

        ArmCountdown(remaining);

        if (progressBar is not null && remaining > TimeSpan.Zero)
        {
            var animation = new Animation(v => progressBar.Progress = v, progressBar.Progress, 0.0);
            animation.Commit(this, "ProgressCountdown", length: (uint)remaining.TotalMilliseconds, easing: Easing.Linear);
        }
    }

    void StartProgressBar()
    {
        // Hovered the moment it appeared: ResumeCountdown starts the bar from where it stands.
        if (progressBar is null || config.Duration <= TimeSpan.Zero || paused)
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
                var totalMs = onePassMs * (uint)loops;
                ArmCountdown(TimeSpan.FromMilliseconds(totalMs));

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
