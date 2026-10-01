using System.Collections.ObjectModel;
using System.Windows.Input;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

public partial class PasswordStrength
{
    static void Refresh(BindableObject b)
        => StyleGuard.WhenReady<PasswordStrength>(b, c => c.ApplyAppearance());

    static void Reevaluate(BindableObject b)
        => StyleGuard.WhenReady<PasswordStrength>(b, c => c.Schedule());

    // ---------------------------------------------------------------------------------------------
    // Value
    // ---------------------------------------------------------------------------------------------

    public static readonly BindableProperty PasswordProperty = BindableProperty.Create(
        nameof(Password), typeof(string), typeof(PasswordStrength), string.Empty,
        BindingMode.TwoWay,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<PasswordStrength>(b, c => c.OnPasswordChanged()));
    /// <summary>The password being typed. Two-way.</summary>
    public string Password
    {
        get => (string)GetValue(PasswordProperty);
        set => SetValue(PasswordProperty, value);
    }

    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder), typeof(string), typeof(PasswordStrength), "Password",
        propertyChanged: (b, _, _) => Refresh(b));
    /// <summary>Placeholder / floating label on the field.</summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public static readonly BindableProperty VariantProperty = BindableProperty.Create(
        nameof(Variant), typeof(TextEntryVariant), typeof(PasswordStrength), TextEntryVariant.Classic,
        propertyChanged: (b, _, _) => Refresh(b));
    /// <summary>Passed straight through to the underlying <see cref="TextEntry"/>.</summary>
    public TextEntryVariant Variant
    {
        get => (TextEntryVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    // ---------------------------------------------------------------------------------------------
    // Policy — see PasswordStrengthRules for why the composition rules default to off
    // ---------------------------------------------------------------------------------------------

    public static readonly BindableProperty MinimumLengthProperty = BindableProperty.Create(
        nameof(MinimumLength), typeof(int), typeof(PasswordStrength), 15,
        propertyChanged: (b, _, _) => Reevaluate(b));
    /// <summary>Shortest acceptable password. Default 15.</summary>
    public int MinimumLength
    {
        get => (int)GetValue(MinimumLengthProperty);
        set => SetValue(MinimumLengthProperty, value);
    }

    public static readonly BindableProperty RequireUppercaseProperty = BindableProperty.Create(
        nameof(RequireUppercase), typeof(bool), typeof(PasswordStrength), false,
        propertyChanged: (b, _, _) => Reevaluate(b));
    /// <summary>Require at least one A-Z.</summary>
    public bool RequireUppercase
    {
        get => (bool)GetValue(RequireUppercaseProperty);
        set => SetValue(RequireUppercaseProperty, value);
    }

    public static readonly BindableProperty RequireLowercaseProperty = BindableProperty.Create(
        nameof(RequireLowercase), typeof(bool), typeof(PasswordStrength), false,
        propertyChanged: (b, _, _) => Reevaluate(b));
    /// <summary>Require at least one a-z.</summary>
    public bool RequireLowercase
    {
        get => (bool)GetValue(RequireLowercaseProperty);
        set => SetValue(RequireLowercaseProperty, value);
    }

    public static readonly BindableProperty RequireNumberProperty = BindableProperty.Create(
        nameof(RequireNumber), typeof(bool), typeof(PasswordStrength), false,
        propertyChanged: (b, _, _) => Reevaluate(b));
    /// <summary>Require at least one digit.</summary>
    public bool RequireNumber
    {
        get => (bool)GetValue(RequireNumberProperty);
        set => SetValue(RequireNumberProperty, value);
    }

    public static readonly BindableProperty RequireSpecialCharacterProperty = BindableProperty.Create(
        nameof(RequireSpecialCharacter), typeof(bool), typeof(PasswordStrength), false,
        propertyChanged: (b, _, _) => Reevaluate(b));
    /// <summary>Require at least one character from <see cref="SpecialCharacters"/>.</summary>
    public bool RequireSpecialCharacter
    {
        get => (bool)GetValue(RequireSpecialCharacterProperty);
        set => SetValue(RequireSpecialCharacterProperty, value);
    }

    public static readonly BindableProperty SpecialCharactersProperty = BindableProperty.Create(
        nameof(SpecialCharacters), typeof(string), typeof(PasswordStrength), PasswordStrengthRules.DefaultSpecialCharacters,
        propertyChanged: (b, _, _) => Reevaluate(b));
    /// <summary>Which characters count as special. Defaults to the printable ASCII symbols.</summary>
    public string SpecialCharacters
    {
        get => (string)GetValue(SpecialCharactersProperty);
        set => SetValue(SpecialCharactersProperty, value);
    }

    public static readonly BindableProperty RequireNotCompromisedPasswordProperty = BindableProperty.Create(
        nameof(RequireNotCompromisedPassword), typeof(bool), typeof(PasswordStrength), true,
        propertyChanged: (b, _, _) => Reevaluate(b));
    /// <summary>Refuse the commonly breached values and their obvious dressings-up. On by default.</summary>
    public bool RequireNotCompromisedPassword
    {
        get => (bool)GetValue(RequireNotCompromisedPasswordProperty);
        set => SetValue(RequireNotCompromisedPasswordProperty, value);
    }

    public static readonly BindableProperty BlockedPasswordsProperty = BindableProperty.Create(
        nameof(BlockedPasswords), typeof(IList<string>), typeof(PasswordStrength), null,
        propertyChanged: (b, _, _) => Reevaluate(b));
    /// <summary>Extra values to refuse — the product name, the company name, whatever else is banned.</summary>
    public IList<string>? BlockedPasswords
    {
        get => (IList<string>?)GetValue(BlockedPasswordsProperty);
        set => SetValue(BlockedPasswordsProperty, value);
    }

    public static readonly BindableProperty UserInputsProperty = BindableProperty.Create(
        nameof(UserInputs), typeof(IList<string>), typeof(PasswordStrength), null,
        propertyChanged: (b, _, _) => Reevaluate(b));
    /// <summary>
    /// This user's own details — email, username, display name. A password containing any of them is
    /// refused, and scored as if the matched run were not there.
    /// </summary>
    public IList<string>? UserInputs
    {
        get => (IList<string>?)GetValue(UserInputsProperty);
        set => SetValue(UserInputsProperty, value);
    }

    public static readonly BindableProperty MinimumTimeToCrackProperty = BindableProperty.Create(
        nameof(MinimumTimeToCrack), typeof(TimeSpan), typeof(PasswordStrength), TimeSpan.Zero,
        propertyChanged: (b, _, _) => Reevaluate(b));
    /// <summary>
    /// The least time the password must hold out against <see cref="GuessesPerSecond"/> before it is
    /// acceptable — <c>36525.00:00:00</c> in XAML for a century. Zero (the default) turns the rule off.
    /// </summary>
    public TimeSpan MinimumTimeToCrack
    {
        get => (TimeSpan)GetValue(MinimumTimeToCrackProperty);
        set => SetValue(MinimumTimeToCrackProperty, value);
    }

    public static readonly BindableProperty GuessesPerSecondProperty = BindableProperty.Create(
        nameof(GuessesPerSecond), typeof(double), typeof(PasswordStrength), PasswordStrengthRules.DefaultGuessesPerSecond,
        propertyChanged: (b, _, _) => Reevaluate(b));
    /// <summary>
    /// The attacker the time to crack is estimated against. Default 10 billion a second — offline,
    /// fast hash. See <see cref="PasswordStrengthRules.GuessesPerSecond"/>.
    /// </summary>
    public double GuessesPerSecond
    {
        get => (double)GetValue(GuessesPerSecondProperty);
        set => SetValue(GuessesPerSecondProperty, value);
    }

    // ---------------------------------------------------------------------------------------------
    // Scoring
    // ---------------------------------------------------------------------------------------------

    public static readonly BindableProperty EvaluatorProperty = BindableProperty.Create(
        nameof(Evaluator), typeof(IPasswordStrengthEvaluator), typeof(PasswordStrength), null,
        propertyChanged: (b, _, _) => Reevaluate(b));
    /// <summary>
    /// The scorer. Null resolves <see cref="IPasswordStrengthEvaluator"/> from the app's service
    /// provider, and falls back to <see cref="DefaultPasswordStrengthEvaluator"/> when nothing is
    /// registered — so this is only for the one-off case of a single field wanting its own scorer.
    /// </summary>
    public IPasswordStrengthEvaluator? Evaluator
    {
        get => (IPasswordStrengthEvaluator?)GetValue(EvaluatorProperty);
        set => SetValue(EvaluatorProperty, value);
    }

    public static readonly BindableProperty DebounceMillisecondsProperty = BindableProperty.Create(
        nameof(DebounceMilliseconds), typeof(int), typeof(PasswordStrength), 250);
    /// <summary>
    /// How long typing has to pause before the password is scored. Keeps a network-backed evaluator
    /// off the wire for every keystroke; set 0 to score on each one.
    /// </summary>
    public int DebounceMilliseconds
    {
        get => (int)GetValue(DebounceMillisecondsProperty);
        set => SetValue(DebounceMillisecondsProperty, value);
    }

    public static readonly BindableProperty LocalizerProperty = BindableProperty.Create(
        nameof(Localizer), typeof(PasswordStrengthLocalizer), typeof(PasswordStrength), null,
        propertyChanged: (b, _, _) => Refresh(b));
    /// <summary>Replaces the control's wording. Return null from it to keep a given default.</summary>
    public PasswordStrengthLocalizer? Localizer
    {
        get => (PasswordStrengthLocalizer?)GetValue(LocalizerProperty);
        set => SetValue(LocalizerProperty, value);
    }

    // ---------------------------------------------------------------------------------------------
    // Results — bind a submit button to IsAcceptable
    // ---------------------------------------------------------------------------------------------

    public static readonly BindableProperty ScoreProperty = BindableProperty.Create(
        nameof(Score), typeof(int), typeof(PasswordStrength), 0, BindingMode.OneWayToSource);
    /// <summary>0-100, as last reported by the evaluator. Read-only in practice.</summary>
    public int Score
    {
        get => (int)GetValue(ScoreProperty);
        private set => SetValue(ScoreProperty, value);
    }

    public static readonly BindableProperty LevelProperty = BindableProperty.Create(
        nameof(Level), typeof(PasswordStrengthLevel), typeof(PasswordStrength),
        PasswordStrengthLevel.None, BindingMode.OneWayToSource);
    /// <summary>The bucket <see cref="Score"/> falls in. Read-only in practice.</summary>
    public PasswordStrengthLevel Level
    {
        get => (PasswordStrengthLevel)GetValue(LevelProperty);
        private set => SetValue(LevelProperty, value);
    }

    public static readonly BindableProperty IsAcceptableProperty = BindableProperty.Create(
        nameof(IsAcceptable), typeof(bool), typeof(PasswordStrength), false, BindingMode.OneWayToSource);
    /// <summary>
    /// True when every rule is met — the property a submit button's <c>IsEnabled</c> should bind to.
    /// A high <see cref="Score"/> is not the same thing: a long passphrase still fails a policy that
    /// demands a digit.
    /// </summary>
    public bool IsAcceptable
    {
        get => (bool)GetValue(IsAcceptableProperty);
        private set => SetValue(IsAcceptableProperty, value);
    }

    public static readonly BindableProperty ResultProperty = BindableProperty.Create(
        nameof(Result), typeof(PasswordStrengthResult), typeof(PasswordStrength), null,
        BindingMode.OneWayToSource);
    /// <summary>The evaluator's full verdict, including the rule checklist and any suggestions.</summary>
    public PasswordStrengthResult? Result
    {
        get => (PasswordStrengthResult?)GetValue(ResultProperty);
        private set => SetValue(ResultProperty, value);
    }

    public static readonly BindableProperty StrengthChangedCommandProperty = BindableProperty.Create(
        nameof(StrengthChangedCommand), typeof(ICommand), typeof(PasswordStrength), null);
    /// <summary>Invoked with the <see cref="PasswordStrengthResult"/> each time the score changes.</summary>
    public ICommand? StrengthChangedCommand
    {
        get => (ICommand?)GetValue(StrengthChangedCommandProperty);
        set => SetValue(StrengthChangedCommandProperty, value);
    }

    // ---------------------------------------------------------------------------------------------
    // What is shown
    // ---------------------------------------------------------------------------------------------

    public static readonly BindableProperty ShowVisibilityToggleProperty = BindableProperty.Create(
        nameof(ShowVisibilityToggle), typeof(bool), typeof(PasswordStrength), true,
        propertyChanged: (b, _, _) => Refresh(b));
    /// <summary>The eye button that reveals what has been typed.</summary>
    public bool ShowVisibilityToggle
    {
        get => (bool)GetValue(ShowVisibilityToggleProperty);
        set => SetValue(ShowVisibilityToggleProperty, value);
    }

    public static readonly BindableProperty ShowPasswordIconProperty = BindableProperty.Create(
        nameof(ShowPasswordIcon), typeof(ImageSource), typeof(PasswordStrength), null,
        propertyChanged: (b, _, _) => Refresh(b));
    /// <summary>
    /// Icon for the toggle while the password is hidden. Null falls back to the word "Show", which
    /// is the one thing that renders identically on every platform and reads correctly to a screen
    /// reader; supply a <c>FontImageSource</c> from your icon font to replace it.
    /// </summary>
    public ImageSource? ShowPasswordIcon
    {
        get => (ImageSource?)GetValue(ShowPasswordIconProperty);
        set => SetValue(ShowPasswordIconProperty, value);
    }

    public static readonly BindableProperty HidePasswordIconProperty = BindableProperty.Create(
        nameof(HidePasswordIcon), typeof(ImageSource), typeof(PasswordStrength), null,
        propertyChanged: (b, _, _) => Refresh(b));
    /// <summary>Icon for the toggle while the password is revealed. Null falls back to "Hide".</summary>
    public ImageSource? HidePasswordIcon
    {
        get => (ImageSource?)GetValue(HidePasswordIconProperty);
        set => SetValue(HidePasswordIconProperty, value);
    }

    public static readonly BindableProperty ShowMeterProperty = BindableProperty.Create(
        nameof(ShowMeter), typeof(bool), typeof(PasswordStrength), true,
        propertyChanged: (b, _, _) => Refresh(b));
    /// <summary>The strength meter under the field.</summary>
    public bool ShowMeter
    {
        get => (bool)GetValue(ShowMeterProperty);
        set => SetValue(ShowMeterProperty, value);
    }

    public static readonly BindableProperty ShowStrengthLabelProperty = BindableProperty.Create(
        nameof(ShowStrengthLabel), typeof(bool), typeof(PasswordStrength), true,
        propertyChanged: (b, _, _) => Refresh(b));
    /// <summary>The Weak/Fair/Good/Strong caption beside the meter.</summary>
    public bool ShowStrengthLabel
    {
        get => (bool)GetValue(ShowStrengthLabelProperty);
        set => SetValue(ShowStrengthLabelProperty, value);
    }

    public static readonly BindableProperty ShowRulesProperty = BindableProperty.Create(
        nameof(ShowRules), typeof(bool), typeof(PasswordStrength), true,
        propertyChanged: (b, _, _) => Refresh(b));
    /// <summary>The checklist of rules and whether each is met.</summary>
    public bool ShowRules
    {
        get => (bool)GetValue(ShowRulesProperty);
        set => SetValue(ShowRulesProperty, value);
    }

    public static readonly BindableProperty ShowWarningProperty = BindableProperty.Create(
        nameof(ShowWarning), typeof(bool), typeof(PasswordStrength), true,
        propertyChanged: (b, _, _) => Refresh(b));
    /// <summary>
    /// Whether the evaluator's warning ("this is one of the most commonly used passwords") is shown
    /// as the field's hint text.
    /// </summary>
    public bool ShowWarning
    {
        get => (bool)GetValue(ShowWarningProperty);
        set => SetValue(ShowWarningProperty, value);
    }

    public static readonly BindableProperty ShowTimeToCrackProperty = BindableProperty.Create(
        nameof(ShowTimeToCrack), typeof(bool), typeof(PasswordStrength), false,
        propertyChanged: (b, _, _) => Refresh(b));
    /// <summary>
    /// A "Time to crack: 21 days" caption under the meter. Off by default; shown only while there is
    /// a password and the evaluator estimates one.
    /// </summary>
    public bool ShowTimeToCrack
    {
        get => (bool)GetValue(ShowTimeToCrackProperty);
        set => SetValue(ShowTimeToCrackProperty, value);
    }

    // ---------------------------------------------------------------------------------------------
    // Appearance. Null colours follow the theme, so a theme swap restyles the meter live.
    // ---------------------------------------------------------------------------------------------

    public static readonly BindableProperty MeterStyleProperty = BindableProperty.Create(
        nameof(MeterStyle), typeof(PasswordStrengthMeterStyle), typeof(PasswordStrength),
        PasswordStrengthMeterStyle.Segments,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<PasswordStrength>(b, c => c.RebuildMeter()));
    /// <summary>Four discrete blocks (default) or one continuous bar filled to the score.</summary>
    public PasswordStrengthMeterStyle MeterStyle
    {
        get => (PasswordStrengthMeterStyle)GetValue(MeterStyleProperty);
        set => SetValue(MeterStyleProperty, value);
    }

    public static readonly BindableProperty MeterHeightProperty = BindableProperty.Create(
        nameof(MeterHeight), typeof(double), typeof(PasswordStrength), 6d,
        propertyChanged: (b, _, _) => Refresh(b));
    /// <summary>Meter thickness.</summary>
    public double MeterHeight
    {
        get => (double)GetValue(MeterHeightProperty);
        set => SetValue(MeterHeightProperty, value);
    }

    public static readonly BindableProperty MeterCornerRadiusProperty = BindableProperty.Create(
        nameof(MeterCornerRadius), typeof(double), typeof(PasswordStrength), 3d,
        propertyChanged: (b, _, _) => Refresh(b));
    /// <summary>Meter corner radius.</summary>
    public double MeterCornerRadius
    {
        get => (double)GetValue(MeterCornerRadiusProperty);
        set => SetValue(MeterCornerRadiusProperty, value);
    }

    public static readonly BindableProperty SegmentSpacingProperty = BindableProperty.Create(
        nameof(SegmentSpacing), typeof(double), typeof(PasswordStrength), 4d,
        propertyChanged: (b, _, _) => Refresh(b));
    /// <summary>Gap between segments. Ignored when <see cref="MeterStyle"/> is Bar.</summary>
    public double SegmentSpacing
    {
        get => (double)GetValue(SegmentSpacingProperty);
        set => SetValue(SegmentSpacingProperty, value);
    }

    public static readonly BindableProperty TrackColorProperty = BindableProperty.Create(
        nameof(TrackColor), typeof(Color), typeof(PasswordStrength), null,
        propertyChanged: (b, _, _) => Refresh(b));
    /// <summary>Unfilled meter colour. Null follows the SurfaceContainerHighest token.</summary>
    public Color? TrackColor
    {
        get => (Color?)GetValue(TrackColorProperty);
        set => SetValue(TrackColorProperty, value);
    }

    public static readonly BindableProperty WeakColorProperty = BindableProperty.Create(
        nameof(WeakColor), typeof(Color), typeof(PasswordStrength), null,
        propertyChanged: (b, _, _) => Refresh(b));
    /// <summary>Null follows the Critical token.</summary>
    public Color? WeakColor
    {
        get => (Color?)GetValue(WeakColorProperty);
        set => SetValue(WeakColorProperty, value);
    }

    public static readonly BindableProperty FairColorProperty = BindableProperty.Create(
        nameof(FairColor), typeof(Color), typeof(PasswordStrength), null,
        propertyChanged: (b, _, _) => Refresh(b));
    /// <summary>Null follows the Caution token.</summary>
    public Color? FairColor
    {
        get => (Color?)GetValue(FairColorProperty);
        set => SetValue(FairColorProperty, value);
    }

    public static readonly BindableProperty GoodColorProperty = BindableProperty.Create(
        nameof(GoodColor), typeof(Color), typeof(PasswordStrength), null,
        propertyChanged: (b, _, _) => Refresh(b));
    /// <summary>Null follows the Warning token.</summary>
    public Color? GoodColor
    {
        get => (Color?)GetValue(GoodColorProperty);
        set => SetValue(GoodColorProperty, value);
    }

    public static readonly BindableProperty StrongColorProperty = BindableProperty.Create(
        nameof(StrongColor), typeof(Color), typeof(PasswordStrength), null,
        propertyChanged: (b, _, _) => Refresh(b));
    /// <summary>Null follows the Success token.</summary>
    public Color? StrongColor
    {
        get => (Color?)GetValue(StrongColorProperty);
        set => SetValue(StrongColorProperty, value);
    }

    public static readonly BindableProperty RuleTextColorProperty = BindableProperty.Create(
        nameof(RuleTextColor), typeof(Color), typeof(PasswordStrength), null,
        propertyChanged: (b, _, _) => Refresh(b));
    /// <summary>Colour of an unsatisfied checklist row. Null follows the OnSurfaceVariant token.</summary>
    public Color? RuleTextColor
    {
        get => (Color?)GetValue(RuleTextColorProperty);
        set => SetValue(RuleTextColorProperty, value);
    }

    public static readonly BindableProperty RuleFontSizeProperty = BindableProperty.Create(
        nameof(RuleFontSize), typeof(double), typeof(PasswordStrength), 13d,
        propertyChanged: (b, _, _) => Refresh(b));
    /// <summary>Checklist font size.</summary>
    public double RuleFontSize
    {
        get => (double)GetValue(RuleFontSizeProperty);
        set => SetValue(RuleFontSizeProperty, value);
    }
}
