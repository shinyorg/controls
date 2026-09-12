using System.Text.Json;
using Shiny.ThemeGen;

// Resolve repo root by walking up looking for the solution file.
var root = FindRepoRoot();
if (root is null)
{
    Console.Error.WriteLine("Could not locate repo root (Shiny.Controls.slnx).");
    return 1;
}

var themesDir = Path.Combine(root, "themes");
var jsonFiles = Directory
    .EnumerateFiles(themesDir, "*.json")
    .Where(f => !Path.GetFileName(f).Equals("shiny-theme.schema.json", StringComparison.OrdinalIgnoreCase))
    .OrderBy(f => f)
    .ToList();

if (jsonFiles.Count == 0)
{
    Console.Error.WriteLine($"No theme JSON files found in {themesDir}");
    return 1;
}

Console.WriteLine($"Repo root: {root}");
Console.WriteLine($"Found {jsonFiles.Count} theme(s).\n");

// The derivation table is the contract; a gap in it would emit a stylesheet that silently drops a
// variable every control reads, so it is checked before a single file is written.
AuthoringLayer.Validate();

foreach (var file in jsonFiles)
{
    var theme = LoadTheme(file);

    // Seeds still build a full Material scheme - that is how a theme gets a coherent palette out of
    // one brand colour. What changed is that the scheme is no longer the output: the authoring layer
    // is lifted out of it, the theme's own `tokens` block overrides whatever it wants to state
    // directly, and the roles are then derived back. A theme that states every token never has its
    // seeds consulted for anything but the tokens it left out.
    var palettes = Palettes.FromSeeds(theme.Seeds);
    var lightAuthoring = AuthoringLayer.FromScheme(SchemeBuilder.Build(palettes, dark: false), theme.LightTokens);
    var darkAuthoring = AuthoringLayer.FromScheme(SchemeBuilder.Build(palettes, dark: true), theme.DarkTokens);

    var light = AuthoringLayer.ToRoles(lightAuthoring);
    var dark = AuthoringLayer.ToRoles(darkAuthoring);

    var data = new ThemeData(
        theme.Name,
        theme.Slug,
        theme.Description,
        light,
        dark,
        lightAuthoring,
        darkAuthoring,
        Resolve.Shape(theme.Shape),
        Resolve.Type(theme.Type),
        theme.Type,
        theme.Elevation,
        Resolve.CssElevation(theme.Elevation),
        Resolve.Spacing(theme.Density),
        Resolve.Density(theme.Density),
        Resolve.State(theme.State),
        Resolve.Border(theme.Border));

    var isCore = theme.Slug == "basic";
    var mauiDir = isCore
        ? Path.Combine(root, "src", "Shiny.Maui.Controls", "Themes", "Generated")
        : Path.Combine(root, "src", $"Shiny.Maui.Controls.Themes.{theme.Name}", "Generated");
    var blazorCss = isCore
        ? Path.Combine(root, "src", "Shiny.Blazor.Controls", "wwwroot", "css", "shiny-theme.css")
        : Path.Combine(root, "src", $"Shiny.Blazor.Controls.Themes.{theme.Name}", "wwwroot", "css", $"shiny-theme-{theme.Slug}.css");

    // The scheme dictionaries are XAML so they can be read, copied and overridden the way every
    // other MAUI resource dictionary is. The C# ones they replace are deleted rather than left
    // behind: the class names are the same, and two of each would not compile.
    foreach (var scheme in new[] { false, true })
    {
        var name = $"{theme.Name}{(scheme ? "Dark" : "Light")}Theme";
        WriteFile(Path.Combine(mauiDir, $"{name}.xaml"), Emitter.MauiXaml(data, scheme));
        WriteFile(Path.Combine(mauiDir, $"{name}.xaml.cs"), Emitter.MauiXamlCodeBehind(data, scheme));
        DeleteIfPresent(Path.Combine(mauiDir, $"{name}.cs"));
    }

    WriteFile(Path.Combine(mauiDir, $"{theme.Name}Theme.cs"), Emitter.MauiTheme(data));
    WriteFile(blazorCss, Emitter.Css(data, core: isCore));

    Console.WriteLine($"  {theme.Name,-10} -> MAUI {(isCore ? "[core]" : "[pack]")}  +  Blazor css");
}

// ShinyThemeKeys is the MAUI contract — emit once into the core library.
WriteFile(
    Path.Combine(root, "src", "Shiny.Maui.Controls", "Themes", "Generated", "ShinyThemeKeys.cs"),
    Emitter.ThemeKeys());

Console.WriteLine("\nDone.");
return 0;

static void DeleteIfPresent(string path)
{
    if (File.Exists(path))
    {
        File.Delete(path);
        Console.WriteLine($"    removed {Path.GetFileName(path)} (replaced by XAML)");
    }
}

static void WriteFile(string path, string content)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    // Normalize to LF and a single trailing newline for stable diffs.
    var normalized = content.Replace("\r\n", "\n").TrimEnd('\n') + "\n";
    File.WriteAllText(path, normalized);
}

static string? FindRepoRoot()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Shiny.Controls.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
    }
    return null;
}

static ThemeSource LoadTheme(string path)
{
    using var doc = JsonDocument.Parse(File.ReadAllText(path));
    var rootEl = doc.RootElement;

    var seeds = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var prop in rootEl.GetProperty("seeds").EnumerateObject())
        seeds[prop.Name] = prop.Value.GetString()!;

    return new ThemeSource(
        rootEl.GetProperty("name").GetString()!,
        rootEl.GetProperty("slug").GetString()!,
        rootEl.TryGetProperty("description", out var d) ? d.GetString()! : "",
        seeds,
        ReadTokens(rootEl, "light"),
        ReadTokens(rootEl, "dark"),
        ReadShape(rootEl),
        ReadType(rootEl),
        ReadElevation(rootEl),
        ReadDensity(rootEl),
        ReadBorder(rootEl),
        ReadState(rootEl));
}

/// <summary>
/// The optional `tokens.light` / `tokens.dark` block: authoring values stated outright rather than
/// derived from the seeds. This is the hand-authoring path - a theme can be written as nothing but
/// these, and the seeds then only fill the gaps.
/// </summary>
static Dictionary<string, string>? ReadTokens(JsonElement root, string scheme)
{
    if (!root.TryGetProperty("tokens", out var tokens) || !tokens.TryGetProperty(scheme, out var block))
        return null;

    var map = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var prop in block.EnumerateObject())
        map[prop.Name] = prop.Value.GetString()!;

    return map;
}

// Every block below is optional and falls back to the shared default, so a theme only states the
// axes it actually wants to differ on.

static ShapeSpec ReadShape(JsonElement root)
{
    if (!root.TryGetProperty("shape", out var el))
        return ShapeSpec.Default;

    var scale = Num(el, "scale") ?? 1d;
    var corners = new Dictionary<string, double>(StringComparer.Ordinal);
    if (el.TryGetProperty("corners", out var cornersEl))
        foreach (var prop in cornersEl.EnumerateObject())
            corners[prop.Name] = prop.Value.GetDouble();

    return new ShapeSpec(scale, corners);
}

static TypeSpec ReadType(JsonElement root)
{
    if (!root.TryGetProperty("typography", out var el))
        return TypeSpec.Default;

    return new TypeSpec(
        Str(el, "fontFamily") ?? "",
        Str(el, "displayFamily") ?? "",
        Str(el, "monoFamily") ?? "",
        Num(el, "scale") ?? 1d,
        (int)(Num(el, "weightOffset") ?? 0d),
        Num(el, "trackingOffset") ?? 0d,
        Num(el, "lineHeightScale") ?? 1d);
}

static ElevationSpec ReadElevation(JsonElement root)
{
    if (!root.TryGetProperty("elevation", out var el))
        return ElevationSpec.Default;

    var style = (Str(el, "style") ?? "shadow").ToLowerInvariant() switch
    {
        "flat" => ElevationStyle.Flat,
        "outline" => ElevationStyle.Outline,
        "glow" => ElevationStyle.Glow,
        "shadow" => ElevationStyle.Shadow,
        var other => throw new InvalidOperationException($"Unknown elevation style '{other}'.")
    };

    return new ElevationSpec(
        style,
        Num(el, "intensity") ?? 1d,
        Num(el, "softness") ?? 1d,
        (Str(el, "tint") ?? "neutral").Equals("primary", StringComparison.OrdinalIgnoreCase));
}

static DensitySpec ReadDensity(JsonElement root)
{
    if (!root.TryGetProperty("density", out var el))
        return DensitySpec.Default;

    return new DensitySpec(
        Num(el, "scale") ?? 1d,
        Num(el, "controlHeight"),
        Num(el, "controlHeightSmall"),
        Num(el, "rowHeight"));
}

static BorderSpec ReadBorder(JsonElement root)
{
    if (!root.TryGetProperty("border", out var el))
        return BorderSpec.Default;

    var d = BorderSpec.Default;
    return new BorderSpec(Num(el, "thin") ?? d.Thin, Num(el, "medium") ?? d.Medium, Num(el, "thick") ?? d.Thick);
}

static StateSpec ReadState(JsonElement root)
{
    if (!root.TryGetProperty("state", out var el))
        return StateSpec.Default;

    var d = StateSpec.Default;
    return new StateSpec(
        Num(el, "hover") ?? d.Hover,
        Num(el, "focus") ?? d.Focus,
        Num(el, "pressed") ?? d.Pressed,
        Num(el, "dragged") ?? d.Dragged);
}

static double? Num(JsonElement el, string name) =>
    el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

static string? Str(JsonElement el, string name) =>
    el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

sealed record ThemeSource(
    string Name,
    string Slug,
    string Description,
    IReadOnlyDictionary<string, string> Seeds,
    IReadOnlyDictionary<string, string>? LightTokens,
    IReadOnlyDictionary<string, string>? DarkTokens,
    ShapeSpec Shape,
    TypeSpec Type,
    ElevationSpec Elevation,
    DensitySpec Density,
    BorderSpec Border,
    StateSpec State);
