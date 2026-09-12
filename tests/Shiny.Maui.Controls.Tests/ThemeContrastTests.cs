using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.Maui.Controls.Themes;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// Every theme has to be readable, and "readable" is a number rather than an opinion.
/// </summary>
/// <remarks>
/// <para>
/// The colour roles are no longer hand-listed per theme — they are derived from a small authoring
/// layer by <c>tools/ShinyThemeGen</c>, so a change to one derivation moves every pack at once and a
/// bad mix would land in all five without anyone retyping a colour. That is worth having a number
/// stand over: an ink/ground pair a control actually paints together must clear WCAG AA.
/// </para>
/// <para>
/// It also guards the thing the theme composer will make easy to get wrong — an author who drags the
/// primary swatch somewhere pale still gets a container tint derived from it, and this is the rule
/// that says whether the result can be read.
/// </para>
/// </remarks>
public class ThemeContrastTests(ITestOutputHelper output)
{
    /// <summary>WCAG AA for body text. The controls use these pairs for labels, not for decoration.</summary>
    const double AA = 4.5d;

    public static TheoryData<string> Themes =>
    [
        "Basic", "Ocean", "Material", "Terminal", "Aurora"
    ];

    /// <summary>The nine semantic families, each of which paints an `on-` ink over two grounds.</summary>
    static readonly string[] Families =
    [
        "Primary", "Secondary", "Tertiary", "Error",
        "Success", "Info", "Warning", "Caution", "Critical"
    ];


    static ResourceDictionary Scheme(string theme, bool dark)
    {
        // Each pack is its own assembly, so the dictionary is resolved by assembly-qualified name
        // rather than off the core one - looking it up on Shiny.Maui.Controls finds only Basic and
        // would quietly pass every pack without checking a single colour.
        var assembly = theme == "Basic" ? "Shiny.Maui.Controls" : $"Shiny.Maui.Controls.Themes.{theme}";
        var name = $"Shiny.Maui.Controls.Themes.{theme}{(dark ? "Dark" : "Light")}Theme, {assembly}";

        var type = Type.GetType(name)
            ?? throw new InvalidOperationException($"Could not load '{name}'. Is the pack referenced by the test project?");

        return (ResourceDictionary)Activator.CreateInstance(type)!;
    }


    static double Luminance(Color c)
    {
        static double Channel(float v) => v <= 0.03928f ? v / 12.92d : Math.Pow((v + 0.055d) / 1.055d, 2.4d);
        return (0.2126d * Channel(c.Red)) + (0.7152d * Channel(c.Green)) + (0.0722d * Channel(c.Blue));
    }


    static double Ratio(Color a, Color b)
    {
        var (x, y) = (Luminance(a), Luminance(b));
        var (hi, lo) = x > y ? (x, y) : (y, x);
        return (hi + 0.05d) / (lo + 0.05d);
    }


    static Color Get(ResourceDictionary d, string key) => (Color)d[key];


    [Theory]
    [MemberData(nameof(Themes))]
    public void EveryInkClearsAAOverTheGroundItIsPaintedOn(string theme)
    {
        var failures = new List<string>();

        foreach (var dark in new[] { false, true })
        {
            var scheme = Scheme(theme, dark);
            var label = dark ? "dark" : "light";

            void Check(string ink, string ground)
            {
                var ratio = Ratio(Get(scheme, $"Shiny.Color.{ink}"), Get(scheme, $"Shiny.Color.{ground}"));
                output.WriteLine($"{theme} {label}: {ink} on {ground} = {ratio:0.00}");

                if (ratio < AA)
                    failures.Add($"{theme} {label}: {ink} on {ground} is {ratio:0.00}, needs {AA}");
            }

            foreach (var family in Families)
            {
                Check($"On{family}", family);
                Check($"On{family}Container", $"{family}Container");
            }

            // The surfaces every control paints text on.
            Check("OnBackground", "Background");
            Check("OnSurface", "Surface");
            Check("OnSurfaceVariant", "SurfaceVariant");
            Check("OnSurface", "SurfaceContainerLowest");
            Check("OnSurface", "SurfaceContainerLow");
            Check("OnSurface", "SurfaceContainer");
            Check("OnSurface", "SurfaceContainerHigh");
            Check("OnSurface", "SurfaceContainerHighest");
            Check("InverseOnSurface", "InverseSurface");
        }

        failures.ShouldBeEmpty(
            "A theme has to be readable. Re-run tools/ShinyThemeGen after adjusting the seeds, or " +
            "state the failing token outright in the theme's `tokens` block.\n" +
            String.Join("\n", failures)
        );
    }


    /// <summary>
    /// The outline has to be *visible* against the surfaces it separates. It is not text, so AA does
    /// not apply — 3:1 is the non-text contrast minimum, and an outline that fails it is the "why is
    /// this control's border invisible in dark mode" bug.
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void TheOutlineIsVisibleAgainstItsSurface(string theme)
    {
        foreach (var dark in new[] { false, true })
        {
            var scheme = Scheme(theme, dark);
            var ratio = Ratio(Get(scheme, "Shiny.Color.Outline"), Get(scheme, "Shiny.Color.Surface"));
            output.WriteLine($"{theme} {(dark ? "dark" : "light")}: Outline on Surface = {ratio:0.00}");

            ratio.ShouldBeGreaterThanOrEqualTo(3d, $"{theme} {(dark ? "dark" : "light")}: the outline is not visible on the surface");
        }
    }
}
