using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Kanban.Internal;

/// <summary>
/// The default column header: a colour rule, the title, the card count against its WIP limit, and a
/// collapse chevron.
/// </summary>
/// <remarks>
/// The count is the header's whole job. A Kanban's limit is worth nothing if reading it means
/// counting cards, so it sits next to the title in the one place the eye already goes, and turns
/// the error colour the instant the column is over.
/// </remarks>
sealed class KanbanColumnHeaderView : Border
{
    readonly KanbanView owner;
    readonly BoxView rule;
    readonly Label title;
    readonly Label subtitle;
    readonly Label count;
    readonly Label chevron;
    readonly Grid row;

    public KanbanColumnHeaderView(KanbanView owner, KanbanColumn column, int cardCount)
    {
        this.owner = owner;
        this.Column = column;

        this.StrokeThickness = 0;
        this.Padding = new Thickness(10, 8);

        // Rounded on top and square where it meets the column well, so the two read as one shape.
        // The radius comes off the owner's CornerRadiusProbe - zeroing two corners means doing
        // arithmetic on the token, and a DynamicResource is not a value you can read.
        this.Shape = new RoundRectangle();
        this.StrokeShape = this.Shape;

        this.rule = new BoxView { HeightRequest = 3, VerticalOptions = LayoutOptions.Start };

        this.title = new Label
        {
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalTextAlignment = TextAlignment.Center
        }.WithFontSize(ShinyThemeKeys.Type.TitleSmallSize);

        this.subtitle = new Label
        {
            LineBreakMode = LineBreakMode.TailTruncation,
            IsVisible = false
        }.WithFontSize(ShinyThemeKeys.Type.LabelSmallSize);

        this.count = new Label
        {
            FontAttributes = FontAttributes.Bold,
            // Never wraps. In a 52-wide spine a wrapping "3 / 4" broke into lines a single-line row
            // cannot show, and iOS centres them vertically - all that was left on screen was "/ 4".
            LineBreakMode = LineBreakMode.NoWrap,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.End
        }.WithFontSize(ShinyThemeKeys.Type.LabelMediumSize);

        this.chevron = new Label
        {
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            WidthRequest = 18
        }.WithFontSize(ShinyThemeKeys.Type.LabelMediumSize);

        // Columns, rows and placement are set per state in ArrangeRow.
        this.row = new Grid();
        this.row.Add(this.title);
        this.row.Add(this.count);
        this.row.Add(this.chevron);

        var stack = new VerticalStackLayout
        {
            Spacing = 2,
            Children = { this.row, this.subtitle }
        };

        var outer = new Grid { RowSpacing = 6 };
        outer.Add(this.rule);
        outer.Add(stack);
        stack.Margin = new Thickness(0, 7, 0, 0);

        this.Content = outer;

        this.Refresh(cardCount);
    }


    public KanbanColumn Column { get; }

    /// <summary>The chevron's hit target - the view the collapse gesture is attached to.</summary>
    public View Chevron => this.chevron;

    /// <summary>The header's own shape, so the owner can round its top two corners from the theme.</summary>
    public RoundRectangle Shape { get; }

    /// <summary>The count / WIP-limit label.</summary>
    internal Label CountLabel => this.count;

    /// <summary>The row holding the title, the count and the chevron.</summary>
    internal Grid Row => this.row;


    /// <summary>
    /// Lays the row out for the column's state. Expanded it is title | count | chevron across the
    /// header. Collapsed there is no title, and a 52-wide spine has no room for three columns with
    /// spacing between them - the count was handed whatever was left over, which was nothing - so it
    /// stacks: the chevron on top, the count centred under it.
    /// </summary>
    void ArrangeRow(bool collapsed)
    {
        this.row.ColumnDefinitions.Clear();
        this.row.RowDefinitions.Clear();

        if (collapsed)
        {
            this.Padding = new Thickness(4, 8);
            this.row.ColumnSpacing = 0;
            this.row.RowSpacing = 4;
            this.row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            this.row.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            this.row.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            Place(this.title, 0, 0);
            Place(this.chevron, 0, 0);
            Place(this.count, 0, 1);

            this.chevron.HorizontalOptions = LayoutOptions.Center;
            this.count.HorizontalOptions = LayoutOptions.Center;
            this.count.HorizontalTextAlignment = TextAlignment.Center;
        }
        else
        {
            this.Padding = new Thickness(10, 8);
            this.row.ColumnSpacing = 6;
            this.row.RowSpacing = 0;
            this.row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            this.row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            this.row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            this.row.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            Place(this.title, 0, 0);
            Place(this.count, 1, 0);
            Place(this.chevron, 2, 0);

            this.chevron.HorizontalOptions = LayoutOptions.Fill;
            this.count.HorizontalOptions = LayoutOptions.Fill;
            this.count.HorizontalTextAlignment = TextAlignment.End;
        }

        static void Place(View view, int column, int row)
        {
            Grid.SetColumn(view, column);
            Grid.SetRow(view, row);
        }
    }


    /// <summary>
    /// The count as the header shows it. A collapsed spine drops the spaces around the slash, so
    /// "12/15" fits the spine where "12 / 15" does not.
    /// </summary>
    internal static string FormatCount(KanbanColumnCount display, int cardCount, int? limit, bool collapsed, IFormatProvider culture)
    {
        var count = cardCount.ToString(culture);

        return display switch
        {
            KanbanColumnCount.None => String.Empty,
            KanbanColumnCount.Count => count,
            _ when limit is null => count,
            _ => collapsed
                ? $"{count}/{limit.Value.ToString(culture)}"
                : $"{count} / {limit.Value.ToString(culture)}"
        };
    }


    public void Refresh(int cardCount)
    {
        var column = this.Column;
        var collapsed = column.IsCollapsed;

        this.ArrangeRow(collapsed);

        KanbanChrome.Token(this, BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainer);
        KanbanChrome.Token(this.title, Label.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
        KanbanChrome.Token(this.subtitle, Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        KanbanChrome.Token(this.chevron, Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

        var accent = KanbanChrome.Parse(column.Color);
        this.rule.IsVisible = accent is not null;
        if (accent is not null)
            this.rule.Color = accent;

        this.title.Text = column.Title;
        this.title.IsVisible = !collapsed;

        this.subtitle.Text = column.Description ?? String.Empty;
        this.subtitle.IsVisible = !collapsed && !String.IsNullOrWhiteSpace(column.Description);

        this.chevron.IsVisible = this.owner.AllowColumnCollapse && !this.owner.IsReadOnly;
        this.chevron.Text = collapsed ? "›" : "‹";

        var limit = column.WipLimit is > 0 ? column.WipLimit : null;
        var over = limit is not null && cardCount > limit;

        this.count.Text = FormatCount(this.owner.ColumnCountDisplay, cardCount, limit, collapsed, this.owner.EffectiveCulture);
        this.count.IsVisible = this.count.Text.Length > 0;

        // Over-limit colouring is ShowWipLimits' business, not WipBehavior's: a board can refuse
        // the drop without shouting about it, or shout without refusing.
        if (over && this.owner.ShowWipLimits)
        {
            KanbanChrome.Token(this, BackgroundColorProperty, ShinyThemeKeys.Color.ErrorContainer);
            KanbanChrome.Token(this.count, Label.TextColorProperty, ShinyThemeKeys.Color.OnErrorContainer);
            KanbanChrome.Token(this.title, Label.TextColorProperty, ShinyThemeKeys.Color.OnErrorContainer);
        }
        else
        {
            KanbanChrome.Token(this.count, Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        }
    }
}
