using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// The scorer's behaviour, which is where the interesting decisions live.
/// </summary>
/// <remarks>
/// A deliberate copy of the MAUI package's suite. The evaluator is duplicated across the two core
/// packages, the way <c>CountryData</c> and <c>TextEntryMaskHelper</c> are, so it is exactly the
/// thing that can silently drift — testing it on one host only would be testing the wrong half.
/// </remarks>
public class PasswordStrengthEvaluatorTests
{
    static PasswordStrengthResult Score(string password, Action<PasswordStrengthRules>? configure = null)
    {
        var rules = new PasswordStrengthRules();
        configure?.Invoke(rules);

        return DefaultPasswordStrengthEvaluator.Instance
            .EvaluateAsync(new PasswordStrengthRequest(password, rules))
            .GetAwaiter()
            .GetResult();
    }


    [Fact]
    public void EmptyIsNeitherScoredNorAcceptable()
    {
        var result = Score("");

        result.Score.ShouldBe(0);
        result.Level.ShouldBe(PasswordStrengthLevel.None);
        result.IsAcceptable.ShouldBeFalse();
        result.Rules.ShouldNotBeEmpty();
    }


    /// <summary>The whole point of the defaults: a long, ordinary phrase beats a short cryptic one.</summary>
    [Fact]
    public void PassphraseBeatsCompositionTheatre()
    {
        var passphrase = Score("the slow red barn on nine");
        var cryptic = Score("Xk7!q");

        passphrase.Level.ShouldBe(PasswordStrengthLevel.Strong);
        passphrase.IsAcceptable.ShouldBeTrue();
        passphrase.Score.ShouldBeGreaterThan(cryptic.Score);
    }


    [Theory]
    [InlineData("password")]
    [InlineData("Passw0rd!")]      // leet plus a trailing symbol
    [InlineData("P@ssw0rd2024")]   // leet plus a year
    [InlineData("MONKEY")]         // case only
    [InlineData("letmein123")]
    public void KnownPasswordsAreCaught(string password)
    {
        var result = Score(password);

        result.Level.ShouldBe(PasswordStrengthLevel.Weak);
        result.Warning.ShouldNotBeNull();
        result.Rules
            .Single(x => x.Kind == PasswordRuleKind.NotCompromised)
            .IsSatisfied
            .ShouldBeFalse();
    }


    /// <summary>Length alone is not entropy: repeats and runs are free to a cracker.</summary>
    [Theory]
    [InlineData("aaaaaaaaaaaaaaaaaaaa")]
    [InlineData("abcabcabcabcabcabc")]
    [InlineData("12345678901234567890")]
    public void RepetitionDoesNotBuyLength(string password)
    {
        var result = Score(password);

        password.Length.ShouldBeGreaterThan(15);
        result.Level.ShouldBe(PasswordStrengthLevel.Weak);
    }


    [Fact]
    public void MinimumLengthRuleFollowsThePolicy()
    {
        var result = Score("abcdefgh", r => r.MinimumLength = 12);

        var rule = result.Rules.Single(x => x.Kind == PasswordRuleKind.MinimumLength);
        rule.IsSatisfied.ShouldBeFalse();
        rule.Argument.ShouldBe(12);
        rule.Description.ShouldContain("12");

        Score("abcdefghijkl", r => r.MinimumLength = 12)
            .Rules
            .Single(x => x.Kind == PasswordRuleKind.MinimumLength)
            .IsSatisfied
            .ShouldBeTrue();
    }


    /// <summary>A rule that was not asked for must not appear in the checklist at all.</summary>
    [Fact]
    public void OnlyRequestedRulesAppear()
    {
        var defaults = Score("something");
        defaults.Rules.ShouldNotContain(x => x.Kind == PasswordRuleKind.Uppercase);
        defaults.Rules.ShouldNotContain(x => x.Kind == PasswordRuleKind.SpecialCharacter);
        defaults.Rules.ShouldNotContain(x => x.Kind == PasswordRuleKind.NotBlocked);

        var strict = Score("something", r =>
        {
            r.RequireUppercase = true;
            r.RequireNumber = true;
            r.RequireSpecialCharacter = true;
        });

        strict.Rules.ShouldContain(x => x.Kind == PasswordRuleKind.Uppercase && !x.IsSatisfied);
        strict.Rules.ShouldContain(x => x.Kind == PasswordRuleKind.Number && !x.IsSatisfied);
        strict.Rules.ShouldContain(x => x.Kind == PasswordRuleKind.SpecialCharacter && !x.IsSatisfied);
    }


    [Fact]
    public void CompromisedCheckCanBeTurnedOff()
    {
        Score("password", r => r.RequireNotCompromisedPassword = false)
            .Rules
            .ShouldNotContain(x => x.Kind == PasswordRuleKind.NotCompromised);
    }


    [Fact]
    public void BlockedListIsRefused()
    {
        var result = Score("Shiny", r => r.BlockedPasswords = ["shiny", "acme"]);

        result.Rules.Single(x => x.Kind == PasswordRuleKind.NotBlocked).IsSatisfied.ShouldBeFalse();
        result.Level.ShouldBe(PasswordStrengthLevel.Weak);
        result.Warning.ShouldNotBeNull();
    }


    /// <summary>
    /// The email is split on its punctuation, so a password built around the local part or the
    /// domain is caught even though the whole address never appears.
    /// </summary>
    [Fact]
    public void OwnDetailsAreRefused()
    {
        var result = Score(
            "lovelace-and-more-words-here",
            r => r.UserInputs = ["ada.lovelace@example.com"]
        );

        result.Rules.Single(x => x.Kind == PasswordRuleKind.NoUserInput).IsSatisfied.ShouldBeFalse();
        result.Warning.ShouldNotBeNull();
    }


    /// <summary>Short fragments match everything; they are noise, not a leak.</summary>
    [Fact]
    public void ShortUserInputFragmentsAreIgnored()
    {
        Score("the slow red barn on nine", r => r.UserInputs = ["a.b@c.io", "Jo"])
            .Rules
            .Single(x => x.Kind == PasswordRuleKind.NoUserInput)
            .IsSatisfied
            .ShouldBeTrue();
    }


    /// <summary>
    /// The distinction the control's docs lean on: score and acceptability are different questions,
    /// and a strong password can fail a policy.
    /// </summary>
    [Fact]
    public void StrongIsNotTheSameAsAcceptable()
    {
        var result = Score("the slow red barn on nine", r => r.RequireNumber = true);

        result.Level.ShouldBe(PasswordStrengthLevel.Strong);
        result.IsAcceptable.ShouldBeFalse();
    }


    [Fact]
    public void SuggestionsPointAtLengthFirst()
        => Score("short").Suggestions.ShouldContain(x => x.Contains("15"));


    [Fact]
    public async Task CancellationIsHonoured()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // The built-in evaluator is synchronous, so it is allowed to answer - what must not happen is
        // an exception escaping to the control's fire-and-forget caller.
        var result = await DefaultPasswordStrengthEvaluator.Instance.EvaluateAsync(
            new PasswordStrengthRequest("anything at all", new PasswordStrengthRules()),
            cts.Token
        );

        result.ShouldNotBeNull();
    }


    /// <summary>
    /// Every built-in warning carries a key so the control can localize it. A warning without one is
    /// shown verbatim, which is how "pass is a very common password" stayed English under a French
    /// localizer.
    /// </summary>
    [Fact]
    public void BuiltInWarningsCarryALocalizationKey()
    {
        var common = Score("passXq7!");
        common.WarningKey.ShouldBe(PasswordStrengthTextKey.WarningCommonPassword);
        common.WarningValue.ShouldBe("pass");

        Score("password").WarningKey.ShouldBe(PasswordStrengthTextKey.WarningCompromised);
        Score("Shiny", r => r.BlockedPasswords = ["shiny"]).WarningKey.ShouldBe(PasswordStrengthTextKey.WarningBlocked);

        var own = Score("lovelace-and-more-words-here", r => r.UserInputs = ["ada.lovelace@example.com"]);
        own.WarningKey.ShouldBe(PasswordStrengthTextKey.WarningUserInput);
        own.WarningValue.ShouldBe("lovelace");

        var clean = Score("the slow red barn on nine");
        clean.Warning.ShouldBeNull();
        clean.WarningKey.ShouldBeNull();
    }
}


/// <summary>The wordlist and the disguises it has to see through.</summary>
public class CommonPasswordsTests
{
    [Theory]
    [InlineData("password")]
    [InlineData("PASSWORD")]
    [InlineData("P@ssw0rd")]
    [InlineData("passw0rd!")]
    [InlineData("qwerty2025")]
    [InlineData("!monkey!")]
    public void DisguisesAreSeenThrough(string password)
        => CommonPasswords.IsCompromised(password).ShouldBeTrue();


    [Theory]
    [InlineData("")]
    [InlineData("the slow red barn on nine")]
    [InlineData("quartz-lantern-fifteen")]
    public void OrdinaryPhrasesAreNot(string password)
        => CommonPasswords.IsCompromised(password).ShouldBeFalse();


    [Fact]
    public void BuriedWordsAreFoundForScoring()
    {
        CommonPasswords.FindLongestMatch("xxmonkeyxx").ShouldBe("monkey");
        CommonPasswords.FindLongestMatch("quartz lantern").ShouldBeNull();
    }
}
