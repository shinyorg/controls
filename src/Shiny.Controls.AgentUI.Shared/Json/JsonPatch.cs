using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shiny.Controls.AgentUI.Json;

/// <summary>The patch could not be applied. Nothing was changed.</summary>
public sealed class JsonPatchException(string message) : Exception(message);

/// <summary>
/// RFC 6902 JSON Patch over <see cref="JsonNode"/> trees, as AG-UI's <c>STATE_DELTA</c> and
/// <c>ACTIVITY_DELTA</c> carry it.
/// </summary>
public static class JsonPatch
{
    /// <summary>
    /// Applies a patch document (a JSON array of operations) and returns the new root.
    /// Atomic: the operations run against a copy, so on failure the original is untouched.
    /// </summary>
    /// <exception cref="JsonPatchException">An operation is malformed, a path doesn't exist, or a <c>test</c> failed.</exception>
    public static JsonNode? Apply(JsonNode? root, JsonElement patch)
    {
        if (patch.ValueKind != JsonValueKind.Array)
            throw new JsonPatchException("A JSON Patch document must be an array of operations.");
        return Apply(root, (JsonArray)JsonNode.Parse(patch.GetRawText())!);
    }

    /// <inheritdoc cref="Apply(JsonNode?, JsonElement)" />
    public static JsonNode? Apply(JsonNode? root, JsonArray operations)
    {
        var doc = root?.DeepClone();
        for (var i = 0; i < operations.Count; i++)
        {
            if (operations[i] is not JsonObject op)
                throw new JsonPatchException($"Operation {i} is not an object.");
            doc = ApplyOne(doc, op, i);
        }
        return doc;
    }

    static JsonNode? ApplyOne(JsonNode? doc, JsonObject op, int index)
    {
        var name = op["op"]?.GetValue<string>() ?? throw new JsonPatchException($"Operation {index} has no 'op'.");
        var path = Pointer(op, "path", index);

        switch (name)
        {
            case "add":
                return Add(doc, path, Value(op, index), index);

            case "remove":
                if (path.Count == 0)
                    return null;
                if (!JsonPointer.Remove(doc, path))
                    throw Missing(index, path);
                return doc;

            case "replace":
                if (!JsonPointer.TryGet(doc, path, out _))
                    throw Missing(index, path);
                if (path.Count == 0)
                    return Value(op, index);

                // in place, so the member keeps its position in the object
                JsonPointer.TryGet(doc, path.Take(path.Count - 1).ToArray(), out var container);
                if (container is JsonObject replaceIn)
                    replaceIn[path[^1]] = Value(op, index);
                else if (container is JsonArray replaceAt && JsonPointer.TryIndex(path[^1], replaceAt.Count, allowAppend: false, out var at))
                    replaceAt[at] = Value(op, index);
                return doc;

            case "move":
            {
                var from = Pointer(op, "from", index);
                if (path.Count > from.Count && path.Take(from.Count).SequenceEqual(from))
                    throw new JsonPatchException($"Operation {index}: cannot move a value into one of its own children.");
                if (!JsonPointer.TryGet(doc, from, out var moved))
                    throw Missing(index, from);
                moved = moved?.DeepClone();
                if (from.Count == 0)
                    doc = null;
                else
                    JsonPointer.Remove(doc, from);
                return Add(doc, path, moved, index);
            }

            case "copy":
            {
                var from = Pointer(op, "from", index);
                if (!JsonPointer.TryGet(doc, from, out var copied))
                    throw Missing(index, from);
                return Add(doc, path, copied?.DeepClone(), index);
            }

            case "test":
                if (!JsonPointer.TryGet(doc, path, out var actual) || !JsonNode.DeepEquals(actual, Value(op, index)))
                    throw new JsonPatchException($"Operation {index}: test failed at '{JsonPointer.Format(path)}'.");
                return doc;

            default:
                throw new JsonPatchException($"Operation {index}: unknown op '{name}'.");
        }
    }

    // RFC 6902 'add': objects upsert the member, arrays insert (shifting the rest), and the parent must exist.
    static JsonNode? Add(JsonNode? doc, IReadOnlyList<string> path, JsonNode? value, int index)
    {
        if (path.Count == 0)
            return value;

        if (!JsonPointer.TryGet(doc, path.Take(path.Count - 1).ToArray(), out var parent))
            throw Missing(index, path.Take(path.Count - 1).ToArray());

        switch (parent)
        {
            case JsonObject obj:
                obj[path[^1]] = value;
                return doc;

            case JsonArray arr when JsonPointer.TryIndex(path[^1], arr.Count, allowAppend: true, out var i):
                arr.Insert(i, value);
                return doc;

            default:
                throw new JsonPatchException($"Operation {index}: cannot add at '{JsonPointer.Format(path)}'.");
        }
    }

    static IReadOnlyList<string> Pointer(JsonObject op, string member, int index)
    {
        if (op[member] is not JsonValue v || !v.TryGetValue<string>(out var s))
            throw new JsonPatchException($"Operation {index} has no '{member}'.");
        try
        {
            return JsonPointer.Parse(s);
        }
        catch (FormatException ex)
        {
            throw new JsonPatchException($"Operation {index}: {ex.Message}");
        }
    }

    static JsonNode? Value(JsonObject op, int index)
    {
        if (!op.TryGetPropertyValue("value", out var value))
            throw new JsonPatchException($"Operation {index} has no 'value'.");
        return value?.DeepClone();
    }

    static JsonPatchException Missing(int index, IReadOnlyList<string> path)
        => new($"Operation {index}: path '{JsonPointer.Format(path)}' does not exist.");
}
