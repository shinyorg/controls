using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace Shiny.Maui.Controls.Analysis.Ai;

// The JSON document the Swift bridge decodes (SfmRequest in Native/ShinyFoundationModels.swift).
sealed record FoundationModelsPayload(
    string? Instructions,
    List<FoundationModelsTurn>? History,
    FoundationModelsTurn Prompt,
    string? Schema,
    double? Temperature,
    int? MaxTokens,
    List<string>? Tools,
    bool Stream,
    string? UseCase
);

sealed record FoundationModelsTurn(string Role, string Text, List<int>? Images);

// One request: the payload plus the image bytes it points at by index, and whether the JSON shape had to be
// asked for in the instructions (so the reply may come wrapped in a markdown code fence).
sealed record FoundationModelsRequest(FoundationModelsPayload Payload, List<ReadOnlyMemory<byte>> Images, bool JsonInPrompt)
{
    public string ToJson() => JsonSerializer.Serialize(this.Payload, FoundationModelsJsonContext.Default.FoundationModelsPayload);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(FoundationModelsPayload))]
partial class FoundationModelsJsonContext : JsonSerializerContext;


// Maps MEAI messages and options onto the bridge's request. Platform-neutral so it is unit tested on net10.0.
static class FoundationModelsRequestBuilder
{
    public static FoundationModelsRequest Build(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        FoundationModelsChatClientOptions clientOptions,
        bool stream)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (options?.Tools is { Count: > 0 })
            throw new NotSupportedException(
                "FoundationModelsChatClient does not support AIFunction tools yet. Apple's Vision tools are enabled through FoundationModelsChatClientOptions.");

        var instructions = new List<string>();
        if (!String.IsNullOrWhiteSpace(options?.Instructions))
            instructions.Add(options.Instructions);

        var images = new List<ReadOnlyMemory<byte>>();
        var turns = new List<FoundationModelsTurn>();
        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System)
            {
                if (!String.IsNullOrWhiteSpace(message.Text))
                    instructions.Add(message.Text);
            }
            else if (message.Role == ChatRole.User || message.Role == ChatRole.Assistant)
            {
                turns.Add(ToTurn(message, images));
            }
            // tool results and other roles have nowhere to go without AIFunction support
        }

        var promptIndex = turns.FindLastIndex(t => t.Role == "user");
        if (promptIndex < 0)
            throw new ArgumentException("At least one user message is required.", nameof(messages));

        // anything after the last user message (a trailing assistant turn) has no place in a new request
        var history = turns.Take(promptIndex).ToList();
        var prompt = turns[promptIndex];

        string? schema = null;
        var jsonInPrompt = false;
        if (options?.ResponseFormat is ChatResponseFormatJson json)
        {
            schema = json.Schema is { } s ? FoundationModelsSchema.ToAppleSchema(s) : null;
            if (schema is null)
            {
                // no schema, or one Foundation Models cannot take: ask for the JSON instead of constraining it
                jsonInPrompt = true;
                instructions.Add(json.Schema is { } raw
                    ? "Respond only with a JSON value that matches this JSON schema, with no other text:\n" + raw.GetRawText()
                    : "Respond only with valid JSON, with no other text.");
            }
        }

        var tools = new List<string>();
        if (clientOptions.UseOcrTool)
            tools.Add("ocr");
        if (clientOptions.UseBarcodeReaderTool)
            tools.Add("barcode");

        var payload = new FoundationModelsPayload(
            instructions.Count == 0 ? null : String.Join("\n\n", instructions),
            history.Count == 0 ? null : history,
            prompt,
            schema,
            options?.Temperature,
            options?.MaxOutputTokens,
            tools.Count == 0 ? null : tools,
            stream && schema is null,
            clientOptions.UseCase == FoundationModelsUseCase.ContentTagging ? "contentTagging" : null
        );
        return new FoundationModelsRequest(payload, images, jsonInPrompt);
    }

    static FoundationModelsTurn ToTurn(ChatMessage message, List<ReadOnlyMemory<byte>> images)
    {
        var text = new StringBuilder();
        List<int>? indices = null;
        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case TextContent t:
                    if (text.Length > 0)
                        text.Append('\n');
                    text.Append(t.Text);
                    break;

                case DataContent d when d.HasTopLevelMediaType("image"):
                    indices ??= [];
                    indices.Add(images.Count);
                    images.Add(d.Data);
                    break;

                case UriContent u when u.HasTopLevelMediaType("image"):
                    throw new NotSupportedException(
                        $"FoundationModelsChatClient runs on the device and cannot fetch '{u.Uri}'. Download the image and pass it as DataContent.");
            }
        }
        return new FoundationModelsTurn(message.Role == ChatRole.Assistant ? "assistant" : "user", text.ToString(), indices);
    }

    // When the JSON was only asked for, the model sometimes wraps it in ```json … ``` like a chat reply.
    public static string StripCodeFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return text;

        var firstNewLine = trimmed.IndexOf('\n');
        var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (firstNewLine < 0 || end <= firstNewLine)
            return text;

        return trimmed[(firstNewLine + 1)..end].Trim();
    }
}


// Rewrites a JSON schema (as MEAI's AIJsonUtilities writes it) into the dialect GenerationSchema decodes:
//   - one "type" string, never ["string","null"]; a nullable property is instead left out of "required"
//   - anyOf/oneOf with a {"type":"null"} branch collapses to the other branch, likewise optional
//   - no "$ref": MEAI points at the first occurrence of a repeated type ("#/properties/a/items"), which
//     Foundation Models cannot resolve, so every reference is inlined; recursive types cannot be, and fall back
// Returns null when the schema cannot be expressed, and the caller asks for the JSON in the instructions instead.
static class FoundationModelsSchema
{
    const int MaxDepth = 32;

    public static string? ToAppleSchema(JsonElement schema)
    {
        if (JsonNode.Parse(schema.GetRawText()) is not JsonObject root)
            return null;

        try
        {
            var inlined = Inline(root, root, 0);
            if (inlined is not JsonObject obj)
                return null;

            obj.Remove("$schema");
            obj.Remove("$defs");
            obj.Remove("definitions");
            return Rewrite(obj, out _) ? obj.ToJsonString() : null;
        }
        catch (UnsupportedSchemaException)
        {
            return null;
        }
    }

    sealed class UnsupportedSchemaException : Exception;

    static JsonNode? Inline(JsonNode? node, JsonObject root, int depth)
    {
        if (depth > MaxDepth)
            throw new UnsupportedSchemaException(); // a recursive type

        switch (node)
        {
            case JsonObject obj when obj["$ref"] is JsonValue refValue:
                var target = Resolve(root, refValue.GetValue<string>()) ?? throw new UnsupportedSchemaException();
                var copy = (JsonObject)Inline(target.DeepClone(), root, depth + 1)!;
                // keep keywords written next to the $ref (a property's description, say)
                foreach (var (key, value) in obj.Where(p => p.Key != "$ref").ToList())
                    copy[key] = Inline(value?.DeepClone(), root, depth + 1);
                return copy;

            case JsonObject obj:
                var result = new JsonObject();
                foreach (var (key, value) in obj.ToList())
                    result[key] = Inline(value?.DeepClone(), root, depth + 1);
                return result;

            case JsonArray array:
                var items = new JsonArray();
                foreach (var item in array.ToList())
                    items.Add(Inline(item?.DeepClone(), root, depth + 1));
                return items;

            default:
                return node?.DeepClone();
        }
    }

    // "#", "#/properties/a/items", "#/$defs/Line" (JSON pointer, with ~0 / ~1 escapes)
    static JsonNode? Resolve(JsonObject root, string reference)
    {
        if (!reference.StartsWith('#'))
            return null;

        JsonNode? current = root;
        var parts = reference[1..].Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Replace("~1", "/").Replace("~0", "~"));
        foreach (var part in parts)
        {
            current = current switch
            {
                JsonObject o => o[part],
                JsonArray a when Int32.TryParse(part, out var i) && i >= 0 && i < a.Count => a[i],
                _ => null
            };
            if (current is null)
                return null;
        }
        return current;
    }

    // Rewrites one schema node in place. nullable reports whether it accepted null, so the parent object can
    // drop it from "required". Returns false when the node cannot be expressed.
    static bool Rewrite(JsonObject node, out bool nullable)
    {
        nullable = false;

        if (node["type"] is JsonArray types)
        {
            var named = types.Select(t => t?.GetValue<string>()).ToList();
            nullable = named.Contains("null");
            var rest = named.Where(t => t is not null and not "null").ToList();
            if (rest.Count > 1)
                return false; // a real union ("string or number") has no equivalent
            if (rest.Count == 1)
                node["type"] = rest[0];
            else
                node.Remove("type");
        }

        foreach (var keyword in new[] { "anyOf", "oneOf" })
        {
            if (node[keyword] is JsonArray branches)
            {
                var nonNull = branches.OfType<JsonObject>().Where(b => b["type"]?.GetValue<string>() != "null").ToList();
                if (nonNull.Count < branches.Count)
                {
                    nullable = true;
                    if (nonNull.Count == 1)
                    {
                        node.Remove(keyword);
                        foreach (var (key, value) in nonNull[0].ToList())
                        {
                            if (!node.ContainsKey(key))
                                node[key] = value?.DeepClone();
                        }
                    }
                    else
                    {
                        branches.Clear();
                        foreach (var b in nonNull)
                            branches.Add(b.DeepClone());
                    }
                }

                if (node[keyword] is JsonArray remaining)
                {
                    foreach (var branch in remaining.OfType<JsonObject>())
                    {
                        if (!Rewrite(branch, out _))
                            return false;
                    }
                }
            }
        }

        if (node["properties"] is JsonObject properties)
        {
            var optional = new List<string>();
            foreach (var (name, value) in properties.ToList())
            {
                if (value is JsonObject property)
                {
                    if (!Rewrite(property, out var propertyNullable))
                        return false;
                    if (propertyNullable)
                        optional.Add(name);
                }
            }

            if (optional.Count > 0 && node["required"] is JsonArray required)
            {
                var keep = required.Select(r => r?.GetValue<string>()).Where(r => r is not null && !optional.Contains(r)).ToList();
                required.Clear();
                foreach (var r in keep)
                    required.Add((JsonNode?)JsonValue.Create(r));
            }
        }

        if (node["items"] is JsonObject items && !Rewrite(items, out _))
            return false;

        return true;
    }
}
