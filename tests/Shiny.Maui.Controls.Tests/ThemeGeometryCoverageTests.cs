using System.Text.RegularExpressions;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Path = System.IO.Path;
using Shiny.Maui.Controls.Themes;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// The geometry/type companion to <see cref="ThemeTokenCoverageTests"/>.
///
/// Colours were already tokenised; sizes, radii and shadows were not, which is why swapping theme
/// packs used to shift the hue and nothing else. These tests lock that in from both ends: a source
/// scan that catches a literal creeping back, and a live swap proving a control actually re-renders.
/// </summary>
public class ThemeGeometryCoverageTests(ITestOutputHelper output)
{
    /// <summary>Sizes that are exactly a role in the M3 type scale, so a literal is never justified.</summary>
    static readonly Regex OnScaleFontSize = new(
        @"(?<![A-Za-z0-9_.])FontSize\s*=\s*(11|12|14|16|22|24|28|32|36|45|57)\s*[,;}]",
        RegexOptions.Compiled);

    static readonly Regex RoundRectangleRadius = new(
        @"RoundRectangle\s*\{[^}]*CornerRadius\s*=\s*(?:new\s+CornerRadius\()?\s*[0-9]",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Files that legitimately build their own <see cref="Shadow"/>. Both own the instance and mutate
    /// it in place, so they must not share the theme's - one dictionary entry is handed to every
    /// control that resolves it, and mutating it would bleed across all of them.
    /// </summary>
    static readonly Dictionary<string, string> ShadowIsOwned = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ShinyButton.cs"] = "one Shadow built at construction and toggled by Opacity - reassigning one mid-interaction unfocuses the content on Android",
        ["TextEntry.cs"] = "focusGlow is animated by mutating Radius/Opacity on the instance",
        ["TooltipBubble.cs"] = "one Shadow built at construction and toggled by Opacity for HasShadow - the same reason as ShinyButton, since reassigning Shadow tears the native layer down",
        ["FlyoutPanel.cs"] = "surfaceShadow is built at construction and toggled by Opacity as the panel opens - assigning a fresh Shadow mid-interaction unfocuses whatever is inside it on Android",
    };

    /// <summary>
    /// Radii that are a deliberate zero rather than an unthemed literal. A docked panel sits flush
    /// against the window edge, and rounding it there would leave a wedge of page showing through the
    /// corner - so the square is the design, not an oversight.
    /// </summary>
    static readonly Dictionary<string, string> RadiusIsIntrinsic = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FlyoutPanel.cs"] = "the docked panel is flush to the window edge, so its surface is square by design",
    };

    [Fact]
    public void NoOnScaleFontSizeLiterals()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (OnScaleFontSize.IsMatch(lines[i]))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}  {lines[i].Trim()}");
            }
        }

        foreach (var o in offenders)
            output.WriteLine("HARDCODED TYPE SIZE: " + o);

        offenders.ShouldBeEmpty(
            "These sizes sit exactly on the type scale, so a theme with a type scale or weight offset " +
            "cannot move them. Chain .WithFontSize(ShinyThemeKeys.Type.…Size) after the initializer, " +
            "or use SetTokenOrValue when the consumer can override it."
        );
    }

    [Fact]
    public void NoLiteralCornerRadiusOnRoundRectangles()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (RadiusIsIntrinsic.ContainsKey(Path.GetFileName(file)))
                continue;

            var source = File.ReadAllText(file);
            foreach (Match m in RoundRectangleRadius.Matches(source))
            {
                var line = source.Take(m.Index).Count(c => c == '\n') + 1;
                offenders.Add($"{Path.GetFileName(file)}:{line}");
            }
        }

        foreach (var o in offenders)
            output.WriteLine("HARDCODED RADIUS: " + o);

        offenders.ShouldBeEmpty(
            "A literal radius pins the corner geometry, which is the single biggest lever a theme has. " +
            "Chain .WithCornerRadius(ShinyThemeKeys.Shape.…Radius), or SetCornerTokenOrValue when the " +
            "consumer can override it. A radius computed from another value (a circle at Size / 2) is " +
            "intrinsic to the control and does not match this pattern."
        );
    }

    [Fact]
    public void NoAdHocShadows()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            var name = Path.GetFileName(file);
            if (ShadowIsOwned.ContainsKey(name))
                continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimStart();
                if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("///", StringComparison.Ordinal))
                    continue;

                if (line.Contains("new Shadow", StringComparison.Ordinal))
                    offenders.Add($"{name}:{i + 1}  {lines[i].Trim()}");
            }
        }

        foreach (var o in offenders)
            output.WriteLine("AD-HOC SHADOW: " + o);

        offenders.ShouldBeEmpty(
            "A Shadow built from literals ignores the theme's elevation style, so a flat, outline or " +
            "glow pack still casts a Material drop shadow. Use " +
            "SetDynamicResource(VisualElement.ShadowProperty, ShinyThemeKeys.Elevation.LevelN) - or add " +
            "the file to ShadowIsOwned with a reason if the control mutates its own instance."
        );
    }

    /// <summary>
    /// The end-to-end guarantee: a theme swap must actually re-render a control's geometry and type,
    /// not just its colours. Two dictionaries differing only in the shape and type tokens.
    /// </summary>
    [Fact]
    public void SwappingThemeRestylesGeometryAndType()
    {
        var app = new Application();

        var sharp = new ResourceDictionary
        {
            { ShinyThemeKeys.Shape.CornerMediumRadius, new CornerRadius(3) },
            { ShinyThemeKeys.Type.BodySmallSize, 9d },
        };
        var round = new ResourceDictionary
        {
            { ShinyThemeKeys.Shape.CornerMediumRadius, new CornerRadius(30) },
            { ShinyThemeKeys.Type.BodySmallSize, 21d },
        };

        app.Resources.MergedDictionaries.Add(sharp);

        var pill = new PillView { Text = "Live" };

        // Dynamic resources only re-resolve for elements in the application's element tree; a control
        // held on its own resolves once at assignment and then never hears about the swap.
        var page = new ContentPage { Content = pill };
        page.Parent = app;

        var border = (Border)pill.Content!;
        var label = (Label)border.Content!;

        ((RoundRectangle)border.StrokeShape!).CornerRadius.TopLeft.ShouldBe(3);
        label.FontSize.ShouldBe(9d);

        app.Resources.MergedDictionaries.Remove(sharp);
        app.Resources.MergedDictionaries.Add(round);

        ((RoundRectangle)border.StrokeShape!).CornerRadius.TopLeft.ShouldBe(30);
        label.FontSize.ShouldBe(21d);
    }

    static IEnumerable<string> SourceFiles()
        => Directory.EnumerateFiles(FindLibraryRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                !f.Contains($"{Path.DirectorySeparatorChar}Generated{Path.DirectorySeparatorChar}"));

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
