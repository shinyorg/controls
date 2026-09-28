using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Shiny.Controls.Office.Spreadsheet.Calc;

namespace Shiny.Blazor.Controls.Office;

/// <summary>
/// The spreadsheet's JS module, loaded once per component and tolerant of the places a module cannot
/// be loaded — prerendering, a torn-down circuit.
/// </summary>
sealed class SpreadsheetScript(IJSRuntime js) : IAsyncDisposable
{
    Task<IJSObjectReference>? module;

    async Task<IJSObjectReference?> ModuleAsync()
    {
        try
        {
            this.module ??= js.InvokeAsync<IJSObjectReference>("import", "./_content/Shiny.Blazor.Controls.Office/spreadsheet.js").AsTask();
            return await this.module;
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException or JSDisconnectedException)
        {
            this.module = null;
            return null;
        }
    }

    public async Task InvokeAsync(string name, params object?[] args)
    {
        if (await this.ModuleAsync() is not { } m)
            return;

        try
        {
            await m.InvokeVoidAsync(name, args);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or TaskCanceledException)
        {
            // A helper failing is never worth failing the component over.
        }
    }

    /// <summary>The caret's offset in an input, or <paramref name="fallback"/> when it cannot be read.</summary>
    public async Task<int> CaretAsync(ElementReference input, int fallback)
    {
        if (await this.ModuleAsync() is not { } m)
            return fallback;

        try
        {
            return await m.InvokeAsync<int>("caret", input);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or TaskCanceledException)
        {
            return fallback;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (this.module is null)
            return;

        try
        {
            var m = await this.module;
            await m.DisposeAsync();
        }
        catch (Exception)
        {
            // The runtime is already gone.
        }
    }
}

/// <summary>
/// The state of one autocomplete list — the in-cell editor's or the formula bar's — over
/// <see cref="FormulaAssist"/>, which does the actual work.
/// </summary>
sealed class FormulaAssistSession
{
    public FormulaAssistState State { get; private set; } = FormulaAssistState.None;

    public int Index { get; private set; }

    /// <summary>Whether the suggestion list is showing, which is when the arrow keys belong to it.</summary>
    public bool IsOpen => this.State.HasSuggestions && !this.dismissed;

    bool dismissed;

    public void Update(string text, int caret, IEnumerable<string> names)
    {
        var previous = this.State.Token;
        this.State = FormulaAssist.Analyze(text, caret, names);

        // A new token reopens a list Escape closed; the same one keeps it closed.
        if (!string.Equals(previous, this.State.Token, StringComparison.OrdinalIgnoreCase))
        {
            this.dismissed = false;
            this.Index = 0;
        }

        this.Index = Math.Clamp(this.Index, 0, Math.Max(0, this.State.Suggestions.Count - 1));
    }

    public void Move(int delta)
    {
        if (this.State.Suggestions.Count == 0)
            return;

        this.Index = (this.Index + delta + this.State.Suggestions.Count) % this.State.Suggestions.Count;
    }

    public void Dismiss() => this.dismissed = true;

    public void Reset()
    {
        this.State = FormulaAssistState.None;
        this.Index = 0;
        this.dismissed = false;
    }

    /// <summary>The text and caret after taking the highlighted suggestion, or null when there is none.</summary>
    public (string Text, int Caret)? Accept(string text, int? index = null)
    {
        if (!this.IsOpen)
            return null;

        var pick = this.State.Suggestions[Math.Clamp(index ?? this.Index, 0, this.State.Suggestions.Count - 1)];
        var result = FormulaAssist.Accept(text, this.State, pick);
        this.Reset();
        return result;
    }
}
