using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using Shiny.Controls.RangePickers;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

/// <summary>
/// The field-plus-popup shared by <see cref="DateRangePicker"/>, <see cref="TimeRangePicker"/> and
/// <see cref="DateTimeRangePicker"/>. The field shows the committed range; the popup edits a draft
/// that only reaches the bound properties on Apply, so Cancel (or tapping outside) leaves them untouched.
/// </summary>
/// <remarks>
/// The popup is a card over a scrim in the page's overlay layer, so it needs no
/// <c>ShinyContentPage</c> or <c>OverlayHost</c> - any <see cref="ContentPage"/> works.
/// </remarks>
public abstract partial class RangePickerBase : ContentView
{
    /// <summary>Appended to the picker's <see cref="Element.AutomationId"/> to name the field that opens the popup.</summary>
    public const string FieldAutomationIdSuffix = "_Field";

    /// <summary>Appended to the picker's <see cref="Element.AutomationId"/> to name the label showing the committed range.</summary>
    public const string ValueAutomationIdSuffix = "_Value";

    /// <summary>Appended to the picker's <see cref="Element.AutomationId"/> to name the popup card while it is open.</summary>
    public const string PopupAutomationIdSuffix = "_Popup";

    /// <summary>Appended to the picker's <see cref="Element.AutomationId"/> to name the popup's title (the draft summary).</summary>
    public const string TitleAutomationIdSuffix = "_Title";

    /// <summary>Appended to the picker's <see cref="Element.AutomationId"/> to name the popup's Apply button.</summary>
    public const string ApplyAutomationIdSuffix = "_Apply";

    /// <summary>Appended to the picker's <see cref="Element.AutomationId"/> to name the popup's Cancel button.</summary>
    public const string CancelAutomationIdSuffix = "_Cancel";

    /// <summary>Appended to the picker's <see cref="Element.AutomationId"/> to name the popup's Clear button.</summary>
    public const string ClearAutomationIdSuffix = "_Clear";

    readonly Border field;
    readonly Label valueLabel;
    readonly Label iconLabel;
    readonly AndroidBackButton backButton;
    Grid? overlay;
    Button? applyButton;
    Button? clearButton;
    Button? cancelButton;
    Label? titleLabel;
    PageOverlay.RangePickerLayer? layer;

    protected RangePickerBase(string iconGlyph)
    {
        this.valueLabel = new Label
        {
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };

        this.iconLabel = new Label
        {
            Text = iconGlyph,
            VerticalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        }.WithFontSize(ShinyThemeKeys.Type.BodyLargeSize);
        this.iconLabel.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

        var row = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        row.Add(this.valueLabel, 0, 0);
        row.Add(this.iconLabel, 1, 0);

        this.field = new Border
        {
            Padding = new Thickness(12, 10),
            MinimumHeightRequest = 44,
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerSmallRadius),
            Content = row
        }.WithStrokeThickness(ShinyThemeKeys.Border.Thin);
        this.field.SetDynamicResource(Border.StrokeProperty, ShinyThemeKeys.Brush.Outline);
        this.field.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Surface);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => this.Open();
        this.field.GestureRecognizers.Add(tap);

        this.backButton = new AndroidBackButton(() => this.Dispatcher.Dispatch(this.Cancel));

        // The back-button list is static: a page left with the popup open must not stay rooted by it.
        // Only the claim is dropped, not the popup - installing the overlay root on the first open
        // re-parents the page's content, which unloads and reloads the picker mid-open.
        this.Loaded += (_, _) => this.backButton.SetActive(this.overlay is not null);
        this.Unloaded += (_, _) => this.backButton.SetActive(false);
        this.Content = this.field;

        AutomationIds.Link(this, this.field, FieldAutomationIdSuffix);
        AutomationIds.Link(this, this.valueLabel, ValueAutomationIdSuffix);
        // The popup is rebuilt on every open, so it is (re)named there; this catches an id that
        // changes while it is open.
        AutomationIds.Track(this, () =>
        {
            if (this.overlay is not null)
                this.ApplyPopupAutomationIds();
        });

        StyleGuard.MarkReady(this, typeof(RangePickerBase));
    }


    /// <summary>Raised when the popup opens.</summary>
    public event EventHandler? Opened;

    /// <summary>Raised when the popup closes, whether applied or cancelled.</summary>
    public event EventHandler? Closed;

    /// <summary>The popup card, while open. Test seam.</summary>
    internal View? PopupCard { get; private set; }

    protected CultureInfo EffectiveCulture => this.Culture ?? CultureInfo.CurrentCulture;


    /// <summary>Opens the popup, seeding its draft from the committed value.</summary>
    public void Open()
    {
        if (this.overlay is not null || !this.IsEnabled)
            return;

        this.OnOpening();
        var content = this.CreatePopupContent();

        this.layer = PageOverlay.GetOrCreateLayer<PageOverlay.RangePickerLayer>(this, PageOverlay.Layers.RangePicker);
        this.overlay = this.BuildOverlay(content);
        this.RefreshPopupState();

        if (this.layer is not null)
        {
            this.layer.Children.Add(this.overlay);
            this.FadeIn(this.overlay);
        }

        this.backButton.SetActive(true);
        this.SetValue(IsOpenProperty, true);
        this.Opened?.Invoke(this, EventArgs.Empty);
    }


    void FadeIn(View view)
    {
        view.Opacity = 0;
        try
        {
            _ = view.FadeToAsync(1, 150, Easing.CubicOut);
        }
        catch (ArgumentException)
        {
            // No animation manager reachable yet (no handler up the tree): just show it.
            view.Opacity = 1;
            return;
        }

        // A host whose animation ticker never ticks would leave the popup invisible.
        this.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), () =>
        {
            if (ReferenceEquals(this.overlay, view))
                view.Opacity = 1;
        });
    }


    /// <summary>Commits the draft and closes. Does nothing while the draft is not a valid range.</summary>
    public void Apply()
    {
        if (this.overlay is null || !this.CanApply)
            return;

        this.CommitDraft();
        this.RefreshText();
        this.Close();
        FeedbackHelper.Execute(this, "RangeSelected");
    }


    /// <summary>Discards the draft and closes.</summary>
    public void Cancel() => this.Close();


    /// <summary>Clears the draft (and, with <see cref="AutoApply"/>-style pickers, the value).</summary>
    public void ClearDraft()
    {
        this.OnClearDraft();
        this.RefreshPopupState();
    }


    void Close()
    {
        if (this.overlay is null)
            return;

        this.backButton.SetActive(false);
        this.layer?.Children.Remove(this.overlay);
        this.overlay = null;
        this.PopupCard = null;
        this.applyButton = null;
        this.clearButton = null;
        this.cancelButton = null;
        this.titleLabel = null;
        this.OnClosed();
        this.SetValue(IsOpenProperty, false);
        this.Closed?.Invoke(this, EventArgs.Empty);
    }


    Grid BuildOverlay(View content)
    {
        var scrim = new BoxView { Opacity = 0.32 };
        scrim.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.Scrim);
        var scrimTap = new TapGestureRecognizer();
        scrimTap.Tapped += (_, _) => this.Cancel();
        scrim.GestureRecognizers.Add(scrimTap);

        this.titleLabel = new Label { Margin = new Thickness(4, 0, 4, 8), FontAttributes = FontAttributes.Bold }
            .WithFontSize(ShinyThemeKeys.Type.TitleMediumSize);
        this.titleLabel.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurface);

        this.clearButton = TextButton(this.ClearText, this.ClearDraft);
        var cancel = this.cancelButton = TextButton(this.CancelText, this.Cancel);
        this.applyButton = new Button
        {
            Text = this.ApplyText,
            CornerRadius = 20,
            HeightRequest = 40,
            Padding = new Thickness(20, 0)
        }.Neutralize().WithFontSize(ShinyThemeKeys.Type.LabelLargeSize);
        this.applyButton.SetDynamicResource(Button.BackgroundColorProperty, ShinyThemeKeys.Color.Primary);
        this.applyButton.SetDynamicResource(Button.TextColorProperty, ShinyThemeKeys.Color.OnPrimary);
        this.applyButton.Clicked += (_, _) => this.Apply();

        var footer = new Grid
        {
            ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)],
            ColumnSpacing = 8,
            Margin = new Thickness(0, 12, 0, 0)
        };
        footer.Add(this.clearButton, 0, 0);
        footer.Add(cancel, 2, 0);
        footer.Add(this.applyButton, 3, 0);

        var stack = new VerticalStackLayout { Children = { this.titleLabel, content, footer } };

        var card = new Border
        {
            Padding = new Thickness(16),
            Margin = new Thickness(16),
            MaximumWidthRequest = this.PopupMaxWidth,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerExtraLargeRadius),
            // One scroll owner per axis. Content that scrolls itself (the time picker's two columns) is
            // given the room instead; wrapping it in a card scroller too meant that on a short or
            // landscape screen both scrolled, and a flick in a column ran the card at its end.
            Content = this.PopupContentScrolls ? stack : HugContent(stack)
        }
        .WithStrokeThickness(ShinyThemeKeys.Border.Thin)
        .WithElevation(ShinyThemeKeys.Elevation.Level3);
        card.SetDynamicResource(Border.StrokeProperty, ShinyThemeKeys.Brush.OutlineVariant);
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHigh);

        // Taps on the card must not fall through to the scrim's cancel.
        card.GestureRecognizers.Add(new TapGestureRecognizer());

        this.PopupCard = card;
        this.ApplyPopupAutomationIds();
        var overlay = new Grid
        {
            InputTransparent = false,
            CascadeInputTransparent = false,
            Children = { scrim, card }
        };

        if (this.PopupContentScrolls)
        {
            void Fit(object? sender, EventArgs e) => this.FitPopupContent(overlay, card, stack, content);
            overlay.SizeChanged += Fit;
            card.SizeChanged += Fit;
        }
        return overlay;
    }


    /// <summary>
    /// Tells self-scrolling content how tall it may be: whatever the page has left once the card's own
    /// chrome (title, footer, padding, margin) is taken off.
    /// </summary>
    /// <remarks>
    /// The chrome is added up from the card's other rows rather than taken as "card less content": a
    /// card taller than the page is clipped to it, which would understate the chrome exactly when it
    /// matters. None of it changes when the content is resized, so this settles in one pass.
    /// </remarks>
    void FitPopupContent(Grid overlay, Border card, VerticalStackLayout stack, View content)
    {
        if (!ReferenceEquals(this.overlay, overlay) || overlay.Height <= 0)
            return;

        var chrome = card.Padding.VerticalThickness + (card.StrokeThickness * 2);
        foreach (var child in stack.Children.OfType<View>())
        {
            if (ReferenceEquals(child, content) || !child.IsVisible)
                continue;

            if (child.Height <= 0)
                return; // not laid out yet; the next SizeChanged comes back here

            chrome += child.Height + child.Margin.VerticalThickness;
        }

        var room = overlay.Height - card.Margin.VerticalThickness - chrome;
        if (room > 0)
            this.OnPopupContentRoom(room);
    }


    /// <summary>
    /// The card's scroller for content that does not scroll itself, no taller than its content wants.
    /// </summary>
    static ScrollView HugContent(View content) => new HuggingScrollView { Content = content };


    /// <summary>
    /// A vertical ScrollView that reports its content's desired height when that is less than the
    /// height it was offered.
    /// </summary>
    /// <remarks>
    /// On Android the native measure of a vertical ScrollView returns the whole height it is offered,
    /// not its content's, so the centred card grew to nearly the page with the calendar at the top and
    /// empty card below it (DateRangePicker on a Galaxy S21: the stack wants 400 dp, the scroller
    /// reported 712). The content's own height is no use as a cap - it is arranged to the scroller's
    /// height, so it is 712 as well - but its DesiredSize after the measure is the 400. On a screen too
    /// short for the content the offered height is the smaller one and the card scrolls as before.
    /// </remarks>
    sealed class HuggingScrollView : ScrollView
    {
        protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
        {
            var measured = base.MeasureOverride(widthConstraint, heightConstraint);
            if (this.Content is IView content)
            {
                var wanted = content.DesiredSize.Height + this.Padding.VerticalThickness;
                if (wanted > 0 && wanted < measured.Height)
                    return new Size(measured.Width, wanted);
            }
            return measured;
        }
    }


    static Button TextButton(string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            BackgroundColor = Colors.Transparent,
            HeightRequest = 40,
            Padding = new Thickness(12, 0)
        }.Neutralize().WithFontSize(ShinyThemeKeys.Type.LabelLargeSize);
        button.SetDynamicResource(Button.TextColorProperty, ShinyThemeKeys.Color.Primary);
        button.Clicked += (_, _) => action();
        return button;
    }


    /// <summary>
    /// Names the open popup's parts after the picker. The card lives in the page's overlay layer, not
    /// under the picker, so a test can only find it by these ids. Pickers override to name their content.
    /// </summary>
    protected virtual void ApplyPopupAutomationIds()
    {
        if (this.PopupCard is { } card)
            AutomationIds.Set(card, AutomationIds.For(this, PopupAutomationIdSuffix));
        if (this.titleLabel is { } title)
            AutomationIds.Set(title, AutomationIds.For(this, TitleAutomationIdSuffix));
        if (this.applyButton is { } apply)
            AutomationIds.Set(apply, AutomationIds.For(this, ApplyAutomationIdSuffix));
        if (this.cancelButton is { } cancel)
            AutomationIds.Set(cancel, AutomationIds.For(this, CancelAutomationIdSuffix));
        if (this.clearButton is { } clear)
            AutomationIds.Set(clear, AutomationIds.For(this, ClearAutomationIdSuffix));
    }


    /// <summary>Call when the draft changes: re-evaluates Apply and the popup title.</summary>
    protected void RefreshPopupState()
    {
        if (this.applyButton is not null)
            this.applyButton.IsEnabled = this.CanApply;

        if (this.clearButton is not null)
            this.clearButton.IsVisible = this.ShowClear;

        if (this.titleLabel is not null)
        {
            var title = this.DraftSummary() ?? this.Title;
            this.titleLabel.Text = title;
            this.titleLabel.IsVisible = !string.IsNullOrEmpty(title);
        }
    }


    /// <summary>Call when the committed value changes: re-renders the field.</summary>
    /// <remarks>Each picker calls it at the end of its own constructor - the base cannot, the value it formats does not exist yet.</remarks>
    protected void RefreshText()
    {
        var text = this.FormatValue();
        if (string.IsNullOrEmpty(text))
        {
            this.valueLabel.Text = this.Placeholder ?? this.DefaultPlaceholder;
            ThemeProbe.Tint(this.valueLabel, Label.TextColorProperty, this.PlaceholderColor, ShinyThemeKeys.Color.OnSurfaceVariant);
        }
        else
        {
            this.valueLabel.Text = text;
            ThemeProbe.Tint(this.valueLabel, Label.TextColorProperty, this.TextColor, ShinyThemeKeys.Color.OnSurface);
        }

        this.valueLabel.SetTokenOrValue(Label.FontSizeProperty, this.FontSize, ShinyThemeKeys.Type.BodyLargeSize);
        SemanticProperties.SetDescription(this.field, string.IsNullOrEmpty(text) ? this.Placeholder ?? this.DefaultPlaceholder : text);
    }


    /// <summary>The field text when there is no value and no <see cref="Placeholder"/>.</summary>
    protected abstract string DefaultPlaceholder { get; }

    /// <summary>
    /// True when <see cref="CreatePopupContent"/> scrolls itself vertically, so the card must not wrap it
    /// in a scroller of its own. The content is then sized through <see cref="OnPopupContentRoom"/>.
    /// </summary>
    protected virtual bool PopupContentScrolls => false;

    /// <summary>
    /// The vertical room self-scrolling content has inside the card on this screen. Only called when
    /// <see cref="PopupContentScrolls"/> is true; called again when the page resizes (rotation).
    /// </summary>
    protected virtual void OnPopupContentRoom(double height) { }

    /// <summary>The widest the popup card may be.</summary>
    protected virtual double PopupMaxWidth => 400;

    /// <summary>Whether the draft can be applied.</summary>
    protected abstract bool CanApply { get; }

    /// <summary>Seed the draft from the committed value.</summary>
    protected abstract void OnOpening();

    /// <summary>Build the editor for the draft.</summary>
    protected abstract View CreatePopupContent();

    /// <summary>Write the draft to the committed properties.</summary>
    protected abstract void CommitDraft();

    protected abstract void OnClearDraft();

    /// <summary>The committed value as field text, or null when there is none.</summary>
    protected abstract string? FormatValue();

    /// <summary>The draft as a popup title ("Mar 3 – 9, 2026"), or null to show <see cref="Title"/>.</summary>
    protected virtual string? DraftSummary() => null;

    protected virtual void OnClosed() { }
}
