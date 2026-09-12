using Shiny.Controls.Office.Icons;
using Shiny.Controls.Office.Shapes;
using Shiny.Maui.Controls.Ribbons;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// Builds the ribbon items the three Office bars are made of.
/// </summary>
/// <remarks>
/// <para>
/// The spreadsheet, document and slide bars all draw from the shared Office icon set and all want the
/// same two shapes - a command and a toggle, small, icon over an optional label. Written out per bar
/// they drifted before: that is how the three ended up with different marks for the same command,
/// which is the whole reason <see cref="OfficeIcons"/> exists. One factory keeps them honest.
/// </para>
/// <para>
/// The icons are <b>drawn</b> rather than loaded, so they cannot go through <see cref="RibbonItem.Icon"/>,
/// which takes an <see cref="ImageSource"/>. <see cref="RibbonItem.IconTemplate"/> is the escape hatch
/// for exactly this, and it is instantiated per button - it has to be, since one view cannot be in two
/// places at once.
/// </para>
/// </remarks>
static class OfficeRibbonItems
{
    /// <summary>A command button: runs and returns, no state of its own.</summary>
    public static RibbonButton Command(OfficeIcon icon, string tooltip, Action action, string? text = null, string? automationId = null)
        => new()
        {
            Text = text,
            Tooltip = tooltip,
            Size = RibbonItemSize.Small,
            AutomationId = automationId,
            IconTemplate = IconTemplateFor(icon),
            Command = new Command(action)
        };

    /// <summary>A toggle: reflects a piece of the caret's or selection's formatting.</summary>
    public static RibbonToggleButton Toggle(OfficeIcon icon, string tooltip, Action action, string? automationId = null)
        => new()
        {
            Tooltip = tooltip,
            Size = RibbonItemSize.Small,
            AutomationId = automationId,
            IconTemplate = IconTemplateFor(icon),
            Command = new Command(action)
        };

    /// <summary>
    /// A row of items, for a group whose commands are a run people read across.
    /// </summary>
    /// <remarks>
    /// The three bars all have them: bold/italic/underline/strike, the alignments, insert-row beside
    /// insert-column. Filling a group's columns instead turned each run into a 2×N block read top to
    /// bottom - underline above italic - and put a font-name box in the same column as a toggle, where
    /// the column took the box's width and stretched the 16px mark across all of it.
    /// </remarks>
    public static RibbonRow Row(params RibbonItem[] items)
    {
        var row = new RibbonRow();

        foreach (var item in items)
            row.Items.Add(item);

        return row;
    }

    /// <summary>
    /// A large command: the icon over its name, one column to itself.
    /// </summary>
    /// <remarks>
    /// One per group at most, and only where the group has a command that is plainly its head — Paste,
    /// AutoSum, a spell check. It is what gives a group a shape to recognise at a glance; a bar of
    /// nothing but 16px marks has none, and it is also the shape three small items want, since two
    /// rows of three leave a hole where the third would be.
    /// </remarks>
    public static RibbonButton LargeCommand(OfficeIcon icon, string text, string tooltip, Action action, string? automationId = null)
        => new()
        {
            Text = text,
            Tooltip = tooltip,
            Size = RibbonItemSize.Large,
            AutomationId = automationId,
            IconTemplate = IconTemplateFor(icon),
            Command = new Command(action)
        };

    /// <summary>Hosts a control the ribbon has no item kind for - a picker with its own popup.</summary>
    /// <remarks>
    /// One row tall, deliberately. A row-spanning host is centred across the rows, so a 30px picker
    /// floats in the middle of a 76px column while the buttons beside it sit on the rows - three
    /// different vertical alignments in one group. It also staircases in the simplified single-row
    /// layout, where the second control in a stack has nowhere to go.
    /// </remarks>
    public static RibbonContentItem Host(View content)
        => new() { Size = RibbonItemSize.Small, Content = content };

    /// <summary>
    /// Hosts a control that is the whole of its group, spanning the rows rather than sitting on one.
    /// </summary>
    /// <remarks>
    /// For the find bar and the single-control groups. On one small row they left the row underneath
    /// them empty for the width of a search box, which is the widest hole a ribbon can have.
    /// </remarks>
    public static RibbonContentItem HostLarge(View content)
        => new() { Size = RibbonItemSize.Large, Content = content };

    /// <summary>
    /// A tab of shapes, each drawn as the shape it inserts.
    /// </summary>
    /// <remarks>
    /// A gallery, not a dropdown. Twenty shapes behind a button is a panel large enough to cover the
    /// document it is about to draw on, and it has to be dismissed before the result can be seen. On a
    /// tab they are simply there, and the tab strip already says what the alternative is.
    /// </remarks>
    public static RibbonTab ShapesTab(Action<ShapeGeometry> insert)
    {
        var tab = new RibbonTab { Title = "Shapes", Key = "shapes" };

        // Grouped the way the shapes themselves divide, so the gallery can be scanned rather than read:
        // things with corners, things without, and the arrows - which are what most of a diagram is.
        Add("Rectangles", 100,
            ShapeGeometry.Rectangle,
            ShapeGeometry.RoundedRectangle,
            ShapeGeometry.Parallelogram,
            ShapeGeometry.Trapezoid);

        Add("Basic", 90,
            ShapeGeometry.Ellipse,
            ShapeGeometry.Triangle,
            ShapeGeometry.RightTriangle,
            ShapeGeometry.Diamond,
            ShapeGeometry.Pentagon,
            ShapeGeometry.Hexagon,
            ShapeGeometry.Star5,
            ShapeGeometry.Plus,
            ShapeGeometry.Can,
            ShapeGeometry.Cloud,
            ShapeGeometry.Line);

        Add("Arrows", 80,
            ShapeGeometry.RightArrow,
            ShapeGeometry.LeftArrow,
            ShapeGeometry.UpArrow,
            ShapeGeometry.DownArrow,
            ShapeGeometry.Chevron);

        return tab;

        void Add(string title, int priority, params ShapeGeometry[] geometries)
        {
            var group = new RibbonGroup { Title = title, Priority = priority };

            foreach (var geometry in geometries)
            {
                var name = OfficeMenus.Shapes.FirstOrDefault(x => x.Geometry == geometry).Name ?? geometry.ToString();
                group.Items.Add(OfficeRibbonItems.Shape(geometry, name, () => insert(geometry)));
            }

            tab.Groups.Add(group);
        }
    }

    /// <summary>A gallery button whose icon is the shape it inserts.</summary>
    public static RibbonButton Shape(ShapeGeometry geometry, string name, Action action)
        => new()
        {
            Tooltip = name,
            Size = RibbonItemSize.Small,
            AutomationId = "Shape" + geometry,
            IconTemplate = new DataTemplate(() => new GraphicsView
            {
                Drawable = new OfficeToolbarIconDrawable { Shapes = ShapeIcons.For(geometry), Color = IconTint },
                HeightRequest = 18,
                WidthRequest = 18,
                InputTransparent = true,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }),
            Command = new Command(action)
        };

    public static DataTemplate IconTemplateFor(OfficeIcon icon)
        => new(() => new GraphicsView
        {
            Drawable = new OfficeToolbarIconDrawable { Icon = icon, Color = IconTint },
            HeightRequest = 18,
            WidthRequest = 18,
            InputTransparent = true,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        });

    /// <summary>
    /// The ribbon draws on a themed surface, so the icons take the theme's ink rather than the
    /// near-white the old floating bars used.
    /// </summary>
    static Color IconTint
        => Application.Current?.Resources.TryGetValue(ShinyThemeKeys.Color.OnSurfaceVariant, out var v) == true && v is Color c
            ? c
            : Colors.Gray;
}
