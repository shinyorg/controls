namespace Shiny.Blazor.Controls;

/// <summary>
/// How strong a password is, in the four buckets a meter actually shows.
/// </summary>
public enum PasswordStrengthLevel
{
    /// <summary>Nothing has been typed yet — the meter is empty rather than red.</summary>
    None,
    Weak,
    Fair,
    Good,
    Strong
}


/// <summary>The kinds of rule a <see cref="PasswordStrengthRules"/> can ask for.</summary>
public enum PasswordRuleKind
{
    MinimumLength,
    Uppercase,
    Lowercase,
    Number,
    SpecialCharacter,
    NotCompromised,
    NotBlocked,
    NoUserInput
}


/// <summary>One rule, and whether the password currently meets it.</summary>
/// <param name="Kind">Which rule this is.</param>
/// <param name="Description">Default English wording, shown unless a localizer replaces it.</param>
/// <param name="IsSatisfied">Whether the password meets the rule right now.</param>
/// <param name="Argument">
/// The rule's number, where it has one — the required length for
/// <see cref="PasswordRuleKind.MinimumLength"/>, zero otherwise. Carried separately from
/// <paramref name="Description"/> so a localizer can rebuild the sentence in another language.
/// </param>
public sealed record PasswordRuleResult(
    PasswordRuleKind Kind,
    string Description,
    bool IsSatisfied,
    int Argument = 0
);


/// <summary>
/// What a password has to satisfy. The defaults follow passphrase-first guidance: length is the
/// requirement, breached values are refused, and the character-composition rules are all off.
/// </summary>
/// <remarks>
/// Composition rules ("must contain a symbol") push people towards <c>Passw0rd!</c>, which is short,
/// memorable to nobody and already in every wordlist. NIST SP 800-63B dropped them for exactly that
/// reason, and so do these defaults — turn them on only when an external policy forces your hand.
/// </remarks>
public sealed class PasswordStrengthRules
{
    /// <summary>Shortest acceptable password. Default 15.</summary>
    public int MinimumLength { get; set; } = 15;

    /// <summary>Require at least one A-Z. Off by default.</summary>
    public bool RequireUppercase { get; set; }

    /// <summary>Require at least one a-z. Off by default.</summary>
    public bool RequireLowercase { get; set; }

    /// <summary>Require at least one digit. Off by default.</summary>
    public bool RequireNumber { get; set; }

    /// <summary>Require at least one character from <see cref="SpecialCharacters"/>. Off by default.</summary>
    public bool RequireSpecialCharacter { get; set; }

    /// <summary>The printable ASCII symbols, plus the space.</summary>
    public const string DefaultSpecialCharacters = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~ ";

    /// <summary>
    /// Which characters count as special. Defaults to <see cref="DefaultSpecialCharacters"/>; narrow
    /// it when a downstream system rejects some of them.
    /// </summary>
    public string SpecialCharacters { get; set; } = DefaultSpecialCharacters;

    /// <summary>
    /// Refuse passwords that appear in the built-in list of the most commonly breached values,
    /// including the obvious dressings-up of them (<c>P@ssw0rd1!</c>). On by default.
    /// </summary>
    public bool RequireNotCompromisedPassword { get; set; } = true;

    /// <summary>Extra values to refuse — the product name, the company name, whatever else is banned.</summary>
    public IReadOnlyList<string>? BlockedPasswords { get; set; }

    /// <summary>
    /// Things about this particular user — email, username, display name. A password containing any
    /// of them is refused and scored as if the matched run were not there.
    /// </summary>
    public IReadOnlyList<string>? UserInputs { get; set; }
}


/// <summary>What an <see cref="IPasswordStrengthEvaluator"/> is asked to judge.</summary>
/// <param name="Password">The candidate. Never logged or persisted by anything in this package.</param>
/// <param name="Rules">The policy it is judged against.</param>
public sealed record PasswordStrengthRequest(string Password, PasswordStrengthRules Rules);


/// <summary>An evaluator's verdict.</summary>
public sealed class PasswordStrengthResult
{
    /// <summary>0-100. Drives the meter's fill.</summary>
    public required int Score { get; init; }

    /// <summary>The bucket <see cref="Score"/> falls in, and the label shown next to the meter.</summary>
    public required PasswordStrengthLevel Level { get; init; }

    /// <summary>Every rule that was asked for, in display order, each with its current state.</summary>
    public required IReadOnlyList<PasswordRuleResult> Rules { get; init; }

    /// <summary>
    /// True when every rule is satisfied — the value a form should gate its submit button on.
    /// A high <see cref="Score"/> alone is not enough: a 40-character passphrase still fails a
    /// policy that demands a digit.
    /// </summary>
    public bool IsAcceptable => this.Rules.All(x => x.IsSatisfied);

    /// <summary>Why the score is low, when there is a specific reason. Null when there is not.</summary>
    public string? Warning { get; init; }

    /// <summary>
    /// Which known sentence <see cref="Warning"/> is, so the control can hand it to its
    /// <c>Localizer</c> like every other string it paints. Null for free-form text from a custom
    /// evaluator, which is then shown exactly as written.
    /// </summary>
    public PasswordStrengthTextKey? WarningKey { get; init; }

    /// <summary>
    /// The word the warning is about, where there is one — the matched common password for
    /// <see cref="PasswordStrengthTextKey.WarningCommonPassword"/>, the matched detail for
    /// <see cref="PasswordStrengthTextKey.WarningUserInput"/>. Passed to the localizer as
    /// <see cref="PasswordStrengthText.Value"/>.
    /// </summary>
    public string? WarningValue { get; init; }

    /// <summary>Concrete things that would help, in priority order. May be empty.</summary>
    public IReadOnlyList<string> Suggestions { get; init; } = [];

    /// <summary>The verdict on an empty box: no score, no level, and every rule unsatisfied.</summary>
    public static PasswordStrengthResult Empty(IReadOnlyList<PasswordRuleResult> rules) => new()
    {
        Score = 0,
        Level = PasswordStrengthLevel.None,
        Rules = rules
    };
}


/// <summary>Every string the control paints, so all of them can be replaced together.</summary>
public enum PasswordStrengthTextKey
{
    RuleMinimumLength,
    RuleUppercase,
    RuleLowercase,
    RuleNumber,
    RuleSpecialCharacter,
    RuleNotCompromised,
    RuleNotBlocked,
    RuleNoUserInput,
    LevelWeak,
    LevelFair,
    LevelGood,
    LevelStrong,
    ShowPassword,
    HidePassword,

    /// <summary>"This is one of the most commonly used passwords." — the whole password is a known one.</summary>
    WarningCompromised,

    /// <summary>"This password is not allowed." — it is on the <c>BlockedPasswords</c> list.</summary>
    WarningBlocked,

    /// <summary>"This contains your own details, which an attacker already knows." <see cref="PasswordStrengthText.Value"/> is the matched detail.</summary>
    WarningUserInput,

    /// <summary><c>"pass" is a very common password.</c> <see cref="PasswordStrengthText.Value"/> is the matched word.</summary>
    WarningCommonPassword
}


/// <summary>One piece of text the control is about to show, with everything needed to translate it.</summary>
/// <param name="Key">Which string this is.</param>
/// <param name="Default">The English the control would show if nothing replaces it.</param>
/// <param name="Argument">
/// The number in the sentence, where there is one — the required length for
/// <see cref="PasswordStrengthTextKey.RuleMinimumLength"/>. Zero otherwise.
/// </param>
public sealed record PasswordStrengthText(PasswordStrengthTextKey Key, string Default, int Argument = 0)
{
    /// <summary>
    /// The word in the sentence, where there is one — the matched common password for
    /// <see cref="PasswordStrengthTextKey.WarningCommonPassword"/>. Null otherwise.
    /// </summary>
    public string? Value { get; init; }
}


/// <summary>
/// Replaces the control's wording. Return null (or the default) to leave a given string alone, so a
/// localizer only has to know about the strings it actually translates.
/// </summary>
public delegate string? PasswordStrengthLocalizer(PasswordStrengthText text);


/// <summary>How the meter is drawn.</summary>
public enum PasswordStrengthMeterStyle
{
    /// <summary>Four discrete blocks that light up one level at a time. The default.</summary>
    Segments,

    /// <summary>One continuous track filled to <see cref="PasswordStrengthResult.Score"/> percent.</summary>
    Bar
}
