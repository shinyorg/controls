using Shiny.Controls.Office.Icons;
using System.Globalization;

namespace Shiny.Controls.Office.Shell;

/// <summary>The pages of the File backstage.</summary>
public enum OfficeBackstagePage
{
    Home,
    New,
    Open,
    Info,
    Save,
    SaveAs,
    Print,
    Export,
    History,
    Options
}


/// <summary>One entry on the backstage's left rail.</summary>
/// <param name="Page">The page it opens.</param>
/// <param name="Text">Its label.</param>
/// <param name="Icon">Its icon.</param>
/// <param name="IsCommand">
/// True for an entry that runs straight away rather than opening a page — Save is the one: in Office it
/// saves and closes the backstage instead of showing anything.
/// </param>
/// <param name="StartsFooter">True for the entries pinned to the bottom of the rail (Options).</param>
public sealed record OfficeBackstageEntry(
    OfficeBackstagePage Page,
    string Text,
    OfficeShellIcon Icon,
    bool IsCommand = false,
    bool StartsFooter = false);


/// <summary>A template the backstage offers on Home and New.</summary>
/// <remarks>
/// The shell only shows these; the host decides what "create from this" means. <see cref="Open"/> is the
/// usual answer — a stream the editor loads — and a host that builds documents in code can leave it null
/// and handle the <c>TemplateSelected</c> event instead.
/// </remarks>
public sealed class OfficeTemplate
{
    public OfficeTemplate() { }

    public OfficeTemplate(string id, string name)
    {
        this.Id = id;
        this.Name = name;
    }

    /// <summary>The template's key.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Its caption under the thumbnail.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>An optional second line.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// A picture of the template — an image URL or a path the host can load. Null draws a plain page in
    /// the app's shape (portrait for a document, a grid for a workbook, landscape for a deck).
    /// </summary>
    public string? Thumbnail { get; init; }

    /// <summary>Groups templates on the New page. Null is "Featured".</summary>
    public string? Category { get; init; }

    /// <summary>True for the blank document — drawn first, and without a thumbnail picture.</summary>
    public bool IsBlank { get; init; }

    /// <summary>Opens the template's content, or null when the host builds it itself.</summary>
    public Func<CancellationToken, Task<Stream>>? Open { get; init; }

    /// <summary>Anything the host wants back.</summary>
    public object? Tag { get; init; }

    /// <summary>The blank document for an app.</summary>
    public static OfficeTemplate Blank(OfficeApp app) => new("blank", BlankName(app)) { IsBlank = true };

    static string BlankName(OfficeApp app) => app switch
    {
        OfficeApp.Excel => "Blank workbook",
        OfficeApp.PowerPoint => "Blank presentation",
        OfficeApp.OneNote => "Blank notebook",
        _ => "Blank document"
    };
}


/// <summary>A file in the backstage's recent list.</summary>
public sealed class OfficeRecentFile
{
    public OfficeRecentFile() { }

    public OfficeRecentFile(string name, string? location, DateTimeOffset lastOpened)
    {
        this.Name = name;
        this.Location = location;
        this.LastOpened = lastOpened;
    }

    public string Name { get; init; } = string.Empty;

    /// <summary>Where it is — a folder path, a URL, "OneDrive › Documents". Shown under the name.</summary>
    public string? Location { get; init; }

    public DateTimeOffset LastOpened { get; init; }

    /// <summary>Pinned files sort first.</summary>
    public bool IsPinned { get; init; }

    /// <summary>Which app the file belongs to, for the icon beside it. Null uses the shell's own.</summary>
    public OfficeApp? App { get; init; }

    /// <summary>Anything the host wants back when it is opened — a path, a handle, a stream factory.</summary>
    public object? Tag { get; init; }
}


/// <summary>A document property shown on the Info page.</summary>
public sealed record OfficeDocumentProperty(string Name, string Value);


/// <summary>What the Info page says about the open document.</summary>
/// <remarks>
/// Everything is supplied by the host, because only the editor knows its own word or slide count and
/// only the host knows where the file lives.
/// </remarks>
public sealed class OfficeDocumentInfo
{
    public string? Title { get; init; }

    public string? Author { get; init; }

    public string? Location { get; init; }

    public DateTimeOffset? Created { get; init; }

    public DateTimeOffset? Modified { get; init; }

    /// <summary>Size in bytes, or null when it has not been saved.</summary>
    public long? SizeBytes { get; init; }

    /// <summary>App-specific counts — "Pages 3", "Words 1,204", "Slides 12", "Sheets 4".</summary>
    public IReadOnlyList<OfficeDocumentProperty> Statistics { get; init; } = [];

    /// <summary>The properties the Info page lists, in order, with the empty ones left out.</summary>
    public IReadOnlyList<OfficeDocumentProperty> Properties(CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var list = new List<OfficeDocumentProperty>();

        if (this.SizeBytes is { } size)
            list.Add(new("Size", OfficeBackstageText.FormatSize(size, culture)));

        list.AddRange(this.Statistics);

        if (!string.IsNullOrWhiteSpace(this.Title))
            list.Add(new("Title", this.Title));

        if (this.Created is { } created)
            list.Add(new("Created", created.ToLocalTime().ToString("g", culture)));

        if (this.Modified is { } modified)
            list.Add(new("Last modified", modified.ToLocalTime().ToString("g", culture)));

        if (!string.IsNullOrWhiteSpace(this.Author))
            list.Add(new("Author", this.Author));

        if (!string.IsNullOrWhiteSpace(this.Location))
            list.Add(new("Location", this.Location));

        return list;
    }
}


/// <summary>The backstage's words: the rail, the greeting, file sizes and relative dates.</summary>
public static class OfficeBackstageText
{
    /// <summary>The rail, top to bottom, the way Office orders it.</summary>
    /// <param name="includeHistory">History is optional — only a host that keeps versions has one.</param>
    public static IReadOnlyList<OfficeBackstageEntry> Entries(bool includeHistory = false)
    {
        var list = new List<OfficeBackstageEntry>
        {
            new(OfficeBackstagePage.Home, "Home", OfficeShellIcon.Home),
            new(OfficeBackstagePage.New, "New", OfficeShellIcon.NewDocument),
            new(OfficeBackstagePage.Open, "Open", OfficeShellIcon.Open),
            new(OfficeBackstagePage.Info, "Info", OfficeShellIcon.Info),
            new(OfficeBackstagePage.Save, "Save", OfficeShellIcon.Save, IsCommand: true),
            new(OfficeBackstagePage.SaveAs, "Save As", OfficeShellIcon.SaveAs),
            new(OfficeBackstagePage.Print, "Print", OfficeShellIcon.Print),
            new(OfficeBackstagePage.Export, "Export", OfficeShellIcon.Export)
        };

        if (includeHistory)
            list.Add(new(OfficeBackstagePage.History, "History", OfficeShellIcon.History));

        list.Add(new(OfficeBackstagePage.Options, "Options", OfficeShellIcon.Options, StartsFooter: true));
        return list;
    }


    /// <summary>"Good morning", "Good afternoon" or "Good evening" for the hour.</summary>
    public static string Greeting(DateTime now) => now.Hour switch
    {
        < 5 => "Good evening",
        < 12 => "Good morning",
        < 18 => "Good afternoon",
        _ => "Good evening"
    };


    /// <summary>The Home page headline — "Good writing starts here." for Word, and its twin for each app.</summary>
    public static string Headline(OfficeApp app) => app switch
    {
        OfficeApp.Excel => "Good analysis starts here.",
        OfficeApp.PowerPoint => "Good storytelling starts here.",
        OfficeApp.OneNote => "Good note-taking starts here.",
        _ => "Good writing starts here."
    };


    /// <summary>"12 KB", "1.4 MB" — binary units, one decimal above a megabyte, the way Explorer shows them.</summary>
    public static string FormatSize(long bytes, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;

        if (bytes < 1024)
            return bytes.ToString("N0", culture) + " bytes";

        if (bytes < 1024 * 1024)
            return Math.Ceiling(bytes / 1024d).ToString("N0", culture) + " KB";

        if (bytes < 1024L * 1024 * 1024)
            return (bytes / (1024d * 1024)).ToString("0.#", culture) + " MB";

        return (bytes / (1024d * 1024 * 1024)).ToString("0.##", culture) + " GB";
    }


    /// <summary>
    /// When a recent file was opened, relative to <paramref name="now"/>: "Just now", "12m ago",
    /// "Yesterday", or the date.
    /// </summary>
    public static string Relative(DateTimeOffset when, DateTimeOffset now, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var elapsed = now - when;

        if (elapsed < TimeSpan.FromMinutes(1))
            return "Just now";

        if (elapsed < TimeSpan.FromHours(1))
            return $"{(int)elapsed.TotalMinutes}m ago";

        if (when.LocalDateTime.Date == now.LocalDateTime.Date)
            return $"{(int)elapsed.TotalHours}h ago";

        if (when.LocalDateTime.Date == now.LocalDateTime.Date.AddDays(-1))
            return "Yesterday";

        return when.LocalDateTime.ToString("d", culture);
    }


    /// <summary>Recent files in the order the backstage lists them: pinned first, then newest first.</summary>
    public static IReadOnlyList<OfficeRecentFile> Order(IEnumerable<OfficeRecentFile>? files)
        => files?
               .OrderByDescending(x => x.IsPinned)
               .ThenByDescending(x => x.LastOpened)
               .ToList()
           ?? (IReadOnlyList<OfficeRecentFile>)[];


    /// <summary>
    /// The templates to show: the blank one first (the app's own when the host did not supply one),
    /// then the host's.
    /// </summary>
    public static IReadOnlyList<OfficeTemplate> WithBlank(OfficeApp app, IEnumerable<OfficeTemplate>? templates)
    {
        var list = templates?.ToList() ?? [];
        var blank = list.FirstOrDefault(x => x.IsBlank);

        if (blank is not null)
            list.Remove(blank);

        list.Insert(0, blank ?? OfficeTemplate.Blank(app));
        return list;
    }
}
