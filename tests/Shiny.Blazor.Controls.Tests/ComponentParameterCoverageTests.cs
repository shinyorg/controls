using System.Text.RegularExpressions;
using Shouldly;
using Xunit;
using Xunit.Abstractions;
using Path = System.IO.Path;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// Checks that every attribute set on one of our own components is a parameter that component has.
/// </summary>
/// <remarks>
/// <para>
/// Blazor resolves parameter names by <b>reflection at render time</b>. A typo compiles perfectly and
/// then throws <c>InvalidOperationException: does not have a property matching the name 'X'</c> the
/// first time the markup holding it is rendered — which, for anything behind a ribbon tab, a dropdown
/// or a conditional, is the first time a user clicks that thing rather than anything the build or a
/// smoke test would reach. One shipped that way: an insert menu was handed <c>Rejected</c> where the
/// component declares <c>ImageRejected</c>, and the Notebook ribbon's Insert tab crashed on click.
/// </para>
/// <para>
/// A source scan rather than reflection over the compiled types, because this test project references
/// only the core package while the mistake is just as easy to make in the add-ons — and the add-ons
/// are exactly where a component is being consumed by someone who did not write it.
/// </para>
/// </remarks>
public class ComponentParameterCoverageTests(ITestOutputHelper output)
{
    /// <summary>A <c>[Parameter]</c> declaration, in a <c>.razor</c> code block or a code-behind.</summary>
    static readonly Regex ParameterDeclaration = new(
        @"\[Parameter[^\]]*\]\s*(?:public\s+)?[^\s;{]+(?:<[^>]*>)?\??\s+(\w+)\s*\{",
        RegexOptions.Compiled);

    static readonly Regex ClassDeclaration = new(
        @"\bpublic\s+(?:sealed\s+|abstract\s+|partial\s+)*class\s+(\w+)\s*(?:<[^>]*>)?\s*(?::\s*([^\{]+))?",
        RegexOptions.Compiled);

    static readonly Regex Inherits = new(@"^@inherits\s+([\w\.<>]+)", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>A <c>@typeparam</c> is set like a parameter in markup but is not one.</summary>
    static readonly Regex TypeParam = new(@"^@typeparam\s+(\w+)", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>A component that splats takes anything, so nothing about it can be checked.</summary>
    static readonly Regex Splats = new(@"CaptureUnmatchedValues\s*=\s*true", RegexOptions.Compiled);

    static readonly Regex Usage = new(@"<([A-Z]\w+)((?:\s+[^<>]*?)?)/?>", RegexOptions.Compiled | RegexOptions.Singleline);

    static readonly Regex Attribute = new(@"(@?[\w:-]+)\s*=\s*[""']", RegexOptions.Compiled);

    sealed class Component
    {
        public HashSet<string> Parameters { get; } = new(StringComparer.Ordinal);
        public string? Base { get; set; }
        public bool Splats { get; set; }
    }

    [Fact]
    public void EveryComponentAttributeIsAParameterThatComponentHas()
    {
        var components = Catalogue();
        var offenders = new List<string>();

        foreach (var file in SourceFiles(".razor"))
        {
            var text = File.ReadAllText(file);

            foreach (Match usage in Usage.Matches(text))
            {
                var name = usage.Groups[1].Value;

                // Anything we did not write — a framework component, or one from a package — cannot be
                // checked from source, and a name we have never seen is not evidence of a mistake.
                if (!components.TryGetValue(name, out _) || SplatsAnywhere(components, name))
                    continue;

                var declared = ParametersOf(components, name);
                if (declared.Count == 0)
                    continue;

                foreach (Match attribute in Attribute.Matches(usage.Groups[2].Value))
                {
                    var raw = attribute.Groups[1].Value;

                    if (raw.StartsWith("@bind-", StringComparison.Ordinal))
                    {
                        // Two-way binding needs both halves; only one of them appears in the markup.
                        var bound = raw["@bind-".Length..].Split(':')[0];

                        foreach (var required in new[] { bound, bound + "Changed" })
                        {
                            if (!declared.Contains(required))
                                offenders.Add($"{Path.GetFileName(file)}: <{name} @bind-{bound}> needs a '{required}' parameter");
                        }

                        continue;
                    }

                    // @ref, @key and the event directives are the renderer's, not the component's;
                    // a hyphen or a colon marks a directive or a plain HTML attribute.
                    if (raw.StartsWith('@') || raw.Contains(':') || raw.Contains('-'))
                        continue;

                    if (!declared.Contains(raw))
                        offenders.Add($"{Path.GetFileName(file)}: <{name}> has no parameter '{raw}'");
                }
            }
        }

        foreach (var offender in offenders.Distinct().Order())
            output.WriteLine(offender);

        offenders.Distinct().ShouldBeEmpty(
            "A parameter name is resolved by reflection when the markup renders, so a typo compiles " +
            "and then throws the first time that markup is reached — which for anything behind a tab " +
            "or a dropdown is a click, not a build. Check the component's own [Parameter] names.");
    }

    /// <summary>Every component we ship, with its parameters and what it inherits.</summary>
    static Dictionary<string, Component> Catalogue()
    {
        var components = new Dictionary<string, Component>(StringComparer.Ordinal);

        Component For(string name)
        {
            if (!components.TryGetValue(name, out var component))
                components[name] = component = new Component();

            return component;
        }

        foreach (var file in SourceFiles(".razor", ".cs"))
        {
            var text = File.ReadAllText(file);

            // A .razor and its .razor.cs are one component, so both feed the same entry.
            var stem = Path.GetFileName(file).Split('.')[0];
            var self = For(stem);

            foreach (Match m in ParameterDeclaration.Matches(text))
                self.Parameters.Add(m.Groups[1].Value);

            foreach (Match m in TypeParam.Matches(text))
                self.Parameters.Add(m.Groups[1].Value);

            if (Splats.IsMatch(text))
                self.Splats = true;

            if (Inherits.Match(text) is { Success: true } inherits)
                self.Base ??= Simple(inherits.Groups[1].Value);

            // Several components share one file, and a base carrying the parameters is routinely one
            // of them — the ribbon's items are all RibbonItem.
            foreach (Match m in ClassDeclaration.Matches(text))
            {
                var declared = For(m.Groups[1].Value);

                foreach (Match p in ParameterDeclaration.Matches(text))
                    declared.Parameters.Add(p.Groups[1].Value);

                if (Splats.IsMatch(text))
                    declared.Splats = true;

                if (!m.Groups[2].Success)
                    continue;

                var first = Simple(m.Groups[2].Value.Split(',')[0]);

                // Interfaces first in a base list are common and are never the base type.
                if (first.Length > 1 && char.IsUpper(first[0]) && !(first[0] == 'I' && char.IsUpper(first[1])))
                    declared.Base ??= first;
            }
        }

        return components;
    }

    static string Simple(string type) => type.Trim().Split('<')[0].Split('.')[^1].Trim();

    static HashSet<string> ParametersOf(Dictionary<string, Component> components, string name)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (name is not null && seen.Add(name) && components.TryGetValue(name, out var component))
        {
            result.UnionWith(component.Parameters);
            name = component.Base!;
        }

        return result;
    }

    static bool SplatsAnywhere(Dictionary<string, Component> components, string name)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (name is not null && seen.Add(name) && components.TryGetValue(name, out var component))
        {
            if (component.Splats)
                return true;

            name = component.Base!;
        }

        return false;
    }

    static IEnumerable<string> SourceFiles(params string[] extensions)
        => LibraryRoots()
            .SelectMany(root => Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            .Where(f =>
                extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase) &&
                !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    /// <summary>Every shipped Blazor package, so an add-on cannot quietly reintroduce this.</summary>
    static IEnumerable<string> LibraryRoots()
    {
        var src = FindSrcRoot();
        return Directory.EnumerateDirectories(src, "Shiny.Blazor.Controls*", SearchOption.TopDirectoryOnly);
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
