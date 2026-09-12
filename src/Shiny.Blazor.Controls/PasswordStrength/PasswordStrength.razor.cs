using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Shiny.Blazor.Controls;

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
/// Gate your submit button on <see cref="IsAcceptable"/>, not on <see cref="Score"/>. The score says
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
public partial class PasswordStrength : ComponentBase, IDisposable
{
    const int SegmentCount = 4;
    const string SatisfiedGlyph = "✓";
    const string UnsatisfiedGlyph = "○";

    [Inject] IServiceProvider Services { get; set; } = null!;

    readonly List<TextEntryTool> tools = new();
    TextEntryTool? visibilityTool;
    CancellationTokenSource? evaluation;
    PasswordStrengthResult? result;
    string? lastEvaluated;
    string? lastPolicy;

    // ---------------------------------------------------------------------------------------------
    // Value
    // ---------------------------------------------------------------------------------------------

    /// <summary>The password being typed. Use <c>@bind-Password</c>.</summary>
    [Parameter] public string Password { get; set; } = "";

    /// <summary>Fires as the user types, so <c>@bind-Password</c> works.</summary>
    [Parameter] public EventCallback<string> PasswordChanged { get; set; }

    /// <summary>Placeholder / floating label on the field.</summary>
    [Parameter] public string Placeholder { get; set; } = "Password";

    /// <summary>Passed straight through to the underlying <see cref="TextEntry"/>.</summary>
    [Parameter] public TextEntryVariant Variant { get; set; } = TextEntryVariant.Classic;

    /// <summary>Fires on the return key.</summary>
    [Parameter] public EventCallback Completed { get; set; }

    // ---------------------------------------------------------------------------------------------
    // Policy — see PasswordStrengthRules for why the composition rules default to off
    // ---------------------------------------------------------------------------------------------

    /// <summary>Shortest acceptable password. Default 15.</summary>
    [Parameter] public int MinimumLength { get; set; } = 15;

    /// <summary>Require at least one A-Z.</summary>
    [Parameter] public bool RequireUppercase { get; set; }

    /// <summary>Require at least one a-z.</summary>
    [Parameter] public bool RequireLowercase { get; set; }

    /// <summary>Require at least one digit.</summary>
    [Parameter] public bool RequireNumber { get; set; }

    /// <summary>Require at least one character from <see cref="SpecialCharacters"/>.</summary>
    [Parameter] public bool RequireSpecialCharacter { get; set; }

    /// <summary>Which characters count as special. Defaults to the printable ASCII symbols.</summary>
    [Parameter] public string SpecialCharacters { get; set; } = PasswordStrengthRules.DefaultSpecialCharacters;

    /// <summary>Refuse the commonly breached values and their obvious dressings-up. On by default.</summary>
    [Parameter] public bool RequireNotCompromisedPassword { get; set; } = true;

    /// <summary>Extra values to refuse — the product name, the company name, whatever else is banned.</summary>
    [Parameter] public IReadOnlyList<string>? BlockedPasswords { get; set; }

    /// <summary>
    /// This user's own details — email, username, display name. A password containing any of them is
    /// refused, and scored as if the matched run were not there.
    /// </summary>
    [Parameter] public IReadOnlyList<string>? UserInputs { get; set; }

    // ---------------------------------------------------------------------------------------------
    // Scoring
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The scorer. Null resolves <see cref="IPasswordStrengthEvaluator"/> from DI, and falls back to
    /// <see cref="DefaultPasswordStrengthEvaluator"/> when nothing is registered.
    /// </summary>
    [Parameter] public IPasswordStrengthEvaluator? Evaluator { get; set; }

    /// <summary>
    /// How long typing has to pause before the password is scored. Keeps a network-backed evaluator
    /// off the wire for every keystroke; set 0 to score on each one.
    /// </summary>
    [Parameter] public int DebounceMilliseconds { get; set; } = 250;

    /// <summary>Replaces the control's wording. Return null from it to keep a given default.</summary>
    [Parameter] public PasswordStrengthLocalizer? Localizer { get; set; }

    /// <summary>Raised each time the evaluator reports a new verdict.</summary>
    [Parameter] public EventCallback<PasswordStrengthChangedEventArgs> StrengthChanged { get; set; }

    // ---------------------------------------------------------------------------------------------
    // What is shown
    // ---------------------------------------------------------------------------------------------

    /// <summary>The eye button that reveals what has been typed.</summary>
    [Parameter] public bool ShowVisibilityToggle { get; set; } = true;

    /// <summary>The strength meter under the field.</summary>
    [Parameter] public bool ShowMeter { get; set; } = true;

    /// <summary>The Weak/Fair/Good/Strong caption beside the meter.</summary>
    [Parameter] public bool ShowStrengthLabel { get; set; } = true;

    /// <summary>The checklist of rules and whether each is met.</summary>
    [Parameter] public bool ShowRules { get; set; } = true;

    /// <summary>
    /// Whether the evaluator's warning ("this is one of the most commonly used passwords") is shown
    /// as the field's hint text.
    /// </summary>
    [Parameter] public bool ShowWarning { get; set; } = true;

    /// <summary>
    /// Toggle content while the password is hidden. Defaults to the word "Show", which renders
    /// identically everywhere and reads correctly to a screen reader; pass your own icon markup or
    /// glyph to replace it.
    /// </summary>
    [Parameter] public string? ShowPasswordIcon { get; set; }

    /// <summary>Toggle content while the password is revealed. Defaults to "Hide".</summary>
    [Parameter] public string? HidePasswordIcon { get; set; }

    // ---------------------------------------------------------------------------------------------
    // Appearance. Null colours fall through to the theme custom properties.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Four discrete blocks (default) or one continuous bar filled to the score.</summary>
    [Parameter] public PasswordStrengthMeterStyle MeterStyle { get; set; } = PasswordStrengthMeterStyle.Segments;

    /// <summary>Meter thickness in px.</summary>
    [Parameter] public double MeterHeight { get; set; } = 6;

    /// <summary>Meter corner radius, as any CSS length.</summary>
    [Parameter] public string MeterCornerRadius { get; set; } = "3px";

    /// <summary>Gap between segments in px. Ignored when <see cref="MeterStyle"/> is Bar.</summary>
    [Parameter] public double SegmentSpacing { get; set; } = 4;

    /// <summary>Unfilled meter colour. Null follows the surface-container-highest token.</summary>
    [Parameter] public string? TrackColor { get; set; }

    /// <summary>Null follows the critical token.</summary>
    [Parameter] public string? WeakColor { get; set; }

    /// <summary>Null follows the caution token.</summary>
    [Parameter] public string? FairColor { get; set; }

    /// <summary>Null follows the warning token.</summary>
    [Parameter] public string? GoodColor { get; set; }

    /// <summary>Null follows the success token.</summary>
    [Parameter] public string? StrongColor { get; set; }

    /// <summary>Colour of an unsatisfied checklist row. Null follows the on-surface-variant token.</summary>
    [Parameter] public string? RuleTextColor { get; set; }

    /// <summary>Checklist font size in px.</summary>
    [Parameter] public double RuleFontSize { get; set; } = 13;

    /// <summary>Extra classes on the host element.</summary>
    [Parameter] public string? CssClass { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    // ---------------------------------------------------------------------------------------------
    // Results
    // ---------------------------------------------------------------------------------------------

    /// <summary>0-100, as last reported by the evaluator.</summary>
    public int Score => result?.Score ?? 0;

    /// <summary>The bucket <see cref="Score"/> falls in.</summary>
    public PasswordStrengthLevel Level => result?.Level ?? PasswordStrengthLevel.None;

    /// <summary>
    /// True when every rule is met — what a submit button's <c>disabled</c> should be driven by.
    /// A high <see cref="Score"/> is not the same thing.
    /// </summary>
    public bool IsAcceptable => result?.IsAcceptable ?? false;

    /// <summary>The evaluator's full verdict, including the rule checklist and any suggestions.</summary>
    public PasswordStrengthResult? Result => result;

    /// <summary>Whether the typed characters are currently visible.</summary>
    public bool IsPasswordRevealed { get; private set; }

    // ---------------------------------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------------------------------

    protected override void OnInitialized()
    {
        visibilityTool = new TextEntryTool { Clicked = this.ToggleReveal };
        tools.Add(visibilityTool);
    }


    protected override void OnParametersSet()
    {
        visibilityTool!.IsVisible = this.ShowVisibilityToggle;
        visibilityTool.Text = this.IsPasswordRevealed
            ? this.HidePasswordIcon ?? this.Localize(PasswordStrengthTextKey.HidePassword, "Hide")
            : this.ShowPasswordIcon ?? this.Localize(PasswordStrengthTextKey.ShowPassword, "Show");

        // The policy can change without the password changing - a MinimumLength bound to a dropdown,
        // an email arriving into UserInputs. Re-scoring on every parent render instead would restart
        // the debounce each time the page above happened to re-render.
        var signature = this.PolicySignature();
        if (result is not null && signature == lastPolicy && lastEvaluated == this.Password)
            return;

        var passwordChanged = lastEvaluated != this.Password;
        lastPolicy = signature;
        _ = this.EvaluateAsync(passwordChanged ? this.DebounceMilliseconds : 0);
    }


    /// <summary>
    /// Everything the evaluator reads, flattened. Lists are included by content rather than by
    /// reference, so a rebuilt-but-identical list does not count as a change.
    /// </summary>
    string PolicySignature() => string.Join(
        '\u001f',
        this.MinimumLength,
        this.RequireUppercase,
        this.RequireLowercase,
        this.RequireNumber,
        this.RequireSpecialCharacter,
        this.SpecialCharacters,
        this.RequireNotCompromisedPassword,
        this.BlockedPasswords is null ? "" : string.Join(',', this.BlockedPasswords),
        this.UserInputs is null ? "" : string.Join(',', this.UserInputs)
    );


    /// <summary>
    /// Scores the current password immediately, bypassing the debounce. Call it after mutating a
    /// list passed to <see cref="UserInputs"/> or <see cref="BlockedPasswords"/> in place.
    /// </summary>
    public Task EvaluateNowAsync() => this.EvaluateAsync(0);


    /// <summary>Shows or hides the typed characters, exactly as the toggle button does.</summary>
    public void SetPasswordRevealed(bool revealed)
    {
        if (this.IsPasswordRevealed == revealed)
            return;

        this.IsPasswordRevealed = revealed;
        this.StateHasChanged();
    }


    public void Dispose()
    {
        evaluation?.Cancel();
        evaluation?.Dispose();
        evaluation = null;
        GC.SuppressFinalize(this);
    }


    // ---------------------------------------------------------------------------------------------
    // Input
    // ---------------------------------------------------------------------------------------------

    void ToggleReveal() => this.SetPasswordRevealed(!this.IsPasswordRevealed);


    async Task OnTextChanged(string value)
    {
        this.Password = value;
        await this.PasswordChanged.InvokeAsync(value);
        await this.EvaluateAsync(this.DebounceMilliseconds);
    }


    // ---------------------------------------------------------------------------------------------
    // Evaluation
    // ---------------------------------------------------------------------------------------------

    async Task EvaluateAsync(int delay)
    {
        var previous = evaluation;
        var source = new CancellationTokenSource();
        evaluation = source;

        previous?.Cancel();
        previous?.Dispose();

        var token = source.Token;
        var password = this.Password ?? string.Empty;
        lastEvaluated = password;

        try
        {
            // An empty box has nothing to debounce - clearing the field should clear the meter at once.
            if (delay > 0 && password.Length > 0)
                await Task.Delay(delay, token);

            var request = new PasswordStrengthRequest(password, this.BuildRules());
            var evaluator = this.ResolveEvaluator();

            PasswordStrengthResult verdict;
            try
            {
                verdict = await evaluator.EvaluateAsync(request, token);
            }
            catch (Exception) when (evaluator is not DefaultPasswordStrengthEvaluator && !token.IsCancellationRequested)
            {
                // A custom evaluator is usually a network call. Losing the network should downgrade
                // the meter to the local heuristic, not freeze it on a stale verdict.
                verdict = await DefaultPasswordStrengthEvaluator.Instance.EvaluateAsync(request, token);
            }

            if (token.IsCancellationRequested)
                return;

            var changed = result is null
                          || result.Score != verdict.Score
                          || result.Level != verdict.Level
                          || result.IsAcceptable != verdict.IsAcceptable;

            result = verdict;
            await this.InvokeAsync(this.StateHasChanged);

            if (changed && this.StrengthChanged.HasDelegate)
                await this.StrengthChanged.InvokeAsync(new PasswordStrengthChangedEventArgs(verdict));
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
        BlockedPasswords = this.BlockedPasswords,
        UserInputs = this.UserInputs
    };


    IPasswordStrengthEvaluator ResolveEvaluator()
        => this.Evaluator
           ?? this.Services.GetService<IPasswordStrengthEvaluator>()
           ?? DefaultPasswordStrengthEvaluator.Instance;


    // ---------------------------------------------------------------------------------------------
    // Rendering helpers
    // ---------------------------------------------------------------------------------------------

    string? WarningText => this.ShowWarning ? result?.Warning : null;

    string LevelAriaText => this.Level == PasswordStrengthLevel.None ? "Empty" : this.LevelText;

    string TrackColorValue => this.TrackColor ?? "var(--shiny-color-surface-container-highest, #D7DCE1)";

    string FillColor => this.Level switch
    {
        PasswordStrengthLevel.Weak => this.WeakColor ?? "var(--shiny-color-critical, #C20014)",
        PasswordStrengthLevel.Fair => this.FairColor ?? "var(--shiny-color-caution, #B42800)",
        PasswordStrengthLevel.Good => this.GoodColor ?? "var(--shiny-color-warning, #9C4500)",
        PasswordStrengthLevel.Strong => this.StrongColor ?? "var(--shiny-color-success, #00711C)",
        _ => this.TrackColorValue
    };

    string MeterStyleAttribute
        => $"height: {this.MeterHeight}px; " +
           $"border-radius: {this.MeterCornerRadius}; " +
           $"gap: {(this.MeterStyle == PasswordStrengthMeterStyle.Bar ? 0 : this.SegmentSpacing)}px;";

    string RuleRowStyle(PasswordRuleResult rule)
        => rule.IsSatisfied
            ? $"color: {this.StrongColor ?? "var(--shiny-color-success, #00711C)"};"
            : $"color: {this.RuleTextColor ?? "var(--shiny-color-on-surface-variant, #3B475B)"};";

    string LevelText => this.Level switch
    {
        PasswordStrengthLevel.Weak => this.Localize(PasswordStrengthTextKey.LevelWeak, "Weak"),
        PasswordStrengthLevel.Fair => this.Localize(PasswordStrengthTextKey.LevelFair, "Fair"),
        PasswordStrengthLevel.Good => this.Localize(PasswordStrengthTextKey.LevelGood, "Good"),
        PasswordStrengthLevel.Strong => this.Localize(PasswordStrengthTextKey.LevelStrong, "Strong"),
        _ => string.Empty
    };

    string RuleText(PasswordRuleResult rule)
    {
        var key = rule.Kind switch
        {
            PasswordRuleKind.MinimumLength => PasswordStrengthTextKey.RuleMinimumLength,
            PasswordRuleKind.Uppercase => PasswordStrengthTextKey.RuleUppercase,
            PasswordRuleKind.Lowercase => PasswordStrengthTextKey.RuleLowercase,
            PasswordRuleKind.Number => PasswordStrengthTextKey.RuleNumber,
            PasswordRuleKind.SpecialCharacter => PasswordStrengthTextKey.RuleSpecialCharacter,
            PasswordRuleKind.NotCompromised => PasswordStrengthTextKey.RuleNotCompromised,
            PasswordRuleKind.NotBlocked => PasswordStrengthTextKey.RuleNotBlocked,
            _ => PasswordStrengthTextKey.RuleNoUserInput
        };
        return this.Localize(key, rule.Description, rule.Argument);
    }

    string Localize(PasswordStrengthTextKey key, string fallback, int argument = 0)
        => this.Localizer?.Invoke(new PasswordStrengthText(key, fallback, argument)) ?? fallback;
}
