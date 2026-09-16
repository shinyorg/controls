using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Kanban.Internal;

/// <summary>
/// The default card: an accent stripe, label chips, a badge, a title, a clamped description and a
/// footer carrying the assignee and the due date.
/// </summary>
/// <remarks>
/// Every part of it is optional and every part of it is driven off the owning view's display
/// properties, so a board that wants less says so rather than reaching for
/// <c>KanbanView.CardTemplate</c>. The template is there for a card that is genuinely a different
/// shape - a photo, a chart - not for one that merely wants its due date hidden.
/// </remarks>
sealed class KanbanCardView : Border
{
    const double AvatarSize = 22;

    readonly KanbanView owner;
    readonly BoxView accent;
    readonly FlexLayout labels;
    readonly Label badge;
    readonly Label title;
    readonly Label description;
    readonly Grid footer;
    readonly Border avatar;
    readonly Label avatarInitials;
    readonly Image avatarImage;
    readonly Label assignee;
    readonly Label due;
    readonly Label locked;

    public KanbanCardView(KanbanView owner, KanbanCard card)
    {
        this.owner = owner;
        this.Card = card;

        this.StrokeThickness = 1;
        this.StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerMediumRadius);
        this.Padding = 0;

        this.accent = new BoxView { WidthRequest = 4, IsVisible = false };

        this.labels = new FlexLayout
        {
            Wrap = FlexWrap.Wrap,
            Direction = FlexDirection.Row,
            IsVisible = false
        };

        this.badge = new Label
        {
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            IsVisible = false
        }.WithFontSize(ShinyThemeKeys.Type.LabelSmallSize);

        this.title = new Label
        {
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 3
        }.WithFontSize(ShinyThemeKeys.Type.TitleSmallSize);

        this.description = new Label
        {
            LineBreakMode = LineBreakMode.TailTruncation,
            IsVisible = false
        }.WithFontSize(ShinyThemeKeys.Type.BodySmallSize);

        this.avatarInitials = new Label
        {
            FontSize = 9,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };

        this.avatarImage = new Image { Aspect = Aspect.AspectFill, IsVisible = false };

        this.avatar = new Border
        {
            WidthRequest = AvatarSize,
            HeightRequest = AvatarSize,
            StrokeThickness = 0,
            Padding = 0,
            // Half the avatar, not a theme radius: a circle is intrinsic to what an avatar is, and a
            // theme that squares every other corner should not turn this one into a rounded square.
            StrokeShape = new RoundRectangle { CornerRadius = AvatarSize / 2 },
            Content = new Grid { Children = { this.avatarInitials, this.avatarImage } },
            VerticalOptions = LayoutOptions.Center
        };

        this.assignee = new Label
        {
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        }.WithFontSize(ShinyThemeKeys.Type.LabelSmallSize);

        this.due = new Label
        {
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.End,
            VerticalTextAlignment = TextAlignment.Center
        }.WithFontSize(ShinyThemeKeys.Type.LabelSmallSize);

        // A padlock rather than a dimmed card: a card that merely looks faded reads as disabled or
        // as a rendering glitch, and the user goes on trying to drag it.
        this.locked = new Label
        {
            Text = "\U0001F512",
            FontSize = 10,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            IsVisible = false
        };

        var assigneeRow = new HorizontalStackLayout
        {
            Spacing = 6,
            Children = { this.avatar, this.assignee }
        };

        this.footer = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            IsVisible = false
        };
        this.footer.Add(assigneeRow, 0, 0);
        this.footer.Add(this.due, 1, 0);

        var body = new VerticalStackLayout
        {
            Spacing = 4,
            Padding = new Thickness(10, 8),
            Children = { this.labels, this.title, this.description, this.footer }
        };

        var top = new Grid();
        top.Add(body);
        top.Add(this.badge);
        top.Add(this.locked);
        this.badge.Margin = new Thickness(0, 6, 10, 0);
        this.locked.Margin = new Thickness(0, 6, 10, 0);

        var root = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 0
        };
        root.Add(this.accent, 0, 0);
        root.Add(top, 1, 0);

        this.Content = root;

        this.Refresh();
    }


    public KanbanCard Card { get; }


    /// <summary>Repaints from the card. Cheap enough to call on every one of its property changes.</summary>
    public void Refresh()
    {
        var card = this.Card;

        KanbanChrome.Token(this, BackgroundColorProperty, ShinyThemeKeys.Color.Surface);
        KanbanChrome.Token(this, StrokeProperty, ShinyThemeKeys.Color.OutlineVariant);
        KanbanChrome.Token(this.title, Label.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
        KanbanChrome.Token(this.description, Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        KanbanChrome.Token(this.assignee, Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        KanbanChrome.Token(this.badge, Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

        var accentColor = KanbanChrome.Parse(card.Color);
        this.accent.IsVisible = accentColor is not null;
        if (accentColor is not null)
            this.accent.Color = accentColor;

        this.title.Text = card.Title;

        var lines = this.owner.DescriptionLineLimit;
        this.description.IsVisible = lines > 0 && !String.IsNullOrWhiteSpace(card.Description);
        this.description.Text = card.Description ?? String.Empty;
        if (lines > 0)
            this.description.MaxLines = lines;

        this.badge.IsVisible = !String.IsNullOrWhiteSpace(card.Badge);
        this.badge.Text = card.Badge ?? String.Empty;

        this.locked.IsVisible = card.IsLocked && !this.badge.IsVisible;

        this.RefreshLabels();
        this.RefreshFooter();
    }


    void RefreshLabels()
    {
        this.labels.Children.Clear();

        if (!this.owner.ShowLabels || this.Card.Labels.Count == 0)
        {
            this.labels.IsVisible = false;
            return;
        }

        this.labels.IsVisible = true;

        foreach (var label in this.Card.Labels)
        {
            var fill = KanbanChrome.Parse(label.Color)
                       ?? KanbanChrome.Fallback(ShinyThemeKeys.Color.SecondaryContainer);

            // A label with no text is a colour dot, which is how a dense board shows six tags in the
            // width of one word.
            if (String.IsNullOrEmpty(label.Text))
            {
                this.labels.Children.Add(new BoxView
                {
                    WidthRequest = 22,
                    HeightRequest = 6,
                    CornerRadius = 3,
                    Color = fill,
                    Margin = new Thickness(0, 0, 4, 4)
                });
                continue;
            }

            var chip = new Border
            {
                BackgroundColor = fill,
                StrokeThickness = 0,
                Padding = new Thickness(6, 2),
                Margin = new Thickness(0, 0, 4, 4),
                StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerExtraSmallRadius),
                Content = new Label
                {
                    Text = label.Text,
                    FontSize = 10,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = KanbanChrome.InkOn(fill)
                }
            };
            this.labels.Children.Add(chip);
        }
    }


    void RefreshFooter()
    {
        var card = this.Card;

        var wantsAssignee = this.owner.ShowAssignee &&
            (!String.IsNullOrWhiteSpace(card.AssigneeName) || !String.IsNullOrWhiteSpace(card.AssigneeImage));

        var wantsDue = this.owner.ShowDueDate && card.DueDate is not null;

        this.footer.IsVisible = wantsAssignee || wantsDue;
        if (!this.footer.IsVisible)
            return;

        this.avatar.IsVisible = wantsAssignee;
        this.assignee.IsVisible = wantsAssignee;

        if (wantsAssignee)
        {
            KanbanChrome.Token(this.avatar, BackgroundColorProperty, ShinyThemeKeys.Color.SecondaryContainer);
            KanbanChrome.Token(this.avatarInitials, Label.TextColorProperty, ShinyThemeKeys.Color.OnSecondaryContainer);

            this.assignee.Text = card.AssigneeName ?? String.Empty;
            this.avatarInitials.Text = Initials(card.AssigneeName);

            var hasImage = !String.IsNullOrWhiteSpace(card.AssigneeImage);
            this.avatarImage.IsVisible = hasImage;
            this.avatarInitials.IsVisible = !hasImage;
            this.avatarImage.Source = hasImage ? ImageSource.FromUri(SafeUri(card.AssigneeImage!)) : null;

            // A relative path or a bundled resource is not a URI; fall back to the file source
            // rather than showing an empty circle where a face should be.
            if (hasImage && this.avatarImage.Source is null)
                this.avatarImage.Source = ImageSource.FromFile(card.AssigneeImage!);
        }

        this.due.IsVisible = wantsDue;
        if (!wantsDue)
            return;

        var date = card.DueDate!.Value;
        this.due.Text = date.ToString(this.owner.DueDateFormat, this.owner.EffectiveCulture);

        var now = DateTimeOffset.Now;
        var key = date < now
            ? ShinyThemeKeys.Color.Error
            : date - now <= this.owner.DueSoonWindow
                ? ShinyThemeKeys.Color.Warning
                : ShinyThemeKeys.Color.OnSurfaceVariant;

        KanbanChrome.Token(this.due, Label.TextColorProperty, key);
    }


    static Uri? SafeUri(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;


    static string Initials(string? name)
    {
        if (String.IsNullOrWhiteSpace(name))
            return "?";

        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant()
        };
    }
}
