using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Themes;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.Dialogs;

sealed class DialogView : ContentView
{
    const uint InDuration = 240;
    const uint OutDuration = 170;
    const double SlideOffset = 80;

    static readonly Color FallbackSurface = Color.FromArgb("#FFFFFF");
    static readonly Color FallbackOnSurface = Color.FromArgb("#1A1A1A");
    static readonly Color FallbackOnSurfaceVariant = Color.FromArgb("#4A4A4A");
    static readonly Color FallbackPrimary = Color.FromArgb("#7C3AED");
    static readonly Color FallbackOnPrimary = Color.FromArgb("#FFFFFF");
    static readonly Color FallbackSurfaceVariant = Color.FromArgb("#ECECEC");
    static readonly Color FallbackError = Color.FromArgb("#C20014");

    readonly DialogConfig config;
    readonly DialogContext context;
    readonly BoxView backdrop;
    readonly View card;
    bool isDismissing;
    Action? onDismissed;

    public DialogView(DialogConfig config, DialogOptions options)
    {
        this.config = config;
        this.context = new DialogContext(config, this.OnConfirm, this.OnCancel, this.OnSelect);

        this.backdrop = new BoxView
        {
            Color = Colors.Black,
            Opacity = 0
        };
        if (config.DismissOnBackdrop)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => this.OnCancel();
            this.backdrop.GestureRecognizers.Add(tap);
        }

        this.card = options.ContentTemplate is { } template
            ? this.BuildTemplatedCard(template)
            : this.BuildDefaultCard();

        this.card.BindingContext = this.context;
        // action sheets sit full-width at the bottom; everything else is a centered card
        if (config.Kind == DialogKind.ActionSheet)
        {
            this.card.HorizontalOptions = LayoutOptions.Fill;
            this.card.VerticalOptions = LayoutOptions.End;
        }
        else
        {
            this.card.HorizontalOptions = LayoutOptions.Center;
            this.card.VerticalOptions = LayoutOptions.Center;
        }

        var root = new Grid { Padding = new Thickness(24) };
        root.Children.Add(this.backdrop);
        Grid.SetColumnSpan(this.backdrop, 1);
        root.Children.Add(this.card);

        // Backdrop must ignore the card's padding inset
        this.backdrop.Margin = new Thickness(-24);

        this.Content = root;
        AutomationProperties.SetName(this, string.IsNullOrEmpty(config.Title) ? config.Message : config.Title);

        // While the dialog is up, the page behind it must not react to its keyboard shortcuts — and
        // on a hardware keyboard Escape should do what Cancel (or a backdrop tap) does.
        KeyboardShortcuts.SetIsModal(this, true);
        if (!string.IsNullOrEmpty(config.CancelText) || config.DismissOnBackdrop)
        {
            KeyboardShortcuts.GetShortcuts(this).Add(new KeyboardShortcut
            {
                Key = "Escape",
                TextInput = Shiny.Controls.Keyboard.TextInputBehavior.Always,
                Command = new Command(this.OnCancel)
            });
        }
    }

    public DialogOutcome Outcome { get; private set; }

    public void SetOnDismissed(Action callback) => this.onDismissed = callback;

    View BuildTemplatedCard(DataTemplate template)
        => template.CreateContent() as View ?? this.BuildDefaultCard();

    View BuildDefaultCard()
    {
        var stack = new VerticalStackLayout { Spacing = 12 };

        if (!string.IsNullOrEmpty(this.config.Title))
        {
            var title = new Label
            {
                Text = this.config.Title,
                FontSize = 17,
                FontAttributes = FontAttributes.Bold
            };
            ApplyColor(title, Label.TextColorProperty, this.config.TitleColor, ShinyThemeKeys.Color.OnSurface, FallbackOnSurface);
            stack.Add(title);
        }

        if (!string.IsNullOrEmpty(this.config.Message))
        {
            var message = new Label
            {
                Text = this.config.Message}.WithFontSize(ShinyThemeKeys.Type.BodyMediumSize);
            ApplyColor(message, Label.TextColorProperty, this.config.MessageColor, ShinyThemeKeys.Color.OnSurfaceVariant, FallbackOnSurfaceVariant);
            stack.Add(message);
        }

        if (this.config.Kind == DialogKind.ActionSheet)
        {
            this.AddActionSheetBody(stack);
        }
        else
        {
            if (this.config.Kind == DialogKind.Prompt)
            {
                var entry = new Entry
                {
                    Placeholder = this.config.Placeholder,
                    Keyboard = this.config.Keyboard,
                    IsPassword = this.config.IsPassword,
                    MaxLength = this.config.MaxLength,
                    ReturnType = ReturnType.Done
                };
                entry.SetBinding(Entry.TextProperty, new Binding(nameof(DialogContext.PromptValue), BindingMode.TwoWay));
                entry.Completed += (_, _) => this.OnConfirm();
                stack.Add(entry);
            }

            var buttons = new HorizontalStackLayout
            {
                Spacing = 8,
                HorizontalOptions = LayoutOptions.End
            };

            if (!string.IsNullOrEmpty(this.config.CancelText))
            {
                var cancel = this.BuildButton(
                    this.config.CancelText!,
                    this.config.CancelButtonColor, ShinyThemeKeys.Color.SurfaceVariant, FallbackSurfaceVariant,
                    this.config.CancelButtonTextColor, ShinyThemeKeys.Color.OnSurfaceVariant, FallbackOnSurfaceVariant
                );
                cancel.Clicked += (_, _) => this.OnCancel();
                buttons.Add(cancel);
            }

            var ok = this.BuildButton(
                this.config.OkText,
                this.config.OkButtonColor, ShinyThemeKeys.Color.Primary, FallbackPrimary,
                this.config.OkButtonTextColor, ShinyThemeKeys.Color.OnPrimary, FallbackOnPrimary
            );
            ok.Clicked += (_, _) => this.OnConfirm();
            buttons.Add(ok);

            stack.Add(buttons);
        }

        var border = new Border
        {
            Content = stack,
            Padding = new Thickness(20),
            StrokeThickness = 0,
            MaximumWidthRequest = this.config.MaxWidth,
            StrokeShape = new RoundRectangle { CornerRadius = this.config.CornerRadius }
        }
        .WithElevation(ShinyThemeKeys.Elevation.Level5);
        ApplyColor(border, VisualElement.BackgroundColorProperty, this.config.BackgroundColor, ShinyThemeKeys.Color.Surface, FallbackSurface);
        return border;
    }

    // ActionSheet: a full-width button per option (destructive one in red) + a separated cancel button.
    void AddActionSheetBody(VerticalStackLayout stack)
    {
        var list = new VerticalStackLayout { Spacing = 8 };
        foreach (var option in this.config.Actions)
        {
            var button = new Button
            {
                Text = option,
                FontSize = 15,
                Padding = new Thickness(16, 12),
                CornerRadius = 10,
                HorizontalOptions = LayoutOptions.Fill
            }.Neutralize();
            ApplyColor(button, Button.BackgroundColorProperty, null, ShinyThemeKeys.Color.SurfaceVariant, FallbackSurfaceVariant);
            if (string.Equals(option, this.config.DestructiveAction, StringComparison.Ordinal))
                ApplyColor(button, Button.TextColorProperty, null, ShinyThemeKeys.Color.Error, FallbackError);
            else
                ApplyColor(button, Button.TextColorProperty, null, ShinyThemeKeys.Color.OnSurface, FallbackOnSurface);

            var captured = option;
            button.Clicked += (_, _) => this.OnSelect(captured);
            list.Add(button);
        }

        // keep long option lists scrollable rather than overflowing the screen
        stack.Add(new ScrollView { Content = list, Orientation = ScrollOrientation.Vertical, MaximumHeightRequest = 360 });

        if (!string.IsNullOrEmpty(this.config.CancelText))
        {
            var cancel = this.BuildButton(
                this.config.CancelText!,
                this.config.CancelButtonColor, ShinyThemeKeys.Color.SurfaceVariant, FallbackSurfaceVariant,
                this.config.CancelButtonTextColor, ShinyThemeKeys.Color.OnSurfaceVariant, FallbackOnSurfaceVariant
            );
            cancel.HorizontalOptions = LayoutOptions.Fill;
            cancel.Margin = new Thickness(0, 4, 0, 0);
            cancel.Clicked += (_, _) => this.OnCancel();
            stack.Add(cancel);
        }
    }

    Button BuildButton(string text, Color? overrideBg, string bgToken, Color bgFallback, Color? overrideText, string textToken, Color textFallback)
    {
        var button = new Button
        {
            Text = text,
            FontAttributes = FontAttributes.Bold,
            Padding = new Thickness(16, 9),
            CornerRadius = 8
        }.Neutralize().WithFontSize(ShinyThemeKeys.Type.BodyMediumSize);
        ApplyColor(button, Button.BackgroundColorProperty, overrideBg, bgToken, bgFallback);
        ApplyColor(button, Button.TextColorProperty, overrideText, textToken, textFallback);
        return button;
    }

    static void ApplyColor(VisualElement element, BindableProperty property, Color? explicitColor, string token, Color fallback)
    {
        if (explicitColor is not null)
        {
            element.SetValue(property, explicitColor);
        }
        else
        {
            // Seed a hardcoded fallback first (for non-Application contexts), then bind the token so
            // a runtime theme/appearance switch restyles the dialog live.
            element.SetValue(property, fallback);
            element.SetDynamicResource(property, token);
        }
    }

    void OnConfirm()
    {
        this.Outcome = new DialogOutcome(true, this.config.Kind == DialogKind.Prompt ? this.context.PromptValue : null);
        _ = this.AnimateOutAsync();
    }

    void OnCancel()
    {
        this.Outcome = new DialogOutcome(false, null);
        _ = this.AnimateOutAsync();
    }

    void OnSelect(string option)
    {
        this.Outcome = new DialogOutcome(true, option);
        _ = this.AnimateOutAsync();
    }

    public async Task AnimateInAsync()
    {
        this.SetInitialCardState();
        this.IsVisible = true;

        var tasks = new List<Task>
        {
            this.backdrop.FadeToAsync(this.config.BackdropOpacity, InDuration, Easing.CubicOut)
        };

        switch (this.config.Animation)
        {
            case DialogAnimation.None:
                this.card.Opacity = 1;
                break;
            case DialogAnimation.Fade:
                tasks.Add(this.card.FadeToAsync(1, InDuration, Easing.CubicOut));
                break;
            case DialogAnimation.SlideTop:
            case DialogAnimation.SlideBottom:
            case DialogAnimation.SlideLeft:
            case DialogAnimation.SlideRight:
                tasks.Add(this.card.TranslateToAsync(0, 0, InDuration, Easing.CubicOut));
                tasks.Add(this.card.FadeToAsync(1, InDuration, Easing.CubicOut));
                break;
            case DialogAnimation.Zoom:
                tasks.Add(this.card.ScaleToAsync(1, InDuration, Easing.CubicOut));
                tasks.Add(this.card.FadeToAsync(1, InDuration, Easing.CubicOut));
                break;
            case DialogAnimation.Pop:
                tasks.Add(this.card.ScaleToAsync(1, InDuration + 80, Easing.SpringOut));
                tasks.Add(this.card.FadeToAsync(1, InDuration - 60, Easing.CubicOut));
                break;
        }

        await Task.WhenAll(tasks);
    }

    public async Task AnimateOutAsync()
    {
        if (this.isDismissing)
            return;
        this.isDismissing = true;

        var tasks = new List<Task>
        {
            this.backdrop.FadeToAsync(0, OutDuration, Easing.CubicIn)
        };

        switch (this.config.Animation)
        {
            case DialogAnimation.None:
                this.card.Opacity = 0;
                break;
            case DialogAnimation.Fade:
                tasks.Add(this.card.FadeToAsync(0, OutDuration, Easing.CubicIn));
                break;
            case DialogAnimation.SlideTop:
                tasks.Add(this.card.TranslateToAsync(0, -SlideOffset, OutDuration, Easing.CubicIn));
                tasks.Add(this.card.FadeToAsync(0, OutDuration, Easing.CubicIn));
                break;
            case DialogAnimation.SlideBottom:
                tasks.Add(this.card.TranslateToAsync(0, SlideOffset, OutDuration, Easing.CubicIn));
                tasks.Add(this.card.FadeToAsync(0, OutDuration, Easing.CubicIn));
                break;
            case DialogAnimation.SlideLeft:
                tasks.Add(this.card.TranslateToAsync(-SlideOffset, 0, OutDuration, Easing.CubicIn));
                tasks.Add(this.card.FadeToAsync(0, OutDuration, Easing.CubicIn));
                break;
            case DialogAnimation.SlideRight:
                tasks.Add(this.card.TranslateToAsync(SlideOffset, 0, OutDuration, Easing.CubicIn));
                tasks.Add(this.card.FadeToAsync(0, OutDuration, Easing.CubicIn));
                break;
            case DialogAnimation.Zoom:
            case DialogAnimation.Pop:
                tasks.Add(this.card.ScaleToAsync(0.9, OutDuration, Easing.CubicIn));
                tasks.Add(this.card.FadeToAsync(0, OutDuration, Easing.CubicIn));
                break;
        }

        await Task.WhenAll(tasks);

        this.IsVisible = false;
        this.onDismissed?.Invoke();
    }

    void SetInitialCardState()
    {
        this.card.TranslationX = 0;
        this.card.TranslationY = 0;
        this.card.Scale = 1;
        this.card.Opacity = this.config.Animation == DialogAnimation.None ? 1 : 0;

        switch (this.config.Animation)
        {
            case DialogAnimation.SlideTop:
                this.card.TranslationY = -SlideOffset;
                break;
            case DialogAnimation.SlideBottom:
                this.card.TranslationY = SlideOffset;
                break;
            case DialogAnimation.SlideLeft:
                this.card.TranslationX = -SlideOffset;
                break;
            case DialogAnimation.SlideRight:
                this.card.TranslationX = SlideOffset;
                break;
            case DialogAnimation.Zoom:
                this.card.Scale = 0.85;
                break;
            case DialogAnimation.Pop:
                this.card.Scale = 0.7;
                break;
        }
    }
}
