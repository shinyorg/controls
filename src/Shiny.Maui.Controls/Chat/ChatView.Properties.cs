using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;
namespace Shiny.Maui.Controls.Chat;

public partial class ChatView
{
    // ------- provider / session -------

    public static readonly BindableProperty ProviderProperty = BindableProperty.Create(
        nameof(Provider),
        typeof(IChatSessionProvider),
        typeof(ChatView),
        null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ChatView), () =>
            {
                ((ChatView)b).OnProviderOrSessionChanged();
            }));

    public IChatSessionProvider? Provider
    {
        get => (IChatSessionProvider?)GetValue(ProviderProperty);
        set => SetValue(ProviderProperty, value);
    }

    public static readonly BindableProperty SessionIdProperty = BindableProperty.Create(
        nameof(SessionId),
        typeof(string),
        typeof(ChatView),
        null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ChatView), () =>
            {
                ((ChatView)b).OnProviderOrSessionChanged();
            }));

    public string? SessionId
    {
        get => (string?)GetValue(SessionIdProperty);
        set => SetValue(SessionIdProperty, value);
    }

    public static readonly BindableProperty PageSizeProperty = BindableProperty.Create(
        nameof(PageSize),
        typeof(int),
        typeof(ChatView),
        30);

    public int PageSize
    {
        get => (int)GetValue(PageSizeProperty);
        set => SetValue(PageSizeProperty, value);
    }

    public static readonly BindableProperty OpenImagesInViewerProperty = BindableProperty.Create(
        nameof(OpenImagesInViewer),
        typeof(bool),
        typeof(ChatView),
        true);

    public bool OpenImagesInViewer
    {
        get => (bool)GetValue(OpenImagesInViewerProperty);
        set => SetValue(OpenImagesInViewerProperty, value);
    }

    public static readonly BindableProperty InputActionsProperty = BindableProperty.Create(
        nameof(InputActions),
        typeof(IList<ChatInputAction>),
        typeof(ChatView),
        null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ChatView), () =>
            {
                ((ChatView)b).SyncInputActionsVisibility();
            }));

    /// <summary>Consumer-supplied input-bar actions surfaced via the overflow button.</summary>
    public IList<ChatInputAction>? InputActions
    {
        get => (IList<ChatInputAction>?)GetValue(InputActionsProperty);
        set => SetValue(InputActionsProperty, value);
    }

    public static readonly BindableProperty CustomBubbleActionsProperty = BindableProperty.Create(
        nameof(CustomBubbleActions),
        typeof(IList<ChatBubbleAction>),
        typeof(ChatView),
        null);

    /// <summary>Consumer-supplied bubble actions appended to the built-in action set.</summary>
    public IList<ChatBubbleAction>? CustomBubbleActions
    {
        get => (IList<ChatBubbleAction>?)GetValue(CustomBubbleActionsProperty);
        set => SetValue(CustomBubbleActionsProperty, value);
    }

    // ------- bubble colors -------
    //
    // Defaults resolve from the theme rather than a fixed WhatsApp-ish green/white: own messages take
    // the primary container pair, everyone else's the neutral surface container pair. Bubbles are the
    // largest surface in the control, so hardcoding them made a theme switch look like a no-op.
    // The getters resolve the app-level token at read time. Rendered bubbles never read them: they bind
    // the tokens themselves (ChatBubbleView), so a light/dark flip or a scoped palette repaints them live.

    public static readonly BindableProperty MyBubbleColorProperty = BindableProperty.Create(
        nameof(MyBubbleColor), typeof(Color), typeof(ChatView), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ChatView), () =>
            {
                ((ChatView)b).RefreshBubbles();
            }));

    /// <summary>The raw value - null when nothing was set and the theme should drive it.</summary>
    internal Color? ExplicitMyBubbleColor => (Color?)GetValue(MyBubbleColorProperty);

    /// <summary>Leave unset to follow the active theme.</summary>
    public Color MyBubbleColor
    {
        get => (Color?)GetValue(MyBubbleColorProperty) ?? ThemeColor(ShinyThemeKeys.Color.PrimaryContainer);
        set => SetValue(MyBubbleColorProperty, value);
    }

    public static readonly BindableProperty MyTextColorProperty = BindableProperty.Create(
        nameof(MyTextColor), typeof(Color), typeof(ChatView), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ChatView), () =>
            {
                ((ChatView)b).RefreshBubbles();
            }));

    /// <summary>The raw value - null when nothing was set and the theme should drive it.</summary>
    internal Color? ExplicitMyTextColor => (Color?)GetValue(MyTextColorProperty);

    /// <summary>Leave unset to follow the active theme.</summary>
    public Color MyTextColor
    {
        get => (Color?)GetValue(MyTextColorProperty) ?? ThemeColor(ShinyThemeKeys.Color.OnPrimaryContainer);
        set => SetValue(MyTextColorProperty, value);
    }

    public static readonly BindableProperty OtherBubbleColorProperty = BindableProperty.Create(
        nameof(OtherBubbleColor), typeof(Color), typeof(ChatView), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ChatView), () =>
            {
                ((ChatView)b).RefreshBubbles();
            }));

    /// <summary>The raw value - null when nothing was set and the theme should drive it.</summary>
    internal Color? ExplicitOtherBubbleColor => (Color?)GetValue(OtherBubbleColorProperty);

    /// <summary>Leave unset to follow the active theme.</summary>
    public Color OtherBubbleColor
    {
        get => (Color?)GetValue(OtherBubbleColorProperty) ?? ThemeColor(ShinyThemeKeys.Color.SurfaceContainerHigh);
        set => SetValue(OtherBubbleColorProperty, value);
    }

    public static readonly BindableProperty OtherTextColorProperty = BindableProperty.Create(
        nameof(OtherTextColor), typeof(Color), typeof(ChatView), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ChatView), () =>
            {
                ((ChatView)b).RefreshBubbles();
            }));

    /// <summary>The raw value - null when nothing was set and the theme should drive it.</summary>
    internal Color? ExplicitOtherTextColor => (Color?)GetValue(OtherTextColorProperty);

    /// <summary>Leave unset to follow the active theme.</summary>
    public Color OtherTextColor
    {
        get => (Color?)GetValue(OtherTextColorProperty) ?? ThemeColor(ShinyThemeKeys.Color.OnSurface);
        set => SetValue(OtherTextColorProperty, value);
    }

    public static readonly BindableProperty ChatBackgroundColorProperty = BindableProperty.Create(
        nameof(ChatBackgroundColor), typeof(Color), typeof(ChatView), null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ChatView), () =>
            {
                ((ChatView)b).messageArea.BackgroundColor = n as Color;
            }));

    public Color? ChatBackgroundColor
    {
        get => (Color?)GetValue(ChatBackgroundColorProperty);
        set => SetValue(ChatBackgroundColorProperty, value);
    }

    // ------- input bar -------

    public static readonly BindableProperty PlaceholderTextProperty = BindableProperty.Create(
        nameof(PlaceholderText), typeof(string), typeof(ChatView), "Type a message...",
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ChatView), () =>
            {
                ((ChatView)b).inputBar.PlaceholderText = (string)n;
            }));

    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public static readonly BindableProperty SendButtonTextProperty = BindableProperty.Create(
        nameof(SendButtonText), typeof(string), typeof(ChatView), "Send",
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ChatView), () =>
            {
                ((ChatView)b).inputBar.SendButtonText = (string)n;
            }));

    public string SendButtonText
    {
        get => (string)GetValue(SendButtonTextProperty);
        set => SetValue(SendButtonTextProperty, value);
    }

    public static readonly BindableProperty SendButtonBackgroundColorProperty = BindableProperty.Create(
        nameof(SendButtonBackgroundColor), typeof(Color), typeof(ChatView), null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ChatView), () =>
            {
                // null => leave ChatInputBar's own themed value in place.
                if (n is Color sendButtonBackgroundColor)
                    ((ChatView)b).inputBar.SendButtonBackgroundColor = sendButtonBackgroundColor;
            }));

    /// <summary>Leave unset to follow the active theme.</summary>
    public Color? SendButtonBackgroundColor
    {
        get => (Color?)GetValue(SendButtonBackgroundColorProperty);
        set => SetValue(SendButtonBackgroundColorProperty, value);
    }

    public static readonly BindableProperty SendButtonTextColorProperty = BindableProperty.Create(
        nameof(SendButtonTextColor), typeof(Color), typeof(ChatView), null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ChatView), () =>
            {
                // null => leave ChatInputBar's own themed value in place.
                if (n is Color sendButtonTextColor)
                    ((ChatView)b).inputBar.SendButtonTextColor = sendButtonTextColor;
            }));

    /// <summary>Leave unset to follow the active theme.</summary>
    public Color? SendButtonTextColor
    {
        get => (Color?)GetValue(SendButtonTextColorProperty);
        set => SetValue(SendButtonTextColorProperty, value);
    }

    public static readonly BindableProperty InputBarBackgroundColorProperty = BindableProperty.Create(
        nameof(InputBarBackgroundColor), typeof(Color), typeof(ChatView), null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ChatView), () =>
            {
                // null => leave ChatInputBar's own themed value in place.
                if (n is Color inputBarBackgroundColor)
                    ((ChatView)b).inputBar.BarBackgroundColor = inputBarBackgroundColor;
            }));

    /// <summary>Leave unset to follow the active theme.</summary>
    public Color? InputBarBackgroundColor
    {
        get => (Color?)GetValue(InputBarBackgroundColorProperty);
        set => SetValue(InputBarBackgroundColorProperty, value);
    }

    public static readonly BindableProperty InputBarBorderColorProperty = BindableProperty.Create(
        nameof(InputBarBorderColor), typeof(Color), typeof(ChatView), null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ChatView), () =>
            {
                // null => leave ChatEntryView's own themed value in place.
                if (n is Color inputBarBorderColor)
                    ((ChatView)b).inputBar.BorderColor = inputBarBorderColor;
            }));

    /// <summary>
    /// Outline of the rounded composer. (Before the composer was rounded this coloured a hairline rule
    /// between the message list and the bar; that rule is gone.) Leave unset to follow the active theme.
    /// </summary>
    public Color? InputBarBorderColor
    {
        get => (Color?)GetValue(InputBarBorderColorProperty);
        set => SetValue(InputBarBorderColorProperty, value);
    }

    public static readonly BindableProperty InputBarProperty = BindableProperty.Create(
        nameof(InputBar), typeof(ChatEntryView), typeof(ChatView), null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ChatView), () =>
            {
                ((ChatView)b).SwapInputBar(n as ChatEntryView);
            }));

    /// <summary>
    /// The composer hosted at the bottom of the chat. Assign your own <see cref="ChatEntryView"/> to
    /// replace the built-in one - the view re-wires its events and re-applies the session state. Reads
    /// back whichever composer is currently in use, so <c>InputBar.MaxLines = 3</c> works without
    /// supplying one. Setting it to null restores the built-in composer.
    /// </summary>
    public ChatEntryView InputBar
    {
        get => (ChatEntryView?)GetValue(InputBarProperty) ?? this.defaultInputBar;
        set => SetValue(InputBarProperty, value);
    }

    /// <summary>
    /// Replays the pass-through style properties onto a newly-swapped-in composer. Only values the
    /// consumer explicitly set on the ChatView are pushed: a supplied ChatEntryView configures itself,
    /// and copying unconditionally would stamp ChatView's defaults over its own placeholder and labels.
    /// </summary>
    void ApplyInputBarStyling()
    {
        var bar = this.inputBar;

        if (this.IsSet(PlaceholderTextProperty))
            bar.PlaceholderText = this.PlaceholderText;
        if (this.IsSet(SendButtonTextProperty))
            bar.SendButtonText = this.SendButtonText;
        if (this.SendButtonBackgroundColor is Color sendBg)
            bar.SendButtonBackgroundColor = sendBg;
        if (this.SendButtonTextColor is Color sendFg)
            bar.SendButtonTextColor = sendFg;
        if (this.InputBarBackgroundColor is Color barBg)
            bar.BarBackgroundColor = barBg;
        if (this.InputBarBorderColor is Color barBorder)
            bar.BorderColor = barBorder;
    }

    public static readonly BindableProperty IsInputBarVisibleProperty = BindableProperty.Create(
        nameof(IsInputBarVisible), typeof(bool), typeof(ChatView), true,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ChatView), () =>
            {
                ((ChatView)b).inputBar.IsVisible = (bool)n;
            }));

    public bool IsInputBarVisible
    {
        get => (bool)GetValue(IsInputBarVisibleProperty);
        set => SetValue(IsInputBarVisibleProperty, value);
    }

    // ------- typing -------

    public static readonly BindableProperty ShowTypingIndicatorProperty = BindableProperty.Create(
        nameof(ShowTypingIndicator), typeof(bool), typeof(ChatView), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ChatView), () =>
            {
                ((ChatView)b).SyncTypingBubbles();
            }));

    public bool ShowTypingIndicator
    {
        get => (bool)GetValue(ShowTypingIndicatorProperty);
        set => SetValue(ShowTypingIndicatorProperty, value);
    }

    // ------- message template -------

    public static readonly BindableProperty MessageTemplateProperty = BindableProperty.Create(
        nameof(MessageTemplate), typeof(DataTemplate), typeof(ChatView), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ChatView), () =>
            {
                ((ChatView)b).RefreshBubbles();
            }));

    public DataTemplate? MessageTemplate
    {
        get => (DataTemplate?)GetValue(MessageTemplateProperty);
        set => SetValue(MessageTemplateProperty, value);
    }

    public static readonly BindableProperty MessageTemplateSelectorProperty = BindableProperty.Create(
        nameof(MessageTemplateSelector), typeof(DataTemplateSelector), typeof(ChatView), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ChatView), () =>
            {
                ((ChatView)b).RefreshBubbles();
            }));

    public DataTemplateSelector? MessageTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(MessageTemplateSelectorProperty);
        set => SetValue(MessageTemplateSelectorProperty, value);
    }

    // ------- fonts / shape -------

    public static readonly BindableProperty BubbleFontSizeProperty = BindableProperty.Create(
        nameof(BubbleFontSize), typeof(double), typeof(ChatView), 15.0,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ChatView), () =>
            {
                ((ChatView)b).RefreshBubbles();
            }));

    public double BubbleFontSize
    {
        get => (double)GetValue(BubbleFontSizeProperty);
        set => SetValue(BubbleFontSizeProperty, value);
    }

    public static readonly BindableProperty BubbleFontFamilyProperty = BindableProperty.Create(
        nameof(BubbleFontFamily), typeof(string), typeof(ChatView), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ChatView), () =>
            {
                ((ChatView)b).RefreshBubbles();
            }));

    public string? BubbleFontFamily
    {
        get => (string?)GetValue(BubbleFontFamilyProperty);
        set => SetValue(BubbleFontFamilyProperty, value);
    }

    public static readonly BindableProperty TimestampFontSizeProperty = BindableProperty.Create(
        nameof(TimestampFontSize), typeof(double), typeof(ChatView), 11.0,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ChatView), () =>
            {
                ((ChatView)b).RefreshBubbles();
            }));

    public double TimestampFontSize
    {
        get => (double)GetValue(TimestampFontSizeProperty);
        set => SetValue(TimestampFontSizeProperty, value);
    }

    public static readonly BindableProperty BubbleCornerRadiusProperty = BindableProperty.Create(
        nameof(BubbleCornerRadius), typeof(double), typeof(ChatView), 18.0,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ChatView), () =>
            {
                ((ChatView)b).RefreshBubbles();
            }));

    public double BubbleCornerRadius
    {
        get => (double)GetValue(BubbleCornerRadiusProperty);
        set => SetValue(BubbleCornerRadiusProperty, value);
    }

    // ------- behavior -------

    public static readonly BindableProperty UseFeedbackProperty = BindableProperty.Create(
        nameof(UseFeedback), typeof(bool), typeof(ChatView), true);

    public bool UseFeedback
    {
        get => (bool)GetValue(UseFeedbackProperty);
        set => SetValue(UseFeedbackProperty, value);
    }

    /// <summary>
    /// When true, the chat view pads its bottom edge by the on-screen keyboard height so the
    /// input bar is never covered. iOS only (Android resizes via WindowSoftInputMode).
    /// </summary>
    public static readonly BindableProperty AdjustForKeyboardProperty = BindableProperty.Create(
        nameof(AdjustForKeyboard), typeof(bool), typeof(ChatView), true);

    public bool AdjustForKeyboard
    {
        get => (bool)GetValue(AdjustForKeyboardProperty);
        set => SetValue(AdjustForKeyboardProperty, value);
    }

    public static readonly BindableProperty ScrollToFirstUnreadProperty = BindableProperty.Create(
        nameof(ScrollToFirstUnread), typeof(bool), typeof(ChatView), false);

    public bool ScrollToFirstUnread
    {
        get => (bool)GetValue(ScrollToFirstUnreadProperty);
        set => SetValue(ScrollToFirstUnreadProperty, value);
    }

    /// <summary>
    /// Resolves an app-level theme token at the moment of reading, transparent if the pack lacks the key.
    /// Only the public getters use this; nothing rendered is painted from it.
    /// </summary>
    static Color ThemeColor(string key)
        => Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c
            ? c
            : Colors.Transparent;
}
