using System.Reflection;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// Keeps controls on the theme tokens.
///
/// A control that hardcodes <c>Colors.DodgerBlue</c> or <c>Color.FromArgb("#007AFF")</c> cannot
/// respond to <c>ShinyThemeManager.SetTheme</c> at all - swapping theme packs leaves it looking
/// identical, which is exactly the bug this guards. Every source file that mentions a colour must
/// either reference <see cref="Themes.ShinyThemeKeys"/> or be listed in
/// <see cref="ColourIsContentNotChrome"/> with a reason.
///
/// This is a source scan rather than a reflection test because the colours live in constructors and
/// bindable-property defaults that a headless test host cannot meaningfully exercise.
/// </summary>
public class ThemeTokenCoverageTests(ITestOutputHelper output)
{
    /// <summary>
    /// Files whose colour literals are deliberate: they render content or a fixed-contrast affordance
    /// rather than themed chrome. Each entry needs a reason - the list should only shrink for the
    /// wrong reason (a file being deleted), never grow casually.
    /// </summary>
    static readonly Dictionary<string, string> ColourIsContentNotChrome = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ColorSpectrumView.cs"] = "draws the HSV colour space itself; the crosshair is white-on-black so it stays visible over any hue",
        ["HueBarView.cs"] = "draws the hue ramp; the thumb is white-on-black for the same reason",
        ["ColorPicker.Properties.cs"] = "SelectedColor's default is a data value, not chrome",
        ["SignaturePadDrawable.cs"] = "signature ink and paper - a captured signature stays black-on-white for export",
        ["SignaturePad.Properties.cs"] = "StrokeColor/SignatureBackgroundColor are the ink and paper (see SignaturePadDrawable)",
        ["ImageEditorDrawable.cs"] = "the colour being drawn onto the image is content",
        ["SelectionBackgroundConverter.cs"] = "fallbacks only - DataGrid.ApplyTheme overwrites both from theme tokens",
        ["SectionRenderer.cs"] = "deliberately emulates the iOS system separator/label colours, and already ships a light/dark pair for each",
        ["ColorPickerButton.cs"] = "the swatch fill and SelectedColor's default are the colour being picked - data, not chrome (its border, popup surface, backdrop and Done button are themed)",
        ["ConfettiOptions.cs"] = "the default palette is canvas-confetti's own - the burst's colours are the effect itself, and callers pass their own Colors",
        ["ConfettiCanvas.cs"] = "emoji particles are drawn with a fixed ink (colour emoji ignore it); everything else is the particle's own colour",
        ["ScreenGlowOptions.cs"] = "the glow palette is the effect itself - a Siri-style rainbow wash whose colours are its identity, not chrome that should follow a theme pack",
        ["ImageEditor.cs"] = "toolbar chrome is a fixed dark scrim over arbitrary photos, so its separator must stay a mid grey that reads on that scrim in both schemes; only the semantic Delete/Confirm buttons are themed",

        // The SVG renderer. Every colour in it is either a value the artwork itself declared or the
        // spec's own default for one that did not - an SVG that says fill="red" must draw red on
        // every theme pack, or it is not that drawing any more. The one colour an app does get to
        // choose is ShinyImage.SvgTintColor, which a caller can bind to a theme token.
        ["SvgPaint.cs"] = "SVG paint servers carry the colours the file declared - content, not chrome",
        ["SvgParser.cs"] = "the spec's defaults for a file that declares no fill or no stop-color",
        ["SvgStyle.cs"] = "the initial inherited style an SVG root starts from, which the spec fixes as black",
        ["SvgDrawable.cs"] = "the currentColor fallback for artwork drawn without an explicit tint",
        ["ShinyImage.Svg.cs"] = "the same currentColor fallback, applied when SvgTintColor is unset",
    };

    /// <summary>
    /// Named colours that are practically always a themed-chrome mistake. Unlike the file-level scan
    /// this is per-line, so a file that references a token *somewhere* cannot smuggle one of these in
    /// (which is exactly how a hardcoded white loader scrim survived the first pass).
    /// </summary>
    static readonly Regex ChromeColour = new(
        @"\bColors\.(DodgerBlue|CornflowerBlue|LightBlue|Blue|Red|Gray|Grey|LightGray|LightGrey|SlateGray|Green|Orange|Yellow|Purple|Pink)\b",
        RegexOptions.Compiled);

    [Fact]
    public void NoChromeColourLiteralsOutsideThemeFallbacks()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            var name = Path.GetFileName(file);
            if (ColourIsContentNotChrome.ContainsKey(name))
                continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!ChromeColour.IsMatch(line))
                    continue;

                // A literal handed to ThemeColor(...)/ResolveColor(...) is the documented fallback for
                // "the pack is missing this key", which is fine.
                if (line.Contains("ThemeColor(", StringComparison.Ordinal) ||
                    line.Contains("ResolveColor(", StringComparison.Ordinal) ||
                    line.Contains("Fallback", StringComparison.Ordinal))
                    continue;

                offenders.Add($"{name}:{i + 1}  {line.Trim()}");
            }
        }

        foreach (var o in offenders)
            output.WriteLine("CHROME LITERAL: " + o);

        offenders.ShouldBeEmpty(
            "These lines paint chrome with a fixed colour, so it cannot follow the theme. Use " +
            "SetDynamicResource with a ShinyThemeKeys token (or pass the literal as the fallback " +
            "argument to ThemeColor/ResolveColor if it is only a missing-key guard)."
        );
    }

    static IEnumerable<string> SourceFiles()
        => Directory.EnumerateFiles(FindLibraryRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                !f.Contains($"{Path.DirectorySeparatorChar}Generated{Path.DirectorySeparatorChar}"));

    [Fact]
    public void EveryControlThatUsesColourAlsoUsesThemeTokens()
    {
        var root = FindLibraryRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}Generated{Path.DirectorySeparatorChar}"))
                continue;

            var name = Path.GetFileName(file);
            if (ColourIsContentNotChrome.ContainsKey(name))
                continue;

            var source = File.ReadAllText(file);
            if (source.Contains("ShinyThemeKeys", StringComparison.Ordinal))
                continue;

            // Transparent and White carry no theme meaning on their own (spacers, scrims, ink on an
            // accent fill), so they do not by themselves make a file "unthemed".
            var literals = Regex.Matches(source, @"FromArgb\(""#").Count
                + Regex.Matches(source, @"\bColors\.(?!Transparent\b|White\b)[A-Z]\w+").Count;

            if (literals > 0)
                offenders.Add($"{name} ({literals} colour literal(s), 0 theme tokens)");
        }

        foreach (var o in offenders)
            output.WriteLine("UNTHEMED: " + o);

        offenders.ShouldBeEmpty(
            "These files hardcode colours and never reference ShinyThemeKeys, so switching theme " +
            "packs cannot change them. Route the colour through " +
            "SetDynamicResource(prop, ShinyThemeKeys.Color.X) - or, if the colour is genuinely " +
            "content rather than chrome, add the file to ColourIsContentNotChrome with a reason."
        );
    }

    static string FindLibraryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Shiny.Maui.Controls");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate src/Shiny.Maui.Controls from the test output directory.");
    }
}
