namespace Shiny.Controls.Office.Shell;

/// <summary>
/// One command the title bar's "Search for tools, help, and more" box can find and run.
/// </summary>
/// <remarks>
/// Host-agnostic on purpose. Each host harvests its ribbon into these (the MAUI ribbon is a descriptor
/// model it can walk; the Blazor ribbon registers the items it has rendered), and an editor can add
/// commands the ribbon does not carry — "Go to page", "Word count" — so the search is not limited to
/// what happens to have a button.
/// </remarks>
public sealed class OfficeCommand
{
    public OfficeCommand(string label, Func<Task> execute)
    {
        this.Label = label;
        this.Execute = execute;
    }

    public OfficeCommand(string label, Action execute)
        : this(label, () => { execute(); return Task.CompletedTask; }) { }

    /// <summary>A stable id. Defaults to <c>Category/Label</c>, which is what de-duplicates a re-harvest.</summary>
    public string Id
    {
        get => this.id ?? (this.Category is null ? this.Label : $"{this.Category}/{this.Label}");
        init => this.id = value;
    }

    string? id;

    /// <summary>What the result line says — the button's label or tooltip.</summary>
    public string Label { get; }

    /// <summary>Where it lives — "Home › Font". Shown after the label and searched as well.</summary>
    public string? Category { get; init; }

    /// <summary>A second line: what the command does.</summary>
    public string? Description { get; init; }

    /// <summary>The keyboard shortcut, shown right-aligned — "Ctrl+B".</summary>
    public string? Shortcut { get; init; }

    /// <summary>Other words it should be found by — "bold" for "Strong", "size" for "Grow font".</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>The artwork of the button it came from, when the host has it. Opaque to the index.</summary>
    public object? Icon { get; init; }

    /// <summary>Asked each time the results are drawn, so a disabled command shows dimmed. Null is always enabled.</summary>
    public Func<bool>? CanExecute { get; init; }

    /// <summary>Runs the command.</summary>
    public Func<Task> Execute { get; }

    public bool IsEnabled => this.CanExecute?.Invoke() ?? true;

    public override string ToString() => this.Category is null ? this.Label : $"{this.Label} ({this.Category})";
}


/// <summary>A command the search found, and how well it matched.</summary>
public sealed record OfficeCommandMatch(OfficeCommand Command, double Score);


/// <summary>
/// The searchable list behind the title bar's command search.
/// </summary>
/// <remarks>
/// <para>
/// Ranking is the whole job, and it follows what people type into Office's own box: a whole-label hit
/// beats a label that starts with the query, which beats a word inside the label starting with it
/// ("font" finds "Grow Font"), which beats a keyword or category hit, which beats a subsequence
/// ("fcol" finds "Font Colour"). Ties go to the shorter label — "Bold" before "Bold all headings".
/// </para>
/// <para>
/// Not thread-safe: it is UI state, owned by the shell and mutated on the UI thread.
/// </para>
/// </remarks>
public sealed class OfficeCommandIndex
{
    readonly List<OfficeCommand> commands = [];

    /// <summary>Raised when the list changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Everything indexed, in the order added.</summary>
    public IReadOnlyList<OfficeCommand> Commands => this.commands;

    public int Count => this.commands.Count;


    /// <summary>Adds a command, replacing any with the same <see cref="OfficeCommand.Id"/>.</summary>
    public void Add(OfficeCommand command)
    {
        this.Replace(command);
        this.Changed?.Invoke(this, EventArgs.Empty);
    }


    /// <summary>Adds many at once, raising <see cref="Changed"/> once.</summary>
    public void AddRange(IEnumerable<OfficeCommand> commands)
    {
        foreach (var command in commands)
            this.Replace(command);

        this.Changed?.Invoke(this, EventArgs.Empty);
    }


    /// <summary>Convenience for <c>Add(new OfficeCommand(label, execute) { Category = category, Shortcut = shortcut })</c>.</summary>
    public OfficeCommand Add(string label, Action execute, string? category = null, string? shortcut = null, params string[] keywords)
    {
        var command = new OfficeCommand(label, execute) { Category = category, Shortcut = shortcut, Keywords = keywords };
        this.Add(command);
        return command;
    }


    public bool Remove(string id)
    {
        var removed = this.commands.RemoveAll(x => x.Id == id) > 0;
        if (removed)
            this.Changed?.Invoke(this, EventArgs.Empty);

        return removed;
    }


    /// <summary>Drops every command whose category starts with <paramref name="categoryPrefix"/> — a re-harvest of one source.</summary>
    public void RemoveCategory(string categoryPrefix)
    {
        if (this.commands.RemoveAll(x => x.Category?.StartsWith(categoryPrefix, StringComparison.Ordinal) == true) > 0)
            this.Changed?.Invoke(this, EventArgs.Empty);
    }


    public void Clear()
    {
        this.commands.Clear();
        this.Changed?.Invoke(this, EventArgs.Empty);
    }


    void Replace(OfficeCommand command)
    {
        var index = this.commands.FindIndex(x => x.Id == command.Id);
        if (index >= 0)
            this.commands[index] = command;
        else
            this.commands.Add(command);
    }


    /// <summary>The best matches for <paramref name="query"/>, best first. An empty query finds nothing.</summary>
    public IReadOnlyList<OfficeCommandMatch> Search(string? query, int max = 8)
    {
        if (string.IsNullOrWhiteSpace(query) || max <= 0)
            return [];

        var q = query.Trim();

        return this.commands
            .Select(x => new OfficeCommandMatch(x, Score(x, q)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Command.Label.Length)
            .ThenBy(x => x.Command.Label, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
    }


    /// <summary>Runs the best match for <paramref name="query"/>. Returns it, or null when nothing matched or it is disabled.</summary>
    public async Task<OfficeCommand?> ExecuteBestAsync(string? query)
    {
        var best = this.Search(query, 1).FirstOrDefault()?.Command;
        if (best is null || !best.IsEnabled)
            return null;

        await best.Execute().ConfigureAwait(false);
        return best;
    }


    /// <summary>How well <paramref name="command"/> matches — 0 for not at all, 100 for the whole label.</summary>
    public static double Score(OfficeCommand command, string query)
    {
        var q = query.Trim();
        if (q.Length == 0)
            return 0;

        var label = command.Label;
        var best = ScoreText(label, q, 1.0);

        foreach (var keyword in command.Keywords)
            best = Math.Max(best, ScoreText(keyword, q, 0.7));

        if (command.Category is { } category)
            best = Math.Max(best, ScoreText(category, q, 0.4));

        if (command.Shortcut is { } shortcut && string.Equals(Normalise(shortcut), Normalise(q), StringComparison.OrdinalIgnoreCase))
            best = Math.Max(best, 60);

        // Every word of a multi-word query landing somewhere in the label ("font col" → "Font Colour").
        if (best < 50 && q.Contains(' '))
        {
            var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.All(w => WordStarts(label, w)))
                best = Math.Max(best, 55);
        }

        return best;
    }


    static double ScoreText(string text, string query, double weight)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        if (string.Equals(text, query, StringComparison.OrdinalIgnoreCase))
            return 100 * weight;

        if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 80 * weight;

        if (WordStarts(text, query))
            return 65 * weight;

        if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
            return 45 * weight;

        if (IsSubsequence(text, query))
            return 20 * weight;

        return 0;
    }


    /// <summary>Whether a word inside <paramref name="text"/> starts with <paramref name="query"/>.</summary>
    static bool WordStarts(string text, string query)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var atWord = i == 0 || !char.IsLetterOrDigit(text[i - 1]) || (char.IsUpper(text[i]) && char.IsLower(text[i - 1]));
            if (atWord && string.Compare(text, i, query, 0, query.Length, StringComparison.OrdinalIgnoreCase) == 0)
                return true;
        }

        return false;
    }


    static bool IsSubsequence(string text, string query)
    {
        var j = 0;
        foreach (var c in text)
        {
            if (j < query.Length && char.ToLowerInvariant(c) == char.ToLowerInvariant(query[j]))
                j++;
        }

        // Spaces in the query are not characters the label has to contain.
        return j == query.Length && query.Length >= 2;
    }


    static string Normalise(string s) => s.Replace(" ", string.Empty, StringComparison.Ordinal);
}
