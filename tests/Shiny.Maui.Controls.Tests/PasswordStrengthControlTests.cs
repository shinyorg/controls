using Microsoft.Maui.Controls;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// The control's wiring: what it publishes after an evaluation, how the checklist tracks a changing
/// policy, and the reveal toggle. The debounce is bypassed with <c>EvaluateNowAsync</c> — waiting on
/// a timer here would be testing <see cref="Task.Delay"/>.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class PasswordStrengthControlTests
{
    public PasswordStrengthControlTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
    }

    static VerticalStackLayout Root(PasswordStrength control) => (VerticalStackLayout)control.Content;

    static VerticalStackLayout Rules(PasswordStrength control) => (VerticalStackLayout)Root(control).Children[2];


    [Fact]
    public async Task PublishesTheVerdict()
    {
        var control = new PasswordStrength { Password = "the slow red barn on nine" };
        await control.EvaluateNowAsync();

        control.Level.ShouldBe(PasswordStrengthLevel.Strong);
        control.Score.ShouldBeGreaterThan(0);
        control.IsAcceptable.ShouldBeTrue();
        control.Result.ShouldNotBeNull();
    }


    [Fact]
    public async Task StrengthChangedFiresOnceForOneVerdict()
    {
        var control = new PasswordStrength();
        await control.EvaluateNowAsync();

        var raised = new List<PasswordStrengthChangedEventArgs>();
        control.StrengthChanged += (_, e) => raised.Add(e);

        control.Password = "hunter2";
        await control.EvaluateNowAsync();

        raised.Count.ShouldBe(1);
        raised[0].Level.ShouldBe(PasswordStrengthLevel.Weak);
        raised[0].IsAcceptable.ShouldBeFalse();

        // Re-scoring the same password is not a change and must not raise again.
        await control.EvaluateNowAsync();
        raised.Count.ShouldBe(1);
    }


    /// <summary>
    /// A policy change has to reach the evaluator even though the password did not move — otherwise
    /// a MinimumLength bound to a dropdown would leave a stale checklist on screen.
    /// </summary>
    [Fact]
    public async Task ChecklistTracksTheChangingPolicy()
    {
        var control = new PasswordStrength { Password = "abcdefghijklmnop" };
        await control.EvaluateNowAsync();

        var initial = Rules(control).Children.Count;
        initial.ShouldBe(control.Result!.Rules.Count);

        control.RequireNumber = true;
        control.RequireUppercase = true;
        await control.EvaluateNowAsync();

        Rules(control).Children.Count.ShouldBe(initial + 2);
        control.IsAcceptable.ShouldBeFalse();
    }


    [Fact]
    public async Task ClearingThePasswordEmptiesTheMeter()
    {
        var control = new PasswordStrength { Password = "the slow red barn on nine" };
        await control.EvaluateNowAsync();
        control.Level.ShouldBe(PasswordStrengthLevel.Strong);

        control.Password = "";
        await control.EvaluateNowAsync();

        control.Level.ShouldBe(PasswordStrengthLevel.None);
        control.Score.ShouldBe(0);
        control.IsAcceptable.ShouldBeFalse();
    }


    [Fact]
    public void RevealTogglesTheMask()
    {
        var control = new PasswordStrength();
        var entry = (TextEntry)Root(control).Children[0];

        entry.IsPassword.ShouldBeTrue();

        control.IsPasswordRevealed = true;
        entry.IsPassword.ShouldBeFalse();

        control.IsPasswordRevealed = false;
        entry.IsPassword.ShouldBeTrue();
    }


    [Fact]
    public async Task SwitchingMeterStyleKeepsTheCurrentVerdict()
    {
        var control = new PasswordStrength { Password = "the slow red barn on nine" };
        await control.EvaluateNowAsync();

        control.MeterStyle = PasswordStrengthMeterStyle.Bar;

        control.Level.ShouldBe(PasswordStrengthLevel.Strong);
        control.Result.ShouldNotBeNull();
    }


    /// <summary>
    /// A custom evaluator that falls over must not freeze the meter — the local heuristic answers
    /// instead, which is what keeps the control usable when the network goes away.
    /// </summary>
    [Fact]
    public async Task ThrowingEvaluatorFallsBackToTheBuiltIn()
    {
        var control = new PasswordStrength
        {
            Evaluator = new ThrowingEvaluator(),
            Password = "the slow red barn on nine"
        };
        await control.EvaluateNowAsync();

        control.Level.ShouldBe(PasswordStrengthLevel.Strong);
    }


    /// <summary>
    /// The warning shown under the field goes through the localizer with the rest of the wording,
    /// including the matched word — and follows the localizer being swapped after the verdict.
    /// </summary>
    [Fact]
    public async Task WarningIsLocalized()
    {
        var control = new PasswordStrength { Password = "passXq7!" };
        await control.EvaluateNowAsync();

        var entry = (TextEntry)Root(control).Children[0];
        entry.HintText.ShouldBe("\"pass\" is a very common password.");

        control.Localizer = text => text.Key == PasswordStrengthTextKey.WarningCommonPassword
            ? $"« {text.Value} » est un mot de passe très courant."
            : null;

        entry.HintText.ShouldBe("« pass » est un mot de passe très courant.");
        entry.HasError.ShouldBeTrue();
    }


    /// <summary>A custom evaluator's untagged warning is shown exactly as written.</summary>
    [Fact]
    public async Task UntaggedWarningIsShownVerbatim()
    {
        var control = new PasswordStrength
        {
            Evaluator = new FixedWarningEvaluator(),
            Localizer = _ => "translated",
            Password = "anything"
        };
        await control.EvaluateNowAsync();

        ((TextEntry)Root(control).Children[0]).HintText.ShouldBe("custom");
    }


    sealed class FixedWarningEvaluator : IPasswordStrengthEvaluator
    {
        public ValueTask<PasswordStrengthResult> EvaluateAsync(
            PasswordStrengthRequest request,
            CancellationToken cancellationToken = default
        ) => new(new PasswordStrengthResult
        {
            Score = 10,
            Level = PasswordStrengthLevel.Weak,
            Rules = [],
            Warning = "custom"
        });
    }


    sealed class ThrowingEvaluator : IPasswordStrengthEvaluator
    {
        public ValueTask<PasswordStrengthResult> EvaluateAsync(
            PasswordStrengthRequest request,
            CancellationToken cancellationToken = default
        ) => throw new HttpRequestException("no network");
    }
}
