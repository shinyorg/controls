using System.Globalization;
using Shiny.Controls.Gantt;

namespace Shiny.Maui.Controls.Gantt;

/// <summary>
/// A column of the task pane — the grid to the left of the timeline.
/// </summary>
/// <remarks>
/// Deliberately not a <c>DataGridColumn</c>. The task pane always binds to a <see cref="GanttTask"/>,
/// so a column can offer a small set of named fields plus the custom bag, and skip the reflection and
/// value-formatter machinery a general grid needs. The escape hatch for anything else is
/// <see cref="CellTemplate"/>.
/// </remarks>
public class GanttColumn : BindableObject
{
    /// <summary>
    /// The built-in fields a column can show without a template. Aliases of <see cref="GanttFields"/>
    /// so XAML can write <c>Field="{x:Static gantt:GanttColumn.StartField}"</c> without reaching into
    /// the shared package.
    /// </summary>
    public const string NameField = GanttFields.Name;
    public const string StartField = GanttFields.Start;
    public const string EndField = GanttFields.End;
    public const string DurationField = GanttFields.Duration;
    public const string ProgressField = GanttFields.Progress;
    public const string ResourceField = GanttFields.Resource;
    public const string SlackField = GanttFields.Slack;
    public const string DeadlineField = GanttFields.Deadline;

    public static readonly BindableProperty HeaderProperty = BindableProperty.Create(
        nameof(Header), typeof(string), typeof(GanttColumn), string.Empty);

    public static readonly BindableProperty FieldProperty = BindableProperty.Create(
        nameof(Field), typeof(string), typeof(GanttColumn), NameField);

    public static readonly BindableProperty WidthProperty = BindableProperty.Create(
        nameof(Width), typeof(double), typeof(GanttColumn), 140d);

    public static readonly BindableProperty FormatProperty = BindableProperty.Create(
        nameof(Format), typeof(string), typeof(GanttColumn));

    public static readonly BindableProperty HorizontalAlignmentProperty = BindableProperty.Create(
        nameof(HorizontalAlignment), typeof(TextAlignment), typeof(GanttColumn), TextAlignment.Start);

    public static readonly BindableProperty CellTemplateProperty = BindableProperty.Create(
        nameof(CellTemplate), typeof(DataTemplate), typeof(GanttColumn));

    public static readonly BindableProperty ShowHierarchyProperty = BindableProperty.Create(
        nameof(ShowHierarchy), typeof(bool), typeof(GanttColumn), false);

    /// <summary>Header text. Defaults to <see cref="Field"/> when left empty.</summary>
    public string Header
    {
        get => (string)this.GetValue(HeaderProperty);
        set => this.SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// One of the built-in field names, or a key into <see cref="GanttTask.Fields"/> for anything
    /// the model does not have a property for.
    /// </summary>
    public string Field
    {
        get => (string)this.GetValue(FieldProperty);
        set => this.SetValue(FieldProperty, value);
    }

    /// <summary>Fixed width in device-independent pixels.</summary>
    public double Width
    {
        get => (double)this.GetValue(WidthProperty);
        set => this.SetValue(WidthProperty, value);
    }

    /// <summary>A standard .NET format string applied to the value, e.g. <c>d</c> or <c>0%</c>.</summary>
    public string? Format
    {
        get => (string?)this.GetValue(FormatProperty);
        set => this.SetValue(FormatProperty, value);
    }

    /// <summary>Text alignment within the cell.</summary>
    public TextAlignment HorizontalAlignment
    {
        get => (TextAlignment)this.GetValue(HorizontalAlignmentProperty);
        set => this.SetValue(HorizontalAlignmentProperty, value);
    }

    /// <summary>Full control over the cell. Its binding context is the <see cref="GanttTask"/>.</summary>
    public DataTemplate? CellTemplate
    {
        get => (DataTemplate?)this.GetValue(CellTemplateProperty);
        set => this.SetValue(CellTemplateProperty, value);
    }

    /// <summary>
    /// Whether this column carries the indent and the expander chevron. Exactly one column should;
    /// the view turns it on for the first column when no column claims it.
    /// </summary>
    public bool ShowHierarchy
    {
        get => (bool)this.GetValue(ShowHierarchyProperty);
        set => this.SetValue(ShowHierarchyProperty, value);
    }


    /// <summary>Renders this column's value for a task.</summary>
    public string GetText(GanttTask task, CultureInfo culture) =>
        GanttFields.TextOf(task, this.Field, this.Format, culture);


    /// <summary>
    /// The raw value behind <see cref="GetText"/>, for sorting or a template to read.
    /// </summary>
    /// <remarks>
    /// Not <c>GetValue</c>: <see cref="BindableObject"/> already declares
    /// <see cref="BindableObject.GetValue(BindableProperty)"/>, and an overload that differs only in
    /// its parameter type shadows it for anyone reading the class — the sort of collision the repo's
    /// accessor-shadowing guard test exists to catch.
    /// </remarks>
    public object? GetFieldValue(GanttTask task) => GanttFields.ValueOf(task, this.Field);
}
