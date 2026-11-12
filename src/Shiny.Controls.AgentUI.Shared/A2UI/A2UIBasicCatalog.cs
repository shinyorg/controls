using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Shiny.Controls.AgentUI.A2UI;

/// <summary>The A2UI v1.0 basic catalog: the pinned catalog JSON plus every function's renderer-side implementation.</summary>
static class A2UIBasicCatalog
{
    static readonly Lazy<string> Json = new(() =>
    {
        using var stream = typeof(A2UIBasicCatalog).Assembly.GetManifestResourceStream("Shiny.Controls.AgentUI.A2UI.basic_catalog.json")
                           ?? throw new InvalidOperationException("The basic catalog resource is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    static readonly Regex Email = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static A2UICatalog Create()
    {
        var catalog = A2UICatalog.Parse((JsonObject)JsonNode.Parse(Json.Value)!);

        catalog.Implement("required", ctx =>
        {
            var value = ctx.Arg("value");
            var present = value switch
            {
                null => false,
                JsonArray a => a.Count > 0,
                JsonValue v when v.GetValueKind() == System.Text.Json.JsonValueKind.String => v.GetValue<string>().Length > 0,
                JsonValue v => v.GetValueKind() != System.Text.Json.JsonValueKind.Null,
                _ => true
            };
            return new A2UIValidationResult(present).ToJson();
        });

        catalog.Implement("regex", ctx =>
            A2UIRegex.Test(ctx.ArgString("value"), A2UIValues.AsString(ctx.Arg("pattern")) ?? "").ToJson());

        catalog.Implement("length", ctx =>
        {
            // UTF-16 length, as the web reference renderer measures it
            var length = ctx.ArgString("value").Length;
            var ok = (ctx.ArgNumber("min") is not { } min || length >= min)
                     && (ctx.ArgNumber("max") is not { } max || length <= max);
            return new A2UIValidationResult(ok).ToJson();
        });

        catalog.Implement("numeric", ctx =>
        {
            var ok = ctx.ArgNumber("value") is { } value
                     && (ctx.ArgNumber("min") is not { } min || value >= min)
                     && (ctx.ArgNumber("max") is not { } max || value <= max);
            return new A2UIValidationResult(ok).ToJson();
        });

        catalog.Implement("email", ctx => new A2UIValidationResult(Email.IsMatch(ctx.ArgString("value"))).ToJson());

        catalog.Implement("formatString", ctx =>
        {
            var template = ctx.ArgString("value");
            var scope = ctx.Scope.WithCatalog(ctx.CatalogId);
            var sb = new StringBuilder();
            foreach (var part in FormatString.Compile(template))
                sb.Append(part.Literal ?? A2UIValues.ToDisplayString(ctx.Surface.Evaluate(part.Expression, scope)));
            return sb.ToString();
        });

        catalog.Implement("formatNumber", ctx =>
            ctx.ArgNumber("value") is { } value
                ? A2UIFormatting.FormatNumber(value, Decimals(ctx), !ctx.Has("grouping") || ctx.ArgBoolean("grouping"), ctx.Culture)
                : "");

        catalog.Implement("formatCurrency", ctx =>
            ctx.ArgNumber("value") is { } value
                ? A2UIFormatting.FormatCurrency(value, ctx.ArgString("currency"), Decimals(ctx), !ctx.Has("grouping") || ctx.ArgBoolean("grouping"), ctx.Culture)
                : "");

        catalog.Implement("formatDate", ctx =>
            A2UIFormatting.TryParseDate(ctx.Arg("value"), ctx.Host.TimeZone, out var date)
                ? A2UIFormatting.FormatDate(date, ctx.ArgString("format"), ctx.Culture, ctx.Host.TimeZone)
                : "");

        catalog.Implement("pluralize", ctx =>
        {
            var count = ctx.ArgNumber("value") ?? 0;
            var category = A2UIFormatting.PluralCategory(count, ctx.Culture);
            return ctx.Has(category) ? ctx.ArgString(category) : ctx.ArgString("other");
        });

        catalog.Implement("openUrl", ctx =>
        {
            var url = ctx.ArgString("url");
            if (!A2UIUrlPolicy.TryResolveOpenUrl(url, ctx.Host.BaseUri, out var uri))
                throw new A2UIExpressionException($"openUrl only opens http and https URLs ('{url}').");
            if (ctx.Host.OpenUrl is not { } open)
                throw new A2UIExpressionException("This renderer can't open URLs.", A2UIErrorCodes.ExecutionError);
            open(uri);
            return null;
        });

        catalog.Implement("and", ctx => Values(ctx).All(ctx.ResolveBoolean));
        catalog.Implement("or", ctx => Values(ctx).Any(ctx.ResolveBoolean));
        catalog.Implement("not", ctx => !ctx.ArgBoolean("value"));

        return catalog;

        static int? Decimals(A2UIFunctionContext ctx) => ctx.ArgNumber("decimals") is { } d ? (int)d : null;

        // short-circuits: items resolve one at a time, in order
        static IEnumerable<JsonNode?> Values(A2UIFunctionContext ctx) => ctx.RawArguments["values"] as JsonArray ?? [];
    }
}

/// <summary>
/// <c>formatString</c>'s <c>${...}</c> interpolation, compiled once per template into literal text
/// and ordinary A2UI expressions (bindings and calls), so evaluation reuses the normal evaluator.
/// </summary>
static class FormatString
{
    public readonly record struct Part(string? Literal, JsonNode? Expression);

    static readonly ConcurrentDictionary<string, IReadOnlyList<Part>> Cache = new(StringComparer.Ordinal);

    public static IReadOnlyList<Part> Compile(string template)
    {
        if (Cache.TryGetValue(template, out var cached))
            return cached;

        var parts = new Parser(template).ParseTemplate();
        if (Cache.Count > 512)
            Cache.Clear();
        Cache[template] = parts;
        return parts;
    }

    sealed class Parser(string s)
    {
        int pos;

        public IReadOnlyList<Part> ParseTemplate()
        {
            var parts = new List<Part>();
            var literal = new StringBuilder();
            while (pos < s.Length)
            {
                if (s[pos] == '\\' && Starts(pos + 1, "${"))
                {
                    literal.Append("${");
                    pos += 3;
                }
                else if (Starts(pos, "${"))
                {
                    if (literal.Length > 0)
                    {
                        parts.Add(new Part(literal.ToString(), null));
                        literal.Clear();
                    }
                    pos += 2;
                    var expr = ParseExpression();
                    SkipSpace();
                    Expect('}');
                    parts.Add(new Part(null, expr));
                }
                else
                {
                    literal.Append(s[pos++]);
                }
            }
            if (literal.Length > 0)
                parts.Add(new Part(literal.ToString(), null));
            return parts;
        }

        // literal | path | call | ${expr}
        JsonNode? ParseExpression()
        {
            SkipSpace();
            if (pos >= s.Length)
                throw Error("an expression was expected");

            if (Starts(pos, "${"))
            {
                pos += 2;
                var inner = ParseExpression();
                SkipSpace();
                Expect('}');
                return inner;
            }

            var c = s[pos];
            if (c is '\'' or '"')
                return JsonValue.Create(ParseQuoted(c));

            if (c == '-' || char.IsAsciiDigit(c))
            {
                var start = pos;
                pos++;
                while (pos < s.Length && (char.IsAsciiDigit(s[pos]) || s[pos] is '.' or 'e' or 'E' or '+' or '-'))
                    pos++;
                if (double.TryParse(s.AsSpan(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    return JsonValue.Create(number);
                pos = start;
            }

            // identifier( → call; otherwise a path (absolute or relative)
            var tokenStart = pos;
            while (pos < s.Length && s[pos] is not ('}' or ',' or ')' or '(') && !char.IsWhiteSpace(s[pos]))
                pos++;
            var token = s[tokenStart..pos];
            SkipSpace();

            if (pos < s.Length && s[pos] == '(' && token.Length > 0 && !token.Contains('/'))
            {
                pos++;
                var args = new JsonObject();
                SkipSpace();
                while (pos < s.Length && s[pos] != ')')
                {
                    var nameStart = pos;
                    while (pos < s.Length && s[pos] != ':' && s[pos] != ')' && !char.IsWhiteSpace(s[pos]))
                        pos++;
                    var name = s[nameStart..pos];
                    SkipSpace();
                    Expect(':');
                    args[name] = ParseExpression();
                    SkipSpace();
                    if (pos < s.Length && s[pos] == ',')
                    {
                        pos++;
                        SkipSpace();
                    }
                }
                Expect(')');
                var call = new JsonObject { ["@call"] = token };
                if (args.Count > 0)
                    call["args"] = args;
                return call;
            }

            return token switch
            {
                "true" => JsonValue.Create(true),
                "false" => JsonValue.Create(false),
                "null" => null,
                "" => throw Error("an expression was expected"),
                _ => new JsonObject { ["@path"] = token }
            };
        }

        string ParseQuoted(char quote)
        {
            pos++;
            var sb = new StringBuilder();
            while (pos < s.Length && s[pos] != quote)
            {
                if (s[pos] == '\\' && pos + 1 < s.Length)
                    pos++;
                sb.Append(s[pos++]);
            }
            Expect(quote);
            return sb.ToString();
        }

        void SkipSpace()
        {
            while (pos < s.Length && char.IsWhiteSpace(s[pos]))
                pos++;
        }

        void Expect(char c)
        {
            if (pos >= s.Length || s[pos] != c)
                throw Error($"'{c}' was expected at position {pos}");
            pos++;
        }

        bool Starts(int at, string text) => at + text.Length <= s.Length && string.CompareOrdinal(s, at, text, 0, text.Length) == 0;

        A2UIExpressionException Error(string why) => new($"formatString '{s}': {why}.");
    }
}
