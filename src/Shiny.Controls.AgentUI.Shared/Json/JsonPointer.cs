using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Shiny.Controls.AgentUI.Json;

/// <summary>
/// RFC 6901 JSON Pointers over <see cref="JsonNode"/> trees.
/// </summary>
/// <remarks>
/// RFC 6901 reads <c>""</c> as the whole document and <c>"/"</c> as the property named "" under it.
/// A2UI reads <c>"/"</c> as the whole data model too (it is <c>updateDataModel</c>'s default path),
/// so every parse takes <c>slashIsRoot</c>. JSON Patch passes false; A2UI passes true.
/// </remarks>
public static class JsonPointer
{
    /// <summary>Splits a pointer into unescaped reference tokens. Returns an empty list for the root.</summary>
    /// <exception cref="FormatException">The pointer is not empty and does not start with '/'.</exception>
    public static IReadOnlyList<string> Parse(string? pointer, bool slashIsRoot = false)
    {
        if (string.IsNullOrEmpty(pointer) || (slashIsRoot && pointer == "/"))
            return [];

        if (pointer[0] != '/')
            throw new FormatException($"JSON Pointer '{pointer}' must start with '/'.");

        var parts = pointer[1..].Split('/');
        for (var i = 0; i < parts.Length; i++)
            parts[i] = Unescape(parts[i]);
        return parts;
    }

    /// <summary>Joins tokens back into a pointer, escaping '~' and '/'.</summary>
    public static string Format(IEnumerable<string> tokens)
    {
        var sb = new StringBuilder();
        foreach (var t in tokens)
            sb.Append('/').Append(Escape(t));
        return sb.ToString();
    }

    /// <summary>Escapes one reference token ('~' to '~0', '/' to '~1').</summary>
    public static string Escape(string token) => token.Replace("~", "~0").Replace("/", "~1");

    /// <summary>Unescapes one reference token.</summary>
    public static string Unescape(string token) => token.Replace("~1", "/").Replace("~0", "~");

    /// <summary>Appends one token to a pointer.</summary>
    public static string Append(string pointer, string token) => (pointer == "/" ? "" : pointer) + "/" + Escape(token);

    /// <summary>Appends one array index to a pointer.</summary>
    public static string Append(string pointer, int index) => Append(pointer, index.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// True when a change at <paramref name="changed"/> can affect a read of <paramref name="read"/>:
    /// one is the other, or one sits underneath the other.
    /// </summary>
    public static bool Overlaps(string read, string changed)
    {
        read = Normalize(read);
        changed = Normalize(changed);
        if (read.Length == 0 || changed.Length == 0)
            return true;

        return IsSameOrUnder(read, changed) || IsSameOrUnder(changed, read);

        static bool IsSameOrUnder(string a, string b) =>
            a.Length == b.Length ? a == b : a.Length > b.Length && a.StartsWith(b, StringComparison.Ordinal) && a[b.Length] == '/';
    }

    static string Normalize(string p) => p == "/" ? "" : p;

    /// <summary>Looks up a value. Returns false when the location doesn't exist (a JSON null that exists returns true).</summary>
    public static bool TryGet(JsonNode? root, IReadOnlyList<string> tokens, out JsonNode? value)
    {
        var current = root;
        foreach (var token in tokens)
        {
            switch (current)
            {
                case JsonObject obj:
                    if (!obj.TryGetPropertyValue(token, out current))
                    {
                        value = null;
                        return false;
                    }
                    break;

                case JsonArray arr:
                    if (!TryIndex(token, arr.Count, allowAppend: false, out var i))
                    {
                        value = null;
                        return false;
                    }
                    current = arr[i];
                    break;

                default:
                    value = null;
                    return false;
            }
        }
        value = current;
        return true;
    }

    /// <inheritdoc cref="TryGet(JsonNode?, IReadOnlyList{string}, out JsonNode?)" />
    public static bool TryGet(JsonNode? root, string pointer, out JsonNode? value, bool slashIsRoot = false)
        => TryGet(root, Parse(pointer, slashIsRoot), out value);

    /// <summary>
    /// Upserts a value, creating any missing parent objects (A2UI's write semantics). A null
    /// <paramref name="value"/> removes the key instead. Returns the (possibly new) root.
    /// </summary>
    /// <remarks>
    /// The value is attached as is, so it must not already have a parent; clone it first if it does.
    /// </remarks>
    public static JsonNode? Upsert(JsonNode? root, IReadOnlyList<string> tokens, JsonNode? value)
    {
        if (tokens.Count == 0)
            return value;

        if (value is null)
        {
            Remove(root, tokens);
            return root;
        }

        root ??= new JsonObject();
        var current = root;
        for (var i = 0; i < tokens.Count - 1; i++)
        {
            var token = tokens[i];
            JsonNode? next;
            switch (current)
            {
                case JsonObject obj:
                    if (!obj.TryGetPropertyValue(token, out next) || next is not (JsonObject or JsonArray))
                    {
                        next = new JsonObject();
                        obj[token] = next;
                    }
                    break;

                case JsonArray arr when TryIndex(token, arr.Count, allowAppend: true, out var idx):
                    if (idx == arr.Count)
                    {
                        next = new JsonObject();
                        arr.Add(next);
                    }
                    else if (arr[idx] is JsonObject or JsonArray)
                    {
                        next = arr[idx];
                    }
                    else
                    {
                        next = new JsonObject();
                        arr[idx] = next;
                    }
                    break;

                default:
                    throw new InvalidOperationException($"Cannot write through '{Format(tokens.Take(i + 1))}': it is not an object or array.");
            }
            current = next!;
        }

        var last = tokens[^1];
        switch (current)
        {
            case JsonObject obj:
                obj[last] = value;
                break;

            case JsonArray arr when TryIndex(last, arr.Count, allowAppend: true, out var idx):
                if (idx == arr.Count)
                    arr.Add(value);
                else
                    arr[idx] = value;
                break;

            default:
                throw new InvalidOperationException($"Cannot write '{Format(tokens)}'.");
        }
        return root;
    }

    /// <summary>Removes a value. Returns false when it wasn't there.</summary>
    public static bool Remove(JsonNode? root, IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0 || !TryGet(root, tokens.Take(tokens.Count - 1).ToArray(), out var parent))
            return false;

        switch (parent)
        {
            case JsonObject obj:
                return obj.Remove(tokens[^1]);

            case JsonArray arr when TryIndex(tokens[^1], arr.Count, allowAppend: false, out var i):
                arr.RemoveAt(i);
                return true;

            default:
                return false;
        }
    }

    /// <summary>Parses an array index token: digits without a leading zero, or "-" (one past the end) when appending.</summary>
    internal static bool TryIndex(string token, int count, bool allowAppend, out int index)
    {
        if (token == "-")
        {
            index = count;
            return allowAppend;
        }

        if (token.Length == 0 || (token.Length > 1 && token[0] == '0') || !token.All(char.IsAsciiDigit)
            || !int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out index))
        {
            index = -1;
            return false;
        }
        return allowAppend ? index <= count : index < count;
    }
}
