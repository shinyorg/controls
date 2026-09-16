using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Themes;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.QuickEntry;

/// <summary>
/// The default content of the quick entry popup: an assistant-style prompt bar — animated orb,
/// single-line prompt, and an expanding area beneath it for suggestions and a response.
/// </summary>
/// <remarks>
/// <para>
/// The view is deliberately AI-shaped but AI-agnostic: it raises <see cref="Submitted"/> and
/// leaves the request to you. Push results back by setting <see cref="IsBusy"/> while you work and
/// assigning <see cref="ResponseContent"/> (a <c>MarkdownView</c>, a <c>ChatView</c>, a
/// <c>Label</c>) when you have something to show.
/// </para>
/// <para>
/// It is an ordinary <see cref="ContentView"/>, so it is equally usable inside a normal page —
/// the popup is just where it usually lives.
/// </para>
/// </remarks>
public class PromptView : ContentView, IQuickEntryKeyHandler, IQuickEntryPresentationAware, IQuickEntryBusyState, IQuickEntryAutoSize
{
    // -----------------------------------------------------------------------------------------
    // Content
    // -----------------------------------------------------------------------------------------

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(PromptView), String.Empty,
        defaultBindingMode: BindingMode.TwoWay,
        propertyChanged: (b, _, _) => ((PromptView)b).OnTextChanged()
    );

    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder), typeof(string), typeof(PromptView), "Ask anything…",
        propertyChanged: (b, _, n) => ((PromptView)b).Apply(v => v.entry.Placeholder = (string?)n)
    );

    public static readonly BindableProperty IsBusyProperty = BindableProperty.Create(
        nameof(IsBusy), typeof(bool), typeof(PromptView), false,
        propertyChanged: (b, _, _) =>
        {
            var view = (PromptView)b;
            view.Apply(v => v.UpdateBusy());
            view.BusyChanged?.Invoke(view, EventArgs.Empty);
        }
    );

    public static readonly BindableProperty BusyTextProperty = BindableProperty.Create(
        nameof(BusyText), typeof(string), typeof(PromptView), "Thinking…",
        propertyChanged: (b, _, n) => ((PromptView)b).Apply(v => v.busyLabel.Text = (string?)n)
    );

    public static readonly BindableProperty SuggestionsProperty = BindableProperty.Create(
        nameof(Suggestions), typeof(IEnumerable), typeof(PromptView), null,
        propertyChanged: (b, o, n) => ((PromptView)b).OnSuggestionsChanged(o as IEnumerable, n as IEnumerable)
    );

    public static readonly BindableProperty SuggestionTemplateProperty = BindableProperty.Create(
        nameof(SuggestionTemplate), typeof(DataTemplate), typeof(PromptView), null,
        propertyChanged: (b, _, _) => ((PromptView)b).Apply(v => v.RebuildSuggestions())
    );

    public static readonly BindableProperty MaxVisibleSuggestionsProperty = BindableProperty.Create(
        nameof(MaxVisibleSuggestions), typeof(int), typeof(PromptView), 6,
        propertyChanged: (b, _, _) => ((PromptView)b).Apply(v => v.RebuildSuggestions())
    );

    public static readonly BindableProperty IconProperty = BindableProperty.Create(
        nameof(Icon), typeof(ImageSource), typeof(PromptView), null,
        propertyChanged: (b, _, _) => ((PromptView)b).Apply(v => v.RebuildLeading())
    );

    public static readonly BindableProperty IconContentProperty = BindableProperty.Create(
        nameof(IconContent), typeof(View), typeof(PromptView), null,
        propertyChanged: (b, _, _) => ((PromptView)b).Apply(v => v.RebuildLeading())
    );

    public static readonly BindableProperty ShowIconProperty = BindableProperty.Create(
        nameof(ShowIcon), typeof(bool), typeof(PromptView), true,
        propertyChanged: (b, _, _) => ((PromptView)b).Apply(v => v.RebuildLeading())
    );

    public static readonly BindableProperty IconSizeProperty = BindableProperty.Create(
        nameof(IconSize), typeof(double), typeof(PromptView), 26d,
        propertyChanged: (b, _, _) => ((PromptView)b).Apply(v => v.RebuildLeading())
    );

    public static readonly BindableProperty DropdownContentProperty = BindableProperty.Create(
        nameof(DropdownContent), typeof(View), typeof(PromptView), null,
        propertyChanged: (b, _, n) => ((PromptView)b).Apply(v =>
        {
            v.dropdownHost.Content = (View?)n;
            v.dropdownHost.IsVisible = n != null;
            v.UpdateBodyVisibility();
        })
    );

    public static readonly BindableProperty DropdownHeightProperty = BindableProperty.Create(
        nameof(DropdownHeight), typeof(double), typeof(PromptView), -1d,
        propertyChanged: (b, _, _) => ((PromptView)b).Apply(v => v.UpdateDropdownHeight())
    );

    public static readonly BindableProperty ResponseContentProperty = BindableProperty.Create(
        nameof(ResponseContent), typeof(View), typeof(PromptView), null,
        propertyChanged: (b, _, _) => ((PromptView)b).Apply(v => v.UpdateResponse())
    );

    public static readonly BindableProperty ResponseProperty = BindableProperty.Create(
        nameof(Response), typeof(string), typeof(PromptView), null,
        propertyChanged: (b, _, _) => ((PromptView)b).Apply(v => v.UpdateResponse())
    );

    public static readonly BindableProperty FooterProperty = BindableProperty.Create(
        nameof(Footer), typeof(View), typeof(PromptView), null,
        propertyChanged: (b, _, n) => ((PromptView)b).Apply(v =>
        {
            v.footerHost.Content = (View?)n;
            v.footerHost.IsVisible = n != null;
            v.UpdateBodyVisibility();
        })
    );

    // -----------------------------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------------------------

    public static readonly BindableProperty SubmitCommandProperty = BindableProperty.Create(
        nameof(SubmitCommand), typeof(ICommand), typeof(PromptView), null
    );

    public static readonly BindableProperty SuggestionCommandProperty = BindableProperty.Create(
        nameof(SuggestionCommand), typeof(ICommand), typeof(PromptView), null
    );

    public static readonly BindableProperty MicrophoneCommandProperty = BindableProperty.Create(
        nameof(MicrophoneCommand), typeof(ICommand), typeof(PromptView), null
    );

    public static readonly BindableProperty ShowMicrophoneProperty = BindableProperty.Create(
        nameof(ShowMicrophone), typeof(bool), typeof(PromptView), false,
        propertyChanged: (b, _, n) => ((PromptView)b).Apply(v => v.micButton.IsVisible = (bool)n)
    );

    public static readonly BindableProperty ShowSubmitButtonProperty = BindableProperty.Create(
        nameof(ShowSubmitButton), typeof(bool), typeof(PromptView), true,
        propertyChanged: (b, _, _) => ((PromptView)b).Apply(v => v.UpdateBusy())
    );

    public static readonly BindableProperty ClearOnSubmitProperty = BindableProperty.Create(
        nameof(ClearOnSubmit), typeof(bool), typeof(PromptView), true
    );

    // -----------------------------------------------------------------------------------------
    // Tools
    // -----------------------------------------------------------------------------------------

    public static readonly BindableProperty LeadingToolsProperty = BindableProperty.Create(
        nameof(LeadingTools), typeof(IList<PromptTool>), typeof(PromptView), null,
        propertyChanged: (b, o, n) => ((PromptView)b).Apply(v => v.OnToolsChanged(
            (IList<PromptTool>?)o, (IList<PromptTool>?)n, v.leadingToolsLayout))
    );

    public static readonly BindableProperty TrailingToolsProperty = BindableProperty.Create(
        nameof(TrailingTools), typeof(IList<PromptTool>), typeof(PromptView), null,
        propertyChanged: (b, o, n) => ((PromptView)b).Apply(v => v.OnToolsChanged(
            (IList<PromptTool>?)o, (IList<PromptTool>?)n, v.trailingToolsLayout))
    );

    // -----------------------------------------------------------------------------------------
    // Appearance. Defaults are app-theme bindings applied in the constructor, so they follow
    // light/dark on their own and are replaced wholesale the moment a consumer assigns a value.
    // -----------------------------------------------------------------------------------------

    public static readonly BindableProperty AccentColorProperty = BindableProperty.Create(
        nameof(AccentColor), typeof(Color), typeof(PromptView), null,
        propertyChanged: (b, _, _) => ((PromptView)b).Apply(v => v.UpdateAccent())
    );

    public static readonly BindableProperty SurfaceColorProperty = BindableProperty.Create(
        nameof(SurfaceColor), typeof(Color), typeof(PromptView), null,
        propertyChanged: (b, _, n) => ((PromptView)b).Apply(v => v.card.BackgroundColor = (Color?)n)
    );

    public static readonly BindableProperty OutlineColorProperty = BindableProperty.Create(
        nameof(OutlineColor), typeof(Color), typeof(PromptView), null,
        propertyChanged: (b, _, n) => ((PromptView)b).Apply(v =>
        {
            v.card.Stroke = new SolidColorBrush((Color?)n ?? Colors.Transparent);
            v.separator.Color = (Color?)n ?? Colors.Transparent;
        })
    );

    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor), typeof(Color), typeof(PromptView), null,
        propertyChanged: (b, _, _) => ((PromptView)b).Apply(v => v.UpdateTextColors())
    );

    public static readonly BindableProperty PlaceholderColorProperty = BindableProperty.Create(
        nameof(PlaceholderColor), typeof(Color), typeof(PromptView), null,
        propertyChanged: (b, _, n) => ((PromptView)b).Apply(v => v.entry.PlaceholderColor = (Color?)n)
    );

    public static readonly BindableProperty SubtleTextColorProperty = BindableProperty.Create(
        nameof(SubtleTextColor), typeof(Color), typeof(PromptView), null,
        propertyChanged: (b, _, _) => ((PromptView)b).Apply(v => v.UpdateTextColors())
    );

    public static readonly BindableProperty HighlightColorProperty = BindableProperty.Create(
        nameof(HighlightColor), typeof(Color), typeof(PromptView), null,
        propertyChanged: (b, _, _) => ((PromptView)b).Apply(v => v.UpdateHighlight())
    );

    public static readonly BindableProperty CornerRadiusProperty = BindableProperty.Create(
        nameof(CornerRadius), typeof(double), typeof(PromptView), ThemeTokens.Unset,
        propertyChanged: (b, _, _) => ((PromptView)b).Apply(v => v.UpdateCornerRadius())
    );

    public static readonly BindableProperty PromptFontSizeProperty = BindableProperty.Create(
        nameof(PromptFontSize), typeof(double), typeof(PromptView), 17d,
        propertyChanged: (b, _, _) => ((PromptView)b).Apply(v => v.UpdatePromptMetrics())
    );

    // -----------------------------------------------------------------------------------------

    readonly Border card;
    readonly VerticalStackLayout cardContent;
    readonly RoundRectangle cardShape;
    readonly Grid inputRow;
    readonly HorizontalStackLayout leadingSlot;
    readonly HorizontalStackLayout leadingToolsLayout;
    readonly HorizontalStackLayout trailingToolsLayout;
    readonly ContentView leadingHost;
    readonly PromptOrbView orb;
    readonly BorderlessEntry entry;
    readonly Border micButton;
    readonly Label micGlyph;
    readonly Border submitButton;
    readonly Label submitGlyph;
    readonly ActivityIndicator busyIndicator;
    readonly BoxView separator;
    readonly VerticalStackLayout body;
    readonly HorizontalStackLayout busyRow;
    readonly Label busyLabel;
    readonly ContentView dropdownContainer;
    readonly ScrollView dropdownScroll;
    readonly VerticalStackLayout dropdownStack;
    readonly ContentView responseHost;
    readonly Label responseLabel;
    readonly ContentView dropdownHost;
    readonly VerticalStackLayout suggestionList;
    readonly ContentView footerHost;

    // Used only before the theme dictionary has resolved — a control constructed outside a running
    // app, or a pack missing the key. The tokens in ApplyThemeDefaults are the real source.
    static readonly Color FallbackOnSurface = Color.FromArgb("#15151A");
    static readonly Color FallbackOnSurfaceVariant = Color.FromArgb("#65656F");
    static readonly Color FallbackPrimary = Color.FromArgb("#6D4AFF");
    static readonly Color FallbackHighlight = Color.FromArgb("#12000000");

    readonly List<View> suggestionRows = new();
    readonly List<object> suggestionItems = new();
    IDisposable? suggestionsSubscription;
    IDisposable? leadingToolsSubscription;
    IDisposable? trailingToolsSubscription;
    readonly List<PromptTool> attachedLeadingTools = new();
    readonly List<PromptTool> attachedTrailingTools = new();
    int highlightIndex = -1;
    bool built;
    bool measuring;

    public PromptView()
    {
        this.orb = new PromptOrbView
        {
            VerticalOptions = LayoutOptions.Center,
            HeightRequest = 26,
            WidthRequest = 26
        };
        // Top-aligned, not centred. AppKit draws an NSTextField's text at the top of its frame and
        // gives that frame more height than MAUI measured for it, so a centred orb ends up sitting
        // well below the prompt it belongs to. Anchoring the whole row to the top keeps the three
        // pieces level whatever the native field does with the space.
        this.leadingHost = new ContentView { VerticalOptions = LayoutOptions.Start };
        this.leadingToolsLayout = new HorizontalStackLayout
        {
            Spacing = 2,
            IsVisible = false,
            VerticalOptions = LayoutOptions.Start
        };
        this.leadingSlot = new HorizontalStackLayout
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Start,
            Children = { this.leadingHost, this.leadingToolsLayout }
        };

        this.entry = new BorderlessEntry
        {
            BackgroundColor = Colors.Transparent,
            VerticalOptions = LayoutOptions.Start,
            ReturnType = ReturnType.Send
        };
        this.entry.TextChanged += this.OnEntryTextChanged;
        this.entry.Completed += (_, _) => this.Submit();
        this.entry.HandlerChanged += (_, _) => PromptEntryPolish.Apply(this.entry.Handler?.PlatformView);

        this.micGlyph = new Label { Text = "🎙", HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
            .WithFontSize(ShinyThemeKeys.Type.BodyLargeSize);
        this.micButton = BuildGlyphButton(this.micGlyph, () => this.MicrophoneCommand?.Execute(null));
        this.micButton.IsVisible = false;

        this.submitGlyph = new Label { Text = "↵", HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
            .WithFontSize(ShinyThemeKeys.Type.TitleMediumSize);
        this.submitButton = BuildGlyphButton(this.submitGlyph, this.Submit);

        this.busyIndicator = new ActivityIndicator
        {
            IsVisible = false,
            IsRunning = false,
            WidthRequest = 18,
            HeightRequest = 18,
            VerticalOptions = LayoutOptions.Start
        };

        this.trailingToolsLayout = new HorizontalStackLayout
        {
            Spacing = 2,
            IsVisible = false,
            VerticalOptions = LayoutOptions.Start
        };

        var trailing = new HorizontalStackLayout
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Start,
            Children = { this.busyIndicator, this.trailingToolsLayout, this.micButton, this.submitButton }
        };

        this.inputRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12,
            Padding = new Thickness(18, 14, 12, 14),
            MinimumHeightRequest = 44
        };
        this.inputRow.Add(this.leadingSlot, 0);
        this.inputRow.Add(this.entry, 1);
        this.inputRow.Add(trailing, 2);

        this.separator = new BoxView { HeightRequest = 1, IsVisible = false };

        this.busyLabel = new Label { Text = "Thinking…", VerticalOptions = LayoutOptions.Center }
            .WithFontSize(ShinyThemeKeys.Type.BodyMediumSize);
        this.busyRow = new HorizontalStackLayout
        {
            Spacing = 8,
            IsVisible = false,
            Padding = new Thickness(18, 10),
            Children = { this.busyLabel }
        };

        this.responseHost = new ContentView { Padding = new Thickness(18, 6, 18, 12) };
        this.responseLabel = new Label { LineHeight = 1.35 }
            .WithFontSize(ShinyThemeKeys.Type.BodyMediumSize);
        this.dropdownHost = new ContentView { IsVisible = false };
        this.suggestionList = new VerticalStackLayout { Padding = new Thickness(8, 6) };
        this.footerHost = new ContentView { IsVisible = false, Padding = new Thickness(18, 8) };

        this.dropdownStack = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Start,
            Children = { this.dropdownHost, this.suggestionList, this.responseHost }
        };
        this.dropdownScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Vertical,
            VerticalScrollBarVisibility = ScrollBarVisibility.Default
        };
        this.dropdownContainer = new ContentView { VerticalOptions = LayoutOptions.Start };

        this.body = new VerticalStackLayout
        {
            IsVisible = false,
            VerticalOptions = LayoutOptions.Start,
            Children = { this.busyRow, this.dropdownContainer, this.footerHost }
        };

        this.cardContent = new VerticalStackLayout
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Start,
            Children = { this.inputRow, this.separator, this.body }
        };

        this.cardShape = new RoundRectangle();
        this.card = new Border
        {
            StrokeShape = this.cardShape,
            StrokeThickness = 1,
            Padding = 0,
            // Start, not the default Fill: the popup sizes its window to this view's desired height,
            // so a card that stretches to whatever space it is offered reports the space back as
            // its own height and the window can only ever grow.
            VerticalOptions = LayoutOptions.Start,
            Content = this.cardContent,
        };
        this.card.WithElevation(ShinyThemeKeys.Elevation.Level3);

        this.Content = this.card;

        // The card is the only child that sizes itself to the content — this view is a ContentView
        // and stretches to whatever the popup window currently is — so the card is what the host is
        // told about.
        this.card.SizeChanged += (_, _) => this.RaiseDesiredHeightChanged();
        this.card.MeasureInvalidated += (_, _) => this.RaiseDesiredHeightChanged();

        this.built = true;

        // After `built`, so the property callbacks that hang the collections off their layouts run.
        this.LeadingTools = new ObservableCollection<PromptTool>();
        this.TrailingTools = new ObservableCollection<PromptTool>();

        this.ApplyThemeDefaults();
        this.ApplyAll();
    }

    // -----------------------------------------------------------------------------------------
    // CLR wrappers
    // -----------------------------------------------------------------------------------------

    /// <summary>The prompt text. Two-way by default.</summary>
    public string Text
    {
        get => (string)this.GetValue(TextProperty);
        set => this.SetValue(TextProperty, value);
    }

    /// <summary>Prompt placeholder. Default "Ask anything…".</summary>
    public string Placeholder
    {
        get => (string)this.GetValue(PlaceholderProperty);
        set => this.SetValue(PlaceholderProperty, value);
    }

    /// <summary>Set while your request is in flight: spins the orb, shows a spinner, and swaps the submit glyph for a stop button.</summary>
    public bool IsBusy
    {
        get => (bool)this.GetValue(IsBusyProperty);
        set => this.SetValue(IsBusyProperty, value);
    }

    /// <summary>Status line shown under the prompt while <see cref="IsBusy"/> is set and no <see cref="ResponseContent"/> has arrived yet.</summary>
    public string BusyText
    {
        get => (string)this.GetValue(BusyTextProperty);
        set => this.SetValue(BusyTextProperty, value);
    }

    /// <summary>
    /// Rows shown under the prompt. <see cref="PromptSuggestion"/> renders with the built-in
    /// template; any other type needs a <see cref="SuggestionTemplate"/>. Honours
    /// <see cref="INotifyCollectionChanged"/>, so an <c>ObservableCollection</c> updated as the
    /// user types behaves like autocomplete.
    /// </summary>
    public IEnumerable? Suggestions
    {
        get => (IEnumerable?)this.GetValue(SuggestionsProperty);
        set => this.SetValue(SuggestionsProperty, value);
    }

    /// <summary>Render your own suggestion rows. The template's binding context is the item.</summary>
    public DataTemplate? SuggestionTemplate
    {
        get => (DataTemplate?)this.GetValue(SuggestionTemplateProperty);
        set => this.SetValue(SuggestionTemplateProperty, value);
    }

    /// <summary>How many suggestion rows to render. Default 6 — the popup is a HUD, not a list view.</summary>
    public int MaxVisibleSuggestions
    {
        get => (int)this.GetValue(MaxVisibleSuggestionsProperty);
        set => this.SetValue(MaxVisibleSuggestionsProperty, value);
    }

    /// <summary>
    /// Replaces the animated orb with an image. Ignored when <see cref="IconContent"/> is set, and
    /// <see cref="ShowIcon"/> hides the slot entirely.
    /// </summary>
    public ImageSource? Icon
    {
        get => (ImageSource?)this.GetValue(IconProperty);
        set => this.SetValue(IconProperty, value);
    }

    /// <summary>
    /// Replaces the leading slot with an arbitrary view — your own mark, an avatar, a status dot.
    /// Wins over <see cref="Icon"/>.
    /// </summary>
    public View? IconContent
    {
        get => (View?)this.GetValue(IconContentProperty);
        set => this.SetValue(IconContentProperty, value);
    }

    /// <summary>Show the leading slot at all. Default true.</summary>
    public bool ShowIcon
    {
        get => (bool)this.GetValue(ShowIconProperty);
        set => this.SetValue(ShowIconProperty, value);
    }

    /// <summary>Size of the built-in orb or <see cref="Icon"/> image, in device-independent pixels. Default 26.</summary>
    public double IconSize
    {
        get => (double)this.GetValue(IconSizeProperty);
        set => this.SetValue(IconSizeProperty, value);
    }

    /// <summary>
    /// Arbitrary content for the dropdown — the area that expands under the prompt. Set this to take
    /// over that region completely: a command palette, recent items, a model picker, anything.
    /// It renders above <see cref="Suggestions"/>, so the two can also be used together.
    /// </summary>
    public View? DropdownContent
    {
        get => (View?)this.GetValue(DropdownContentProperty);
        set => this.SetValue(DropdownContentProperty, value);
    }

    /// <summary>
    /// Fixed height for the dropdown area, in device-independent pixels. Leave unset (the default,
    /// -1) and it sizes itself to whatever is in it — the popup window grows and shrinks to match.
    /// Set a value and the area is pinned to that height and scrolls instead, which is what you want
    /// for a list that changes length as the user types and would otherwise make the window jump
    /// about under the pointer.
    /// </summary>
    public double DropdownHeight
    {
        get => (double)this.GetValue(DropdownHeightProperty);
        set => this.SetValue(DropdownHeightProperty, value);
    }

    /// <summary>The response area. Assign anything — a Label, a MarkdownView, a ChatView. Null collapses it.</summary>
    /// <remarks>Wins over <see cref="Response"/> when both are set.</remarks>
    public View? ResponseContent
    {
        get => (View?)this.GetValue(ResponseContentProperty);
        set => this.SetValue(ResponseContentProperty, value);
    }

    /// <summary>
    /// The answer as plain text, rendered in a built-in label. For rich content use
    /// <see cref="ResponseContent"/>, which wins when both are set.
    /// </summary>
    /// <remarks>
    /// This is also what a read-aloud tool speaks — a <see cref="View"/> has no text to hand it, so
    /// content pushed through <see cref="ResponseContent"/> alone is invisible to one.
    /// </remarks>
    public string? Response
    {
        get => (string?)this.GetValue(ResponseProperty);
        set => this.SetValue(ResponseProperty, value);
    }

    /// <summary>Tools docked beside the orb, at the leading edge of the prompt row.</summary>
    public IList<PromptTool>? LeadingTools
    {
        get => (IList<PromptTool>?)this.GetValue(LeadingToolsProperty);
        set => this.SetValue(LeadingToolsProperty, value);
    }

    /// <summary>Tools docked at the trailing edge, before the built-in microphone and submit glyphs.</summary>
    public IList<PromptTool>? TrailingTools
    {
        get => (IList<PromptTool>?)this.GetValue(TrailingToolsProperty);
        set => this.SetValue(TrailingToolsProperty, value);
    }

    /// <summary>Optional strip along the bottom — a model picker, token count, keyboard legend.</summary>
    public View? Footer
    {
        get => (View?)this.GetValue(FooterProperty);
        set => this.SetValue(FooterProperty, value);
    }

    /// <summary>Invoked on submit with the prompt text as its parameter. <see cref="Submitted"/> fires either way.</summary>
    public ICommand? SubmitCommand
    {
        get => (ICommand?)this.GetValue(SubmitCommandProperty);
        set => this.SetValue(SubmitCommandProperty, value);
    }

    /// <summary>Invoked when a suggestion is chosen, with the item as its parameter.</summary>
    public ICommand? SuggestionCommand
    {
        get => (ICommand?)this.GetValue(SuggestionCommandProperty);
        set => this.SetValue(SuggestionCommandProperty, value);
    }

    /// <summary>Invoked by the microphone button. Wire it to your speech-to-text and write the result into <see cref="Text"/>.</summary>
    public ICommand? MicrophoneCommand
    {
        get => (ICommand?)this.GetValue(MicrophoneCommandProperty);
        set => this.SetValue(MicrophoneCommandProperty, value);
    }

    /// <summary>Show the microphone button. Default false — there is no speech engine in this package.</summary>
    public bool ShowMicrophone
    {
        get => (bool)this.GetValue(ShowMicrophoneProperty);
        set => this.SetValue(ShowMicrophoneProperty, value);
    }

    /// <summary>Show the submit / stop button. Default true.</summary>
    public bool ShowSubmitButton
    {
        get => (bool)this.GetValue(ShowSubmitButtonProperty);
        set => this.SetValue(ShowSubmitButtonProperty, value);
    }

    /// <summary>Empty the prompt after a successful submit. Default true.</summary>
    public bool ClearOnSubmit
    {
        get => (bool)this.GetValue(ClearOnSubmitProperty);
        set => this.SetValue(ClearOnSubmitProperty, value);
    }

    /// <summary>Drives the orb, the submit button and the suggestion highlight. Defaults to a violet that reads on both light and dark.</summary>
    public Color? AccentColor
    {
        get => (Color?)this.GetValue(AccentColorProperty);
        set => this.SetValue(AccentColorProperty, value);
    }

    /// <summary>The card background.</summary>
    public Color? SurfaceColor
    {
        get => (Color?)this.GetValue(SurfaceColorProperty);
        set => this.SetValue(SurfaceColorProperty, value);
    }

    /// <summary>The hairline around the card and between its sections.</summary>
    public Color? OutlineColor
    {
        get => (Color?)this.GetValue(OutlineColorProperty);
        set => this.SetValue(OutlineColorProperty, value);
    }

    /// <summary>Primary text colour — the prompt and suggestion titles.</summary>
    public Color? TextColor
    {
        get => (Color?)this.GetValue(TextColorProperty);
        set => this.SetValue(TextColorProperty, value);
    }

    /// <summary>Prompt placeholder colour.</summary>
    public Color? PlaceholderColor
    {
        get => (Color?)this.GetValue(PlaceholderColorProperty);
        set => this.SetValue(PlaceholderColorProperty, value);
    }

    /// <summary>Secondary text colour — suggestion descriptions, the busy line, glyph buttons.</summary>
    public Color? SubtleTextColor
    {
        get => (Color?)this.GetValue(SubtleTextColorProperty);
        set => this.SetValue(SubtleTextColorProperty, value);
    }

    /// <summary>Background of the highlighted suggestion row.</summary>
    public Color? HighlightColor
    {
        get => (Color?)this.GetValue(HighlightColorProperty);
        set => this.SetValue(HighlightColorProperty, value);
    }

    /// <summary>Card corner radius. Unset follows the theme's extra-large shape token.</summary>
    public double CornerRadius
    {
        get => (double)this.GetValue(CornerRadiusProperty);
        set => this.SetValue(CornerRadiusProperty, value);
    }

    /// <summary>Prompt font size. Default 17 — deliberately larger than body text, as every assistant HUD does.</summary>
    public double PromptFontSize
    {
        get => (double)this.GetValue(PromptFontSizeProperty);
        set => this.SetValue(PromptFontSizeProperty, value);
    }

    /// <summary>The index of the keyboard-highlighted suggestion, or -1 when the prompt itself has focus.</summary>
    public int HighlightedIndex => this.highlightIndex;

    /// <summary>Raised when the user submits — Enter, the submit button, or picking a suggestion.</summary>
    public event EventHandler<PromptSubmittedEventArgs>? Submitted;

    /// <summary>Raised when a suggestion is chosen, before <see cref="Submitted"/>.</summary>
    public event EventHandler<PromptSubmittedEventArgs>? SuggestionSelected;

    /// <summary>Raised when the stop button is pressed while <see cref="IsBusy"/> is set. Cancel your request here.</summary>
    public event EventHandler? Cancelled;

    /// <summary>
    /// Raised whenever the response area changes — <see cref="Response"/> or
    /// <see cref="ResponseContent"/>, set or cleared. A tool that reacts to the answer arriving
    /// listens here.
    /// </summary>
    public event EventHandler? ResponseChanged;

    /// <inheritdoc />
    public event EventHandler? BusyChanged;

    /// <inheritdoc />
    public event EventHandler? DesiredHeightChanged;

    /// <inheritdoc />
    public double GetDesiredHeight(double width)
    {
        if (width <= 0)
            return 0;

        // Invalidate first. MAUI caches a desired size per measure pass, and every pass the card has
        // been through was constrained by the popup window — the very thing being sized here — so
        // without dropping that cache the answer is always the window's current height and the popup
        // can never change size. The flag stops the invalidation we just caused from being read as
        // "the content changed" and looping straight back in.
        this.measuring = true;
        try
        {
            // The stack inside the card, not the card itself: a Layout measures its children
            // honestly where a Border rounds its content down on some hosts, and at this size a
            // couple of points short is a clipped last row. The stroke is added back on both edges.
            //
            // Invalidating the whole subtree, not just the stack: InvalidateMeasure only drops that
            // element's own cached desired size, so a child still holding a stale one — an Entry
            // that measured before its NSTextField existed is the case that bit here — keeps
            // reporting it and the sum comes out short by exactly that child's error.
            InvalidateSubtree(this.cardContent);
            var stroke = this.card.StrokeThickness * 2;
            var inner = Math.Max(0d, width - stroke);
            return ((IView)this.cardContent).Measure(inner, double.PositiveInfinity).Height + stroke;
        }
        finally
        {
            this.measuring = false;
        }
    }

    static void InvalidateSubtree(IView view)
    {
        view.InvalidateMeasure();
        if (view is Microsoft.Maui.Controls.Layout layout)
        {
            foreach (var child in layout.Children)
            {
                if (child is IView childView)
                    InvalidateSubtree(childView);
            }
        }
        else if (view is IContentView content && content.PresentedContent is IView presented)
        {
            InvalidateSubtree(presented);
        }
    }

    /// <summary>
    /// Pins the prompt row's height instead of letting the native text field pick one.
    /// </summary>
    /// <remarks>
    /// Without this the AppKit <c>NSTextField</c> lays out considerably taller than the height MAUI
    /// measured for it, so the popup window — which is sized from that measurement — comes out
    /// short and clips its own last row. An explicit request makes measure and render agree, and
    /// tying it to the font size keeps it right when a consumer scales the prompt up.
    /// </remarks>
    void UpdateCornerRadius()
        => this.cardShape.SetCornerTokenOrValue(this.CornerRadius, ShinyThemeKeys.Shape.CornerExtraLargeRadius);

    void UpdatePromptMetrics()
    {
        var fontSize = this.PromptFontSize;
        this.entry.FontSize = fontSize;
        this.entry.HeightRequest = Math.Ceiling(fontSize * 1.9);
    }

    void RaiseDesiredHeightChanged()
    {
        if (!this.measuring)
            this.DesiredHeightChanged?.Invoke(this, EventArgs.Empty);
    }

    // -----------------------------------------------------------------------------------------

    /// <summary>Submit the current prompt. Same path as pressing Enter.</summary>
    public void Submit()
    {
        if (this.IsBusy)
        {
            this.Cancelled?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (this.highlightIndex >= 0 && this.highlightIndex < this.suggestionItems.Count)
        {
            this.ChooseSuggestion(this.suggestionItems[this.highlightIndex]);
            return;
        }

        var text = this.Text ?? String.Empty;
        if (String.IsNullOrWhiteSpace(text))
            return;

        this.RaiseSubmit(text, null);
    }

    /// <summary>Clear the prompt, the highlight and any response. What Escape does before it closes the popup.</summary>
    public void Reset()
    {
        this.Text = String.Empty;
        this.SetHighlight(-1);
        this.ResponseContent = null;
        this.Response = null;
    }

    void ChooseSuggestion(object item)
    {
        var text = item is PromptSuggestion s ? s.Text : item.ToString() ?? String.Empty;
        this.SuggestionSelected?.Invoke(this, new PromptSubmittedEventArgs(text, item));
        if (this.SuggestionCommand?.CanExecute(item) == true)
            this.SuggestionCommand.Execute(item);

        this.Text = text;
        this.SetHighlight(-1);
        this.RaiseSubmit(text, item);
    }

    void RaiseSubmit(string text, object? suggestion)
    {
        this.Submitted?.Invoke(this, new PromptSubmittedEventArgs(text, suggestion));
        if (this.SubmitCommand?.CanExecute(text) == true)
            this.SubmitCommand.Execute(text);

        if (this.ClearOnSubmit)
            this.Text = String.Empty;
    }

    // -----------------------------------------------------------------------------------------
    // Keyboard + presentation
    // -----------------------------------------------------------------------------------------

    /// <inheritdoc />
    public bool HandleKey(QuickEntryKey key)
    {
        switch (key)
        {
            case QuickEntryKey.ArrowDown:
                return this.MoveHighlight(1);

            case QuickEntryKey.ArrowUp:
                return this.MoveHighlight(-1);

            case QuickEntryKey.Enter:
                this.Submit();
                return true;

            case QuickEntryKey.Escape:
                // First Escape backs out of whatever state the view is in; only once it is empty
                // does the key fall through to the host and close the popup.
                if (this.IsBusy)
                {
                    this.Cancelled?.Invoke(this, EventArgs.Empty);
                    return true;
                }
                if (this.highlightIndex >= 0)
                {
                    this.SetHighlight(-1);
                    return true;
                }
                if (this.responseHost.Content != null)
                {
                    this.ResponseContent = null;
                    this.Response = null;
                    return true;
                }
                if (!String.IsNullOrEmpty(this.Text))
                {
                    this.Text = String.Empty;
                    return true;
                }
                return false;

            default:
                return false;
        }
    }

    /// <inheritdoc />
    public void OnQuickEntryOpened()
    {
        // Twice: the immediate attempt covers a popup that was already the key window, and the
        // delayed one covers a cold open, where the OS is still moving focus between applications
        // and a Focus() issued before that settles is simply dropped.
        this.Dispatcher.Dispatch(() => this.entry.Focus());
        this.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), () =>
        {
            if (!this.entry.IsFocused)
                this.entry.Focus();
        });
    }

    /// <inheritdoc />
    public void OnQuickEntryClosed()
        => this.SetHighlight(-1);

    bool MoveHighlight(int delta)
    {
        if (this.suggestionItems.Count == 0)
            return false;

        var next = this.highlightIndex + delta;
        if (next < -1)
            next = this.suggestionItems.Count - 1;
        else if (next >= this.suggestionItems.Count)
            next = -1;

        this.SetHighlight(next);
        return true;
    }

    void SetHighlight(int index)
    {
        this.highlightIndex = index;
        this.UpdateHighlight();
    }

    // -----------------------------------------------------------------------------------------
    // Internals
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// MAUI applies implicit styles from <c>StyleableElement</c>'s own constructor, which runs
    /// before this type's field initialisers — so every property callback has to be able to arrive
    /// at a half-built object. Guarding on <see cref="built"/> and replaying everything from
    /// <see cref="ApplyAll"/> at the end of the constructor keeps that from being a crash or a
    /// silently-dropped style.
    /// </summary>
    void Apply(Action<PromptView> action)
    {
        if (this.built)
            action(this);
    }

    /// <summary>
    /// Fills the leading slot: a caller's view wins, then a caller's image, then the built-in orb.
    /// Rebuilt rather than toggled so the orb's ticker is not left running behind a custom icon.
    /// </summary>
    void RebuildLeading()
    {
        if (!this.ShowIcon)
        {
            this.leadingHost.Content = null;
            this.leadingHost.IsVisible = false;
            return;
        }

        this.leadingHost.IsVisible = true;
        var size = Math.Max(1d, this.IconSize);

        if (this.IconContent is { } custom)
        {
            this.leadingHost.Content = custom;
            return;
        }

        if (this.Icon is { } image)
        {
            this.leadingHost.Content = new Image
            {
                Source = image,
                WidthRequest = size,
                HeightRequest = size,
                Aspect = Aspect.AspectFit,
                VerticalOptions = LayoutOptions.Center
            };
            return;
        }

        this.orb.WidthRequest = size;
        this.orb.HeightRequest = size;
        this.leadingHost.Content = this.orb;
    }

    void ApplyAll()
    {
        this.entry.Placeholder = this.Placeholder;
        this.entry.Text = this.Text;
        this.UpdatePromptMetrics();
        this.busyLabel.Text = this.BusyText;
        this.micButton.IsVisible = this.ShowMicrophone;
        this.UpdateCornerRadius();
        this.UpdateResponse();
        this.dropdownHost.Content = this.DropdownContent;
        this.dropdownHost.IsVisible = this.DropdownContent != null;
        this.footerHost.Content = this.Footer;
        this.footerHost.IsVisible = this.Footer != null;
        this.RebuildLeading();
        this.UpdateDropdownHeight();
        this.UpdateAccent();
        this.UpdateTextColors();
        this.UpdateBusy();
        this.RebuildSuggestions();
    }

    /// <summary>
    /// Points every colour property at a theme token, so the popup follows a theme swap and the OS
    /// light/dark switch without the consumer doing anything. Assigning any of these properties
    /// replaces the dynamic resource, which is what makes "themed unless you said otherwise" work.
    /// </summary>
    void ApplyThemeDefaults()
    {
        this.SetDynamicResource(SurfaceColorProperty, ShinyThemeKeys.Color.SurfaceContainerHigh);
        this.SetDynamicResource(OutlineColorProperty, ShinyThemeKeys.Color.OutlineVariant);
        this.SetDynamicResource(TextColorProperty, ShinyThemeKeys.Color.OnSurface);
        this.SetDynamicResource(PlaceholderColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        this.SetDynamicResource(SubtleTextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        this.SetDynamicResource(HighlightColorProperty, ShinyThemeKeys.Color.SurfaceContainerHighest);
        this.SetDynamicResource(AccentColorProperty, ShinyThemeKeys.Color.Primary);
    }

    void OnTextChanged()
    {
        if (!this.built)
            return;

        if (this.entry.Text != this.Text)
            this.entry.Text = this.Text;

        // A new prompt invalidates whatever row was highlighted for the old one.
        if (this.highlightIndex >= 0)
            this.SetHighlight(-1);
    }

    void OnEntryTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (this.Text != e.NewTextValue)
            this.Text = e.NewTextValue ?? String.Empty;
    }

    void UpdateAccent()
    {
        var accent = this.AccentColor ?? FallbackPrimary;
        this.orb.AccentColor = accent;
        this.submitGlyph.TextColor = accent;
        this.UpdateHighlight();
    }

    void UpdateTextColors()
    {
        // Null only before the theme dictionary has resolved; the token is the real source.
        var text = this.TextColor ?? FallbackOnSurface;
        var subtle = this.SubtleTextColor ?? FallbackOnSurfaceVariant;

        this.entry.TextColor = text;
        this.busyLabel.TextColor = subtle;
        this.micGlyph.TextColor = subtle;
        this.responseLabel.TextColor = text;

        foreach (var row in this.suggestionRows)
        {
            if (row is not SuggestionRow built)
                continue;
            built.Title.TextColor = text;
            if (built.Description != null)
                built.Description.TextColor = subtle;
            if (built.Glyph != null)
                built.Glyph.TextColor = subtle;
        }
    }

    /// <summary>
    /// Fills the response area: a caller's view wins, then a caller's text in the built-in label,
    /// then nothing at all.
    /// </summary>
    void UpdateResponse()
    {
        if (this.ResponseContent is { } view)
        {
            this.responseHost.Content = view;
        }
        else if (!String.IsNullOrEmpty(this.Response))
        {
            this.responseLabel.Text = this.Response;
            this.responseHost.Content = this.responseLabel;
        }
        else
        {
            this.responseHost.Content = null;
        }

        // Busy first: the "Thinking…" row is only shown while there is nothing to read, and that
        // just changed. UpdateBusy ends in UpdateBodyVisibility, so the area is laid out once.
        this.UpdateBusy();
        this.ResponseChanged?.Invoke(this, EventArgs.Empty);
    }

    void UpdateBusy()
    {
        var busy = this.IsBusy;
        this.orb.IsBusy = busy;
        this.busyIndicator.IsVisible = busy;
        this.busyIndicator.IsRunning = busy;
        this.busyRow.IsVisible = busy && this.responseHost.Content == null;
        this.submitButton.IsVisible = this.ShowSubmitButton;
        this.submitGlyph.Text = busy ? "■" : "↵";
        this.UpdateBodyVisibility();
    }

    /// <summary>
    /// Swaps the dropdown between sizing to its content and a fixed, scrolling height.
    /// </summary>
    /// <remarks>
    /// The container is swapped rather than the ScrollView merely being given a height. A ScrollView
    /// with no height of its own does not shrink-wrap its content — it takes whatever space the
    /// layout offers and scrolls inside it, which would cap the dropdown at some arbitrary height
    /// and stop the popup growing to fit. With no <see cref="DropdownHeight"/> the stack goes in
    /// directly and its height is the content's; with one, the ScrollView goes back in and pins it.
    /// </remarks>
    void UpdateDropdownHeight()
    {
        var height = this.DropdownHeight;
        if (height >= 0)
        {
            if (!ReferenceEquals(this.dropdownScroll.Content, this.dropdownStack))
            {
                this.dropdownContainer.Content = null;
                this.dropdownScroll.Content = this.dropdownStack;
            }
            this.dropdownContainer.Content = this.dropdownScroll;
            this.dropdownContainer.HeightRequest = height;
        }
        else
        {
            if (ReferenceEquals(this.dropdownScroll.Content, this.dropdownStack))
                this.dropdownScroll.Content = null;

            this.dropdownContainer.Content = this.dropdownStack;
            this.dropdownContainer.HeightRequest = -1;
        }
    }

    void UpdateBodyVisibility()
    {
        var hasBody = this.busyRow.IsVisible
            || this.suggestionRows.Count > 0
            || this.dropdownHost.IsVisible
            || this.responseHost.Content != null
            || this.footerHost.IsVisible;

        this.body.IsVisible = hasBody;
        this.separator.IsVisible = hasBody;
        this.responseHost.IsVisible = this.responseHost.Content != null;
        this.suggestionList.IsVisible = this.suggestionRows.Count > 0;
        this.dropdownContainer.IsVisible = this.dropdownHost.IsVisible || this.responseHost.IsVisible || this.suggestionList.IsVisible;
    }

    void UpdateHighlight()
    {
        var highlight = this.HighlightColor ?? FallbackHighlight;
        for (var i = 0; i < this.suggestionRows.Count; i++)
        {
            if (this.suggestionRows[i] is SuggestionRow row)
                row.Background = i == this.highlightIndex ? new SolidColorBrush(highlight) : null;
        }
    }

    void OnSuggestionsChanged(IEnumerable? oldValue, IEnumerable? newValue)
    {
        // Weak: a bound collection can outlive the view.
        this.suggestionsSubscription?.Dispose();
        this.suggestionsSubscription = WeakEventSubscription.CollectionChanged(newValue, this.OnSuggestionCollectionChanged);

        this.Apply(v => v.RebuildSuggestions());
    }

    void OnSuggestionCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => this.Dispatcher.Dispatch(this.RebuildSuggestions);

    void RebuildSuggestions()
    {
        this.suggestionList.Children.Clear();
        this.suggestionRows.Clear();
        this.suggestionItems.Clear();
        this.highlightIndex = -1;

        if (this.Suggestions != null)
        {
            var max = Math.Max(0, this.MaxVisibleSuggestions);
            foreach (var item in this.Suggestions)
            {
                if (this.suggestionItems.Count >= max)
                    break;
                if (item is null)
                    continue;

                this.suggestionItems.Add(item);
                var row = this.BuildRow(item);
                this.suggestionRows.Add(row);
                this.suggestionList.Children.Add(row);
            }
        }

        this.UpdateTextColors();
        this.UpdateHighlight();
        this.UpdateBodyVisibility();
    }

    View BuildRow(object item)
    {
        if (this.SuggestionTemplate != null)
        {
            var content = (View)this.SuggestionTemplate.CreateContent();
            content.BindingContext = item;
            AddTap(content, () => this.ChooseSuggestion(item));
            return content;
        }

        var suggestion = item as PromptSuggestion;
        var row = new SuggestionRow
        {
            Padding = new Thickness(10, 8),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerSmallRadius)
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10
        };

        var glyphText = suggestion?.Glyph;
        if (!String.IsNullOrEmpty(glyphText))
        {
            row.Glyph = new Label { Text = glyphText, VerticalOptions = LayoutOptions.Center }
                .WithFontSize(ShinyThemeKeys.Type.BodyMediumSize);
            grid.Add(row.Glyph, 0);
        }

        var stack = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center };
        row.Title = new Label { Text = suggestion?.Text ?? item.ToString() ?? String.Empty }
            .WithFontSize(ShinyThemeKeys.Type.BodyMediumSize);
        stack.Children.Add(row.Title);

        if (!String.IsNullOrEmpty(suggestion?.Description))
        {
            row.Description = new Label { Text = suggestion!.Description }
                .WithFontSize(ShinyThemeKeys.Type.BodySmallSize);
            stack.Children.Add(row.Description);
        }

        grid.Add(stack, 1);
        row.Content = grid;
        AddTap(row, () => this.ChooseSuggestion(item));
        return row;
    }

    // -----------------------------------------------------------------------------------------
    // Tools
    // -----------------------------------------------------------------------------------------

    void OnToolsChanged(IList<PromptTool>? oldTools, IList<PromptTool>? newTools, HorizontalStackLayout layout)
    {
        var isLeading = ReferenceEquals(layout, this.leadingToolsLayout);
        ref var subscription = ref isLeading ? ref this.leadingToolsSubscription : ref this.trailingToolsSubscription;

        subscription?.Dispose();
        this.SyncTools(newTools, layout);

        // Weak: a bound collection can outlive the view.
        subscription = WeakEventSubscription.CollectionChanged(newTools, (_, _) => this.SyncTools(newTools, layout));
    }

    /// <summary>
    /// Reconciles the slot against the collection as it stands now.
    /// </summary>
    /// <remarks>
    /// Diffed against what is actually attached rather than against the event's OldItems: a
    /// <c>Reset</c> carries neither list, and the collection the change handler closes over is
    /// already the post-change one — walking it would never find the tool that was just removed, so
    /// that tool would keep whatever it subscribed to in <see cref="IPromptAwareTool.Attach"/>.
    /// </remarks>
    void SyncTools(IList<PromptTool>? tools, HorizontalStackLayout layout)
    {
        var attached = ReferenceEquals(layout, this.leadingToolsLayout)
            ? this.attachedLeadingTools
            : this.attachedTrailingTools;

        var current = tools?.ToList() ?? new List<PromptTool>();

        foreach (var gone in attached.Where(t => !current.Contains(t)).ToList())
        {
            if (gone is IPromptAwareTool aware)
                aware.Detach();

            gone.ParentPrompt = null;
        }

        layout.Children.Clear();
        layout.IsVisible = current.Count > 0;

        // Parented before anything is attached, so a tool can reach the prompt from its own Attach.
        foreach (var tool in current)
        {
            tool.ParentPrompt = this;
            layout.Children.Add(tool);
        }

        foreach (var added in current.Where(t => !attached.Contains(t)))
        {
            if (added is IPromptAwareTool aware)
                aware.Attach(this);
        }

        attached.Clear();
        attached.AddRange(current);

        this.RaiseDesiredHeightChanged();
    }

    static void AddTap(View view, Action action)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => action();
        view.GestureRecognizers.Add(tap);
    }

    static Border BuildGlyphButton(Label glyph, Action tapped)
    {
        var border = new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            Padding = new Thickness(8, 4),
            WidthRequest = 34,
            HeightRequest = 28,
            VerticalOptions = LayoutOptions.Start,
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerSmallRadius),
            Content = glyph
        };
        AddTap(border, tapped);
        return border;
    }

    /// <summary>A built-in suggestion row, kept as a type so recolouring can find its labels without a visual-tree walk.</summary>
    sealed class SuggestionRow : Border
    {
        public Label Title { get; set; } = null!;
        public Label? Description { get; set; }
        public Label? Glyph { get; set; }
    }
}
