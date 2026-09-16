using System.Text;

namespace Shiny.Blazor.Controls;

/// <summary>
/// The built-in scorer. Estimates how many bits of entropy the password really carries — after
/// discounting the parts a cracker gets for free — and reports the rule checklist alongside it.
/// </summary>
/// <remarks>
/// <para>
/// The model is deliberately length-first. A pool-size-times-length calculation would rate
/// <c>P@ss1!</c> above <c>the slow red barn</c>, which is backwards; both the run collapsing and the
/// known-word discount below exist to stop that happening. It is a heuristic, not a cracking
/// simulation — for the real thing, plug zxcvbn or an HIBP range query in through
/// <see cref="IPasswordStrengthEvaluator"/>.
/// </para>
/// <para>Stateless and thread-safe; one instance serves the whole app.</para>
/// </remarks>
public class DefaultPasswordStrengthEvaluator : IPasswordStrengthEvaluator
{
    /// <summary>Bits of entropy that score 100. Roughly the point where offline cracking stops being practical.</summary>
    const double BitsForFullScore = 80d;

    /// <summary>What a word from the common list is worth, however long it is.</summary>
    const double KnownWordBits = 11d;

    // Score thresholds, chosen so the bit counts they correspond to line up with the usual advice:
    // ~28 bits is trivially crackable, ~48 survives casual attempts, ~68 is genuinely strong.
    const int FairScore = 35;
    const int GoodScore = 60;
    const int StrongScore = 85;

    /// <summary>The shared instance used when nothing else is registered.</summary>
    public static DefaultPasswordStrengthEvaluator Instance { get; } = new();

    /// <inheritdoc />
    public virtual ValueTask<PasswordStrengthResult> EvaluateAsync(
        PasswordStrengthRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var password = request.Password ?? string.Empty;
        var rules = BuildRules(password, request.Rules);

        if (password.Length == 0)
            return new(PasswordStrengthResult.Empty(rules));

        var suggestions = new List<string>();
        string? warning = null;
        PasswordStrengthTextKey? warningKey = null;
        string? warningValue = null;

        var matchedUserInput = FindUserInput(password, request.Rules.UserInputs);
        var matchedCommonWord = CommonPasswords.FindLongestMatch(password);
        var isBlocked = IsBlocked(password, request.Rules.BlockedPasswords);

        double bits;
        if (CommonPasswords.IsCompromised(password) || isBlocked)
        {
            // The whole thing is a known value. Dressing it up does not help, so neither does
            // scoring the dressing.
            bits = KnownWordBits;
            warning = isBlocked
                ? "This password is not allowed."
                : "This is one of the most commonly used passwords.";
            warningKey = isBlocked
                ? PasswordStrengthTextKey.WarningBlocked
                : PasswordStrengthTextKey.WarningCompromised;
            suggestions.Add("Use a phrase of several unrelated words instead.");
        }
        else
        {
            bits = EstimateBits(password, matchedCommonWord, matchedUserInput);

            if (matchedUserInput is not null)
            {
                warning = "This contains your own details, which an attacker already knows.";
                warningKey = PasswordStrengthTextKey.WarningUserInput;
                warningValue = matchedUserInput;
                suggestions.Add("Leave your name and email address out of it.");
            }
            else if (matchedCommonWord is not null)
            {
                warning = $"\"{matchedCommonWord}\" is a very common password.";
                warningKey = PasswordStrengthTextKey.WarningCommonPassword;
                warningValue = matchedCommonWord;
                suggestions.Add("Build the password out of words nobody would guess for you.");
            }
        }

        var score = (int)Math.Round(Math.Clamp(bits / BitsForFullScore, 0d, 1d) * 100d);
        var level = ToLevel(score);

        if (password.Length < request.Rules.MinimumLength)
            suggestions.Add($"Make it at least {request.Rules.MinimumLength} characters long.");
        else if (level < PasswordStrengthLevel.Strong)
            suggestions.Add("Length beats complexity — adding another word helps more than another symbol.");

        return new(new PasswordStrengthResult
        {
            Score = score,
            Level = level,
            Rules = rules,
            Warning = warning,
            WarningKey = warningKey,
            WarningValue = warningValue,
            Suggestions = suggestions
        });
    }


    static PasswordStrengthLevel ToLevel(int score) => score switch
    {
        >= StrongScore => PasswordStrengthLevel.Strong,
        >= GoodScore => PasswordStrengthLevel.Good,
        >= FairScore => PasswordStrengthLevel.Fair,
        _ => PasswordStrengthLevel.Weak
    };


    /// <summary>
    /// Builds the checklist. Every rule that was asked for appears, satisfied or not, because a
    /// checklist that hides the rules you have already met is a checklist that keeps changing shape
    /// while you type.
    /// </summary>
    static IReadOnlyList<PasswordRuleResult> BuildRules(string password, PasswordStrengthRules policy)
    {
        var results = new List<PasswordRuleResult>
        {
            new(
                PasswordRuleKind.MinimumLength,
                $"At least {policy.MinimumLength} characters",
                password.Length >= policy.MinimumLength,
                policy.MinimumLength
            )
        };

        if (policy.RequireUppercase)
            results.Add(new(PasswordRuleKind.Uppercase, "An uppercase letter", password.Any(char.IsUpper)));

        if (policy.RequireLowercase)
            results.Add(new(PasswordRuleKind.Lowercase, "A lowercase letter", password.Any(char.IsLower)));

        if (policy.RequireNumber)
            results.Add(new(PasswordRuleKind.Number, "A number", password.Any(char.IsDigit)));

        if (policy.RequireSpecialCharacter)
        {
            var specials = policy.SpecialCharacters;
            results.Add(new(
                PasswordRuleKind.SpecialCharacter,
                "A special character",
                password.Any(c => specials.Contains(c, StringComparison.Ordinal))
            ));
        }

        if (policy.RequireNotCompromisedPassword)
        {
            results.Add(new(
                PasswordRuleKind.NotCompromised,
                "Not a commonly used password",
                password.Length > 0 && !CommonPasswords.IsCompromised(password)
            ));
        }

        if (policy.BlockedPasswords is { Count: > 0 })
        {
            results.Add(new(
                PasswordRuleKind.NotBlocked,
                "Not on the list of disallowed passwords",
                password.Length > 0 && !IsBlocked(password, policy.BlockedPasswords)
            ));
        }

        if (policy.UserInputs is { Count: > 0 })
        {
            results.Add(new(
                PasswordRuleKind.NoUserInput,
                "Doesn't contain your name or email",
                password.Length > 0 && FindUserInput(password, policy.UserInputs) is null
            ));
        }

        return results;
    }


    static bool IsBlocked(string password, IReadOnlyList<string>? blocked)
    {
        if (blocked is not { Count: > 0 })
            return false;

        foreach (var entry in blocked)
        {
            if (!string.IsNullOrWhiteSpace(entry) && password.Equals(entry, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }


    /// <summary>
    /// The longest of the user's own details that appears in the password. Email addresses are split
    /// on their punctuation first, so <c>ada@lovelace.io</c> catches a password containing
    /// <c>lovelace</c>.
    /// </summary>
    static string? FindUserInput(string password, IReadOnlyList<string>? inputs)
    {
        if (inputs is not { Count: > 0 })
            return null;

        var haystack = password.ToLowerInvariant();
        string? longest = null;

        foreach (var input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input))
                continue;

            foreach (var part in input.ToLowerInvariant().Split(['@', '.', '_', '-', '+', ' '], StringSplitOptions.RemoveEmptyEntries))
            {
                // Two- and three-letter fragments match half the dictionary; they are noise, not a leak.
                if (part.Length < 4 || part.Length <= (longest?.Length ?? 0))
                    continue;

                if (haystack.Contains(part, StringComparison.Ordinal))
                    longest = part;
            }
        }
        return longest;
    }


    /// <summary>
    /// Entropy of the password after the free parts are taken out: runs, sequences and repeated
    /// blocks collapse, and any recognised word is charged a flat <see cref="KnownWordBits"/>
    /// instead of its length.
    /// </summary>
    static double EstimateBits(string password, string? commonWord, string? userInput)
    {
        var knownRuns = 0;
        var remainder = password;

        foreach (var known in new[] { userInput, commonWord })
        {
            if (known is null)
                continue;

            var index = remainder.IndexOf(known, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                continue;

            remainder = remainder.Remove(index, known.Length);
            knownRuns++;
        }

        var collapsed = Collapse(remainder);
        var bits = collapsed.Length * Math.Log2(PoolSize(remainder));

        return bits + (knownRuns * KnownWordBits);
    }


    /// <summary>
    /// Throws away the characters an attacker gets for free: the tail of <c>aaaaaa</c>, the tail of
    /// <c>123456</c>, and everything past the first repetition of <c>abcabcabc</c>.
    /// </summary>
    static string Collapse(string password)
    {
        if (password.Length < 2)
            return password;

        var period = SmallestPeriod(password);
        var source = period < password.Length
            ? password[..period] + password[period..Math.Min(password.Length, period + 2)]
            : password;

        var builder = new StringBuilder(source.Length);
        var identicalRun = 1;
        var sequentialRun = 1;

        for (var i = 0; i < source.Length; i++)
        {
            if (i > 0)
            {
                var delta = source[i] - source[i - 1];
                identicalRun = delta == 0 ? identicalRun + 1 : 1;
                sequentialRun = delta is 1 or -1 ? sequentialRun + 1 : 1;
            }

            // The first couple of characters of a run still carry information — that it started, and
            // where. The rest is free.
            if (identicalRun <= 2 && sequentialRun <= 3)
                builder.Append(source[i]);
        }

        // Never collapse to nothing: "aaaa" is weak, not weightless.
        return builder.Length > 0 ? builder.ToString() : source[..1];
    }


    /// <summary>The length of the shortest block the password is a whole repetition of.</summary>
    static int SmallestPeriod(string value)
    {
        for (var period = 1; period <= value.Length / 2; period++)
        {
            if (value.Length % period != 0)
                continue;

            var repeats = true;
            for (var i = period; i < value.Length && repeats; i++)
                repeats = value[i] == value[i - period];

            if (repeats)
                return period;
        }
        return value.Length;
    }


    /// <summary>How many characters an attacker has to try per position, given what was used.</summary>
    static int PoolSize(string password)
    {
        var pool = 0;
        var lower = false;
        var upper = false;
        var digit = false;
        var symbol = false;
        var other = false;

        foreach (var c in password)
        {
            if (char.IsLower(c) && c < 128) lower = true;
            else if (char.IsUpper(c) && c < 128) upper = true;
            else if (char.IsDigit(c) && c < 128) digit = true;
            else if (c < 128) symbol = true;
            else other = true;
        }

        if (lower) pool += 26;
        if (upper) pool += 26;
        if (digit) pool += 10;
        if (symbol) pool += 33;
        if (other) pool += 100; // any non-ASCII at all widens the search enormously

        return Math.Max(pool, 2);
    }
}
