namespace Sample.Blazor;

/// <summary>One demo page: the route, what it is called, its glyph and a one-line description.</summary>
public record CatalogItem(string Href, string Label, string Icon, string Blurb);

/// <summary>A group of demo pages, rendered as one nav section and one block on the home page.</summary>
public record CatalogSection(string Title, string Color, CatalogItem[] Items);

/// <summary>One search hit, flattened so a template can bind it without walking back to its section.</summary>
public record CatalogHit(string Href, string Label, string Icon, string Blurb, string Color, string Section);

/// <summary>
/// The single source of truth for what this gallery contains. Both <c>NavMenu</c> and the home
/// page render from here, so a new demo page shows up in both the moment it is listed — the home
/// page cannot drift out of date with the nav.
/// </summary>
public static class Catalog
{
    /// <summary>Not part of the control catalogue — these live at the top of the nav only.</summary>
    public static readonly CatalogItem[] GettingStarted =
    [
        new("", "Home", "⌂", "Start here"),
        new("kitchensink", "Kitchen Sink", "⚙", "Every control on one page"),
        new("theming", "Theming", "◐", "The design tokens every control reads")
    ];

    public static readonly CatalogSection[] Sections =
    [
        new("Layout & Collections", "#60A5FA",
        [
            new("layout", "Stacks & Grid", "▤", "VStack, HStack and a responsive 12-column grid"),
            new("expander", "Expander", "▾", "Animated disclosure panels and accordion lists"),
            new("applayout", "App Layout", "◫", "Application shell with collapsible, resizable panels"),
            new("carousel", "Carousel Gallery", "◀", "Swipeable image gallery with indicators"),
            new("carousel-advanced", "Carousel", "▶", "Templated carousel with looping and autoplay"),
            new("staggeredgrid", "Staggered Grid", "▦", "Pinterest-style masonry of variable-height items"),
            new("virtualizedgrid", "Virtualized Grid", "▣", "Windowed grid that stays smooth over huge lists"),
            new("parallaxlist", "Parallax List", "▰", "Collection with a hero that scrolls at half speed"),
            new("treeview", "Tree View", "⎇", "Lazy-loaded hierarchy with drag-reorder"),
            new("cells", "Cells", "☰", "The 14 cell types behind TableView"),
            new("tableview", "Table View", "☷", "Settings-style sectioned lists with drag & sort"),
            new("datagrid", "Data Grid", "▦", "Sortable, templated columns over tabular data"),
            new("docking", "Docking", "▨", "Visual-Studio-style tear-off tool windows"),
            new("ribbon", "Ribbon", "☷", "Office-style tabbed command bar for desktop windows")
        ]),

        new("Office Documents", "#34D399",
        [
            new("spreadsheet", "Spreadsheet", "▩", "Open, edit and recalculate .xlsx workbooks"),
            new("document-viewer", "Document Viewer", "▤", "Read .docx with reflowed layout and an outline"),
            new("document-editor", "Document Editor", "✎", "Edit .docx — bare surface or with the toolbar"),
            new("slide-viewer", "Slide Viewer", "◳", "Read .pptx as fitted slides or a thumbnail grid"),
            new("slide-editor", "Slide Editor", "✦", "Edit .pptx — move shapes, edit their text"),
            new("notebook", "Notebook", "✒", "Free-form OneNote-style canvas — write, draw and arrange anywhere")
        ]),

        new("Panels & Overlays", "#22D3EE",
        [
            new("modal", "Modal", "▢", "Modal window with header, footer buttons, drag and resize"),
            new("sheet", "Sheet", "▣", "Bottom sheet with detents and header peek"),
            new("overlay", "Overlay", "▦", "Loading and blocking overlays over any content"),
            new("frostedglass", "Frosted Glass", "◇", "Native blur / glass effect behind content")
        ]),

        new("Input", "#34D399",
        [
            new("textentry", "Text Entry", "✏", "Floating-label entry with validation states"),
            new("autocomplete", "AutoComplete", "≣", "Type-ahead suggestions from any source"),
            new("colorpicker", "Pickers", "◉", "Colour, font family and font size, as panels and as toolbar buttons"),
            new("countryaddress", "Country & Address", "⚑", "Country picker and structured address entry"),
            new("slider", "Slider", "━", "Single-value slider with ticks and labels"),
            new("rangeslider", "Range Slider", "≡", "Two-thumb range selection"),
            new("securitypin", "Security Pin", "✱", "PIN entry with masking and shake-on-error"),
            new("passwordstrength", "Password Strength", "▓", "Password field with a live strength meter and rule checklist"),
            new("captcha", "Captcha", "🛡", "Human check — local challenge, reCAPTCHA, hCaptcha or Turnstile"),
            new("signaturepad", "Signature Pad", "✍", "Draw, clear and export a signature"),
            new("onscreenkeyboard", "On-Screen Keyboard", "⌨", "Touch / kiosk QWERTY that never steals the caret"),
            new("quickentry", "Quick Entry", "✧", "Assistant-style prompt popup with a Siri-style screen glow"),
            new("filedrop", "File Drop", "⇩", "Files dropped anywhere on the page, caught before the browser navigates")
        ]),

        new("Actions & Navigation", "#2DD4BF",
        [
            new("button", "ShinyButton", "🔘", "States, icons, loading and long-press"),
            new("fab", "Fab", "➕", "Floating action button with positioning"),
            new("fab-menu", "Fab Menu", "✦", "Expanding menu of action buttons"),
            new("toolbar", "Toolbar", "☰", "Docked app bar with overflow and frosted glass"),
            new("tabbar", "Tab Bar", "☷", "Bottom tab navigation with badges"),
            new("stateview", "State View", "⇄", "Named branches switched by one string"),
            new("wizard", "Wizard", "➤", "Multi-step flow with a pointed progress bar"),
            new("timeline", "Timeline", "⋮", "Vertical rail of markers with content beside each one"),
            new("walkthrough", "Walkthrough", "☼", "Guided tour that spotlights one control at a time"),
            new("tooltip", "Tooltip", "💬", "Themed tooltip that points at any element")
        ]),

        new("Status & Feedback", "#F472B6",
        [
            new("pills", "Pills", "●", "Status badges in a range of tones"),
            new("badge", "Badge", "●", "Corner badge that wraps any content"),
            new("toast", "Toast", "▬", "Queued toasts with progress and spinners"),
            new("dialogs", "Dialogs", "❕", "Owned alert, confirm, prompt and action sheet"),
            new("progressbar", "Progress Bar", "▣", "Determinate and indeterminate progress"),
            new("progressline", "Progress Line", "━", "Page-edge loading line, docked and service-driven"),
            new("skeleton", "Skeleton", "☰", "Shimmering placeholders while content loads"),
            new("splashscreen", "Splash Screen", "☀", "Startup screen with status and progress"),
            new("motionicons", "Motion Icons", "✦", "42 animated icons on timer, hover, tap or command")
        ]),

        new("Media", "#C084FC",
        [
            new("camera", "Camera", "◉", "Preview, capture and pluggable frame analysis"),
            new("mediaelement", "Media Element", "▶", "Audio and video with a themed transport bar"),
            new("shinyimage", "Shiny Image", "🖼", "Placeholder, download progress and error artwork"),
            new("imageviewer", "Image Viewer", "▣", "Pinch, pan and double-tap zoom"),
            new("imageeditor", "Image Editor", "✎", "Crop, rotate, draw, text, undo and export"),
            new("mediapicker", "Media Picker", "📷", "Pick or capture photos and video")
        ]),

        new("Scheduler", "#F87171",
        [
            new("calendar", "Calendar", "□", "Month grid with custom event providers"),
            new("agenda", "Agenda", "≡", "Timeline of a single day"),
            new("agendalist", "Event List", "☷", "Grouped, scrollable list of upcoming events")
        ]),

        new("Communication", "#38BDF8",
        [
            new("chat", "Chat", "✉", "Bubbles, typing indicators, load-more and input bar")
        ]),

        new("Content", "#A3E635",
        [
            new("markdown", "Markdown", "↓", "Markdig-powered renderer"),
            new("markdown-editor", "Markdown Editor", "✍", "Toolbar editor with live preview"),
            new("mermaid", "Mermaid", "⬓", "Flowcharts and diagrams, with an interactive editor")
        ]),

        new("Barcodes", "#A78BFA",
        [
            new("qrcode", "QR Code", "▦", "QR rendering with sizing and error correction"),
            new("barcodes", "Barcode Gallery", "☰", "All 13 supported symbologies")
        ])
    ];

    public static int TotalControls => Sections.Sum(s => s.Items.Length);

    /// <summary>The display name for a route, so the app bar can title itself. Falls back to the app name.</summary>
    public static string LabelFor(string route)
    {
        route = route.Trim('/');

        foreach (var item in GettingStarted)
        {
            if (item.Href == route)
                return item.Label;
        }

        foreach (var section in Sections)
        {
            foreach (var item in section.Items)
            {
                if (item.Href == route)
                    return item.Label;
            }
        }

        return "Shiny Controls";
    }


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
    /// </remarks>
    public static IReadOnlyList<CatalogHit> Search(string? query)
    {
        var terms = (query ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (terms.Length == 0)
            return [];

        var hits = new List<(int Rank, string Label, CatalogHit Hit)>();

        foreach (var (section, item) in Everything())
        {
            var rank = Rank(item, section.Title, terms);
            if (rank < 0)
                continue;

            hits.Add((rank, item.Label, new CatalogHit(item.Href, item.Label, item.Icon, item.Blurb, section.Color, section.Title)));
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
        var best = int.MaxValue;

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

    /// <summary>Every demo, including the ones that live above the catalogue in the nav.</summary>
    static IEnumerable<(CatalogSection Section, CatalogItem Item)> Everything()
    {
        var gettingStarted = new CatalogSection("Getting started", "#A78BFA", GettingStarted);

        foreach (var item in GettingStarted)
        {
            // Home is where the search box is; offering it as a result is a link back to itself.
            if (item.Href.Length > 0)
                yield return (gettingStarted, item);
        }

        foreach (var section in Sections)
        {
            foreach (var item in section.Items)
                yield return (section, item);
        }
    }
}
