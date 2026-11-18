using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Shiny.Controls.AgentUI.SourceGenerators;

/// <summary>
/// Turns <c>[A2UIComponent]</c> and <c>[A2UIItem]</c> classes into A2UI catalog schema and typed readers:
/// each class gets <c>Describe</c> (the schema, through <c>A2UICatalogBuilder</c>) and <c>Read</c> (the
/// component's evaluated properties), plus a nested <c>A2UIProperties</c> class of JSON names.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class A2UIModelGenerator : IIncrementalGenerator
{
    const string Ns = "Shiny.Controls.AgentUI.A2UI";
    const string G = "global::" + Ns + ".";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var components = context.SyntaxProvider.ForAttributeWithMetadataName(
            Ns + ".A2UIComponentAttribute",
            static (node, _) => node is ClassDeclarationSyntax,
            static (ctx, ct) => Analyze(ctx, isItem: false, ct));

        var items = context.SyntaxProvider.ForAttributeWithMetadataName(
            Ns + ".A2UIItemAttribute",
            static (node, _) => node is ClassDeclarationSyntax,
            static (ctx, ct) => Analyze(ctx, isItem: true, ct));

        var functions = context.SyntaxProvider.ForAttributeWithMetadataName(
            Ns + ".A2UIFunctionAttribute",
            static (node, _) => node is MethodDeclarationSyntax,
            static (ctx, ct) => AnalyzeFunction(ctx, ct));

        context.RegisterSourceOutput(components, Emit);
        context.RegisterSourceOutput(items, Emit);
        context.RegisterSourceOutput(functions.Collect(), EmitFunctions);
    }

    static void Emit(SourceProductionContext context, Result result)
    {
        foreach (var diagnostic in result.Diagnostics)
            context.ReportDiagnostic(diagnostic.ToDiagnostic());

        if (result.Model is { } model)
            context.AddSource(model.HintName + ".A2UI.g.cs", SourceText.From(Render(model), Encoding.UTF8));
    }

    // --- Analysis -------------------------------------------------------------------------------

    static Result Analyze(GeneratorAttributeSyntaxContext ctx, bool isItem, CancellationToken ct)
    {
        var type = (INamedTypeSymbol)ctx.TargetSymbol;
        var syntax = (ClassDeclarationSyntax)ctx.TargetNode;
        var diagnostics = new List<DiagnosticInfo>();

        if (!syntax.Modifiers.Any(SyntaxKind.PartialKeyword))
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.NotPartial, syntax.Identifier.GetLocation(), type.Name));
        if (type.ContainingType is not null)
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.Nested, syntax.Identifier.GetLocation(), type.Name));
        if (type.IsGenericType)
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.Generic, syntax.Identifier.GetLocation(), type.Name));
        if (syntax.ParameterList is not null)
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.PrimaryConstructor, syntax.Identifier.GetLocation(), type.Name));

        if (diagnostics.Count > 0)
            return new Result(null, new EquatableArray<DiagnosticInfo>(diagnostics));

        var attribute = ctx.Attributes[0];
        var name = isItem ? type.Name : attribute.ConstructorArguments.FirstOrDefault().Value as string ?? type.Name;
        var description = Named<string>(attribute, "Description") ?? DocSummary(type, ct) ?? "";

        var properties = new List<PropertyModel>();
        var members = type.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => !p.IsStatic && !p.IsIndexer && p.DeclaredAccessibility == Accessibility.Public)
            .Where(p => !p.GetAttributes().Any(a => Is(a, "A2UIIgnoreAttribute")));

        foreach (var property in members)
        {
            ct.ThrowIfCancellationRequested();
            var model = AnalyzeProperty(property, isItem, ctx.SemanticModel, diagnostics, ct);
            if (model is not null)
                properties.Add(model);
        }

        var hasExplicitConstructor = type.InstanceConstructors.Any(c => !c.IsImplicitlyDeclared);

        var component = new ComponentModel(
            HintName: type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "").Replace('<', '_').Replace('>', '_'),
            Namespace: type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
            ClassName: type.Name,
            IsItem: isItem,
            Name: name,
            Description: description,
            Checkable: Named<bool?>(attribute, "Checkable") ?? false,
            Weight: Named<bool?>(attribute, "Weight") ?? true,
            AllowedParents: NamedStrings(attribute, "AllowedParents"),
            AllowedChildren: NamedStrings(attribute, "AllowedChildren"),
            HasExplicitConstructor: hasExplicitConstructor,
            Properties: new EquatableArray<PropertyModel>(properties));

        return new Result(component, new EquatableArray<DiagnosticInfo>(diagnostics));
    }

    static PropertyModel? AnalyzeProperty(IPropertySymbol property, bool isItem, SemanticModel semanticModel, List<DiagnosticInfo> diagnostics, CancellationToken ct)
    {
        var location = property.Locations.FirstOrDefault() ?? Location.None;

        if (property.SetMethod is null)
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.NotSettable, location, property.Name));
            return null;
        }

        var attribute = property.GetAttributes().FirstOrDefault(a => Is(a, "A2UIPropertyAttribute"));
        var jsonName = (attribute is null ? null : Named<string>(attribute, "Name")) ?? CamelCase(property.Name);
        var description = (attribute is null ? null : Named<string>(attribute, "Description"))
                          ?? property.GetAttributes().Where(a => a.AttributeClass?.ToDisplayString() == "System.ComponentModel.DescriptionAttribute")
                              .Select(a => a.ConstructorArguments.FirstOrDefault().Value as string).FirstOrDefault()
                          ?? DocSummary(property, ct);
        var literal = attribute is not null && (Named<bool?>(attribute, "Literal") ?? false);
        var minimum = attribute is null ? null : Named<double?>(attribute, "Minimum");
        var maximum = attribute is null ? null : Named<double?>(attribute, "Maximum");
        if (minimum is { } min && double.IsNaN(min))
            minimum = null;
        if (maximum is { } max && double.IsNaN(max))
            maximum = null;

        var (kind, elementType) = Classify(property.Type);
        if (kind is null || (isItem && kind is PropertyKind.Action or PropertyKind.Children or PropertyKind.Binding))
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.UnsupportedType, location, property.Name, property.Type.ToDisplayString()));
            return null;
        }

        // Integers and enums are literal by nature: A2UI has no dynamic integer or choice type.
        if (kind is PropertyKind.Integer or PropertyKind.Choice)
            literal = true;

        if (!literal && (minimum is not null || maximum is not null))
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.RangeOnDynamic, location, property.Name));
            minimum = maximum = null;
        }

        var schemaDefault = literal ? ConstantInitializer(property, semanticModel, ct) : null;
        var nullable = property.Type.NullableAnnotation == NullableAnnotation.Annotated
                       || (property.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T });

        return new PropertyModel(
            CsName: property.Name,
            JsonName: jsonName,
            Kind: kind.Value,
            TypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ElementTypeName: elementType,
            Description: description,
            Required: property.IsRequired,
            Literal: literal,
            Minimum: minimum,
            Maximum: maximum,
            SchemaDefault: schemaDefault,
            IsNullable: nullable,
            IsValueType: property.Type.IsValueType);
    }

    static (PropertyKind? Kind, string? Element) Classify(ITypeSymbol type)
    {
        var underlying = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n ? n.TypeArguments[0] : type;

        switch (underlying.SpecialType)
        {
            case SpecialType.System_String: return (PropertyKind.String, null);
            case SpecialType.System_Double: return (PropertyKind.Number, "double");
            case SpecialType.System_Single: return (PropertyKind.Number, "float");
            case SpecialType.System_Int32: return (PropertyKind.Integer, "int");
            case SpecialType.System_Int64: return (PropertyKind.Integer, "long");
            case SpecialType.System_Boolean: return (PropertyKind.Boolean, null);
        }

        if (underlying.TypeKind == TypeKind.Enum)
            return (PropertyKind.Choice, underlying.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

        var full = underlying.ToDisplayString();
        switch (full)
        {
            case "System.Text.Json.Nodes.JsonNode": return (PropertyKind.Value, null);
            case Ns + ".A2UIActionRef": return (PropertyKind.Action, null);
            case Ns + ".A2UIChildRef": return (PropertyKind.Child, null);
            case Ns + ".A2UIChildrenRef": return (PropertyKind.Children, null);
            case Ns + ".A2UIBindingRef": return (PropertyKind.Binding, null);
        }

        var element = ElementType(underlying);
        if (element is null)
            return (null, null);

        if (element.SpecialType == SpecialType.System_String)
            return (PropertyKind.StringList, null);

        if (element.GetAttributes().Any(a => Is(a, "A2UIItemAttribute")))
            return (PropertyKind.Items, element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

        return (null, null);
    }

    // T[], IReadOnlyList<T>, IList<T>, List<T>, IEnumerable<T>, IReadOnlyCollection<T>, ICollection<T>
    static ITypeSymbol? ElementType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
            return array.ElementType;

        if (type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named)
        {
            var definition = named.OriginalDefinition.ToDisplayString();
            if (definition is "System.Collections.Generic.IReadOnlyList<T>" or "System.Collections.Generic.IList<T>"
                or "System.Collections.Generic.List<T>" or "System.Collections.Generic.IEnumerable<T>"
                or "System.Collections.Generic.IReadOnlyCollection<T>" or "System.Collections.Generic.ICollection<T>")
                return named.TypeArguments[0];
        }

        return null;
    }

    static string? ConstantInitializer(IPropertySymbol property, SemanticModel semanticModel, CancellationToken ct)
    {
        var declaration = property.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax(ct))
            .OfType<PropertyDeclarationSyntax>()
            .FirstOrDefault(d => d.Initializer is not null);

        if (declaration?.Initializer is not { } initializer || declaration.SyntaxTree != semanticModel.SyntaxTree)
            return null;

        var constant = semanticModel.GetConstantValue(initializer.Value, ct);
        if (!constant.HasValue || constant.Value is null)
            return null;

        var type = property.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n ? n.TypeArguments[0] : property.Type;

        return constant.Value switch
        {
            string s => SymbolDisplay.FormatLiteral(s, quote: true),
            bool b => b ? "true" : "false",
            _ when type.TypeKind == TypeKind.Enum => $"({type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){Convert.ToInt64(constant.Value)}",
            IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => null
        };
    }

    // --- Functions ------------------------------------------------------------------------------

    static FunctionResult AnalyzeFunction(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        var method = (IMethodSymbol)ctx.TargetSymbol;
        var syntax = (MethodDeclarationSyntax)ctx.TargetNode;
        var container = method.ContainingType;
        var diagnostics = new List<DiagnosticInfo>();

        var containerSyntax = syntax.Parent as TypeDeclarationSyntax;
        if (containerSyntax is null || !containerSyntax.Modifiers.Any(SyntaxKind.PartialKeyword))
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.NotPartial, syntax.Identifier.GetLocation(), container.Name));
        if (container.ContainingType is not null)
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.Nested, syntax.Identifier.GetLocation(), container.Name));
        if (!method.IsStatic)
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.FunctionNotStatic, syntax.Identifier.GetLocation(), method.Name));
        if (method.IsGenericMethod)
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.UnsupportedFunctionType, syntax.Identifier.GetLocation(), method.Name, "a generic method"));

        var returnKind = method.ReturnsVoid ? "Void" : Classify(method.ReturnType).Kind switch
        {
            PropertyKind.String => "String",
            PropertyKind.Number or PropertyKind.Integer => "Number",
            PropertyKind.Boolean => "Boolean",
            _ => null
        };
        if (returnKind is null)
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.UnsupportedFunctionType, syntax.ReturnType.GetLocation(), method.Name, "return type " + method.ReturnType.ToDisplayString()));

        var docXml = DocXml(method, ct);
        var parameters = new List<ParameterModel>();
        foreach (var parameter in method.Parameters)
        {
            var model = AnalyzeParameter(parameter, docXml, diagnostics);
            if (model is not null)
                parameters.Add(model);
        }

        if (diagnostics.Count > 0)
            return new FunctionResult(null, new EquatableArray<DiagnosticInfo>(diagnostics));

        var attribute = ctx.Attributes[0];
        var function = new FunctionModel(
            ContainerKey: container.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Namespace: container.ContainingNamespace.IsGlobalNamespace ? null : container.ContainingNamespace.ToDisplayString(),
            ContainerName: container.Name,
            ContainerIsStatic: container.IsStatic,
            ContainerKeyword: container.TypeKind == TypeKind.Struct ? "struct" : container.IsRecord ? "record" : "class",
            MethodName: method.Name,
            Name: attribute.ConstructorArguments.FirstOrDefault().Value as string ?? CamelCase(method.Name),
            Description: Named<string>(attribute, "Description") ?? Summary(docXml) ?? "",
            ReturnKind: returnKind!,
            ReturnsInteger: Classify(method.ReturnType).Kind == PropertyKind.Integer,
            Callers: Named<int?>(attribute, "AllowedCallers") ?? 0,
            RequiresUserActivation: Named<bool?>(attribute, "RequiresUserActivation") ?? false,
            Parameters: new EquatableArray<ParameterModel>(parameters));

        return new FunctionResult(function, new EquatableArray<DiagnosticInfo>(diagnostics));
    }

    static ParameterModel? AnalyzeParameter(IParameterSymbol parameter, string? docXml, List<DiagnosticInfo> diagnostics)
    {
        var location = parameter.Locations.FirstOrDefault() ?? Location.None;

        if (parameter.Type.ToDisplayString() == Ns + ".A2UIFunctionContext")
            return new ParameterModel(parameter.Name, parameter.Name, PropertyKind.Value, null, null, false, "", IsContext: true);

        var (kind, element) = Classify(parameter.Type);
        if (kind is not (PropertyKind.String or PropertyKind.Number or PropertyKind.Integer or PropertyKind.Boolean or PropertyKind.StringList or PropertyKind.Choice)
            || parameter.RefKind != RefKind.None)
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.UnsupportedFunctionType, location, parameter.ContainingSymbol.Name, $"parameter '{parameter.Name}' of type {parameter.Type.ToDisplayString()}"));
            return null;
        }

        var nullable = parameter.Type.NullableAnnotation == NullableAnnotation.Annotated
                       || parameter.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
        var underlying = parameter.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n ? n.TypeArguments[0] : parameter.Type;

        // Typed, so a conditional like `has ? (int)x : default(int?)` keeps the parameter's own type: an
        // untyped `default` there would read as the branch's type and turn an absent int? into 0.
        var typed = $"default({parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier | SymbolDisplayMiscellaneousOptions.UseSpecialTypes))})";

        string fallback;
        if (parameter.HasExplicitDefaultValue)
        {
            fallback = parameter.ExplicitDefaultValue switch
            {
                null => typed,
                string str => SymbolDisplay.FormatLiteral(str, quote: true),
                bool b => b ? "true" : "false",
                var v when underlying.TypeKind == TypeKind.Enum => $"({underlying.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){Convert.ToInt64(v)}",
                IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                _ => typed
            };
        }
        else if (nullable)
        {
            fallback = typed;
        }
        else
        {
            fallback = kind switch
            {
                PropertyKind.String => "\"\"",
                PropertyKind.StringList => "[]",
                _ => typed
            };
        }

        var description = parameter.GetAttributes()
                              .Where(a => a.AttributeClass?.ToDisplayString() == "System.ComponentModel.DescriptionAttribute")
                              .Select(a => a.ConstructorArguments.FirstOrDefault().Value as string)
                              .FirstOrDefault()
                          ?? ParamDoc(docXml, parameter.Name);

        return new ParameterModel(
            CsName: parameter.Name,
            JsonName: parameter.Name,
            Kind: kind!.Value,
            ElementTypeName: element,
            Description: description,
            Required: !parameter.HasExplicitDefaultValue && !nullable,
            Fallback: fallback,
            IsContext: false);
    }

    static void EmitFunctions(SourceProductionContext context, ImmutableArray<FunctionResult> results)
    {
        foreach (var diagnostic in results.SelectMany(r => r.Diagnostics))
            context.ReportDiagnostic(diagnostic.ToDiagnostic());

        var containers = results
            .Select(r => r.Model)
            .Where(m => m is not null)
            .Select(m => m!)
            .GroupBy(m => m.ContainerKey);

        foreach (var container in containers)
        {
            var first = container.First();
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            if (first.Namespace is not null)
                sb.Append("namespace ").Append(first.Namespace).AppendLine(";");
            sb.AppendLine();
            sb.Append(first.ContainerIsStatic ? "static partial " : "partial ").Append(first.ContainerKeyword).Append(' ').AppendLine(first.ContainerName);
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>Registers this class's A2UI functions: <c>builder.Functions(" + first.ContainerName + ".A2UIFunctions)</c>.</summary>");
            sb.Append("    public static void A2UIFunctions(").Append(G).AppendLine("A2UICatalogBuilder builder)");
            sb.AppendLine("    {");
            foreach (var function in container)
                RenderFunction(sb, function);
            sb.AppendLine("    }");
            sb.AppendLine("}");

            var hint = first.ContainerKey.Replace("global::", "").Replace('<', '_').Replace('>', '_');
            context.AddSource(hint + ".A2UIFunctions.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }
    }

    static void RenderFunction(StringBuilder sb, FunctionModel f)
    {
        sb.Append("        builder.Function(").Append(Lit(f.Name)).Append(", ").Append(Lit(f.Description)).Append(", ")
          .Append(G).Append("A2UIReturnType.").Append(f.ReturnKind).AppendLine(",");
        sb.AppendLine("            f =>");
        sb.AppendLine("            {");
        foreach (var p in f.Parameters.Where(p => !p.IsContext))
        {
            var property = new PropertyModel(p.CsName, p.JsonName, p.Kind, "", p.ElementTypeName, p.Description, p.Required,
                Literal: p.Kind is PropertyKind.Choice or PropertyKind.Integer, null, null, null, false, false);
            sb.Append("                ").Append(DescribeCall(property, "f")).AppendLine(";");
        }
        if (f.Callers != 0)
            sb.Append("                f.AllowedCallers((").Append(G).Append("A2UIAllowedCallers)").Append(f.Callers).AppendLine(");");
        if (f.RequiresUserActivation)
            sb.AppendLine("                f.RequiresUserActivation();");
        sb.AppendLine("            },");

        var args = string.Join(", ", f.Parameters.Select(ArgExpression));
        var call = $"{f.MethodName}({args})";
        if (f.ReturnKind == "Void")
            sb.Append("            ctx => { ").Append(call).AppendLine("; return null; });");
        else if (f.ReturnKind == "Number")
            sb.Append("            ctx => ").Append(G).Append("A2UIModelReader.Result((double)").Append(call).AppendLine("));");
        else
            sb.Append("            ctx => ").Append(G).Append("A2UIModelReader.Result(").Append(call).AppendLine("));");
    }

    static string ArgExpression(ParameterModel p)
    {
        if (p.IsContext)
            return "ctx";

        var name = Lit(p.JsonName);
        var v = "a_" + p.CsName;
        return p.Kind switch
        {
            PropertyKind.String => $"(ctx.Has({name}) ? ctx.ArgString({name}) : {p.Fallback})",
            PropertyKind.Number => $"(ctx.ArgNumber({name}) is double {v} ? ({p.ElementTypeName}){v} : {p.Fallback})",
            PropertyKind.Integer => $"(ctx.ArgNumber({name}) is double {v} ? ({p.ElementTypeName})global::System.Math.Round({v}) : {p.Fallback})",
            PropertyKind.Boolean => $"(ctx.Has({name}) ? ctx.ArgBoolean({name}) : {p.Fallback})",
            PropertyKind.StringList => $"(ctx.Has({name}) ? [.. {G}A2UIModelReader.ArgStringList(ctx, {name})] : {p.Fallback})",
            PropertyKind.Choice => $"(global::System.Enum.TryParse<{p.ElementTypeName}>(ctx.ArgString({name}), true, out var {v}) && global::System.Enum.IsDefined({v}) ? {v} : {p.Fallback})",
            _ => throw new InvalidOperationException(p.Kind.ToString())
        };
    }

    static string? DocXml(ISymbol symbol, CancellationToken ct)
    {
        var xml = symbol.GetDocumentationCommentXml(cancellationToken: ct);
        return string.IsNullOrWhiteSpace(xml) ? DocCommentFromTrivia(symbol, ct) : xml;
    }

    static string? Summary(string? xml) => DocElementText(xml, e => e.Name == "summary");

    static string? ParamDoc(string? xml, string name)
        => DocElementText(xml, e => e.Name == "param" && (string?)e.Attribute("name") == name);

    static string? DocSummary(ISymbol symbol, CancellationToken ct) => Summary(DocXml(symbol, ct));

    static string? DocElementText(string? xml, Func<XElement, bool> match)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            var element = XElement.Parse(xml).Elements().FirstOrDefault(match);
            if (element is null)
                return null;

            // <see cref="X"/> and <c>x</c> read as their text; whitespace from the comment's layout collapses.
            var text = string.Concat(element.Nodes().Select(NodeText));
            var collapsed = string.Join(" ", text.Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries));
            return collapsed.Length == 0 ? null : collapsed;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        static string NodeText(XNode node) => node switch
        {
            XText t => t.Value,
            XElement e when e.Attribute("cref") is { } cref => cref.Value.Substring(cref.Value.IndexOf(':') + 1).Split('.').Last(),
            XElement e when e.Attribute("langword") is { } word => word.Value,
            XElement e => string.Concat(e.Nodes().Select(NodeText)),
            _ => ""
        };
    }

    static string? DocCommentFromTrivia(ISymbol symbol, CancellationToken ct)
    {
        var node = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(ct);
        if (node is null)
            return null;

        var lines = node.GetLeadingTrivia()
            .Select(t => t.ToFullString())
            .SelectMany(t => t.Split('\n'))
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("///", StringComparison.Ordinal))
            .Select(l => l.Substring(3))
            .ToList();

        return lines.Count == 0 ? null : "<doc>" + string.Join("\n", lines) + "</doc>";
    }

    static bool Is(AttributeData attribute, string name)
        => attribute.AttributeClass?.ToDisplayString() == Ns + "." + name;

    static T? Named<T>(AttributeData attribute, string name)
    {
        foreach (var argument in attribute.NamedArguments.Where(a => a.Key == name))
        {
            if (argument.Value.Value is T value)
                return value;
        }
        return default;
    }

    static EquatableArray<string>? NamedStrings(AttributeData attribute, string name)
    {
        var argument = attribute.NamedArguments.FirstOrDefault(a => a.Key == name);
        if (argument.Key is null || argument.Value.IsNull || argument.Value.Kind != TypedConstantKind.Array)
            return null;

        return new EquatableArray<string>(argument.Value.Values.Select(v => v.Value as string).Where(v => v is not null).Select(v => v!).ToList());
    }

    static string CamelCase(string value) => value.Length == 0 ? value : char.ToLowerInvariant(value[0]) + value.Substring(1);

    // --- Rendering ------------------------------------------------------------------------------

    static string Render(ComponentModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS1591");
        if (model.Namespace is not null)
            sb.Append("namespace ").Append(model.Namespace).AppendLine(";");
        sb.AppendLine();

        var self = model.ClassName;
        var iface = model.IsItem ? $"{G}IA2UIItemModel<{self}>" : $"{G}IA2UIComponentModel<{self}>";
        var builder = model.IsItem ? $"{G}A2UIItemBuilder" : $"{G}A2UIComponentBuilder";

        sb.Append("partial class ").Append(self).Append(" : ").AppendLine(iface);
        sb.AppendLine("{");

        // JSON names, for ctx.Write / ctx.Dispatch / ctx.RenderChild without a string literal.
        sb.AppendLine("    /// <summary>The A2UI JSON names of this class's properties.</summary>");
        sb.AppendLine("    public static class A2UIProperties");
        sb.AppendLine("    {");
        if (!model.IsItem)
            sb.Append("        /// <summary>The component type.</summary>\n        public const string Component = ").Append(Lit(model.Name)).AppendLine(";");
        foreach (var p in model.Properties)
            sb.Append("        public const string ").Append(p.CsName).Append(" = ").Append(Lit(p.JsonName)).AppendLine(";");
        sb.AppendLine("    }");
        sb.AppendLine();

        if (!model.IsItem)
        {
            sb.Append("    static string ").Append(iface).Append(".ComponentName => ").Append(Lit(model.Name)).AppendLine(";");
            sb.Append("    static string ").Append(iface).Append(".ComponentDescription => ").Append(Lit(model.Description)).AppendLine(";");
            sb.AppendLine();
        }

        // Describe: the schema, through the same builder hand-written catalogs use.
        sb.Append("    static void ").Append(iface).Append(".Describe(").Append(builder).AppendLine(" b)");
        sb.AppendLine("    {");
        foreach (var p in model.Properties)
            sb.Append("        ").Append(DescribeCall(p)).AppendLine(";");
        if (!model.IsItem)
        {
            if (model.Checkable)
                sb.AppendLine("        b.Checkable();");
            if (!model.Weight)
                sb.AppendLine("        b.NoWeight();");
            if (model.AllowedParents is { } parents)
                sb.Append("        b.AllowedParents(").Append(string.Join(", ", parents.Select(Lit))).AppendLine(");");
            if (model.AllowedChildren is { } children)
                sb.Append("        b.AllowedChildren(").Append(string.Join(", ", children.Select(Lit))).AppendLine(");");
        }
        sb.AppendLine("    }");
        sb.AppendLine();

        // Read: a private constructor, so init-only and required properties can be assigned only when
        // the agent supplied them — anything left out keeps the class's own initializer.
        sb.Append("    static ").Append(self).Append(' ').Append(iface).Append(".Read(").Append(G).AppendLine("A2UIModelSource source)");
        sb.Append("        => new ").Append(self).AppendLine("(source, default(A2UIReadMarker));");
        sb.AppendLine();

        if (!model.IsItem)
        {
            sb.Append("    ").Append(G).AppendLine("A2UIModelSource? a2uiSource;");
            sb.AppendLine();

            // Typed write-back for every property the agent can bind: no JSON at the call site.
            foreach (var p in model.Properties)
                RenderSetters(sb, p);
        }

        if (!model.HasExplicitConstructor)
        {
            sb.AppendLine("    /// <summary>Creates an instance in code; the A2UI reader uses its own constructor.</summary>");
            sb.Append("    public ").Append(self).AppendLine("() { }");
            sb.AppendLine();
        }

        sb.AppendLine("    /// <summary>Disambiguates the generated reader constructor from your own.</summary>");
        sb.AppendLine("    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        sb.AppendLine("    public readonly struct A2UIReadMarker { }");
        sb.AppendLine();
        sb.AppendLine("    [global::System.Diagnostics.CodeAnalysis.SetsRequiredMembers]");
        sb.Append("    ").Append(self).Append("(").Append(G).AppendLine("A2UIModelSource source, A2UIReadMarker _)");
        sb.AppendLine("    {");
        sb.AppendLine("        var properties = source.Properties;");
        sb.AppendLine("        var surface = source.Surface;");
        sb.AppendLine("        var scope = source.Scope;");
        if (!model.IsItem)
            sb.AppendLine("        this.a2uiSource = source;");
        foreach (var p in model.Properties)
            sb.AppendLine(ReadStatement(p));
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static void RenderSetters(StringBuilder sb, PropertyModel p)
    {
        var parameterTypes = p.Kind switch
        {
            PropertyKind.String when !p.Literal => new[] { "string?" },
            PropertyKind.Number when !p.Literal => new[] { "double" },
            PropertyKind.Boolean when !p.Literal => new[] { "bool" },
            PropertyKind.StringList when !p.Literal => new[] { "global::System.Collections.Generic.IEnumerable<string>?" },
            PropertyKind.Binding => new[] { "string?", "double", "bool", "global::System.Collections.Generic.IEnumerable<string>?" },
            _ => System.Array.Empty<string>()
        };

        foreach (var type in parameterTypes)
        {
            sb.Append("    /// <summary>Writes the user's input to the path <c>").Append(Escape(p.JsonName))
              .AppendLine("</c> is bound to. False when the agent gave a literal instead of a binding.</summary>");
            sb.Append("    public bool Set").Append(p.CsName).Append('(').Append(type).Append(" value) => ")
              .Append(G).Append("A2UIModelWriter.Set(this.a2uiSource, ").Append(Lit(p.JsonName)).AppendLine(", value);");
            sb.AppendLine();
        }
    }

    static string Escape(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    static string DescribeCall(PropertyModel p, string r = "b")
    {
        var name = Lit(p.JsonName);
        var description = p.Description is null ? "null" : Lit(p.Description);
        var required = p.Required ? "true" : "false";
        var dynamic = p.Literal ? "false" : "true";
        var min = p.Minimum is { } mn ? D(mn) : "null";
        var max = p.Maximum is { } mx ? D(mx) : "null";
        var def = p.SchemaDefault ?? "null";

        return p.Kind switch
        {
            PropertyKind.String => $"{r}.String({name}, {description}, {required}, {dynamic}, {def})",
            PropertyKind.Number => $"{r}.Number({name}, {description}, {required}, {dynamic}, {min}, {max}, {def})",
            PropertyKind.Integer => $"{r}.Integer({name}, {description}, {required}, {IntOrNull(p.Minimum)}, {IntOrNull(p.Maximum)}, {def})",
            PropertyKind.Boolean => $"{r}.Boolean({name}, {description}, {required}, {dynamic}, {def})",
            PropertyKind.StringList => $"{r}.StringList({name}, {description}, {required}, {dynamic})",
            PropertyKind.Value => $"{r}.Value({name}, {description}, {required})",
            PropertyKind.Choice => $"{r}.Choice<{p.ElementTypeName}>({name}, {description}, {required}, {def})",
            PropertyKind.Binding => $"{r}.Binding({name}, {description}, {required})",
            PropertyKind.Items => $"{r}.Array<{p.ElementTypeName}>({name}, {description}, {required})",
            PropertyKind.Action => $"{r}.Action({name}, {description}, {required})",
            PropertyKind.Child => $"{r}.Child({name}, {description}, {required})",
            PropertyKind.Children => $"{r}.Children({name}, {description}, {required})",
            _ => throw new InvalidOperationException(p.Kind.ToString())
        };
    }

    static string ReadStatement(PropertyModel p)
    {
        var node = $"properties[{Lit(p.JsonName)}]";
        var target = "this." + p.CsName;
        var name = Lit(p.JsonName);

        // A required, non-nullable reference the agent left out (already reported to it as
        // VALIDATION_FAILED) still gets a non-null value, so a renderer never trips over null.
        string Missing(string empty) => p.Required && !p.IsNullable && !p.IsValueType ? $" else {target} = {empty};" : "";

        return p.Kind switch
        {
            PropertyKind.String => $"        if ({node} is {{ }} n_{p.CsName}) {target} = surface.EvaluateString(n_{p.CsName}, scope);{Missing("\"\"")}",
            PropertyKind.Number => $"        if (surface.EvaluateNumber({node}, scope) is {{ }} v_{p.CsName}) {target} = ({p.ElementTypeName})v_{p.CsName};",
            PropertyKind.Integer => $"        if (surface.EvaluateNumber({node}, scope) is {{ }} v_{p.CsName}) {target} = ({p.ElementTypeName})global::System.Math.Round(v_{p.CsName});",
            PropertyKind.Boolean => $"        if ({node} is {{ }} n_{p.CsName}) {target} = surface.EvaluateBoolean(n_{p.CsName}, scope);",
            PropertyKind.StringList => $"        if ({node} is {{ }} n_{p.CsName}) {target} = [.. {G}A2UIModelReader.ReadStringList(n_{p.CsName}, surface, scope)];{Missing("[]")}",
            PropertyKind.Value => $"        if ({node} is {{ }} n_{p.CsName}) {target} = surface.Evaluate(n_{p.CsName}, scope)?.DeepClone();",
            PropertyKind.Choice => $"        if ({node} is {{ }} n_{p.CsName} && global::System.Enum.TryParse<{p.ElementTypeName}>(surface.EvaluateString(n_{p.CsName}, scope), true, out var e_{p.CsName}) && global::System.Enum.IsDefined(e_{p.CsName})) {target} = e_{p.CsName};",
            PropertyKind.Binding => $"        {target} = new {G}A2UIBindingRef({name}, {G}A2UIModelReader.BindingPath({node}));",
            PropertyKind.Items => $"        if ({node} is {{ }} n_{p.CsName}) {target} = [.. {G}A2UIModelReader.ReadItems<{p.ElementTypeName}>(n_{p.CsName}, surface, scope)];{Missing("[]")}",
            PropertyKind.Action => $"        {target} = new {G}A2UIActionRef({name}, {node} is not null);",
            PropertyKind.Child => $"        {target} = new {G}A2UIChildRef({name}, {G}A2UIValues.AsString({node}));",
            PropertyKind.Children => $"        {target} = new {G}A2UIChildrenRef({name});",
            _ => throw new InvalidOperationException(p.Kind.ToString())
        };
    }

    static string Lit(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    static string D(double value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "d";

    static string IntOrNull(double? value) => value is { } v ? ((int)v).ToString(System.Globalization.CultureInfo.InvariantCulture) : "null";
}

sealed record FunctionResult(FunctionModel? Model, EquatableArray<DiagnosticInfo> Diagnostics);

sealed record FunctionModel(
    string ContainerKey,
    string? Namespace,
    string ContainerName,
    bool ContainerIsStatic,
    string ContainerKeyword,
    string MethodName,
    string Name,
    string Description,
    string ReturnKind,
    bool ReturnsInteger,
    int Callers,
    bool RequiresUserActivation,
    EquatableArray<ParameterModel> Parameters);

sealed record ParameterModel(
    string CsName,
    string JsonName,
    PropertyKind Kind,
    string? ElementTypeName,
    string? Description,
    bool Required,
    string Fallback,
    bool IsContext);

enum PropertyKind { String, Number, Integer, Boolean, StringList, Value, Choice, Binding, Items, Action, Child, Children }

sealed record Result(ComponentModel? Model, EquatableArray<DiagnosticInfo> Diagnostics);

sealed record ComponentModel(
    string HintName,
    string? Namespace,
    string ClassName,
    bool IsItem,
    string Name,
    string Description,
    bool Checkable,
    bool Weight,
    EquatableArray<string>? AllowedParents,
    EquatableArray<string>? AllowedChildren,
    bool HasExplicitConstructor,
    EquatableArray<PropertyModel> Properties);

sealed record PropertyModel(
    string CsName,
    string JsonName,
    PropertyKind Kind,
    string TypeName,
    string? ElementTypeName,
    string? Description,
    bool Required,
    bool Literal,
    double? Minimum,
    double? Maximum,
    string? SchemaDefault,
    bool IsNullable,
    bool IsValueType);

/// <summary>An immutable array with value equality, so the incremental pipeline can cache on it.</summary>
readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T> where T : IEquatable<T>
{
    readonly ImmutableArray<T> items;

    public EquatableArray(IEnumerable<T> items) => this.items = items.ToImmutableArray();

    public bool Equals(EquatableArray<T> other)
        => (items.IsDefault ? ImmutableArray<T>.Empty : items).SequenceEqual(other.items.IsDefault ? ImmutableArray<T>.Empty : other.items);

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            foreach (var item in this)
                hash = hash * 31 + (item?.GetHashCode() ?? 0);
            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(items.IsDefault ? ImmutableArray<T>.Empty : items)).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>A diagnostic held as plain data, so it survives the pipeline's caching.</summary>
sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, string? FilePath, TextSpan Span, LinePositionSpan LineSpan, EquatableArray<string> Arguments)
{
    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, Location location, params string[] arguments)
    {
        var line = location.GetLineSpan();
        return new DiagnosticInfo(descriptor, location.SourceTree?.FilePath, location.SourceSpan, line.Span, new EquatableArray<string>(arguments));
    }

    public Diagnostic ToDiagnostic()
    {
        var location = FilePath is null ? Location.None : Location.Create(FilePath, Span, LineSpan);
        return Diagnostic.Create(Descriptor, location, Arguments.Cast<object>().ToArray());
    }
}

static class Diagnostics
{
    const string Category = "Shiny.A2UI";

    public static readonly DiagnosticDescriptor NotPartial = new(
        "SHINYA2UI001", "A2UI model must be partial",
        "'{0}' must be declared partial so the A2UI generator can add its schema and reader",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor Nested = new(
        "SHINYA2UI002", "A2UI model must be top-level",
        "'{0}' is nested in another type; declare A2UI models at namespace level",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor Generic = new(
        "SHINYA2UI003", "A2UI model can't be generic",
        "'{0}' is generic; an A2UI component has one fixed shape",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor PrimaryConstructor = new(
        "SHINYA2UI004", "A2UI model can't have a primary constructor",
        "'{0}' has a primary constructor; use properties with init accessors instead",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor UnsupportedType = new(
        "SHINYA2UI005", "Unsupported A2UI property type",
        "Property '{0}' has type '{1}', which A2UI can't describe. Use string, double, float, int, long, bool, an enum, JsonNode, a string list, a list of an [A2UIItem] class, or A2UIActionRef / A2UIChildRef / A2UIChildrenRef / A2UIBindingRef (components only), or mark it [A2UIIgnore].",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor RangeOnDynamic = new(
        "SHINYA2UI006", "Minimum or Maximum on a dynamic property",
        "Property '{0}' sets Minimum or Maximum but isn't Literal; JSON Schema can't put a range on a binding. Set Literal = true, or validate it with a check.",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor FunctionNotStatic = new(
        "SHINYA2UI008", "A2UI function must be static",
        "'{0}' is an instance method; A2UI functions are static methods",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor UnsupportedFunctionType = new(
        "SHINYA2UI009", "Unsupported A2UI function signature",
        "Function '{0}' uses {1}, which A2UI can't describe. Parameters may be string, double, float, int, long, bool, an enum, a string list or A2UIFunctionContext; the return type string, a number, bool or void.",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor NotSettable = new(
        "SHINYA2UI007", "A2UI property must be settable",
        "Property '{0}' has no setter; give it an init (or set) accessor, or mark it [A2UIIgnore]",
        Category, DiagnosticSeverity.Error, true);
}
