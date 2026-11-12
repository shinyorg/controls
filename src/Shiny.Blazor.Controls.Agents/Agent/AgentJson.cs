using System.Text;
using System.Text.Json;

namespace Shiny.Blazor.Controls.Agents;

/// <summary>JSON display helpers (trim and AOT safe: a reader and writer, no serializer).</summary>
static class AgentJson
{
    /// <summary>Indents JSON for display; returns the text unchanged when it isn't JSON (partial arguments, plain results).</summary>
    public static string Pretty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
                doc.WriteTo(writer);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return json;
        }
    }

    /// <summary>A one-line summary of tool arguments: <c>month: 2026-10, region: eu</c>.</summary>
    public static string Summary(string? json, int max = 90)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return Clip(doc.RootElement.GetRawText(), max);

            var sb = new StringBuilder();
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append(p.Name).Append(": ").Append(p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText());
            }
            return Clip(sb.ToString(), max);
        }
        catch (JsonException)
        {
            // still streaming: show the raw tail
            return Clip(json, max);
        }
    }

    static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
