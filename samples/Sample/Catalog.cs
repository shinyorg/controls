namespace Sample;

/// <summary>One demo page: the Shell route, what it is called, its glyph and a one-line description.</summary>
public record CatalogItem(string Route, string Label, string Icon, string Blurb);

/// <summary>A group of demo pages, rendered as one block on the home page.</summary>
/// <remarks>
/// <paramref name="Accent"/> stays a hex string to match the Blazor catalogue, and is parsed once into
/// <see cref="AccentColor"/> — a binding won't run a type converter, so XAML needs the real thing.
/// </remarks>
public record CatalogSection(string Title, string Accent, CatalogItem[] Items)
{
    public Color AccentColor { get; } = Color.FromArgb(Accent);
}

/// <summary>One search hit, flattened so a DataTemplate can bind it without walking back to its section.</summary>
/// <remarks>
/// The accent is a real <see cref="Color"/> rather than the section's hex string, because a binding
/// does not run a type converter — the same reason <see cref="CatalogSection.AccentColor"/> exists.
/// </remarks>
public record CatalogHit(string Route, string Label, string Icon, string Blurb, Color AccentColor, string Section);

/// <summary>
/// What the gallery contains, mirroring <c>Sample.Blazor.Catalog</c> so both samples present the same
/// catalogue in the same shape. The flyout in <c>AppShell.xaml</c> stays the navigation source of truth —
/// this drives the home page's browse grid, so the routes here must match the flyout's.
/// </summary>
public static class Catalog
{
    public static readonly CatalogSection[] Sections =
    [
        new("Layout & Collections", "#60A5FA",
        [
            new("expander", "Expander", "▾", "Animated disclosure panels and accordion lists"),
            new("carouselgallery", "Carousel Gallery", "◀", "Swipeable image gallery with indicators"),
            new("staggeredgrid", "Staggered Grid", "▦", "Pinterest-style masonry of variable-height items"),
            new("virtualizedgrid", "Virtualized Grid", "▣", "Windowed grid that stays smooth over huge lists"),
            new("parallaxcollectionview", "Parallax Collection", "▰", "Collection with a hero that scrolls at half speed"),
            new("treeview", "Tree View", "⎇", "Lazy-loaded hierarchy with drag-reorder"),
            new("datagrid", "Data Grid", "▩", "Sortable, templated columns over tabular data"),
            new("treedatagrid", "Tree Data Grid", "⊞", "A DataGrid whose rows expand into children"),
            new("datagridgrouping", "Grid Grouping", "▤", "Grouped rows with per-group and grand totals"),
            new("datagridformatting", "Grid Formatting", "◨", "Column presets, alignment and cell styling"),
            new("timeline", "Timeline", "⋮", "Vertical rail of markers with content beside each one"),
            new("docking", "Docking", "▨", "Visual-Studio-style tear-off tool windows"),
            new("ribbon", "Ribbon", "☷", "Office-style tabbed command bar for desktop windows")
        ]),

        new("Table View", "#818CF8",
        [
            new("basic", "Basic", "☰", "Settings-style sectioned lists"),
            new("dynamic", "Dynamic", "☷", "Sections built and mutated at runtime"),
            new("dragsort", "Drag & Sort", "⇅", "Reorder rows by dragging"),
            new("picker", "Picker", "◎", "The picker cell types"),
            new("styling", "Styling", "◧", "Theming rows, sections and separators")
        ]),

        new("Office Documents", "#34D399",
        [
            new("spreadsheet", "Spreadsheet", "▩", "Open, edit and recalculate .xlsx workbooks"),
            new("documentviewer", "Document Viewer", "▤", "Read .docx with reflowed layout and an outline"),
            new("documenteditor", "Document Editor", "✎", "Edit .docx — bare surface or with the toolbar"),
            new("slideviewer", "Slide Viewer", "◳", "Read .pptx as fitted slides or a thumbnail grid"),
            new("slideeditor", "Slide Editor", "✦", "Edit .pptx — move shapes, edit their text"),
            new("notebook", "Notebook", "✒", "Free-form OneNote-style canvas — write, draw and arrange anywhere")
        ]),

        new("Panels & Overlays", "#22D3EE",
        [
            new("flyout", "Flyout", "◧", "Side panel that collapses to a rail and pushes or floats"),
            new("flyoutdrawer", "Flyout Drawer", "◨", "A drawer installed over every page from one declaration"),
            new("tabbedpage", "Tabbed Page", "▤", "Tabs with motion icons, badges, transitions and a centre button"),
            new("sheet", "Floating Panel", "▣", "Bottom sheet with detents"),
            new("minimizedsheetstandalone", "Header Peek", "▤", "Collapsed sheet that peeks its header"),
            new("minimizedsheet", "Bottom Tabs", "▁", "A peeking panel over bottom tabs"),
            new("topsheet", "Top Panel", "▔", "A panel that drops from the top"),
            new("dualpanel", "Dual Panels", "◫", "Top and bottom panels together"),
            new("overlay", "Overlay", "▦", "Loading and blocking overlays over any content"),
            new("frostedglass", "Frosted Glass", "◇", "Native blur / glass effect behind content")
        ]),

        new("Input", "#34D399",
        [
            new("textentry", "Text Entry", "✏", "Floating-label entry with validation states"),
            new("autocomplete", "AutoComplete", "≣", "Type-ahead suggestions from any source"),
            new("colorpicker", "Color Picker", "◉", "Wheel, sliders and swatches"),
            new("countryaddress", "Country & Address", "⚑", "Country picker and structured address entry"),
            new("durationpicker", "Duration Picker", "◷", "Hours and minutes on a floating panel"),
            new("fontpicker", "Font Picker", "𝔸", "Family and size pickers, inline or popup"),
            new("slider", "Slider", "━", "Single-value slider with ticks and labels"),
            new("rangeslider", "Range Slider", "≡", "Two-thumb range selection"),
            new("securitypin", "Security Pin", "✱", "PIN entry with masking and shake-on-error"),
            new("passwordstrength", "Password Strength", "▓", "Password field with a live strength meter and rule checklist"),
            new("signaturepad", "Signature Pad", "✍", "Draw, clear and export a signature"),
            new("onscreenkeyboard", "On-Screen Keyboard", "⌨", "Desktop virtual keyboard for kiosks")
        ]),

        new("Actions & Navigation", "#2DD4BF",
        [
            new("quickentry", "Quick Entry", "⌨", "Assistant-style prompt popup with a screen-edge glow"),
            new("walkthrough", "Walkthrough", "◎", "Guided tour that spotlights one control at a time"),
            new("tooltip", "Tooltip", "▣", "Themed bubble that points at its target and auto-flips"),
            new("buttons", "ShinyButton", "⬭", "States, icons, loading and long-press"),
            new("fab", "Fab & FabMenu", "➕", "Floating action button and expanding menu"),
            new("navigationpage", "Navigation Page", "▢", "A NavigationPage with items on both sides of the title"),
            new("stateview", "State View", "⇄", "Named branches switched by one string"),
            new("wizard", "Wizard", "➤", "Multi-step flow with a pointed progress bar")
        ]),

        new("Status & Feedback", "#F472B6",
        [
            new("progressline", "Progress Line", "▬", "Page-edge loading line, docked clear of the bars"),
            new("pills", "Pills", "●", "Status badges in a range of tones"),
            new("badge", "Badge", "◍", "Corner badge that wraps any content"),
            new("toast", "Toast", "▬", "Queued toasts with progress and spinners"),
            new("dialogs", "Dialogs", "❕", "Owned alert, confirm, prompt and action sheet"),
            new("feedback", "Feedback", "◈", "Haptics and system sounds"),
            new("progressbar", "Progress Bar", "▤", "Determinate and indeterminate progress"),
            new("skeleton", "Skeleton", "☰", "Shimmering placeholders while content loads")
        ]),

        new("Animation", "#FB923C",
        [
            new("keyframe", "Keyframe", "◐", "Seekable CSS-style keyframe animation in XAML"),
            new("motionicons", "Motion Icons", "✦", "42 animated icons on timer, hover, tap or command")
        ]),

        new("Media", "#C084FC",
        [
            new("musiclibrary", "Bottom Tabs", "▤", "Tabbed media browser over the shell\u0027s bottom tabs"),
            new("camera", "Camera", "◉", "Preview, capture and pluggable frame analysis"),
            new("mediaelement", "Media Element", "▶", "Audio and video with a themed transport bar"),
            new("documentsession", "Scanned Documents", "▧", "AI document scanning and extraction"),
            new("shinyimage", "Shiny Image", "▥", "Placeholder, download progress and error artwork"),
            new("imageviewer", "Image Viewer", "▣", "Pinch, pan and double-tap zoom"),
            new("imagegallery", "Image Gallery", "▦", "Paged gallery of zoomable images"),
            new("imageeditor", "Image Editor", "✎", "Crop, rotate, draw, text, undo and export"),
            new("mediapicker", "Media Picker", "◫", "Pick or capture photos and video")
        ]),

        new("Scheduler", "#F87171",
        [
            new("calendar", "Calendar", "□", "Month grid with custom event providers"),
            new("agenda", "Agenda", "≡", "Timeline of a single day"),
            new("agendacalendarpicker", "Calendar Picker", "◰", "Compact date picker over an agenda"),
            new("calendarlist", "Event List", "☷", "Grouped, scrollable list of upcoming events")
        ]),

        new("Communication", "#38BDF8",
        [
            new("chat", "Chat", "✉", "Bubbles, typing indicators, load-more and input bar"),
            new("chattemplates", "Chat Templates", "❏", "Per-message rendering with custom templates")
        ]),

        new("Content", "#A3E635",
        [
            new("markdownview", "Markdown Viewer", "↓", "Markdig-powered renderer"),
            new("markdowneditor", "Markdown Editor", "✍", "Toolbar editor with live preview")
        ]),

        new("Diagrams", "#FBBF24",
        [
            new("flowchart", "Flowchart", "⬓", "Mermaid flowcharts rendered natively"),
            new("directions", "Directions", "⇉", "Every flow direction"),
            new("themes", "Themes", "◑", "Diagram theming"),
            new("subgraphs", "Subgraphs", "⊞", "Nested and grouped nodes"),
            new("editor", "Editor", "✍", "Live Mermaid editor with preview")
        ]),

        new("Barcodes", "#A78BFA",
        [
            new("qrcode", "QR Code", "▦", "QR rendering with sizing and error correction"),
            new("barcodegallery", "Barcode Gallery", "☰", "All 13 supported symbologies")
        ]),

        new("Desktop", "#94A3B8",
        [
            new("trayicon", "System Tray", "◭", "Tray icon with menu and balloon tips"),
            new("filedrop", "File Drop", "⇩", "Window-level file drop, over top of any web view")
        ])
    ];

    public static int TotalControls => Sections.Sum(s => s.Items.Length);



    /// <summary>
    /// Everything matching <paramref name="query"/>, best first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ranked rather than merely filtered. At this many demos an unranked <c>Contains</c> buries the
    /// obvious answer: typing "grid" matches a dozen blurbs, and the control actually called Data Grid
    /// has to come first or the search is worse than scrolling.
    /// </para>
    /// <para>
    /// Every term has to match <i>something</i> — the label, the blurb or the section — so two words
    /// narrow rather than widen, which is what anyone typing a second word means by it. The rank comes
    /// from the best thing any single term hit, so "office grid" still leads with the spreadsheet
    /// rather than being dragged down by the weaker half of the query.
    /// </para>
    /// <para>Mirrors <c>Sample.Blazor.Catalog.Search</c>, so both galleries rank the same way.</para>
    /// </remarks>
    public static IReadOnlyList<CatalogHit> Search(string? query)
    {
        var terms = (query ?? String.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (terms.Length == 0)
            return [];

        var hits = new List<(int Rank, string Label, CatalogHit Hit)>();

        foreach (var section in Sections)
        {
            foreach (var item in section.Items)
            {
                var rank = Rank(item, section.Title, terms);
                if (rank < 0)
                    continue;

                hits.Add((rank, item.Label, new CatalogHit(
                    item.Route, item.Label, item.Icon, item.Blurb, section.AccentColor, section.Title)));
            }
        }

        return
        [
            .. hits
                .OrderBy(x => x.Rank)
                .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Hit)
        ];
    }

    /// <summary>Lower is better; -1 means at least one term matched nothing at all.</summary>
    static int Rank(CatalogItem item, string section, string[] terms)
    {
        var best = Int32.MaxValue;

        foreach (var term in terms)
        {
            var rank =
                item.Label.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 0 :
                item.Label.Contains(term, StringComparison.OrdinalIgnoreCase) ? 1 :
                section.Contains(term, StringComparison.OrdinalIgnoreCase) ? 2 :
                item.Blurb.Contains(term, StringComparison.OrdinalIgnoreCase) ? 3 :
                -1;

            // One term matching nothing rejects the item: a second word is there to narrow.
            if (rank < 0)
                return -1;

            best = Math.Min(best, rank);
        }

        return best;
    }
}
