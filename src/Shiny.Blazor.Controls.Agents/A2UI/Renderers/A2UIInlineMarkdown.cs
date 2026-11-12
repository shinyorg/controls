using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace Shiny.Blazor.Controls.Agents.Renderers;

/// <summary>
/// The "simple Markdown" A2UI allows in a <c>Text</c>: bold, italic, inline code and line breaks. No
/// HTML, images or links. The text is HTML-encoded first, so the only markup that comes out is ours.
/// </summary>
static partial class A2UIInlineMarkdown
{
    public static MarkupString ToHtml(string text)
    {
        if (string.IsNullOrEmpty(text))
            return default;

        var encoded = WebUtility.HtmlEncode(text);
        if (encoded.IndexOfAny(['*', '_', '`', '\n']) < 0)
            return new MarkupString(encoded);

        // code spans first, so their content isn't read as emphasis
        var parts = Code().Split(encoded);
        var sb = new StringBuilder(encoded.Length + 16);
        for (var i = 0; i < parts.Length; i++)
        {
            if (i % 2 == 1)
            {
                sb.Append("<code>").Append(parts[i]).Append("</code>");
                continue;
            }
            var s = Bold().Replace(parts[i], "<strong>$2</strong>");
            s = Italic().Replace(s, "<em>$2</em>");
            sb.Append(s.Replace("\n", "<br />"));
        }
        return new MarkupString(sb.ToString());
    }

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex Code();

    [GeneratedRegex(@"(\*\*|__)(?=\S)(.+?)(?<=\S)\1")]
    private static partial Regex Bold();

    [GeneratedRegex(@"(\*|_)(?=\S)(.+?)(?<=\S)\1")]
    private static partial Regex Italic();
}
