using System.Reflection;
using System.Text;
using Shiny.Maui.Controls;
using Shiny.Maui.Controls.Markdown;

namespace Sample.Controls;

/// <summary>
/// The gallery shows its own source: every feature page docks a collapsed "Source Code" expander
/// holding the .xaml and .xaml.cs that make the demo, rendered by <see cref="MarkdownView"/>. Along
/// the bottom, except on a page whose own chrome owns that edge - see <see cref="Attach"/>.
/// </summary>
/// <remarks>
/// Call this from the page's constructor, immediately after <c>InitializeComponent()</c>. That is the
/// one moment the wrap is free: the page's content is fully built and its handler does not exist yet,
/// so re-parenting costs nothing. Wrap the content once the page is on screen instead and every native
/// view under it is rebuilt — which on the AppKit head means they never paint at all.
/// </remarks>
public static class SampleSourceCode
{
    /// <summary>
    /// Docks the source panel against <paramref name="page"/>'s content — along the bottom, or along
    /// the top for a page whose own chrome already owns the bottom edge. A no-op when nothing was
    /// embedded for the page.
    /// </summary>
    public static void Attach(Page page)
    {
        var pageType = page.GetType();
        if (!SampleSourceFiles.Has(pageType))
            return;

        // A ShinyTabbedPage is a ContentPage, so it lands in the case below like any other page - and
        // its content ends in the tab bar. Docking under that pushes the bar up off the bottom of the
        // screen, which hides the one thing the demo is for and makes the bar's own safe-area inset
        // impossible to see. The panel goes along the top there instead.
        var dockTop = page is ShinyTabbedPage;

        switch (page)
        {
            // ShinyContentPage owns base.Content (its overlay host lives there), so its content goes
            // in and out through PageContent instead.
            case ShinyContentPage shiny:
            {
                var content = shiny.PageContent;
                shiny.PageContent = null; // MAUI throws when a view is handed a second parent
                shiny.PageContent = Wrap(content, pageType, dockTop);
                break;
            }
            case ContentPage contentPage:
            {
                var content = contentPage.Content;
                contentPage.Content = null;
                contentPage.Content = Wrap(content, pageType, dockTop);
                break;
            }
        }
    }


    static Grid Wrap(View? content, Type pageType, bool dockTop)
    {
        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(dockTop ? GridLength.Auto : GridLength.Star),
                new RowDefinition(dockTop ? GridLength.Star : GridLength.Auto)
            },

            // Only when the page owns its own bottom edge. A Grid defaults to
            // SafeAreaRegions.Container, which insets the area its children are arranged into - so
            // this wrapper would hand the tab bar a row that stops 34pt short of the screen and the
            // bar's background could never reach the bottom however it handled its own inset. On an
            // ordinary page the default is right: it is what keeps this panel out of the home
            // indicator.
            SafeAreaEdges = dockTop ? SafeAreaEdges.None : SafeAreaEdges.Default
        };

        var panel = new SourceCodePanel(pageType, dockTop);

        if (content is not null)
            grid.Add(content, 0, dockTop ? 1 : 0);

        grid.Add(panel, 0, dockTop ? 0 : 1);

        // The panel reads as a sheet over the demo rather than a page that grew: cap it at half the
        // page and let the markdown scroll inside. Measured off the wrapper because the expander's
        // own height is what we are setting.
        grid.SizeChanged += (_, _) => panel.AvailableHeight = grid.Height;
        return grid;
    }
}


/// <summary>The docked expander itself. Content is built the first time it is opened.</summary>
class SourceCodePanel : Expander
{
    MarkdownView? markdown;
    double availableHeight;

    public SourceCodePanel(Type pageType, bool dockTop = false)
    {
        this.HeaderText = "</>  Source Code";
        this.HeaderDetail = SampleSourceFiles.Summary(pageType);
        this.AutomationId = "SampleSourceCode";

        // It opens away from the edge it is docked against, so the header stays put rather than being
        // pushed off screen by its own content.
        this.ExpandDirection = dockTop ? ExpandDirection.Down : ExpandDirection.Up;
        this.Animation = ExpanderAnimation.Height | ExpanderAnimation.Fade;
        this.HasShadow = true;

        // Nothing is read, parsed or rendered until someone actually opens it - this runs on every
        // page in the gallery.
        this.LoadContentOnDemand = true;
        this.ContentTemplate = new DataTemplate(() =>
        {
            this.markdown = new MarkdownView
            {
                Markdown = SampleSourceFiles.MarkdownFor(pageType),
                IsScrollEnabled = true,
                HeightRequest = this.ContentHeight
            };
            return this.markdown;
        });
    }


    /// <summary>Height of the page the panel is docked on, which is what its own height is a fraction of.</summary>
    public double AvailableHeight
    {
        get => this.availableHeight;
        set
        {
            this.availableHeight = value;
            if (this.markdown is not null)
                this.markdown.HeightRequest = this.ContentHeight;
        }
    }

    double ContentHeight => this.availableHeight > 0
        ? Math.Clamp(this.availableHeight * 0.5, 220, 560)
        : 320;
}


/// <summary>
/// The demo sources embedded by Sample.Source.targets, keyed by file name. Every feature page file
/// name in the gallery is unique, so the page type's own name is enough to find its files.
/// </summary>
static class SampleSourceFiles
{
    const string Prefix = "SampleSource/";

    // One head links another's files (Sample.MacOS and Sample.Linux compile ..\Sample\**), so the
    // resources live in whichever assembly the page ended up in.
    static readonly Dictionary<Assembly, Dictionary<string, string>> indexes = new();

    static Dictionary<string, string> IndexFor(Assembly assembly)
    {
        lock (indexes)
        {
            if (indexes.TryGetValue(assembly, out var existing))
                return existing;

            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (!name.StartsWith(Prefix, StringComparison.Ordinal))
                    continue;

                // LogicalName keeps the folder for readability; the separator is whatever MSBuild
                // wrote, so both are cut.
                var file = name[(name.LastIndexOfAny(['/', '\\']) + 1)..];
                index[file] = name;
            }
            indexes[assembly] = index;
            return index;
        }
    }


    /// <summary>The page's own files, in the order a reader wants them: markup first, then the code behind it.</summary>
    static IEnumerable<string> FilesFor(Type pageType)
    {
        var index = IndexFor(pageType.Assembly);
        foreach (var file in new[] { pageType.Name + ".xaml", pageType.Name + ".xaml.cs", pageType.Name + ".cs" })
        {
            if (index.ContainsKey(file))
                yield return file;
        }
    }


    public static bool Has(Type pageType) => FilesFor(pageType).Any();

    public static string Summary(Type pageType) => String.Join("  ·  ", FilesFor(pageType));


    public static string MarkdownFor(Type pageType)
    {
        var index = IndexFor(pageType.Assembly);
        var sb = new StringBuilder();

        foreach (var file in FilesFor(pageType))
        {
            var code = Read(pageType.Assembly, index[file]);
            if (code is null)
                continue;

            // The gallery's own markdown demo has fenced code blocks inside its source, so the fence
            // here has to out-run the longest run of backticks in the file or it closes early.
            var fence = new string('`', Math.Max(3, LongestBacktickRun(code) + 1));

            sb.Append("### ").Append(file).Append("\n\n")
              .Append(fence).Append(LanguageFor(file)).Append('\n')
              .Append(code.TrimEnd()).Append('\n')
              .Append(fence).Append("\n\n");
        }
        return sb.ToString();
    }


    static string? Read(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }


    static string LanguageFor(string file) => file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ? "xml" : "csharp";


    static int LongestBacktickRun(string text)
    {
        int longest = 0, run = 0;
        foreach (var c in text)
        {
            run = c == '`' ? run + 1 : 0;
            if (run > longest)
                longest = run;
        }
        return longest;
    }
}
