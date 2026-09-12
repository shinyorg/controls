using System.Text.RegularExpressions;
using Shouldly;
using Xunit;
using Xunit.Abstractions;
using Path = System.IO.Path;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// Source scans that keep dark mode from silently regressing.
/// </summary>
/// <remarks>
/// <para>
/// Every bug these cover shared one property: nothing throws, nothing warns, and the control renders
/// perfectly - in light mode. The only way to notice was to look at the thing in dark mode and
/// recognise that a white toolbar was not intentional. A scan is the only cheap guard.
/// </para>
/// <para>
/// These deliberately scan text rather than reflect over the types. A colour default that is wrong
/// is still a valid string, so there is nothing to assert about at runtime - the mistake only exists
/// in the source.
/// </para>
/// </remarks>
public class DarkModeCoverageTests(ITestOutputHelper output)
{
    /// <summary>
    /// A colour-ish <c>[Parameter] string</c> whose default is a literal. These are emitted as inline
    /// styles, so a literal is not a default a theme can override - it beats every stylesheet, for
    /// good. The fix is a <c>var(--shiny-color-…)</c> reference, which still lets a caller pin a value.
    /// </summary>
    static readonly Regex LiteralColourParameterDefault = new(
        @"\[Parameter\]\s+public\s+string\??\s+\w*(?:Color|Colour|Background|Foreground|Tint|Fill|Stroke)\w*\s*\{\s*get;\s*set;\s*\}\s*=\s*""(#|rgb|hsl|white|black)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// The colour tokens are <c>--shiny-color-*</c>. The short form is not a token, so it falls
    /// through to whatever literal sits beside it and the control never follows the theme - which
    /// looks exactly like a control that was never themed at all.
    /// </summary>
    static readonly Regex WrongTokenPrefix = new(
        @"var\(--shiny-(on-surface|surface|outline|primary|secondary|tertiary|error|background)[,)]",
        RegexOptions.Compiled);

    /// <summary>
    /// A fixed near-black wash is invisible over a dark surface. Hover and pressed states on chrome
    /// whose background the host supplies have to key off <c>currentColor</c> or a token instead.
    /// </summary>
    static readonly Regex FixedBlackWash = new(
        @":(hover|active|focus)[^{]*\{[^}]*background(-color)?\s*:\s*rgba?\(\s*0\s*,\s*0\s*,\s*0\s*[,/]",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Colours that carry meaning or a hard requirement rather than surface, so they are pinned on
    /// purpose and must stay that way.
    /// </summary>
    static readonly Dictionary<string, string> Exempt = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BarcodeView.razor"] = "a barcode has to be dark-on-light to scan; a themed one does not",
        ["ColorPicker.razor.cs"] = "SelectedColor is a value the user is picking, not chrome",
        ["ColorPickerButton.razor"] = "SelectedColor is a value the user is picking, not chrome",
        ["ImageEditor.razor.cs"] = "draw and text colours are applied to the image, not to chrome",
        ["SignaturePad.razor.cs"] = "ink and paper - the signature is captured as an image, not themed",
        ["SignaturePad.razor.css"] = "the clear chip sits on the pad's white paper, so it agrees with the ink rather than the theme",
        ["RangeSlider.razor.cs"] = "the cold-to-hot ramp is semantic",
        ["Slider.razor.cs"] = "the cold-to-hot ramp is semantic",
        ["LoadingOverlay.razor.cs"] = "a scrim and its white content read the same in both schemes",
        ["Overlay.razor.cs"] = "a scrim reads the same in both schemes",
        ["ProgressBar.razor.cs"] = "the pulse sheen sits on the fill, which is already themed",
        ["ProgressLine.razor.cs"] = "the pulse sheen sits on the fill, which is already themed",
        ["TextEntrySpeechToTextButton.razor"] = "idle green and listening red are semantic",
        ["SchedulerAgendaView.razor"] = "the current-time marker is semantic red; event colours are the app's own",
        ["SchedulerCalendarListView.razor"] = "the default event colour is a brand accent the app overrides per event",
    };

    [Fact]
    public void NoLiteralColourParameterDefaults()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles(".razor", ".cs"))
        {
            var name = Path.GetFileName(file);
            if (Exempt.ContainsKey(name))
                continue;

            var text = File.ReadAllText(file);
            foreach (Match match in LiteralColourParameterDefault.Matches(text))
                offenders.Add($"{name}: {Collapse(match.Value)}");
        }

        foreach (var o in offenders)
            output.WriteLine("LITERAL COLOUR DEFAULT: " + o);

        offenders.ShouldBeEmpty(
            "A colour [Parameter] is emitted as an inline style, so a literal default beats every " +
            "stylesheet and cannot be themed away - it is a permanent white toolbar, not a starting " +
            "point. Use \"var(--shiny-color-…, #fallback)\"; a caller passing a value still pins it. " +
            "If the colour is genuinely semantic (a barcode, a scrim, a picked value), add the file " +
            "to Exempt with the reason.");
    }

    [Fact]
    public void NoWrongThemeTokenPrefix()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles(".css", ".razor", ".cs"))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (WrongTokenPrefix.IsMatch(lines[i]))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}  {lines[i].Trim()}");
            }
        }

        foreach (var o in offenders)
            output.WriteLine("WRONG TOKEN PREFIX: " + o);

        offenders.ShouldBeEmpty(
            "The colour tokens are --shiny-color-*, not --shiny-*. A misspelt custom property is not " +
            "an error: it falls through to the literal fallback beside it and the control silently " +
            "stops following the theme.");
    }

    [Fact]
    public void NoFixedBlackHoverWash()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles(".css"))
        {
            var name = Path.GetFileName(file);
            if (Exempt.ContainsKey(name))
                continue;

            var text = File.ReadAllText(file);
            foreach (Match match in FixedBlackWash.Matches(text))
                offenders.Add($"{name}: {Collapse(match.Value)}");
        }

        foreach (var o in offenders)
            output.WriteLine("FIXED BLACK WASH: " + o);

        offenders.ShouldBeEmpty(
            "rgba(0, 0, 0, x) is invisible over a dark surface, so the state simply does not show in " +
            "dark mode. Use color-mix(in srgb, currentColor N%, transparent) - which follows whatever " +
            "the surface's own text colour is - or a surface-container token.");
    }

    /// <summary>
    /// Every <c>var(--shiny-color-…, literal)</c> fallback in component CSS must be the Basic light
    /// theme's own value for that token.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fallback only paints when the theme stylesheet is not linked, so a wrong one is invisible
    /// in every app that themes properly - and in the ones that do not, it is the whole design. Before
    /// this scan there were 892 of them carrying up to 28 different values for a single token:
    /// <c>--shiny-color-on-surface-variant</c> alone was #6b7280, #666, #9CA3AF and #374151 depending
    /// on which control you happened to be reading.
    /// </para>
    /// <para>
    /// Asserting against the generated theme rather than merely against each other is deliberate. Two
    /// controls agreeing on a colour the theme does not use is still wrong; it just fails as a set.
    /// </para>
    /// </remarks>
    [Fact]
    public void ColourFallbacksMatchTheTheme()
    {
        var theme = BasicLightTokens();
        theme.ShouldNotBeEmpty("the generated BasicLightTheme.xaml is the source of truth for this scan");

        var reference = new Regex(@"var\(\s*(--shiny-color-[a-z0-9-]+)\s*,\s*([^()]*?)\s*\)", RegexOptions.Compiled);
        var offenders = new List<string>();

        foreach (var file in SourceFiles(".css", ".razor", ".cs"))
        {
            var name = Path.GetFileName(file);

            foreach (Match match in reference.Matches(File.ReadAllText(file)))
            {
                var token = match.Groups[1].Value;
                var fallback = match.Groups[2].Value;

                // A token the theme does not define has nothing to be checked against - that is what
                // ThemeTokenCoverageTests is for.
                if (!theme.TryGetValue(token, out var expected))
                    continue;

                if (!String.Equals(fallback, expected, StringComparison.OrdinalIgnoreCase))
                    offenders.Add($"{name}: {token} falls back to {fallback}, theme says {expected}");
            }
        }

        foreach (var o in offenders.Distinct().Order(StringComparer.Ordinal))
            output.WriteLine("FALLBACK DRIFT: " + o);

        offenders.ShouldBeEmpty(
            "a var() fallback is what the control paints when the consumer has not linked " +
            "shiny-theme.css. Every one of them has to be that token's own value in the Basic light " +
            "theme, or the unthemed rendering is a collage of six different design systems.");
    }


    /// <summary>
    /// Reads the generated MAUI light dictionary and maps its <c>Shiny.Color.X</c> keys onto the CSS
    /// token names. The two hosts emit from the same table, so either one can stand for the contract;
    /// the XAML is simply the easier of the two to parse without a CSS engine.
    /// </summary>
    static Dictionary<string, string> BasicLightTokens()
    {
        var path = Path.Combine(
            FindSrcRoot(), "Shiny.Maui.Controls", "Themes", "Generated", "BasicLightTheme.xaml");

        if (!File.Exists(path))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var entries = Regex.Matches(
            File.ReadAllText(path),
            @"<Color x:Key=""Shiny\.Color\.(\w+)"">(#[0-9A-Fa-f]{6})</Color>"
        );

        return entries.ToDictionary(
            m => "--shiny-color-" + Kebab(m.Groups[1].Value),
            m => m.Groups[2].Value,
            StringComparer.Ordinal
        );
    }

    static string Kebab(string pascal)
    {
        var sb = new System.Text.StringBuilder(pascal.Length + 8);

        for (var i = 0; i < pascal.Length; i++)
        {
            if (Char.IsUpper(pascal[i]) && i > 0)
                sb.Append('-');

            sb.Append(Char.ToLowerInvariant(pascal[i]));
        }

        return sb.ToString();
    }


    static string Collapse(string value)
    {
        var single = Regex.Replace(value, @"\s+", " ").Trim();
        return single.Length > 120 ? single[..120] + "…" : single;
    }

    static IEnumerable<string> SourceFiles(params string[] extensions)
        => LibraryRoots()
            .SelectMany(root => Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            .Where(f =>
                extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase) &&
                !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                // the theme stylesheets are the source of the tokens, not a consumer of them
                !f.EndsWith("shiny-theme.css", StringComparison.OrdinalIgnoreCase));

    /// <summary>Every shipped Blazor package, so an add-on cannot quietly reintroduce this.</summary>
    static IEnumerable<string> LibraryRoots()
    {
        var src = FindSrcRoot();
        return Directory
            .EnumerateDirectories(src, "Shiny.Blazor.Controls*", SearchOption.TopDirectoryOnly)
            .Where(d => !d.Contains(".Themes.", StringComparison.OrdinalIgnoreCase));
    }

    static string FindSrcRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(Path.Combine(candidate, "Shiny.Blazor.Controls")))
                return candidate;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate src/ from the test output directory.");
    }
}
