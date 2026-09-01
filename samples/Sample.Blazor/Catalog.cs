namespace Sample.Blazor;

/// <summary>One demo page: the route, what it is called, its glyph and a one-line description.</summary>
public record CatalogItem(string Href, string Label, string Icon, string Blurb);

/// <summary>A group of demo pages, rendered as one nav section and one block on the home page.</summary>
public record CatalogSection(string Title, string Color, CatalogItem[] Items);

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
}
