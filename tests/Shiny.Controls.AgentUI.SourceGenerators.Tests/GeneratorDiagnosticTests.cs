using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shiny.Controls.AgentUI.A2UI;

namespace Shiny.Controls.AgentUI.SourceGenerators.Tests;

/// <summary>Misuse of the A2UI attributes is a compile error with a message that says what to do.</summary>
public class GeneratorDiagnosticTests
{
    static (IReadOnlyList<Diagnostic> Generator, IReadOnlyList<Diagnostic> Compile) Run(string source)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Append(MetadataReference.CreateFromFile(typeof(A2UIComponentAttribute).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(System.Text.Json.Nodes.JsonNode).Assembly.Location));

        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText("using System.Collections.Generic; using Shiny.Controls.AgentUI.A2UI;\n" + source, parseOptions)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create([new A2UIModelGenerator().AsSourceGenerator()], parseOptions: parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        return (diagnostics, output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList());
    }

    static void ShouldReport(string source, string id)
    {
        var (generator, _) = Run(source);
        generator.Select(d => d.Id).ShouldContain(id);
    }

    [Fact]
    public void AValidModelAndFunctionsCompileCleanly()
    {
        var (generator, compile) = Run("""
            public enum Tone { A, B }
            [A2UIItem] public partial class Band { public required double From { get; init; } }
            /// <summary>Stars.</summary>
            [A2UIComponent("Rating", Checkable = true)]
            public partial class Rating
            {
                public required double Value { get; init; }
                [A2UIProperty(Literal = true, Minimum = 1)] public int Max { get; init; } = 5;
                public Tone Tone { get; init; }
                public string? Label { get; init; }
                public IReadOnlyList<string> Tags { get; init; } = [];
                public List<Band> Bands { get; init; } = [];
                public A2UIActionRef Action { get; init; }
                public A2UIChildRef Child { get; init; }
                public A2UIChildrenRef Children { get; init; }
                public A2UIBindingRef Selection { get; init; }
            }
            public static partial class Fns
            {
                [A2UIFunction("twice")] public static double Twice(double x, int? cap = null, Tone tone = Tone.B) => x * 2;
                [A2UIFunction("log")] public static void Log(string message, A2UIFunctionContext context) { }
            }
            """);

        generator.ShouldBeEmpty();
        compile.Select(d => d.ToString()).ShouldBeEmpty();
    }

    [Fact]
    public void ANonPartialModelIsAnError()
        => ShouldReport("""[A2UIComponent("X")] public class X { public string? A { get; init; } }""", "SHINYA2UI001");

    [Fact]
    public void ANestedModelIsAnError()
        => ShouldReport("""public partial class Outer { [A2UIComponent("X")] public partial class X { } }""", "SHINYA2UI002");

    [Fact]
    public void AGenericModelIsAnError()
        => ShouldReport("""[A2UIComponent("X")] public partial class X<T> { }""", "SHINYA2UI003");

    [Fact]
    public void APrimaryConstructorIsAnError()
        => ShouldReport("""[A2UIComponent("X")] public partial class X(int a) { }""", "SHINYA2UI004");

    [Fact]
    public void AnUnsupportedPropertyTypeIsAnError()
        => ShouldReport("""[A2UIComponent("X")] public partial class X { public System.DateTime When { get; init; } }""", "SHINYA2UI005");

    [Fact]
    public void AnActionInsideAnItemIsAnError()
        => ShouldReport("""[A2UIItem] public partial class X { public A2UIActionRef Go { get; init; } }""", "SHINYA2UI005");

    [Fact]
    public void ARangeOnADynamicPropertyIsAnError()
        => ShouldReport("""[A2UIComponent("X")] public partial class X { [A2UIProperty(Minimum = 0)] public double V { get; init; } }""", "SHINYA2UI006");

    [Fact]
    public void AGetOnlyPropertyIsAnError()
        => ShouldReport("""[A2UIComponent("X")] public partial class X { public string A => ""; }""", "SHINYA2UI007");

    [Fact]
    public void AnInstanceFunctionIsAnError()
        => ShouldReport("""public partial class F { [A2UIFunction("f")] public string Go() => ""; }""", "SHINYA2UI008");

    [Fact]
    public void AnUnsupportedFunctionSignatureIsAnError()
    {
        ShouldReport("""public static partial class F { [A2UIFunction("f")] public static System.DateTime Go() => default; }""", "SHINYA2UI009");
        ShouldReport("""public static partial class F { [A2UIFunction("f")] public static string Go(System.Guid id) => ""; }""", "SHINYA2UI009");
    }

    [Fact]
    public void ANonPartialFunctionClassIsAnError()
        => ShouldReport("""public static class F { [A2UIFunction("f")] public static string Go() => ""; }""", "SHINYA2UI001");

    [Fact]
    public void LiteralOnlyPropertiesGetNoSetter()
    {
        var (generator, compile) = Run("""
            [A2UIComponent("X")] public partial class X { [A2UIProperty(Literal = true)] public double Size { get; init; } public double Value { get; init; } }
            public static class Use { public static void Go(X x) { x.SetValue(1); x.SetSize(2); } }
            """);

        generator.ShouldBeEmpty();
        compile.ShouldHaveSingleItem().GetMessage().ShouldContain("SetSize");
    }
}
