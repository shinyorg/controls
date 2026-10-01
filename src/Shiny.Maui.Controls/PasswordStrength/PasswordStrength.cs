using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

/// <summary>
/// A password field with a live strength meter and a rule checklist underneath it.
/// </summary>
/// <remarks>
/// <para>
/// The defaults follow passphrase-first guidance: fifteen characters, breached values refused, and
/// no composition rules at all. See <see cref="PasswordStrengthRules"/> for why — turning
/// <see cref="RequireSpecialCharacter"/> on makes passwords worse, not better, unless an external
/// policy forces it.
/// </para>
/// <para>
/// Bind a submit button to <see cref="IsAcceptable"/>, not to <see cref="Score"/>. The score says
/// how hard the password is to crack; only <see cref="IsAcceptable"/> says whether it satisfies the
/// policy, and the two genuinely disagree — a forty-character passphrase scores 100 and still fails
/// a rule demanding a digit.
/// </para>
/// <para>
/// Scoring goes through <see cref="IPasswordStrengthEvaluator"/>, resolved from
/// <see cref="Evaluator"/>, then from DI, then from the built-in heuristic. Keystrokes are debounced
/// by <see cref="DebounceMilliseconds"/> and the previous evaluation is cancelled before the next
/// starts, so an evaluator that goes to the network is workable. If a custom evaluator throws, the
/// built-in one answers instead rather than leaving the meter frozen.
/// </para>
/// </remarks>
public partial class PasswordStrength : ContentView
{
    const int SegmentCount = 4;
    const double RowSpacing = 6;
    const double GlyphWidth = 16;

    // The checklist glyphs. Both are single code points present in every system font, which a
    // check-mark emoji is not.
    const string SatisfiedGlyph = "✓";   // ✓
    const string UnsatisfiedGlyph = "○"; // ○

    readonly VerticalStackLayout root;
    readonly TextEntry entry;
    readonly TextEntryTool visibilityTool;
    readonly Grid meterRow;
    readonly Grid meterHost;
    readonly Label strengthLabel;
    readonly Label timeToCrackLabel;
    readonly VerticalStackLayout rulesLayout;
    readonly List<Border> segments = new();

    Border? barTrack;
    Border? barFill;
    Grid? barColumns;

    CancellationTokenSource? evaluation;
    bool syncingText;
    bool isRevealed;

    /// <summary>Raised each time the evaluator reports a new verdict.</summary>
    public event EventHandler<PasswordStrengthChangedEventArgs>? StrengthChanged;

    /// <summary>Raised when the return key is pressed in the field.</summary>
    public event EventHandler? Completed;

    public PasswordStrength()
    {
        visibilityTool = new TextEntryTool();
        visibilityTool.Clicked += (_, _) => this.ToggleReveal();

        entry = new TextEntry
        {
            IsPassword = true,
            // A password manager filling the field is the desired outcome; prediction and spell
            // check rewriting it is not.
            IsSpellCheckEnabled = false,
            IsTextPredictionEnabled = false
        };
        entry.TextChanged += this.OnEntryTextChanged;
        entry.Completed += (s, e) => this.Completed?.Invoke(this, e);

        meterHost = new Grid { ColumnSpacing = 0 };

        strengthLabel = new Label
        {
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.End,
            FontAttributes = FontAttributes.Bold
        }.WithFontSize(ShinyThemeKeys.Type.LabelMediumSize);

        meterRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8,
            VerticalOptions = LayoutOptions.Center
        };
        meterRow.Add(meterHost, 0);
        meterRow.Add(strengthLabel, 1);

        timeToCrackLabel = new Label { IsVisible = false };

        rulesLayout = new VerticalStackLayout { Spacing = 2 };

        root = new VerticalStackLayout
        {
            Spacing = RowSpacing,
            Children = { entry, meterRow, timeToCrackLabel, rulesLayout }
        };
        this.Content = root;

        this.BuildMeter();

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(PasswordStrength));

        this.ApplyAppearance();
        this.Schedule();
    }


    /// <summary>Gives the field focus and raises the keyboard.</summary>
    public new bool Focus() => entry.Focus();

    /// <summary>Dismisses the keyboard.</summary>
    public new void Unfocus() => entry.Unfocus();

    /// <summary>
    /// Shows or hides the typed characters, exactly as the toggle button does. Setting it while
    /// <see cref="ShowVisibilityToggle"/> is false is the way to drive the reveal from your own UI.
    /// </summary>
    public bool IsPasswordRevealed
    {
        get => isRevealed;
        set
        {
            if (isRevealed == value)
                return;

            isRevealed = value;
            entry.IsPassword = !value;
            this.ApplyToolContent();
        }
    }

    /// <summary>
    /// Scores the current password immediately, bypassing the debounce. Call it after changing
    /// something the evaluator reads that this control cannot observe — a mutated
    /// <see cref="UserInputs"/> list, say, rather than a replaced one.
    /// </summary>
    public Task EvaluateNowAsync(CancellationToken cancellationToken = default)
        => this.EvaluateAsync(delay: 0, cancellationToken);


    // ---------------------------------------------------------------------------------------------
    // Input
    // ---------------------------------------------------------------------------------------------

    void OnEntryTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (syncingText)
            return;

        syncingText = true;
        try
        {
            this.Password = e.NewTextValue ?? string.Empty;
        }
        finally
        {
            syncingText = false;
        }
    }


    void OnPasswordChanged()
    {
        if (!syncingText)
        {
            syncingText = true;
            try
            {
                entry.Text = this.Password ?? string.Empty;
            }
            finally
            {
                syncingText = false;
            }
        }
        this.Schedule();
    }


    void ToggleReveal() => this.IsPasswordRevealed = !isRevealed;


    // ---------------------------------------------------------------------------------------------
    // Evaluation
    // ---------------------------------------------------------------------------------------------

    void Schedule() => _ = this.EvaluateAsync(this.DebounceMilliseconds, CancellationToken.None);


    async Task EvaluateAsync(int delay, CancellationToken cancellationToken)
    {
        var previous = evaluation;
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        evaluation = source;

        previous?.Cancel();
        previous?.Dispose();

        var token = source.Token;
        var password = this.Password ?? string.Empty;

        try
        {
            // An empty box has nothing to debounce - clearing the field should clear the meter at once.
            if (delay > 0 && password.Length > 0)
                await Task.Delay(delay, token).ConfigureAwait(true);

            var request = new PasswordStrengthRequest(password, this.BuildRules());
            var evaluator = this.ResolveEvaluator();

            PasswordStrengthResult result;
            try
            {
                result = await evaluator.EvaluateAsync(request, token).ConfigureAwait(true);
            }
            catch (Exception) when (evaluator is not DefaultPasswordStrengthEvaluator && !token.IsCancellationRequested)
            {
                // A custom evaluator is usually a network call. Losing the network should downgrade
                // the meter to the local heuristic, not freeze it on a stale verdict.
                result = await DefaultPasswordStrengthEvaluator.Instance
                    .EvaluateAsync(request, token)
                    .ConfigureAwait(true);
            }

            if (token.IsCancellationRequested)
                return;

            // Element's own dispatcher rather than MainThread: a custom evaluator is free to complete
            // on a pool thread, and this is the marshalling that also works headless.
            var dispatcher = this.Dispatcher;
            if (dispatcher is null || !dispatcher.IsDispatchRequired)
                this.Apply(result);
            else
                dispatcher.Dispatch(() => this.Apply(result));
        }
        catch (OperationCanceledException)
        {
            // superseded by a later keystroke
        }
    }


    PasswordStrengthRules BuildRules() => new()
    {
        MinimumLength = this.MinimumLength,
        RequireUppercase = this.RequireUppercase,
        RequireLowercase = this.RequireLowercase,
        RequireNumber = this.RequireNumber,
        RequireSpecialCharacter = this.RequireSpecialCharacter,
        SpecialCharacters = this.SpecialCharacters,
        RequireNotCompromisedPassword = this.RequireNotCompromisedPassword,
        BlockedPasswords = this.BlockedPasswords?.ToList(),
        UserInputs = this.UserInputs?.ToList(),
        MinimumTimeToCrack = this.MinimumTimeToCrack,
        GuessesPerSecond = this.GuessesPerSecond
    };


    IPasswordStrengthEvaluator ResolveEvaluator()
    {
        if (this.Evaluator is not null)
            return this.Evaluator;

        var services = this.Handler?.MauiContext?.Services
                       ?? Application.Current?.Handler?.MauiContext?.Services;

        return services?.GetService<IPasswordStrengthEvaluator>()
               ?? DefaultPasswordStrengthEvaluator.Instance;
    }


    void Apply(PasswordStrengthResult result)
    {
        var changed = this.Result is null
                      || this.Result.Score != result.Score
                      || this.Result.Level != result.Level
                      || this.Result.IsAcceptable != result.IsAcceptable;

        this.Result = result;
        this.Score = result.Score;
        this.Level = result.Level;
        this.IsAcceptable = result.IsAcceptable;

        this.ApplyMeter(result);
        this.ApplyRules(result);
        this.ApplyTimeToCrack(result);
        this.ApplyWarning(result);

        if (!changed)
            return;

        var args = new PasswordStrengthChangedEventArgs(result);
        this.StrengthChanged?.Invoke(this, args);

        if (this.StrengthChangedCommand?.CanExecute(result) == true)
            this.StrengthChangedCommand.Execute(result);
    }
}
