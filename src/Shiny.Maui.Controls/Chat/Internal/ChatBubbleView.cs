using Shiny.Maui.Controls.Themes;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.Chat.Internal;

partial class ChatBubbleView : ContentView
{
    readonly ChatView chatView;
    readonly bool isMe;

    readonly Grid rootLayout;
    readonly Grid avatarNameRow;
    readonly Border avatarBorder;
    readonly Label avatarLabel;
    readonly Image avatarImage;
    readonly Label nameLabel;
    readonly Grid bubbleRow;
    readonly Border bubbleBorder;
    readonly VerticalStackLayout defaultContentLayout;
    readonly Label textLabel;
    readonly Image imageView;
    readonly Label timestampLabel;
    readonly Label statusLabel;
    readonly HorizontalStackLayout reactionsLayout;
    readonly Button actionsButton;
    View? customTemplateView;

    public ChatBubbleView(ChatView chatView, bool isMe)
    {
        this.chatView = chatView;
        this.isMe = isMe;

        this.avatarImage = new Image
        {
            WidthRequest = 32,
            HeightRequest = 32,
            Aspect = Aspect.AspectFill
        };
        this.avatarLabel = new Label
        {
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        }.WithFontSize(ShinyThemeKeys.Type.BodySmallSize);
        this.avatarBorder = new Border
        {
            WidthRequest = 32,
            HeightRequest = 32,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerLargeRadius),
            Padding = 0,
            VerticalOptions = LayoutOptions.Center
        };
        this.nameLabel = new Label
        {
            Margin = new Thickness(4, 0, 0, 2),
            VerticalOptions = LayoutOptions.Center
        }.WithFontSize(ShinyThemeKeys.Type.BodySmallSize);
        this.nameLabel.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

        this.avatarNameRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 6,
            Margin = new Thickness(0, 0, 0, 2)
        };
        this.avatarNameRow.Add(this.avatarBorder, 0, 0);
        this.avatarNameRow.Add(this.nameLabel, 1, 0);

        this.textLabel = new Label { LineBreakMode = LineBreakMode.WordWrap };

        this.imageView = new Image
        {
            Aspect = Aspect.AspectFit,
            MaximumHeightRequest = 250,
            MaximumWidthRequest = 250,
            IsVisible = false
        };
        var imageTap = new TapGestureRecognizer();
        imageTap.Tapped += this.OnImageTapped;
        this.imageView.GestureRecognizers.Add(imageTap);

        this.defaultContentLayout = new VerticalStackLayout
        {
            Children = { this.textLabel, this.imageView }
        };

        this.bubbleBorder = new Border
        {
            Padding = new Thickness(12, 8),
            StrokeThickness = 0,
            MaximumWidthRequest = 280,
            Content = this.defaultContentLayout
        };
        var bubbleTap = new TapGestureRecognizer();
        bubbleTap.Tapped += this.OnBubbleTapped;
        this.bubbleBorder.GestureRecognizers.Add(bubbleTap);

        this.actionsButton = new Button
        {
            Text = "⋮",
            BackgroundColor = Colors.Transparent,
            WidthRequest = 28,
            HeightRequest = 28,
            Padding = 0,
            VerticalOptions = LayoutOptions.Center
        }.Neutralize().WithFontSize(ShinyThemeKeys.Type.BodyLargeSize);
        this.actionsButton.SetDynamicResource(Button.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        this.actionsButton.Clicked += this.OnActionsButtonClicked;

        this.bubbleRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 2
        };
        this.bubbleRow.Add(this.bubbleBorder, 0, 0);
        this.bubbleRow.Add(this.actionsButton, 1, 0);

        this.timestampLabel = new Label
        {
            Margin = new Thickness(4, 2, 4, 0)
        }.WithFontSize(ShinyThemeKeys.Type.LabelSmallSize);
        this.timestampLabel.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

        this.statusLabel = new Label
        {
            Margin = new Thickness(4, 0, 4, 0),
            IsVisible = false
        }.WithFontSize(ShinyThemeKeys.Type.LabelSmallSize);
        this.statusLabel.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        var statusTap = new TapGestureRecognizer();
        statusTap.Tapped += this.OnActionsButtonClicked;
        this.statusLabel.GestureRecognizers.Add(statusTap);

        this.reactionsLayout = new HorizontalStackLayout
        {
            Spacing = 4,
            Margin = new Thickness(4, 2, 4, 0),
            IsVisible = false
        };

        this.rootLayout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            Padding = new Thickness(12, 0)
        };
        this.rootLayout.Add(this.avatarNameRow, 0, 0);
        this.rootLayout.Add(this.bubbleRow, 0, 1);
        this.rootLayout.Add(this.reactionsLayout, 0, 2);
        this.rootLayout.Add(this.timestampLabel, 0, 3);
        this.rootLayout.Add(this.statusLabel, 0, 4);

        this.Content = this.rootLayout;
        this.TrackThemeTextColors();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (this.BindingContext is ChatMessage message)
            this.Configure(message);
    }

    void Configure(ChatMessage message)
    {
        var messages = this.chatView.Items;
        if (messages.Count == 0)
            return;

        var index = -1;
        for (var i = 0; i < messages.Count; i++)
        {
            if (ReferenceEquals(messages[i], message))
            {
                index = i;
                break;
            }
        }
        if (index < 0)
            return;

        var prev = index > 0 ? messages[index - 1] : null;
        var next = index < messages.Count - 1 ? messages[index + 1] : null;
        var isFirst = ChatGroupHelper.IsNewGroup(message, prev);
        var isLast = next is null || ChatGroupHelper.IsNewGroup(next, message);

        var user = this.chatView.GetUser(message.SenderId);
        var showAvatar = this.ShouldShowAvatar(isFirst);

        // alignment
        if (this.isMe)
        {
            this.rootLayout.HorizontalOptions = LayoutOptions.End;
            this.timestampLabel.HorizontalTextAlignment = TextAlignment.End;
            this.statusLabel.HorizontalTextAlignment = TextAlignment.End;
            this.reactionsLayout.HorizontalOptions = LayoutOptions.End;
        }
        else
        {
            this.rootLayout.HorizontalOptions = LayoutOptions.Start;
            this.timestampLabel.HorizontalTextAlignment = TextAlignment.Start;
            this.statusLabel.HorizontalTextAlignment = TextAlignment.Start;
            this.reactionsLayout.HorizontalOptions = LayoutOptions.Start;
        }

        Grid.SetColumn(this.bubbleBorder, this.isMe ? 1 : 0);
        Grid.SetColumn(this.actionsButton, this.isMe ? 0 : 1);

        // avatar + name
        this.avatarNameRow.IsVisible = showAvatar;
        if (showAvatar)
        {
            this.nameLabel.Text = user?.DisplayName ?? "Unknown";
            // Through the token, not the resolved OtherBubbleColor getter, so it follows a theme flip.
            ThemeProbe.Tint(this.avatarBorder, VisualElement.BackgroundColorProperty,
                user?.BubbleColor ?? this.chatView.ExplicitOtherBubbleColor, ShinyThemeKeys.Color.SurfaceContainerHigh);

            if (user?.Avatar is not null)
            {
                this.avatarImage.Source = user.Avatar;
                this.avatarBorder.Content = this.avatarImage;
            }
            else
            {
                this.avatarLabel.Text = ChatGroupHelper.GetInitials(user?.DisplayName);
                this.avatarBorder.Content = this.avatarLabel;
            }
        }

        // Bubble colours. When nothing explicit is set anywhere in the chain the bubble binds to the
        // theme token rather than resolving it once, so switching theme packs repaints live bubbles
        // instead of waiting for them to be rebuilt.
        var explicitBubble = this.isMe
            ? this.chatView.ExplicitMyBubbleColor
            : (user?.BubbleColor ?? this.chatView.ExplicitOtherBubbleColor);
        var explicitText = this.isMe
            ? this.chatView.ExplicitMyTextColor
            : this.chatView.ExplicitOtherTextColor;

        ThemeProbe.Tint(this.bubbleBorder, VisualElement.BackgroundColorProperty, explicitBubble,
            this.isMe ? ShinyThemeKeys.Color.PrimaryContainer : ShinyThemeKeys.Color.SurfaceContainerHigh);

        var textColor = explicitText ?? this.ThemeTextColor;

        var radius = this.chatView.BubbleCornerRadius;
        var tailRadius = isLast ? 4 : radius;
        this.bubbleBorder.StrokeShape = this.isMe
            ? new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(radius, radius, radius, tailRadius) }
            : new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(radius, radius, tailRadius, radius) };

        // content: custom template, image, or markdown text
        var template = this.chatView.MessageTemplateSelector?.SelectTemplate(message, this)
                    ?? this.chatView.MessageTemplate;

        if (template is not null)
        {
            this.customTemplateView = (View)template.CreateContent();
            this.customTemplateView.BindingContext = message;
            this.bubbleBorder.Content = this.customTemplateView;
            this.bubbleBorder.Padding = new Thickness(12, 8);
        }
        else if (!string.IsNullOrEmpty(message.ImageUrl))
        {
            this.RestoreDefaultContent();
            this.textLabel.IsVisible = false;
            this.imageView.IsVisible = true;
            this.imageView.Source = message.ImageUrl;
            this.bubbleBorder.Padding = new Thickness(4);
        }
        else
        {
            this.RestoreDefaultContent();
            this.textLabel.IsVisible = true;
            this.imageView.IsVisible = false;
            ChatMarkdownRenderer.Apply(
                this.textLabel,
                message.Body ?? string.Empty,
                textColor,
                this.chatView.BubbleFontSize,
                this.chatView.BubbleFontFamily,
                this.ThemeLinkColor);
            this.bubbleBorder.Padding = new Thickness(12, 8);
        }

        this.ConfigureReactions(message);

        // timestamp
        this.timestampLabel.IsVisible = isLast;
        this.timestampLabel.FontSize = this.chatView.TimestampFontSize;
        if (isLast)
        {
            var ts = ChatGroupHelper.FormatTimestamp(message.Timestamp);
            if (message.EditedTimestamp is not null)
                ts += " (edited)";
            this.timestampLabel.Text = ts;
        }

        // status / read receipt (own messages)
        this.ConfigureStatus(message, isLast);

        // dim pending / failed
        var dim = message.Status is MessageStatus.Sending or MessageStatus.Failed;
        this.bubbleBorder.Opacity = dim ? 0.5 : 1.0;

        this.Margin = new Thickness(0, isFirst ? 12 : 2, 0, 0);
    }

    void RestoreDefaultContent()
    {
        if (this.customTemplateView is not null)
        {
            this.bubbleBorder.Content = this.defaultContentLayout;
            this.customTemplateView = null;
        }
    }

    void ConfigureStatus(ChatMessage message, bool isLast)
    {
        string? text = null;
        var isError = false;

        if (this.isMe)
        {
            switch (message.Status)
            {
                case MessageStatus.Sending:
                    text = "Sending…";
                    break;
                case MessageStatus.Failed:
                    text = "Failed – tap to retry";
                    isError = true;
                    break;
                case MessageStatus.Rejected:
                    text = string.IsNullOrEmpty(message.StatusReason) ? "Not delivered" : message.StatusReason;
                    isError = true;
                    break;
                default:
                    if (isLast && this.HasOtherReadReceipt(message))
                        text = "Read";
                    break;
            }
        }

        if (string.IsNullOrEmpty(text))
        {
            this.statusLabel.IsVisible = false;
            return;
        }

        this.statusLabel.Text = text;
        if (isError)
            this.statusLabel.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.Error);
        else
            this.statusLabel.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        this.statusLabel.IsVisible = true;
    }

    bool HasOtherReadReceipt(ChatMessage message)
    {
        var me = this.chatView.CurrentUserId;
        foreach (var r in message.ReadReceipts)
        {
            if (r.UserId != me)
                return true;
        }
        return false;
    }

    void ConfigureReactions(ChatMessage message)
    {
        this.reactionsLayout.Children.Clear();

        if (message.Reactions is not { Count: > 0 })
        {
            this.reactionsLayout.IsVisible = false;
            return;
        }

        var groups = message.Reactions
            .Where(r => !string.IsNullOrEmpty(r.Emoji))
            .GroupBy(r => r.Emoji)
            .ToList();

        if (groups.Count == 0)
        {
            this.reactionsLayout.IsVisible = false;
            return;
        }

        foreach (var group in groups)
        {
            var count = group.Count();
            var countLabel = new Label
            {
                Text = count > 1 ? count.ToString() : string.Empty,
                VerticalTextAlignment = TextAlignment.Center,
                IsVisible = count > 1
            }.WithFontSize(ShinyThemeKeys.Type.LabelSmallSize);
            countLabel.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

            var badge = new Border
            {
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerMediumRadius),
                Padding = new Thickness(6, 2),
                Content = new HorizontalStackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        new Label { Text = group.Key, VerticalTextAlignment = TextAlignment.Center }.WithFontSize(ShinyThemeKeys.Type.BodySmallSize),
                        countLabel
                    }
                }
            };
            badge.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHigh);
            this.reactionsLayout.Children.Add(badge);
        }

        this.reactionsLayout.IsVisible = true;
    }

    bool ShouldShowAvatar(bool isFirstInGroup)
        => !this.isMe && isFirstInGroup && this.chatView.IsMultiPerson;

    void OnBubbleTapped(object? sender, TappedEventArgs e)
    {
        if (this.BindingContext is ChatMessage msg && string.IsNullOrEmpty(msg.ImageUrl))
            this.chatView.OnMessageTapped(msg);
    }

    void OnImageTapped(object? sender, TappedEventArgs e)
    {
        if (this.BindingContext is ChatMessage msg)
            this.chatView.OnImageTapped(msg, this.imageView.Source);
    }

    void OnActionsButtonClicked(object? sender, EventArgs e)
    {
        if (this.BindingContext is ChatMessage msg)
            this.chatView.ShowBubbleActions(msg);
    }

    // ------- theme-following text colours -------
    //
    // Message text is a FormattedString of spans (markdown runs and links), and a span's colour is a
    // plain value handed over at render. Copying it out of Application.Current.Resources left every
    // rendered bubble in the old palette after a light/dark flip - dark text on a now-dark bubble -
    // and ignored a palette scoped over the chat. These two properties resolve the tokens live through
    // the bubble's own tree; a change recolours the existing spans in place rather than re-rendering.

    static readonly BindableProperty ThemeTextColorProperty = BindableProperty.Create(
        nameof(ThemeTextColor), typeof(Color), typeof(ChatBubbleView), Colors.Transparent,
        propertyChanged: static (b, _, _) => ((ChatBubbleView)b).RecolorText());

    static readonly BindableProperty ThemeLinkColorProperty = BindableProperty.Create(
        nameof(ThemeLinkColor), typeof(Color), typeof(ChatBubbleView), Colors.Transparent,
        propertyChanged: static (b, _, _) => ((ChatBubbleView)b).RecolorText());

    internal Color ThemeTextColor => (Color?)this.GetValue(ThemeTextColorProperty) ?? Colors.Transparent;

    internal Color ThemeLinkColor => (Color?)this.GetValue(ThemeLinkColorProperty) ?? Colors.Transparent;

    /// <summary>Test seam: the label the message body renders into.</summary>
    internal Label TextLabel => this.textLabel;

    void TrackThemeTextColors()
    {
        this.SetDynamicResource(ThemeTextColorProperty,
            this.isMe ? ShinyThemeKeys.Color.OnPrimaryContainer : ShinyThemeKeys.Color.OnSurface);
        this.SetDynamicResource(ThemeLinkColorProperty, ShinyThemeKeys.Color.Primary);
    }

    /// <summary>
    /// Pushes the current theme colours into the spans already rendered. Links are the spans that carry
    /// a tap recognizer; everything else is body text, which keeps an explicit My/OtherTextColor when one
    /// is set (changing those re-renders the bubbles on its own).
    /// </summary>
    void RecolorText()
    {
        // Nothing rendered yet (the tokens first resolve in the constructor), or an image/template bubble.
        if (this.textLabel.FormattedText is not { } formatted)
            return;

        var explicitText = this.isMe ? this.chatView.ExplicitMyTextColor : this.chatView.ExplicitOtherTextColor;
        var text = explicitText ?? this.ThemeTextColor;
        var link = this.ThemeLinkColor;

        foreach (var span in formatted.Spans)
            span.TextColor = span.GestureRecognizers.Count > 0 ? link : text;
    }
}
