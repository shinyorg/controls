# PasswordStrength

[← All Shiny Controls](../../README.md)

A password field with a live strength meter and a rule checklist underneath it, on both hosts. The
defaults follow passphrase-first guidance: fifteen characters, commonly breached values refused, and
**no character-composition rules at all** — those push people towards `Passw0rd!`, which is short,
memorable to nobody and already in every wordlist. Turn them on only when an external policy forces
your hand.

<!-- TODO: capture screenshots for passwordstrength -->

**MAUI**

```xml
<shiny:PasswordStrength Placeholder="Passphrase"
                        Variant="Floating"
                        Password="{Binding Passphrase}"
                        IsAcceptable="{Binding CanSubmit}"
                        StrengthChanged="OnStrengthChanged" />

<Button Text="Create account" IsEnabled="{Binding CanSubmit}" />
```

**Blazor**

```razor
<PasswordStrength @ref="field" @bind-Password="passphrase" Placeholder="Passphrase" />
<button disabled="@(field?.IsAcceptable != true)">Create account</button>
```

Bind your submit button to **`IsAcceptable`**, not to `Score`. The score says how hard the password
is to crack; only `IsAcceptable` says whether it satisfies the policy, and the two genuinely
disagree — a forty-character passphrase scores 100 and still fails a rule demanding a digit.

| Property | Type | Default | Description |
|---|---|---|---|
| Password | string | "" | The value being typed (TwoWay / `@bind-Password`) |
| Placeholder | string | "Password" | Placeholder / floating label |
| Variant | TextEntryVariant | Classic | Passed through to the underlying TextEntry |
| MinimumLength | int | 15 | Shortest acceptable password |
| RequireUppercase / RequireLowercase / RequireNumber / RequireSpecialCharacter | bool | false | Composition rules — off by design |
| SpecialCharacters | string | printable ASCII symbols | What counts as special |
| RequireNotCompromisedPassword | bool | true | Refuse the commonly breached values and their disguises |
| BlockedPasswords | IList&lt;string&gt;? | null | Extra values to refuse |
| UserInputs | IList&lt;string&gt;? | null | This user's email / name — refused, and discounted when scoring |
| MinimumTimeToCrack | TimeSpan | Zero (off) | Least time the password must resist `GuessesPerSecond`; adds a checklist rule |
| GuessesPerSecond | double | 1e10 | The attacker the time to crack is estimated against |
| Evaluator | IPasswordStrengthEvaluator? | null | Per-field scorer override |
| DebounceMilliseconds | int | 250 | Pause before scoring; 0 scores every keystroke |
| Localizer | PasswordStrengthLocalizer? | null | Replaces the wording — level labels, checklist, Show/Hide, and the built-in warnings; return null to keep a default |
| MeterStyle | PasswordStrengthMeterStyle | Segments | Four blocks, or one bar filled to the score |
| MeterHeight / MeterCornerRadius / SegmentSpacing | double | 6 / 3 / 4 | Meter geometry |
| TrackColor / WeakColor / FairColor / GoodColor / StrongColor | Color? | null | Null follows the surface-container-highest / critical / caution / warning / success tokens |
| RuleTextColor / RuleFontSize | Color? / double | null / 13 | Checklist appearance |
| ShowMeter / ShowStrengthLabel / ShowRules / ShowWarning / ShowVisibilityToggle | bool | true | What is drawn |
| ShowTimeToCrack | bool | false | A "Time to crack: 21 days" caption under the meter |
| ShowPasswordIcon / HidePasswordIcon | ImageSource? (MAUI) / string? (Blazor) | null | Toggle content; null uses the words "Show" / "Hide" |
| Score | int | 0 | 0-100, read-only |
| Level | PasswordStrengthLevel | None | None / Weak / Fair / Good / Strong, read-only |
| IsAcceptable | bool | false | Every rule met, read-only |
| Result | PasswordStrengthResult? | null | The full verdict — rules, warning, suggestions |
**Time to crack.** The built-in evaluator turns its entropy estimate into
`PasswordStrengthResult.TimeToCrackSeconds` (and `TimeToCrack`, a `TimeSpan` that saturates at about
29,000 years) — the average time for an attacker making `GuessesPerSecond` guesses to find it.
`ShowTimeToCrack` paints it under the meter. `MinimumTimeToCrack` makes it policy: the password is
not acceptable until the estimate reaches it, so a password can clear the length rule and still be
refused because it would fall in 21 days when the floor is a century.

```xml
<shiny:PasswordStrength MinimumLength="8"
                        MinimumTimeToCrack="36525.00:00:00"
                        ShowTimeToCrack="True"
                        IsAcceptable="{Binding CanSubmit}" />
```

```razor
<PasswordStrength @bind-Password="password"
                  MinimumLength="8"
                  MinimumTimeToCrack="TimeSpan.FromDays(36525)"
                  ShowTimeToCrack="true" />
```

The default rate, 10 billion a second, is an offline attack on a fast hash. Lower it to model what
you actually store — around 10,000 for bcrypt/Argon2, around 10 for a rate-limited login form — and
the same password survives far longer. `PasswordCrackTime.Describe(seconds)` returns the unit, count
and English wording ("21 days") if you want to show it yourself.

Events: `StrengthChanged` (`PasswordStrengthChangedEventArgs`) fires when the verdict changes;
`Completed` fires on the return key. MAUI also has `StrengthChangedCommand`.

Methods: `EvaluateNowAsync()` bypasses the debounce — call it after mutating a `UserInputs` or
`BlockedPasswords` list in place. MAUI adds `Focus()` / `Unfocus()`.

**Pluggable scoring.** The built-in `DefaultPasswordStrengthEvaluator` estimates entropy after
discounting what a cracker gets free — repeats, sequences, repeated blocks, and any word from the
built-in list of commonly breached passwords (seen through case, leet substitution and a bolted-on
year). It needs no network and no data files. Replace it with zxcvbn, a Have I Been Pwned range
query, or your own policy endpoint:

```csharp
public class HibpEvaluator : IPasswordStrengthEvaluator
{
    public async ValueTask<PasswordStrengthResult> EvaluateAsync(
        PasswordStrengthRequest request,
        CancellationToken cancellationToken = default
    )
    {
        // ... hash locally, send only the first five hex characters ...
    }
}

// MAUI
builder.UseShinyControls(x => x.SetCustomPasswordStrengthEvaluator<HibpEvaluator>());

// Blazor
services.AddShinyControls(x => x.SetCustomPasswordStrengthEvaluator<HibpEvaluator>());
```

The interface is asynchronous and cancellable precisely so a network-backed implementation is
possible: keystrokes are debounced and the previous evaluation is cancelled before the next starts.
If a custom evaluator throws, the built-in one answers instead, so losing the network downgrades the
meter rather than freezing it.

**Localized wording.** `Localizer` is handed a `PasswordStrengthText` (`Key`, `Default`, `Argument`,
`Value`) for every string the control paints, and returns the replacement or null to keep the default.
That includes the warning shown under the field: the built-in evaluator tags each one with
`PasswordStrengthResult.WarningKey` (`WarningCompromised`, `WarningBlocked`, `WarningUserInput`,
`WarningCommonPassword`), and `Value` carries the word it is about — so
`$"« {text.Value} » est un mot de passe très courant."` translates `"pass" is a very common password`.
Durations are localized one unit at a time — `DurationSeconds` … `DurationCenturies` with the count in
`Argument`, plus `DurationInstant` and `DurationEons` — and the finished phrase then arrives as `Value`
for `TimeToCrack` ("Time to crack: …") and `RuleMinimumTimeToCrack` ("At least … to crack").
A custom evaluator's warning with no `WarningKey` is shown exactly as written; translate it in the
evaluator. `Suggestions` are not painted by the control and stay as the evaluator wrote them.

**Never send the password itself anywhere.** HIBP's range API takes the first five characters of the
SHA-1 hash and returns a bucket of suffixes exactly so the password — and its full hash — never
leaves the device.
