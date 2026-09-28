namespace Shiny.Controls.Office.Spreadsheet.Calc;

/// <summary>What formula autocomplete has to offer at the caret.</summary>
/// <param name="Suggestions">Functions (and defined names) whose name starts with what is being typed.</param>
/// <param name="Token">The partial name under the caret.</param>
/// <param name="TokenStart">Where that token starts in the text.</param>
/// <param name="ActiveFunction">The function whose parentheses the caret is inside, for the signature tip.</param>
/// <param name="ArgumentIndex">Which of its arguments the caret is on, zero-based.</param>
public sealed record FormulaAssistState(
    IReadOnlyList<FormulaSuggestion> Suggestions,
    string Token,
    int TokenStart,
    FunctionInfo? ActiveFunction,
    int ArgumentIndex)
{
    public static readonly FormulaAssistState None = new([], string.Empty, 0, null, 0);

    public bool HasSuggestions => this.Suggestions.Count > 0;

    /// <summary>The signature, with the current argument marked — <c>SUM(number1, [number2], ...)</c>.</summary>
    public string? SignatureHint => this.ActiveFunction?.Signature;
}

/// <summary>One line of the autocomplete list.</summary>
/// <param name="IsFunction">False for a defined name, which is inserted without a parenthesis.</param>
public sealed record FormulaSuggestion(string Name, string Description, bool IsFunction);

/// <summary>
/// Formula autocomplete, host-agnostic: the list that drops under a cell or the formula bar while a
/// function name is typed, and the signature tip once its parenthesis is open.
/// </summary>
/// <remarks>
/// Pure text in, text out, so the in-cell editor and the formula bar on both hosts share it — and so it
/// can be tested without a UI. Nothing is suggested outside a formula or inside a string literal, which
/// is where a list of function names would be noise.
/// </remarks>
public static class FormulaAssist
{
    /// <summary>How many lines the list shows at most.</summary>
    public const int MaxSuggestions = 12;

    public static FormulaAssistState Analyze(string? text, int caret, IEnumerable<string>? definedNames = null)
    {
        if (string.IsNullOrEmpty(text) || text[0] != '=')
            return FormulaAssistState.None;

        caret = Math.Clamp(caret, 0, text.Length);

        // Walk up to the caret tracking strings and open calls, so the tip knows which call it is in.
        var inString = false;
        var calls = new Stack<(string Name, int Arguments)>();
        var tokenStart = -1;

        for (var i = 1; i < caret; i++)
        {
            var c = text[i];

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
                continue;

            if (c == '(')
            {
                var name = NameBefore(text, i);
                calls.Push((name, 0));
            }
            else if (c == ')')
            {
                if (calls.Count > 0)
                    calls.Pop();
            }
            else if (c == ',' && calls.Count > 0)
            {
                var top = calls.Pop();
                calls.Push((top.Name, top.Arguments + 1));
            }
        }

        FunctionInfo? active = null;
        var argument = 0;
        foreach (var call in calls)
        {
            if (FunctionCatalog.Find(call.Name) is { } info)
            {
                active = info;
                argument = call.Arguments;
                break;
            }
        }

        if (inString)
            return new FormulaAssistState([], string.Empty, caret, active, argument);

        // The partial name ending at the caret.
        tokenStart = caret;
        while (tokenStart > 1 && IsNameChar(text[tokenStart - 1]))
            tokenStart--;

        var token = text[tokenStart..caret];

        // A cell reference being typed ("A1", "$B") or a number is not a name.
        if (token.Length == 0 || !char.IsLetter(token[0]) || (tokenStart > 0 && text[tokenStart - 1] is '$' or '!' or ':'))
            return new FormulaAssistState([], string.Empty, caret, active, argument);

        var functions = FunctionCatalog.All
            .Where(x => x.Name.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            .Select(x => new FormulaSuggestion(x.Name, x.Description, true));

        var names = (definedNames ?? [])
            .Where(x => x.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            .Select(x => new FormulaSuggestion(x, "Defined name", false));

        var suggestions = names.Concat(functions)
            .OrderBy(x => x.Name.Length == token.Length ? 0 : 1)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSuggestions)
            .ToList();

        // A name already typed in full and followed by its parenthesis needs no list.
        if (suggestions.Count == 1 && string.Equals(suggestions[0].Name, token, StringComparison.OrdinalIgnoreCase) &&
            caret < text.Length && text[caret] == '(')
            suggestions.Clear();

        return new FormulaAssistState(suggestions, token, tokenStart, active, argument);
    }

    /// <summary>
    /// Replaces the partial name with the picked suggestion — and an opening parenthesis for a function —
    /// returning the new text and where the caret goes.
    /// </summary>
    public static (string Text, int Caret) Accept(string text, FormulaAssistState state, FormulaSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(state);

        var end = state.TokenStart + state.Token.Length;
        var insert = suggestion.IsFunction ? suggestion.Name + "(" : suggestion.Name;

        // Don't double the parenthesis when one is already there.
        if (suggestion.IsFunction && end < text.Length && text[end] == '(')
            insert = suggestion.Name;

        var result = text[..state.TokenStart] + insert + text[end..];
        return (result, state.TokenStart + insert.Length + (suggestion.IsFunction && insert.EndsWith('(') ? 0 : suggestion.IsFunction ? 1 : 0));
    }

    static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is '.' or '_';

    static string NameBefore(string text, int parenthesis)
    {
        var end = parenthesis;
        var start = end;
        while (start > 0 && IsNameChar(text[start - 1]))
            start--;

        return text[start..end];
    }
}
