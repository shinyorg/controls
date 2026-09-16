using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Ribbons;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.ImageEditor;

/// <summary>
/// The ribbon presentation of the editor's toolbar.
/// </summary>
/// <remarks>
/// <para>
/// The tools were already grouped the way a ribbon wants them - a tool picker, a per-tool options
/// row that appears and disappears, and a row of history and view commands - so this is mostly a
/// change of container. What it buys is the part the hand-rolled bar never had: the options row is a
/// <b>contextual tab</b> now, captioned with the tool it belongs to, rather than an unlabelled strip
/// that silently changes shape under the buttons that caused it.
/// </para>
/// <para>
/// The bar is rebuilt wholesale on every change, as the previous toolbar was. That loses the ribbon's
/// selected tab, so the key is captured first and restored afterwards - otherwise picking a colour
/// would throw you back to Home on every click.
/// </para>
/// </remarks>
public partial class ImageEditor
{
    const string HomeTabKey = "home";
    const string ViewTabKey = "view";
    const string ToolTabKey = "tool";

    /// <summary>
    /// Below this width the ribbon runs in <see cref="RibbonDisplayMode.Simplified"/> - one dense row,
    /// every item small, group titles dropped.
    /// </summary>
    /// <remarks>
    /// An expanded ribbon is about a quarter of a phone screen, and this control's whole job is to
    /// show the picture underneath it. 600 is the usual phone/tablet break, and it is measured against
    /// the editor rather than the window so the same rule holds for an editor in a side panel.
    /// </remarks>
    const double SimplifiedBreakpoint = 600;

    /// <summary>
    /// Every item is <see cref="RibbonItemSize.Small"/>, deliberately, rather than the ribbon's Large
    /// default.
    /// </summary>
    /// <remarks>
    /// Two reasons. Expanded, a dozen large buttons is not a palette - Small stacks them three to a
    /// column and the whole tool set reads at a glance. Simplified, the ribbon keeps a label only on
    /// items that were declared Small, so a mix of sizes produced a row where some tools were labelled
    /// and some were bare icons with no rule a user could see. All Small is uniform in both modes.
    /// </remarks>

    Ribbon? ribbon;

    /// <summary>
    /// Set once the user has collapsed the bar, so a rebuild does not throw their choice away.
    /// </summary>
    /// <remarks>
    /// The toolbar is rebuilt on every tool change and every property change, and each rebuild makes a
    /// fresh <see cref="Ribbon"/> starting at its default - so without this, collapsing the bar and
    /// then picking a tool simply opened it again. The Expanded/Simplified choice needs no help here:
    /// the ribbon makes it itself from <see cref="Ribbon.SimplifyBelowWidth"/>.
    /// </remarks>
    RibbonDisplayMode? userDisplayMode;

    void OnRibbonPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Ribbon.DisplayMode) || sender is not Ribbon bar)
            return;

        // Collapsed only ever comes from the user - the width rule never asks for it.
        this.userDisplayMode = bar.DisplayMode == RibbonDisplayMode.Collapsed
            ? RibbonDisplayMode.Collapsed
            : null;
    }

    View BuildRibbonToolbar()
    {
        // Captured before the rebuild replaces the instance.
        var previousKey = this.ribbon?.SelectedTab?.Key;

        var bar = new Ribbon
        {
            AllowCollapse = true,
            AllowGroupCollapse = true,

            // Two rows, not the ribbon's three. A shorter bar matters more here than in a document
            // app - the picture is the point of the control - and the editor's groups divide more
            // evenly over two, so the columns come out square instead of leaving one item stranded in
            // a column of its own.
            SmallItemRows = 2,

            // The ribbon applies this itself on every resize - an expanded bar is about a quarter of a
            // phone screen, and this control's whole job is to show the picture underneath it.
            SimplifyBelowWidth = SimplifiedBreakpoint,
            DisplayMode = this.userDisplayMode ?? RibbonDisplayMode.Expanded
        };

        // Undo, redo and reset live in the quick access strip rather than a group: they apply to every
        // tool, so they should not move or disappear when the tab does.
        bar.QuickAccessItems.Add(QuickCommand(ImageEditorIcon.Undo, "Undo", this.CanUndo, this.Undo));
        bar.QuickAccessItems.Add(QuickCommand(ImageEditorIcon.Redo, "Redo", this.CanRedo, this.Redo));
        bar.QuickAccessItems.Add(QuickCommand(ImageEditorIcon.Reset, "Reset", this.CanUndo, this.Reset));

        bar.Tabs.Add(this.BuildHomeTab());

        if (this.AllowZoom && this.ShowZoomControls)
            bar.Tabs.Add(this.BuildViewTab());

        if (this.BuildToolTab() is { } toolTab)
            bar.Tabs.Add(toolTab);

        // DisplayMode is TwoWay-bindable but has no event of its own, so the collapse chevron is heard
        // through PropertyChanged.
        bar.PropertyChanged += this.OnRibbonPropertyChanged;
        this.ribbon = bar;

        // A contextual tab that has just appeared is the one the user's last action created, so it is
        // the one to show. Otherwise stay where they were.
        if (toolTabIsNew && bar.SelectTab(ToolTabKey))
            toolTabIsNew = false;
        else if (previousKey is not null)
            bar.SelectTab(previousKey);

        return bar;
    }

    // Set when the tool changes, so the rebuild that follows knows to reveal the contextual tab once
    // rather than yanking the selection back to it on every unrelated redraw.
    bool toolTabIsNew;

    RibbonTab BuildHomeTab()
    {
        var tab = new RibbonTab { Title = "Home", Key = HomeTabKey };

        var tools = new RibbonGroup { Title = "Tools", Priority = 100 };

        if (this.AllowZoom)
            tools.Items.Add(this.ToolToggle(ImageEditorIcon.Move, "Move", ImageEditorToolMode.Move));

        if (this.AllowCrop)
            tools.Items.Add(this.ToolToggle(ImageEditorIcon.Crop, "Crop", ImageEditorToolMode.Crop));

        if (this.AllowDraw)
            tools.Items.Add(this.ToolToggle(ImageEditorIcon.Draw, "Draw", ImageEditorToolMode.Draw));

        if (this.AllowTextAnnotation)
            tools.Items.Add(this.ToolToggle(ImageEditorIcon.Text, "Text", ImageEditorToolMode.Text));

        if (tools.Items.Count > 0)
            tab.Groups.Add(tools);

        var shapes = new RibbonGroup { Title = "Shapes", Priority = 60 };

        if (this.AllowLine)
            shapes.Items.Add(this.ToolToggle(ImageEditorIcon.Line, "Line", ImageEditorToolMode.Line));

        if (this.AllowArrow)
            shapes.Items.Add(this.ToolToggle(ImageEditorIcon.Arrow, "Arrow", ImageEditorToolMode.Arrow));

        if (this.AllowRectangle)
            shapes.Items.Add(this.ToolToggle(ImageEditorIcon.Rectangle, "Rectangle", ImageEditorToolMode.Rectangle));

        if (this.AllowEllipse)
            shapes.Items.Add(this.ToolToggle(ImageEditorIcon.Ellipse, "Ellipse", ImageEditorToolMode.Ellipse));

        if (this.AllowCircle)
            shapes.Items.Add(this.ToolToggle(ImageEditorIcon.Circle, "Circle", ImageEditorToolMode.Circle));

        if (shapes.Items.Count > 0)
            tab.Groups.Add(shapes);

        // Rotate and Save share a group rather than taking one each. A ribbon group costs a divider
        // and a title whatever is in it, so two one-item groups spent more width on chrome than on
        // commands and left the bar looking sparse and arbitrary. CanCollapse is off because Save is
        // in here: it is the one command that must never fold into an overflow button.
        var image = new RibbonGroup { Title = "Image", Priority = 200, CanCollapse = false };

        if (this.AllowRotate)
            image.Items.Add(this.Command(ImageEditorIcon.Rotate, "Rotate", true, () => this.Rotate(90)));

        if (this.SaveCommand is not null)
        {
            // Check, not a save glyph: the icon set is the editor's own and has no disk. It reads as
            // "commit what you have done", which is what saving is here.
            image.Items.Add(this.Command(ImageEditorIcon.Check, this.SaveText, true, this.ExecuteSave));
        }

        if (image.Items.Count > 0)
            tab.Groups.Add(image);

        return tab;
    }

    RibbonTab BuildViewTab()
    {
        var tab = new RibbonTab { Title = "View", Key = ViewTabKey };
        var zoom = new RibbonGroup { Title = "Zoom" };

        zoom.Items.Add(this.Command(ImageEditorIcon.ZoomOut, "Zoom out", true, this.ZoomOut));
        zoom.Items.Add(this.Command(ImageEditorIcon.ZoomIn, "Zoom in", true, this.ZoomIn));
        zoom.Items.Add(this.Command(ImageEditorIcon.ZoomFit, "Fit", true, this.ZoomToFit));

        // The readout is a value, not a command, so it is hosted rather than made into a button.
        this.zoomReadout = new Label
        {
            Text = FormatZoom(this.zoomScale),
            WidthRequest = 46,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        }.WithFontSize(ShinyThemeKeys.Type.LabelSmallSize);

        this.zoomReadout.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        zoom.Items.Add(new RibbonContentItem { Content = this.zoomReadout });

        tab.Groups.Add(zoom);
        return tab;
    }

    /// <summary>
    /// The options for the tool in hand, as a contextual tab. Null when the current tool has none -
    /// Move and Crop carry no options, so no tab appears for them.
    /// </summary>
    RibbonTab? BuildToolTab()
    {
        var mode = this.CurrentToolMode;
        var isInk = mode is ImageEditorToolMode.Draw or ImageEditorToolMode.Line or ImageEditorToolMode.Arrow;
        var isText = mode == ImageEditorToolMode.Text;
        var isShape = ImageEditorDrawable.IsShapeMode(mode);

        if (!isInk && !isText && !isShape)
            return null;

        var tab = new RibbonTab
        {
            Key = ToolTabKey,
            Title = isText ? "Text" : isShape ? "Shape" : "Draw",
            // The band above the strip is what marks a tab contextual, and it is what tells you these
            // options belong to the tool you just picked rather than to the picture.
            ContextTitle = isText ? "Text Tools" : isShape ? "Shape Tools" : "Drawing Tools"
        };

        var appearance = new RibbonGroup { Title = isShape ? "Border" : "Colour", Priority = 100 };
        appearance.Items.Add(new RibbonContentItem { Content = this.CreateDrawColorButton() });

        if ((isInk || isShape) && this.ShowStrokeWidthPicker)
        {
            foreach (var width in this.StrokeWidthPresets)
                appearance.Items.Add(new RibbonContentItem { Content = this.CreateStrokeWidthButton(width) });
        }

        tab.Groups.Add(appearance);

        if (isShape && this.ShowShapeFillPicker)
        {
            var fill = new RibbonGroup { Title = "Fill", Priority = 80 };
            fill.Items.Add(new RibbonContentItem { Content = this.CreateShapeFillButton() });
            fill.Items.Add(new RibbonContentItem { Content = this.CreateShapeFillToggle() });
            tab.Groups.Add(fill);
        }

        if (isText)
        {
            var font = new RibbonGroup { Title = "Font", Priority = 80 };

            if (this.AllowFontSelection && this.AvailableFonts is { Count: > 0 })
                font.Items.Add(new RibbonContentItem { Content = this.CreateFontPickerButton() });

            if (this.AllowFontSizeSelection && this.AvailableFontSizes is { Count: > 0 })
                font.Items.Add(new RibbonContentItem { Content = this.CreateFontSizePickerButton() });

            if (font.Items.Count > 0)
                tab.Groups.Add(font);
        }

        return tab;
    }

    // ---------------------------------------------------------------------------------------------
    // Item factories
    // ---------------------------------------------------------------------------------------------

    RibbonToggleButton ToolToggle(ImageEditorIcon icon, string text, ImageEditorToolMode mode, RibbonItemSize size = RibbonItemSize.Small)
    {
        var selected = this.CurrentToolMode == mode;

        var item = new RibbonToggleButton
        {
            Text = text,
            Tooltip = text,
            Size = size,
            IsChecked = selected,
            IconTemplate = IconTemplateFor(icon),
            AutomationId = $"ImageEditorTool{mode}"
        };

        // Pressing the tool you are already using returns to Move, which is how the previous bar
        // behaved - there is otherwise no way to put a tool down.
        item.Command = new Command(() => this.CurrentToolMode = selected ? ImageEditorToolMode.Move : mode);
        return item;
    }

    RibbonButton Command(ImageEditorIcon icon, string text, bool enabled, Action action, RibbonItemSize size = RibbonItemSize.Small)
        => new()
        {
            Text = text,
            Tooltip = text,
            Size = size,
            IsEnabled = enabled,
            IconTemplate = IconTemplateFor(icon),
            Command = new Command(action)
        };

    RibbonButton QuickCommand(ImageEditorIcon icon, string tooltip, bool enabled, Action action)
        => new()
        {
            Tooltip = tooltip,
            AutomationId = $"ImageEditorQuick{icon}",
            IsEnabled = enabled,
            IconTemplate = IconTemplateFor(icon),
            Command = new Command(action)
        };

    /// <summary>
    /// Wraps one of the editor's vector icons as a ribbon icon.
    /// </summary>
    /// <remarks>
    /// These are drawn, not loaded, so they cannot go through <see cref="RibbonItem.Icon"/>, which
    /// takes an <see cref="ImageSource"/>. The template is instantiated per button - it has to be, a
    /// shared view cannot be in two places.
    /// <para>
    /// The ribbon draws on a themed surface rather than the old dark scrim, so the icons take the
    /// theme's on-surface-variant ink. They used to copy it out of <c>Application.Current.Resources</c>
    /// once, at inflation, so a light/dark flip - or a palette scoped over the editor - left them drawn in
    /// the old ink (dark on a dark ribbon). <see cref="ThemedIconView"/> follows the token instead.
    /// </para>
    /// </remarks>
    static DataTemplate IconTemplateFor(ImageEditorIcon icon)
        => new(() =>
        {
            var drawable = new ImageEditorIconDrawable { Icon = icon };
            return new ThemedIconView(drawable, c => drawable.Color = c, 20);
        });
}
