using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Chat;

/// <summary>
/// The message composer: a rounded, auto-growing multiline entry with optional attach / overflow
/// buttons, a markdown formatting toolbar and a send button.
///
/// <para>
/// <see cref="ChatView"/> builds one of these for itself, so most apps never touch this type
/// directly - they set the pass-through properties on <see cref="ChatView"/> instead. Construct one
/// yourself and hand it to <see cref="ChatView.InputBar"/> when you want a differently-shaped
/// composer, or use it standalone (an AI prompt box, a comment field) and drive
/// <see cref="SendRequested"/> yourself.
/// </para>
/// </summary>
public partial class ChatEntryView : ContentView
{
    /// <summary>Raised when the send button is pressed with non-empty text. The argument is the trimmed text.</summary>
    public event EventHandler<string>? SendRequested;

    /// <summary>Raised when the attach button is pressed. Only shown when <see cref="ShowAttachButton"/> is true.</summary>
    public event EventHandler? AttachRequested;

    /// <summary>Raised when the overflow button is pressed. Only shown when <see cref="ShowActionsButton"/> is true.</summary>
    public event EventHandler? ActionsRequested;

    /// <summary>Raised when the markdown toolbar's link button is pressed. Handle it and call <see cref="InsertLink"/>.</summary>
    public event EventHandler? LinkRequested;

    /// <summary>Raised when the user dismisses the edit banner.</summary>
    public event EventHandler? EditCancelled;

    /// <summary>Raised on every keystroke. Use it to drive a typing indicator.</summary>
    public event EventHandler<string>? TextChanged;

    readonly BorderlessEditor entry;
    readonly Button sendButton;
    readonly Button attachButton;
    readonly Button actionsButton;
    readonly Border composerBorder;
    readonly Grid composerGrid;
    readonly Grid toolbarGrid;
    readonly HorizontalStackLayout leftStack;
    readonly HorizontalStackLayout rightStack;
    readonly HorizontalStackLayout leftCustomHost;
    readonly HorizontalStackLayout rightCustomHost;
    readonly Grid rootGrid;
    readonly HorizontalStackLayout markdownToolbar;
    readonly Border editBanner;
    readonly Label editBannerLabel;

    string defaultSendText = "Send";

    public ChatEntryView()
    {
        this.entry = new BorderlessEditor
        {
            Placeholder = "Type a message...",
            FontSize = 15,
            AutoSize = EditorAutoSizeOption.TextChanges,
            BackgroundColor = Colors.Transparent,
            Margin = new Thickness(8, 2, 8, 0)
        };
        this.entry.TextChanged += this.OnEntryTextChanged;

        this.sendButton = new Button
        {
            Text = "Send",
            CornerRadius = 18,
            HeightRequest = 36,
            MinimumWidthRequest = 36,
            Padding = new Thickness(14, 0)
        }.Neutralize().WithFontSize(ShinyThemeKeys.Type.BodyMediumSize);
        this.sendButton.SetDynamicResource(Button.TextColorProperty, ShinyThemeKeys.Color.OnPrimary);
        this.sendButton.SetDynamicResource(Button.BackgroundColorProperty, ShinyThemeKeys.Color.Primary);
        this.sendButton.Clicked += this.OnSendClicked;

        this.attachButton = new Button
        {
            Text = "+",
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = Colors.Transparent,
            WidthRequest = 36,
            HeightRequest = 36,
            Padding = 0,
            IsVisible = false
        }.Neutralize().WithFontSize(ShinyThemeKeys.Type.TitleLargeSize);
        this.attachButton.SetDynamicResource(Button.TextColorProperty, ShinyThemeKeys.Color.Primary);
        this.attachButton.Clicked += (_, _) => this.AttachRequested?.Invoke(this, EventArgs.Empty);

        this.actionsButton = new Button
        {
            Text = "⋯",
            FontSize = 20,
            BackgroundColor = Colors.Transparent,
            WidthRequest = 34,
            HeightRequest = 36,
            Padding = 0,
            IsVisible = false
        }.Neutralize();
        this.actionsButton.SetDynamicResource(Button.TextColorProperty, ShinyThemeKeys.Color.Primary);
        this.actionsButton.Clicked += (_, _) => this.ActionsRequested?.Invoke(this, EventArgs.Empty);

        // Markdown composition toolbar. It gets its own row inside the card rather than sharing the
        // control row: six format buttons plus attach, overflow and anything a consumer adds does not
        // fit beside the send button on a phone. It sits above the entry so the formatting controls
        // stay put as the entry grows.
        this.markdownToolbar = new HorizontalStackLayout
        {
            Spacing = 2,
            Margin = new Thickness(2, 0),
            IsVisible = false
        };

        this.leftCustomHost = new HorizontalStackLayout { Spacing = 4 };
        this.rightCustomHost = new HorizontalStackLayout { Spacing = 4 };

        this.leftStack = new HorizontalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children = { this.attachButton, this.actionsButton, this.leftCustomHost }
        };
        this.rightStack = new HorizontalStackLayout
        {
            Spacing = 4,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            Children = { this.rightCustomHost, this.sendButton }
        };

        // The left group can outgrow a phone once a couple of custom tools are in it, so it scrolls
        // sideways rather than pushing send off the edge or clipping.
        var leftScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            VerticalOptions = LayoutOptions.Center,
            Content = this.leftStack
        };

        this.toolbarGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 4
        };
        this.toolbarGrid.Add(leftScroll, 0, 0);
        this.toolbarGrid.Add(this.rightStack, 1, 0);

        // Stacked composer: format buttons on top, the entry spanning the full width beneath them, and
        // the control row last. Buttons no longer have to share horizontal space with the text, which
        // is what lets a consumer drop a model picker or a segmented control in alongside send.
        this.composerGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            RowSpacing = 2
        };
        this.composerGrid.Add(this.markdownToolbar, 0, 0);
        this.composerGrid.Add(this.entry, 0, 1);
        this.composerGrid.Add(this.toolbarGrid, 0, 2);

        this.composerBorder = new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerExtraLargeRadius),
            Padding = new Thickness(6, 8),
            Content = this.composerGrid
        }.WithStrokeThickness(ShinyThemeKeys.Border.Thin);
        this.composerBorder.SetDynamicResource(Border.StrokeProperty, ShinyThemeKeys.Color.OutlineVariant);
        this.composerBorder.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerLowest);

        // Edit-mode banner
        this.editBannerLabel = new Label
        {
            Text = "Editing message",
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Start
        }.WithFontSize(ShinyThemeKeys.Type.BodySmallSize);
        this.editBannerLabel.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

        var editCancel = new Button
        {
            Text = "✕",
            BackgroundColor = Colors.Transparent,
            Padding = 0,
            WidthRequest = 32,
            HeightRequest = 28,
            HorizontalOptions = LayoutOptions.End
        }.Neutralize().WithFontSize(ShinyThemeKeys.Type.BodyMediumSize);
        editCancel.SetDynamicResource(Button.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        editCancel.Clicked += (_, _) => this.EditCancelled?.Invoke(this, EventArgs.Empty);

        var editBannerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        editBannerGrid.Add(this.editBannerLabel, 0, 0);
        editBannerGrid.Add(editCancel, 1, 0);

        this.editBanner = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerMediumRadius),
            Padding = new Thickness(12, 4),
            Margin = new Thickness(0, 0, 0, 4),
            IsVisible = false,
            Content = editBannerGrid
        };
        this.editBanner.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHigh);

        // No divider rule between this and the message list - the rounded composer is its own edge,
        // and a hairline BoxView above it was rendering outside the bar's padding.
        this.rootGrid = new Grid
        {
            Padding = new Thickness(8, 6, 8, 8),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            }
        };
        this.rootGrid.Add(this.editBanner, 0, 0);
        this.rootGrid.Add(this.composerBorder, 0, 1);
        this.rootGrid.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Surface);

        this.Content = this.rootGrid;
        this.SyncMaxHeight();

        // Collections initialised here rather than via defaultValueCreator: a lazily-created default
        // never fires propertyChanged, so the CollectionChanged hook would never be wired.
        this.LeftToolbar = new ObservableCollection<IView>();
        this.RightToolbar = new ObservableCollection<IView>();

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(ChatEntryView));
    }

    /// <summary>True while <see cref="EnterEditMode"/> is active.</summary>
    public bool IsEditing { get; private set; }

    /// <summary>Clears the entry text without raising <see cref="TextChanged"/> consumers' send flow.</summary>
    public void ClearText() => this.entry.Text = string.Empty;

    /// <summary>Moves focus into the entry itself (<see cref="VisualElement.Focus"/> would focus the container).</summary>
    public bool FocusInput() => this.entry.Focus();

    /// <summary>Enables or disables every interactive part of the composer and dims it when disabled.</summary>
    public void SetInputEnabled(bool enabled)
    {
        this.entry.IsEnabled = enabled;
        this.sendButton.IsEnabled = enabled;
        this.attachButton.IsEnabled = enabled;
        this.actionsButton.IsEnabled = enabled;
        this.Opacity = enabled ? 1 : 0.6;
    }

    /// <summary>Switches the composer into edit mode: shows the banner, loads <paramref name="body"/> and relabels send to "Save".</summary>
    public void EnterEditMode(string body)
    {
        this.IsEditing = true;
        this.editBanner.IsVisible = true;
        this.entry.Text = body;
        this.sendButton.Text = "Save";
        this.entry.Focus();
    }

    /// <summary>Leaves edit mode, clears the entry and restores the send button label.</summary>
    public void ExitEditMode()
    {
        this.IsEditing = false;
        this.editBanner.IsVisible = false;
        this.sendButton.Text = this.defaultSendText;
        this.ClearText();
    }

    /// <summary>Programmatically submits the current text as if send had been pressed.</summary>
    public void Submit() => this.TrySend();

    // ------- Markdown toolbar -------

    /// <summary>
    /// Rebuilds the formatting toolbar from the flags the session permits. Pass
    /// <see cref="MessageBodyPermissions.None"/> to hide it.
    /// </summary>
    public void SetBodyPermissions(MessageBodyPermissions permissions)
    {
        this.markdownToolbar.Children.Clear();

        if (permissions == MessageBodyPermissions.None)
        {
            this.markdownToolbar.IsVisible = false;
            return;
        }

        if (permissions.HasFlag(MessageBodyPermissions.Bold))
            this.markdownToolbar.Children.Add(this.CreateFormatButton("B", FontAttributes.Bold, () => this.ApplyWrap("**", "**", "bold")));
        if (permissions.HasFlag(MessageBodyPermissions.Italics))
            this.markdownToolbar.Children.Add(this.CreateFormatButton("I", FontAttributes.Italic, () => this.ApplyWrap("*", "*", "italic")));
        if (permissions.HasFlag(MessageBodyPermissions.Underline))
            this.markdownToolbar.Children.Add(this.CreateFormatButton("U", FontAttributes.None, () => this.ApplyWrap("<u>", "</u>", "underline")));
        if (permissions.HasFlag(MessageBodyPermissions.Strikethrough))
            this.markdownToolbar.Children.Add(this.CreateFormatButton("S", FontAttributes.None, () => this.ApplyWrap("~~", "~~", "strike")));
        if (permissions.HasFlag(MessageBodyPermissions.Codeblocks))
            this.markdownToolbar.Children.Add(this.CreateFormatButton("</>", FontAttributes.None, () => this.ApplyWrap("`", "`", "code")));
        if (permissions.HasFlag(MessageBodyPermissions.Links))
            this.markdownToolbar.Children.Add(this.CreateFormatButton("🔗", FontAttributes.None, () => this.LinkRequested?.Invoke(this, EventArgs.Empty)));

        this.markdownToolbar.IsVisible = this.markdownToolbar.Children.Count > 0;
    }

    Button CreateFormatButton(string text, FontAttributes attrs, Action action)
    {
        var btn = new Button
        {
            Text = text,
            FontAttributes = attrs,
            BackgroundColor = Colors.Transparent,
            WidthRequest = 40,
            HeightRequest = 32,
            Padding = 0
        }.Neutralize().WithFontSize(ShinyThemeKeys.Type.BodyMediumSize);
        btn.SetDynamicResource(Button.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        btn.Clicked += (_, _) => action();
        return btn;
    }

    /// <summary>Wraps the current selection (or inserts <paramref name="placeholder"/>) with the given delimiters.</summary>
    public void ApplyWrap(string prefix, string suffix, string placeholder)
    {
        var text = this.entry.Text ?? string.Empty;
        var start = this.entry.CursorPosition;
        if (start < 0 || start > text.Length)
            start = text.Length;

        var len = this.entry.SelectionLength;
        if (len < 0) len = 0;
        if (start + len > text.Length)
            len = Math.Max(0, text.Length - start);

        var selected = len > 0 ? text.Substring(start, len) : placeholder;
        var inserted = prefix + selected + suffix;
        this.entry.Text = text[..start] + inserted + text[(start + len)..];

        // place cursor at the end of the inserted segment
        this.entry.CursorPosition = start + inserted.Length;
        this.entry.Focus();
    }

    /// <summary>Inserts a markdown link at the cursor, replacing any selection.</summary>
    public void InsertLink(string displayText, string url)
    {
        var display = string.IsNullOrWhiteSpace(displayText) ? url : displayText;
        var text = this.entry.Text ?? string.Empty;
        var start = this.entry.CursorPosition;
        if (start < 0 || start > text.Length)
            start = text.Length;

        var len = this.entry.SelectionLength;
        if (len < 0) len = 0;
        if (start + len > text.Length)
            len = Math.Max(0, text.Length - start);

        var inserted = $"[{display}]({url})";
        this.entry.Text = text[..start] + inserted + text[(start + len)..];
        this.entry.CursorPosition = start + inserted.Length;
        this.entry.Focus();
    }

    // ------- toolbar slots -------

    internal void RebuildToolbar(bool left)
    {
        var host = left ? this.leftCustomHost : this.rightCustomHost;
        var source = left ? this.LeftToolbar : this.RightToolbar;

        host.Children.Clear();
        if (source is null)
            return;

        foreach (var view in source)
        {
            if (view is not null)
                host.Children.Add(view);
        }
    }

    IDisposable? leftToolbarSubscription;
    IDisposable? rightToolbarSubscription;

    internal void HookToolbar(object? oldValue, object? newValue, bool left)
    {
        // Weak: a bound toolbar collection can outlive the page.
        ref var subscription = ref left ? ref this.leftToolbarSubscription : ref this.rightToolbarSubscription;
        subscription?.Dispose();
        subscription = WeakEventSubscription.CollectionChanged(newValue, left ? this.OnLeftToolbarChanged : this.OnRightToolbarChanged);

        this.RebuildToolbar(left);
    }

    void OnLeftToolbarChanged(object? sender, NotifyCollectionChangedEventArgs e) => this.RebuildToolbar(true);
    void OnRightToolbarChanged(object? sender, NotifyCollectionChangedEventArgs e) => this.RebuildToolbar(false);

    // ------- internals -------

    // Editor grows a line at a time, so the ceiling is derived from the font metrics rather than a
    // magic pixel count - a consumer raising FontSize should still get MaxLines worth of text.
    void SyncMaxHeight()
        => this.entry.MaximumHeightRequest = Math.Ceiling(Math.Max(1, this.MaxLines) * this.entry.FontSize * 1.35) + 16;

    void OnEntryTextChanged(object? sender, TextChangedEventArgs e)
    {
        var text = e.NewTextValue ?? string.Empty;
        this.SetValue(TextProperty, text);
        this.TextChanged?.Invoke(this, text);
    }

    void OnSendClicked(object? sender, EventArgs e) => this.TrySend();

    void TrySend()
    {
        var text = this.entry.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            this.SendRequested?.Invoke(this, text);
            // The owner clears/exits edit mode as appropriate.
        }
    }
}
