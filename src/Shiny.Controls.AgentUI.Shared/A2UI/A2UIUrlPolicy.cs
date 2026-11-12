namespace Shiny.Controls.AgentUI.A2UI;

/// <summary>
/// The URL rules A2UI makes mandatory, so a surface loads the same media and opens the same links
/// on every platform, and an agent can't smuggle in <c>javascript:</c> or read local files.
/// </summary>
public static class A2UIUrlPolicy
{
    /// <summary>
    /// Media (<c>Image.url</c>, <c>Video.url</c>, <c>Video.posterUrl</c>, <c>AudioPlayer.url</c>):
    /// http(s) with a host, <c>data:</c>, or a same-host relative path. Anything else renders nothing.
    /// Cheap enough to run on every render - only the prefix of a long <c>data:</c> URL is inspected.
    /// </summary>
    public static bool IsAllowedMediaUrl(string? url, out string trimmed)
    {
        trimmed = "";
        if (url is null)
            return false;

        var span = url.AsSpan().Trim().TrimStart('\0').Trim();
        span = TrimControl(span);
        if (span.IsEmpty)
            return false;

        trimmed = span.ToString();

        // browsers drop tab/CR/LF anywhere while parsing; check the head the same way
        var head = Clean(span[..Math.Min(span.Length, 64)]);
        var colon = head.IndexOf(':');
        var firstSlash = head.IndexOfAny('/', '\\');
        var hasScheme = colon > 0 && (firstSlash < 0 || colon < firstSlash) && IsScheme(head[..colon]);

        if (!hasScheme)
        {
            // relative: reject protocol-relative //host (\ counts as /)
            return !(head.Length >= 2 && head[0] is '/' or '\\' && head[1] is '/' or '\\');
        }

        var scheme = head[..colon].ToLowerInvariant();
        if (scheme == "data")
            return true;
        if (scheme is not ("http" or "https"))
            return false;

        // scheme://host - a non-empty host is required
        var rest = head[(colon + 1)..];
        if (rest.Length < 3 || rest[0] is not ('/' or '\\') || rest[1] is not ('/' or '\\'))
            return false;
        return rest[2] is not ('/' or '\\' or '?' or '#');
    }

    /// <summary>
    /// <c>openUrl</c>: resolves a relative URL against <paramref name="baseUri"/>, then allows only
    /// http and https.
    /// </summary>
    public static bool TryResolveOpenUrl(string? url, Uri? baseUri, out Uri resolved)
    {
        resolved = null!;
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var text = Clean(TrimControl(url.AsSpan().Trim()));
        if (!Uri.TryCreate(text, UriKind.Absolute, out var absolute) || absolute.IsFile && !text.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            if (baseUri is null || !Uri.TryCreate(baseUri, text, out absolute))
                return false;
        }

        if (absolute.Scheme is not ("http" or "https") || string.IsNullOrEmpty(absolute.Host))
            return false;

        resolved = absolute;
        return true;
    }

    static ReadOnlySpan<char> TrimControl(ReadOnlySpan<char> s)
    {
        var start = 0;
        var end = s.Length;
        while (start < end && (char.IsControl(s[start]) || char.IsWhiteSpace(s[start]))) start++;
        while (end > start && (char.IsControl(s[end - 1]) || char.IsWhiteSpace(s[end - 1]))) end--;
        return s[start..end];
    }

    static string Clean(ReadOnlySpan<char> s)
    {
        Span<char> buffer = s.Length <= 256 ? stackalloc char[s.Length] : new char[s.Length];
        var n = 0;
        foreach (var c in s)
        {
            if (c is not ('\t' or '\n' or '\r'))
                buffer[n++] = c;
        }
        return buffer[..n].ToString();
    }

    static bool IsScheme(string s) =>
        s.Length > 0 && char.IsAsciiLetter(s[0]) && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.');
}
