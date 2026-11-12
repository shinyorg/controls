using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shiny.Controls.AgentUI.A2UI;

/// <summary>A2UI error codes, as the renderer reports them to the agent.</summary>
public static class A2UIErrorCodes
{
    /// <summary>A message or component doesn't match its schema.</summary>
    public const string ValidationFailed = "VALIDATION_FAILED";
    /// <summary>A component is placed under a parent its catalog doesn't allow.</summary>
    public const string UnallowedParent = "UNALLOWED_PARENT";
    /// <summary>A container holds a child its catalog doesn't allow.</summary>
    public const string UnallowedChild = "UNALLOWED_CHILD";
    /// <summary>A surface was created twice, or a message names a surface that doesn't exist.</summary>
    public const string IntegrityError = "INTEGRITY_ERROR";
    /// <summary>A binding, function call or check couldn't be evaluated.</summary>
    public const string ExpressionError = "EXPRESSION_ERROR";
    /// <summary>A renderer function ran and failed.</summary>
    public const string ExecutionError = "EXECUTION_ERROR";
    /// <summary>The agent called a renderer function it may not, or one that doesn't exist.</summary>
    public const string InvalidFunctionCall = "INVALID_FUNCTION_CALL";
}

/// <summary>A renderer-side error. Reported to the agent as an A2UI <c>error</c> message.</summary>
public sealed record A2UIError(string Code, string Message, string? SurfaceId = null, string? Path = null, string? FunctionCallId = null);

/// <summary>Thrown by functions and the evaluator; turned into an <see cref="A2UIError"/> by the surface.</summary>
public sealed class A2UIExpressionException(string message, string code = A2UIErrorCodes.ExpressionError) : Exception(message)
{
    /// <summary>The A2UI error code to report (<see cref="A2UIErrorCodes"/>).</summary>
    public string Code { get; } = code;
}

/// <summary>A2UI's <c>ValidationResult</c>.</summary>
public sealed record A2UIValidationResult(bool Valid, string? Message = null, string? Code = null, string Severity = "error")
{
    /// <summary>A passing result.</summary>
    public static readonly A2UIValidationResult Passed = new(true);

    /// <summary>The result as A2UI's <c>ValidationResult</c> JSON.</summary>
    public JsonObject ToJson()
    {
        var o = new JsonObject { ["valid"] = Valid };
        if (Code is not null) o["code"] = Code;
        if (Message is not null) o["message"] = Message;
        if (!Valid || Severity != "error") o["severity"] = Severity;
        return o;
    }

    /// <summary>Reads a ValidationResult object; null when the node isn't one (no boolean <c>valid</c>).</summary>
    public static A2UIValidationResult? From(JsonNode? node)
    {
        if (node is not JsonObject o || o["valid"] is not JsonValue v || v.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
            return null;

        return new A2UIValidationResult(
            v.GetValue<bool>(),
            A2UIValues.AsString(o["message"]),
            A2UIValues.AsString(o["code"]),
            A2UIValues.AsString(o["severity"]) ?? "error");
    }
}

/// <summary>A2UI's type conversion rules, in one place so both hosts convert identically.</summary>
public static class A2UIValues
{
    /// <summary>
    /// Interpolation's string form: numbers and booleans in their standard (invariant) form, null
    /// as "", objects and arrays as JSON.
    /// </summary>
    public static string ToDisplayString(JsonNode? node) => node switch
    {
        null => "",
        JsonValue v => v.GetValueKind() switch
        {
            JsonValueKind.String => v.GetValue<string>(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "",
            JsonValueKind.Number => FormatNumber(v),
            _ => v.ToJsonString()
        },
        _ => node.ToJsonString()
    };

    /// <summary>The <c>DynamicBoolean</c> coercion table from the spec.</summary>
    public static bool ToBoolean(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return false;

            case JsonValue v:
                switch (v.GetValueKind())
                {
                    case JsonValueKind.True: return true;
                    case JsonValueKind.False: return false;
                    case JsonValueKind.String:
                        var s = v.GetValue<string>();
                        return s.Equals("true", StringComparison.OrdinalIgnoreCase);
                    case JsonValueKind.Number:
                        return TryGetDouble(v, out var d) && d != 0;
                    default:
                        return false;
                }

            case JsonObject:
                return A2UIValidationResult.From(node)?.Valid ?? false;

            default:
                return false;
        }
    }

    /// <summary>A number, or null. Numeric strings parse (invariant), so <c>numeric</c> can check a text field.</summary>
    public static double? ToNumber(JsonNode? node)
    {
        if (node is not JsonValue v)
            return null;

        switch (v.GetValueKind())
        {
            case JsonValueKind.Number:
                return TryGetDouble(v, out var d) ? d : null;

            case JsonValueKind.String:
                var s = v.GetValue<string>().Trim();
                return s.Length > 0 && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed)
                    ? parsed
                    : null;

            default:
                return null;
        }
    }

    /// <summary>A <c>DynamicStringList</c> value: an array's items as strings; a single string becomes a one-item list.</summary>
    public static IReadOnlyList<string> ToStringList(JsonNode? node) => node switch
    {
        JsonArray a => a.Select(ToDisplayString).ToList(),
        JsonValue v when v.GetValueKind() == JsonValueKind.String => [v.GetValue<string>()],
        _ => []
    };

    /// <summary>The value if it is a JSON string, else null.</summary>
    public static string? AsString(JsonNode? node) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    /// <summary>True for a <c>{"@path": ...}</c> binding.</summary>
    public static bool IsBinding(JsonNode? node, out string path)
    {
        if (node is JsonObject o && o.Count == 1 && AsString(o["@path"]) is { } p)
        {
            path = p;
            return true;
        }
        path = "";
        return false;
    }

    /// <summary>True for a <c>{"@call": ...}</c> function call.</summary>
    public static bool IsCall(JsonNode? node) => node is JsonObject o && AsString(o["@call"]) is not null;

    internal static bool TryGetDouble(JsonValue v, out double d)
    {
        if (v.TryGetValue(out d))
            return true;
        if (v.TryGetValue<JsonElement>(out var e) && e.ValueKind == JsonValueKind.Number)
            return e.TryGetDouble(out d);
        if (v.TryGetValue<int>(out var i)) { d = i; return true; }
        if (v.TryGetValue<long>(out var l)) { d = l; return true; }
        if (v.TryGetValue<decimal>(out var m)) { d = (double)m; return true; }
        if (v.TryGetValue<float>(out var f)) { d = f; return true; }
        return false;
    }

    // JavaScript's Number#toString: integers without a decimal point, everything else shortest round-trip.
    static string FormatNumber(JsonValue v)
    {
        if (!TryGetDouble(v, out var d))
            return v.ToJsonString();
        if (d == Math.Floor(d) && Math.Abs(d) < 1e21)
            return d.ToString("0", CultureInfo.InvariantCulture);
        return d.ToString("R", CultureInfo.InvariantCulture).Replace("E+", "e+").Replace("E-", "e-");
    }
}
