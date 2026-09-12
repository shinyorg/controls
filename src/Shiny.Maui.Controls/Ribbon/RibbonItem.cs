using System.Collections.ObjectModel;

namespace Shiny.Maui.Controls.Ribbons;

/// <summary>
/// One command on a <see cref="RibbonGroup"/>.
/// </summary>
/// <remarks>
/// <para>
/// Items are descriptors, not views. The ribbon reads them and builds the buttons, which is what lets
/// a group re-flow — a hidden item, a size change, a switch to the simplified layout — without the
/// author having rebuilt anything. It also means one item cannot be shown twice, and that an item
/// carries no visual state of its own worth preserving across a rebuild.
/// </para>
/// <para>
/// The concrete kinds are <see cref="RibbonButton"/>, <see cref="RibbonToggleButton"/>,
/// <see cref="RibbonMenuButton"/>, <see cref="RibbonSplitButton"/>, <see cref="RibbonSeparator"/> and
/// <see cref="RibbonContentItem"/>. Deriving further is possible but the ribbon only knows how to draw
/// these, so a new kind needs a <see cref="RibbonContentItem"/> around it.
/// </para>
/// </remarks>
public abstract class RibbonItem : BindableObject
{
    /// <summary>Raised whenever a property the ribbon draws from changes.</summary>
    internal event EventHandler? Changed;

    private protected void RaiseChanged() => this.Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// A <see cref="BindableProperty"/> that repaints the ribbon when it changes. Every property on an
    /// item is one of these — an item that changed quietly would leave a stale button on the bar — so
    /// the callback is declared once here rather than on each of them.
    /// </summary>
    private protected static BindableProperty Redraw(
        string name,
        Type returnType,
        Type declaringType,
        object? defaultValue = null
    )
        => BindableProperty.Create(
            name,
            returnType,
            declaringType,
            defaultValue,
            propertyChanged: (b, _, _) => ((RibbonItem)b).RaiseChanged()
        );


    public static readonly BindableProperty TextProperty = Redraw(nameof(Text), typeof(string), typeof(RibbonItem));

    public static readonly BindableProperty IconProperty = Redraw(nameof(Icon), typeof(ImageSource), typeof(RibbonItem));

    public static readonly BindableProperty IconTemplateProperty = Redraw(nameof(IconTemplate), typeof(DataTemplate), typeof(RibbonItem));

    public static readonly BindableProperty TooltipProperty = Redraw(nameof(Tooltip), typeof(string), typeof(RibbonItem));

    public static readonly BindableProperty DescriptionProperty = Redraw(nameof(Description), typeof(string), typeof(RibbonItem));

    public static readonly BindableProperty SizeProperty = Redraw(nameof(Size), typeof(RibbonItemSize), typeof(RibbonItem), RibbonItemSize.Large);

    public static readonly BindableProperty IsEnabledProperty = Redraw(nameof(IsEnabled), typeof(bool), typeof(RibbonItem), true);

    public static readonly BindableProperty IsVisibleProperty = Redraw(nameof(IsVisible), typeof(bool), typeof(RibbonItem), true);

    public static readonly BindableProperty AutomationIdProperty = Redraw(nameof(AutomationId), typeof(string), typeof(RibbonItem));


    /// <summary>The label under (or beside) the icon.</summary>
    public string? Text
    {
        get => (string?)this.GetValue(TextProperty);
        set => this.SetValue(TextProperty, value);
    }

    /// <summary>
    /// The item's icon. Any <see cref="ImageSource"/> works, so a <see cref="FontImageSource"/> is how
    /// a glyph from an icon font is used without a second property for it.
    /// </summary>
    public ImageSource? Icon
    {
        get => (ImageSource?)this.GetValue(IconProperty);
        set => this.SetValue(IconProperty, value);
    }

    /// <summary>
    /// Draws the icon as a view instead of an image — the escape hatch for icons that are drawn rather
    /// than loaded, such as a <c>GraphicsView</c> over a vector set. Wins over <see cref="Icon"/> when
    /// both are set. The template is instantiated per drawn button, so it must not return a shared view.
    /// </summary>
    public DataTemplate? IconTemplate
    {
        get => (DataTemplate?)this.GetValue(IconTemplateProperty);
        set => this.SetValue(IconTemplateProperty, value);
    }

    /// <summary>
    /// Hover text. Falls back to <see cref="Text"/>, which matters for a small item whose label is
    /// dropped in the simplified layout and for any item drawn icon-only.
    /// </summary>
    public string? Tooltip
    {
        get => (string?)this.GetValue(TooltipProperty);
        set => this.SetValue(TooltipProperty, value);
    }

    /// <summary>A second line under the tooltip's title, for saying what the command actually does.</summary>
    public string? Description
    {
        get => (string?)this.GetValue(DescriptionProperty);
        set => this.SetValue(DescriptionProperty, value);
    }

    /// <summary>How much room the item asks for. See <see cref="RibbonItemSize"/>.</summary>
    public RibbonItemSize Size
    {
        get => (RibbonItemSize)this.GetValue(SizeProperty);
        set => this.SetValue(SizeProperty, value);
    }

    /// <summary>
    /// A disabled item is drawn dimmed and does not respond. Bind this rather than removing the item:
    /// a command that disappears when it cannot run makes the bar move under the pointer.
    /// </summary>
    public bool IsEnabled
    {
        get => (bool)this.GetValue(IsEnabledProperty);
        set => this.SetValue(IsEnabledProperty, value);
    }

    /// <summary>A hidden item is not drawn at all and the group closes over the space.</summary>
    public bool IsVisible
    {
        get => (bool)this.GetValue(IsVisibleProperty);
        set => this.SetValue(IsVisibleProperty, value);
    }

    /// <summary>Automation id applied to the drawn button, for UI tests.</summary>
    public string? AutomationId
    {
        get => (string?)this.GetValue(AutomationIdProperty);
        set => this.SetValue(AutomationIdProperty, value);
    }


    /// <summary>Whether this kind of item can be invoked at all — false for separators and hosted content.</summary>
    internal virtual bool IsInteractive => true;
}


/// <summary>
/// A vertical rule between two runs of items in a group, and a break in the column flow: the small
/// items after a separator start a fresh column rather than filling up the one before it.
/// </summary>
public class RibbonSeparator : RibbonItem
{
    internal override bool IsInteractive => false;
}


/// <summary>
/// A row of items inside a group — the other way to fill a group, and the one a run of formatting
/// marks wants.
/// </summary>
/// <remarks>
/// <para>
/// A group's items normally fill a <em>column</em> top to bottom and start the next when it is full.
/// That is right for a set of interchangeable commands and wrong for a run: bold, italic, underline and
/// strikethrough are read left to right, and a two-row column turns four of them into a 2×2 block with
/// underline above italic. It also forces a font-name box and a bold button into one column, where the
/// column takes the box's width and the 16px B is stretched across all 150px of it.
/// </para>
/// <para>
/// Put <see cref="RibbonRow"/>s in a group and it lays them out as rows instead — which is what Office
/// actually draws. A group is one or the other: rows and loose items are not mixed, because an item
/// with no row of its own has no answer to which row it is on.
/// </para>
/// </remarks>
/// <example>
/// <code language="xaml">
/// &lt;shiny:RibbonGroup Title="Font"&gt;
///     &lt;shiny:RibbonRow&gt;
///         &lt;shiny:RibbonContentItem Size="Small"&gt;&lt;shiny:FontPicker /&gt;&lt;/shiny:RibbonContentItem&gt;
///     &lt;/shiny:RibbonRow&gt;
///     &lt;shiny:RibbonRow&gt;
///         &lt;shiny:RibbonToggleButton Size="Small" Text="Bold" /&gt;
///         &lt;shiny:RibbonToggleButton Size="Small" Text="Italic" /&gt;
///     &lt;/shiny:RibbonRow&gt;
/// &lt;/shiny:RibbonGroup&gt;
/// </code>
/// </example>
[ContentProperty(nameof(Items))]
public class RibbonRow : RibbonItem
{
    readonly ObservableCollection<RibbonItem> items = new();

    public RibbonRow() => this.items.CollectionChanged += (_, _) => this.RaiseChanged();

    /// <summary>The items on this row, left to right.</summary>
    public IList<RibbonItem> Items => this.items;

    internal IReadOnlyList<RibbonItem> VisibleItems => this.items.Where(x => x.IsVisible).ToList();

    internal override bool IsInteractive => false;
}


/// <summary>
/// Hosts an arbitrary view inside a group — a picker, a combo box, a swatch strip, anything the
/// ribbon has no item kind for.
/// </summary>
/// <example>
/// <code language="xaml">
/// &lt;shiny:RibbonContentItem Size="Small"&gt;
///     &lt;shiny:FontPicker SelectedFont="{Binding Font}" WidthRequest="150" /&gt;
/// &lt;/shiny:RibbonContentItem&gt;
/// </code>
/// </example>
/// <remarks>
/// Unlike every other item this one owns a real view, so it is handed to the ribbon rather than built
/// by it. The ribbon re-parents it on each rebuild and never disposes it, which is what lets a hosted
/// picker keep its state while the group around it re-flows.
/// </remarks>
[ContentProperty(nameof(Content))]
public class RibbonContentItem : RibbonItem
{
    public static readonly BindableProperty ContentProperty = Redraw(nameof(Content), typeof(View), typeof(RibbonContentItem));

    /// <summary>The view to place in the group.</summary>
    public View? Content
    {
        get => (View?)this.GetValue(ContentProperty);
        set => this.SetValue(ContentProperty, value);
    }

    internal override bool IsInteractive => false;
}
