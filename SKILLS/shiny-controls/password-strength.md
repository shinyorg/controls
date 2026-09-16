# PasswordStrength

A password field with a live strength meter and a rule checklist underneath it. Available on both
MAUI and Blazor, built on top of [TextEntry](./textentry.md).

Three things to know before generating code:

- **Bind the submit button to `IsAcceptable`, never to `Score`.** The score says how hard the
  password is to crack; only `IsAcceptable` says whether it satisfies the policy. They genuinely
  disagree — a forty-character passphrase scores 100 and still fails a rule demanding a digit.
- **The composition rules are off by default and should stay off.** `MinimumLength` is 15,
  `RequireNotCompromisedPassword` is true, and `RequireUppercase`/`RequireLowercase`/`RequireNumber`/
  `RequireSpecialCharacter` are all false. That is NIST SP 800-63B guidance, not an oversight: a
  "must contain a symbol" rule produces `Passw0rd!`. Turn them on only when the user says an external
  policy requires it.
- **Scoring is pluggable and asynchronous.** `IPasswordStrengthEvaluator` exists so zxcvbn or a Have
  I Been Pwned range query can replace the built-in heuristic. Do not generate an implementation that
  sends the password (or its full hash) anywhere.

## MAUI

**Namespace**: `Shiny.Maui.Controls`
**XAML Namespace**: `http://shiny.net/maui/controls` (prefix: `shiny`)

### Basic Usage

```xml
<shiny:PasswordStrength Placeholder="Passphrase"
                        Variant="Floating"
                        Password="{Binding Passphrase, Mode=TwoWay}"
                        IsAcceptable="{Binding CanSubmit}"
                        StrengthChanged="OnStrengthChanged" />

<Button Text="Create account"
        Command="{Binding SubmitCommand}"
        IsEnabled="{Binding CanSubmit}" />
```

`Score`, `Level`, `IsAcceptable` and `Result` default to `BindingMode.OneWayToSource` — bind them to
view-model properties to read them; do not try to push values into them.

### Properties

| Property | Type | Default | Description |
|---|---|---|---|
| Password | string | "" | Current value (TwoWay) |
| Placeholder | string | "Password" | Placeholder / floating label |
| Variant | TextEntryVariant | Classic | `Classic` or `Floating`, passed to the inner TextEntry |
| MinimumLength | int | 15 | Shortest acceptable password |
| RequireUppercase | bool | false | Require an A-Z |
| RequireLowercase | bool | false | Require an a-z |
| RequireNumber | bool | false | Require a digit |
| RequireSpecialCharacter | bool | false | Require a character from `SpecialCharacters` |
| SpecialCharacters | string | printable ASCII symbols + space | What counts as special |
| RequireNotCompromisedPassword | bool | true | Refuse the commonly breached values and their disguises |
| BlockedPasswords | IList\<string\>? | null | Extra values to refuse outright |
| UserInputs | IList\<string\>? | null | This user's email / name — refused, and discounted when scoring |
| Evaluator | IPasswordStrengthEvaluator? | null | Per-field scorer override; null resolves DI then the built-in |
| DebounceMilliseconds | int | 250 | Pause before scoring; 0 scores every keystroke |
| Localizer | PasswordStrengthLocalizer? | null | Replaces the wording (levels, checklist, Show/Hide, built-in warnings); return null to keep a default |
| MeterStyle | PasswordStrengthMeterStyle | Segments | `Segments` (four blocks) or `Bar` (filled to the score) |
| MeterHeight | double | 6 | Meter thickness |
| MeterCornerRadius | double | 3 | Meter corner radius |
| SegmentSpacing | double | 4 | Gap between segments; ignored when `Bar` |
| TrackColor | Color? | null | Unfilled meter; null follows SurfaceContainerHighest |
| WeakColor / FairColor / GoodColor / StrongColor | Color? | null | Null follows Critical / Caution / Warning / Success |
| RuleTextColor | Color? | null | Unsatisfied checklist row; null follows OnSurfaceVariant |
| RuleFontSize | double | 13 | Checklist font size |
| ShowMeter | bool | true | Draw the meter |
| ShowStrengthLabel | bool | true | Draw the Weak/Fair/Good/Strong caption |
| ShowRules | bool | true | Draw the checklist |
| ShowWarning | bool | true | Surface the evaluator's warning as the field's hint text |
| ShowVisibilityToggle | bool | true | The Show/Hide button |
| ShowPasswordIcon | ImageSource? | null | Toggle icon while hidden; null uses the word "Show" |
| HidePasswordIcon | ImageSource? | null | Toggle icon while revealed; null uses the word "Hide" |
| Score | int | 0 | 0-100 (OneWayToSource) |
| Level | PasswordStrengthLevel | None | None/Weak/Fair/Good/Strong (OneWayToSource) |
| IsAcceptable | bool | false | Every rule met (OneWayToSource) |
| Result | PasswordStrengthResult? | null | Full verdict — rules, warning, suggestions (OneWayToSource) |
| StrengthChangedCommand | ICommand? | null | Invoked with the `PasswordStrengthResult` |

### Events & Methods

| Member | Description |
|---|---|
| `StrengthChanged` | `PasswordStrengthChangedEventArgs(Result, Score, Level, IsAcceptable)`, raised when the verdict changes |
| `Completed` | Return key pressed |
| `EvaluateNowAsync()` | Score immediately, bypassing the debounce |
| `IsPasswordRevealed` | Get/set the reveal state from code |
| `Focus()` / `Unfocus()` | Field focus |

## Blazor

**Namespace**: `Shiny.Blazor.Controls`

```razor
<PasswordStrength @ref="field"
                  @bind-Password="passphrase"
                  Placeholder="Passphrase"
                  StrengthChanged="OnStrengthChanged" />

<button disabled="@(field?.IsAcceptable != true)">Create account</button>

@code {
    PasswordStrength? field;
    string passphrase = "";

    void OnStrengthChanged(PasswordStrengthChangedEventArgs e) { }
}
```

Same parameter names as MAUI, with these differences:

| Difference | Blazor form |
|---|---|
| Colours | CSS colour strings (`WeakColor="#B91C1C"`), not `Color` |
| `MeterCornerRadius` | A CSS length string (`"3px"`), not a double |
| Lists | `IReadOnlyList<string>` for `BlockedPasswords` / `UserInputs` |
| Toggle icons | `ShowPasswordIcon` / `HidePasswordIcon` are `string?` |
| Results | `Score`, `Level`, `IsAcceptable`, `Result` are plain read-only properties on the component — reach them through `@ref`, not through binding |
| Reveal | `SetPasswordRevealed(bool)`; `IsPasswordRevealed` is read-only |
| `CssClass` | Extra classes on the host element |

## Pluggable scoring

```csharp
public interface IPasswordStrengthEvaluator
{
    ValueTask<PasswordStrengthResult> EvaluateAsync(
        PasswordStrengthRequest request,
        CancellationToken cancellationToken = default
    );
}
```

Register one app-wide:

```csharp
// MAUI
builder.UseShinyControls(x => x.SetCustomPasswordStrengthEvaluator<HibpEvaluator>());

// Blazor
services.AddShinyControls(x => x.SetCustomPasswordStrengthEvaluator<HibpEvaluator>());
```

Resolution order is `Evaluator` → DI → `DefaultPasswordStrengthEvaluator.Instance`. Keystrokes are
debounced and the previous evaluation is cancelled before the next starts, so an implementation that
does I/O is workable — it must pass the `CancellationToken` through. If a custom evaluator throws,
the built-in one answers instead, so losing the network downgrades the meter rather than freezing it.

**Never generate an evaluator that transmits the password or its full hash.** The reason HIBP's range
API takes the first five characters of the SHA-1 hash and returns a bucket of suffixes is so neither
ever leaves the device.

### The built-in heuristic

`DefaultPasswordStrengthEvaluator` estimates entropy as collapsed-length × log2(character pool), then
maps it to 0-100 where 80 bits scores 100. "Collapsed" means the characters a cracker gets free are
removed first: runs of the same character, ascending or descending sequences, and everything past the
first repetition of a repeated block. Any word from the built-in commonly-breached list is charged a
flat 11 bits instead of its length, and a password that *is* one of those values — seen through case,
leet substitution and a bolted-on year — is scored as if it were the bare word. It needs no network
and no data files.

`CommonPasswords.IsCompromised(string)` and `CommonPasswords.FindLongestMatch(string)` are public, so
a custom evaluator can reuse the list.

## Types

| Type | Purpose |
|---|---|
| `PasswordStrengthLevel` | `None`, `Weak`, `Fair`, `Good`, `Strong` |
| `PasswordStrengthMeterStyle` | `Segments`, `Bar` |
| `PasswordRuleKind` | `MinimumLength`, `Uppercase`, `Lowercase`, `Number`, `SpecialCharacter`, `NotCompromised`, `NotBlocked`, `NoUserInput` |
| `PasswordRuleResult` | `Kind`, `Description`, `IsSatisfied`, `Argument` (the required length, for the length rule) |
| `PasswordStrengthRules` | The policy an evaluator is handed |
| `PasswordStrengthRequest` | `Password` + `Rules` |
| `PasswordStrengthResult` | `Score`, `Level`, `Rules`, `IsAcceptable`, `Warning`, `WarningKey`, `WarningValue`, `Suggestions` |
| `PasswordStrengthTextKey` | Every string the control paints |
| `PasswordStrengthText` | `Key`, `Default`, `Argument`, `Value` — what the localizer is handed (`Value` = the matched word for `WarningCommonPassword` / `WarningUserInput`) |
| `PasswordStrengthLocalizer` | `string? (PasswordStrengthText)`; return null to keep the default |

## Localization

```csharp
control.Localizer = text => text.Key switch
{
    PasswordStrengthTextKey.LevelWeak => "Faible",
    PasswordStrengthTextKey.LevelStrong => "Fort",
    // Argument carries the number, so the sentence can be rebuilt rather than patched
    PasswordStrengthTextKey.RuleMinimumLength => $"Au moins {text.Argument} caractères",
    // The warning under the field goes through the localizer too; Value is the matched word
    PasswordStrengthTextKey.WarningCommonPassword => $"« {text.Value} » est un mot de passe très courant.",
    PasswordStrengthTextKey.WarningCompromised => "C'est l'un des mots de passe les plus utilisés.",
    _ => null // anything not translated keeps the default
};
```

The warning keys are `WarningCompromised`, `WarningBlocked`, `WarningUserInput`, `WarningCommonPassword`.
The built-in evaluator sets `PasswordStrengthResult.WarningKey`/`WarningValue`; a custom evaluator that
wants its warning localized by the control must set them too — a `Warning` with no `WarningKey` is
shown verbatim. `Suggestions` are never painted by the control, so they are not localized.

## Don't

- Don't gate a submit button on `Score >= 80`. Use `IsAcceptable`.
- Don't turn on the composition rules "to be safe" — the defaults are the safe ones.
- Don't set `DebounceMilliseconds="0"` with a network-backed evaluator.
- Don't reach for `SecurityPin` when the user asks for a password field — that one is a PIN/OTP
  control with fixed-length cells. See [security-pin.md](./security-pin.md).
