using System.Globalization;
using System.Text.Json.Nodes;
using Shiny.Controls.AgentUI.Json;

namespace Shiny.Controls.AgentUI.A2UI;

/// <summary>
/// Where an expression is evaluated: the surface, the collection item it sits in (for relative
/// paths and <c>@index</c>), whether a user gesture is active, and optionally a set that records
/// every data path read so a renderer knows what to re-evaluate when the data model changes.
/// </summary>
public sealed class A2UIScope
{
    A2UIScope(A2UISurface surface, string? basePath, int? index, A2UIScope? parent, bool userActivation, string? catalogOverride, ISet<string>? dependencies)
    {
        Surface = surface;
        BasePath = basePath;
        Index = index;
        Parent = parent;
        UserActivation = userActivation;
        CatalogOverride = catalogOverride;
        Dependencies = dependencies;
    }

    internal static A2UIScope Root(A2UISurface surface) => new(surface, null, null, null, false, null, null);

    /// <summary>The surface being evaluated.</summary>
    public A2UISurface Surface { get; }

    /// <summary>The collection item this scope iterates (e.g. <c>/employees/2</c>); null in the root scope.</summary>
    public string? BasePath { get; }

    /// <summary>The innermost template's 0-based index; null in the root scope.</summary>
    public int? Index { get; }

    /// <summary>The enclosing scope, for nested templates.</summary>
    public A2UIScope? Parent { get; }

    /// <summary>True while an action triggered by a user gesture runs; gates <c>requiresUserActivation</c> functions.</summary>
    public bool UserActivation { get; }

    /// <summary>When set, the data paths each evaluation reads are added here (absolute pointers).</summary>
    public ISet<string>? Dependencies { get; }

    // formatString's ${fn()} resolves against the catalog its enclosing call resolved to
    internal string? CatalogOverride { get; }

    /// <summary>A child scope for one item of a template.</summary>
    public A2UIScope ForItem(string itemPointer, int index) => new(Surface, itemPointer, index, this, UserActivation, null, Dependencies);

    /// <summary>The same scope inside a user gesture (an action).</summary>
    public A2UIScope WithUserActivation() => new(Surface, BasePath, Index, Parent, true, CatalogOverride, Dependencies);

    /// <summary>The same scope, recording reads into <paramref name="dependencies"/>.</summary>
    public A2UIScope Tracking(ISet<string> dependencies) => new(Surface, BasePath, Index, Parent, UserActivation, CatalogOverride, dependencies);

    internal A2UIScope WithCatalog(string? catalogId) => new(Surface, BasePath, Index, Parent, UserActivation, catalogId, Dependencies);

    /// <summary>
    /// Resolves a binding path: absolute paths stand alone, relative ones hang off the collection item
    /// (or the root when there isn't one).
    /// </summary>
    public string Resolve(string path)
    {
        if (path.StartsWith('/'))
            return path;
        if (path.Length == 0)
            return BasePath ?? "/";
        return (BasePath ?? "") + "/" + path;
    }

    internal JsonNode? Read(string path)
    {
        var abs = Resolve(path);
        Dependencies?.Add(abs);
        return JsonPointer.TryGet(Surface.DataModel, JsonPointer.Parse(abs, slashIsRoot: true), out var value) ? value : null;
    }

    /// <summary>The scope's item path and index, for debugging.</summary>
    public override string ToString() => BasePath is null ? "(root)" : $"{BasePath} [{Index?.ToString(CultureInfo.InvariantCulture)}]";
}
