using Shiny.Controls.Gantt;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Gantt.Internal;

/// <summary>One row of the task grid: the indent, the expander, and a cell per column.</summary>
sealed class GanttTaskPaneRow : Grid
{
    const double IndentPerLevel = 14;
    const double ChevronWidth = 18;

    readonly GanttView owner;
    readonly GanttTask task;
    readonly BoxView selectionBackdrop;

    public GanttTaskPaneRow(GanttView owner, GanttRow row, IReadOnlyList<GanttColumn> columns)
    {
        this.owner = owner;
        this.task = row.Task;

        this.HeightRequest = owner.RowHeight;
        this.ColumnSpacing = 0;
        this.BindingContext = row.Task;

        this.selectionBackdrop = new BoxView { IsVisible = owner.IsSelected(row.Task), Opacity = 0.16 };
        this.selectionBackdrop.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.Primary);

        var content = new Grid { ColumnSpacing = 0 };

        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            content.ColumnDefinitions.Add(new ColumnDefinition(column.Width));
            content.Add(this.BuildCell(column, row), i, 0);
        }

        this.Add(this.selectionBackdrop);
        this.Add(content);

        // Command rather than the Tapped event: an event cannot be raised from a test, and the row's
        // selection behaviour is exactly the kind of thing that needs asserting without a device.
        this.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => owner.HandleRowTapped(this.task))
        });
    }

    /// <summary>The task this row draws, so the view can refresh selection without rebuilding.</summary>
    public GanttTask Task => this.task;

    public void UpdateSelection() => this.selectionBackdrop.IsVisible = this.owner.IsSelected(this.task);


    View BuildCell(GanttColumn column, GanttRow row)
    {
        var indent = column.ShowHierarchy ? (row.Depth * IndentPerLevel) + 4 : 8;

        if (column.CellTemplate?.CreateContent() is View templated)
        {
            templated.BindingContext = row.Task;
            templated.Margin = new Thickness(indent, 0, 8, 0);
            return column.ShowHierarchy ? this.WithChevron(templated, row, indent) : templated;
        }

        var label = new Label
        {
            Text = column.GetText(row.Task, this.owner.EffectiveCulture),
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            HorizontalTextAlignment = column.HorizontalAlignment,
            Margin = new Thickness(column.ShowHierarchy ? 0 : indent, 0, 8, 0)
        };
        label.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
        label.WithFontSize(ShinyThemeKeys.Type.BodySmallSize);

        if (row.Task.IsRollup)
            label.FontAttributes = FontAttributes.Bold;

        return column.ShowHierarchy ? this.WithChevron(label, row, indent) : label;
    }


    /// <summary>
    /// Wraps a cell in the indent plus the expander. A leaf still gets the chevron's width so its
    /// text lines up with its siblings' — without it, a leaf between two summaries visibly shifts
    /// left and the tree stops reading as a tree.
    /// </summary>
    View WithChevron(View cell, GanttRow row, double indent)
    {
        var host = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(indent),
                new ColumnDefinition(ChevronWidth),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 0
        };

        if (row.HasChildren)
        {
            var chevron = new Label
            {
                Text = row.IsExpanded ? "▾" : "▸",
                VerticalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center,
                FontSize = 12
            };
            chevron.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

            var hit = new ContentView
            {
                Content = chevron,
                GestureRecognizers =
                {
                    new TapGestureRecognizer { Command = new Command(() => this.owner.ToggleExpand(row.Task)) }
                }
            };
            host.Add(hit, 1, 0);
        }

        host.Add(cell, 2, 0);
        return host;
    }
}
