using System.Globalization;
using System.Text.Json.Nodes;
using Shiny.Controls.AgentUI.A2UI;

namespace Shiny.Blazor.Controls.Agents.Renderers;

/// <summary>Small conversions the renderers share.</summary>
static class A2UIRenderHelpers
{
    /// <summary>A catalog tone as a theme colour (with a fallback for pages without a theme).</summary>
    public static string ToneColor(string? tone) => tone switch
    {
        "good" or "success" => "var(--shiny-color-success, #2E7D32)",
        "warning" => "var(--shiny-color-warning, #B26A00)",
        "caution" => "var(--shiny-color-caution, #C77700)",
        "danger" or "critical" => "var(--shiny-color-error, #BA1A1A)",
        "info" => "var(--shiny-color-primary, #0055D9)",
        _ => "var(--shiny-color-outline, #74777F)"
    };

    public static string Invariant(double value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A date (ISO string or epoch ms) in the surface's culture, or the value as text.</summary>
    public static string FormatTime(JsonNode? value, A2UIRenderContext ctx)
    {
        var host = ctx.Surface.Host;
        if (value is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String
            && A2UIFormatting.TryParseDate(value, host.TimeZone, out var date))
        {
            var s = v.GetValue<string>();
            // a bare date has no time worth showing
            return s.Length <= 10 ? date.ToString("d", host.Culture) : date.ToString("g", host.Culture);
        }
        return A2UIValues.ToDisplayString(value);
    }

    /// <summary>An ISO date (YYYY-MM-DD, or the date part of a date-time).</summary>
    public static DateOnly? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;
        if (s.Length > 10)
            s = s[..10];
        return DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    public static string FormatDate(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A string array as JSON, for writing list bindings.</summary>
    public static JsonArray ToJsonArray(IEnumerable<string> values) => new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

    /// <summary>The <c>options</c> array of a ChoicePicker or ChipGroup, labels resolved.</summary>
    public static IReadOnlyList<A2UIOption> Options(A2UIRenderContext ctx)
    {
        if (ctx.Component["options"] is not JsonArray options)
            return [];
        var list = new List<A2UIOption>(options.Count);
        foreach (var o in options.OfType<JsonObject>())
        {
            if (A2UIValues.AsString(o["value"]) is not { } value)
                continue;
            list.Add(new A2UIOption(ctx.Surface.EvaluateString(o["label"], ctx.Scope), value));
        }
        return list;
    }
}

/// <summary>One choice of a ChoicePicker or ChipGroup.</summary>
public sealed record A2UIOption(string Label, string Value)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}
