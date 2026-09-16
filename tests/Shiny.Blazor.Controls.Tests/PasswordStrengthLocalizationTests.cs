using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// The warning under the field goes through <see cref="PasswordStrength.Localizer"/> like the rest
/// of the wording. It used to be passed through verbatim, so a French checklist sat under an English
/// "pass is a very common password".
/// </summary>
public class PasswordStrengthLocalizationTests
{
    static PasswordStrengthResult Score(string password)
        => DefaultPasswordStrengthEvaluator.Instance
            .EvaluateAsync(new PasswordStrengthRequest(password, new PasswordStrengthRules()))
            .GetAwaiter()
            .GetResult();


    [Fact]
    public void WarningIsLocalizedWithTheMatchedWord()
    {
        var control = new PasswordStrength
        {
            Localizer = text => text.Key == PasswordStrengthTextKey.WarningCommonPassword
                ? $"« {text.Value} » est un mot de passe très courant."
                : null
        };

        control.LocalizedWarning(Score("passXq7!")).ShouldBe("« pass » est un mot de passe très courant.");
    }


    [Fact]
    public void WarningKeepsTheDefaultWithoutALocalizer()
        => new PasswordStrength()
            .LocalizedWarning(Score("passXq7!"))
            .ShouldBe("\"pass\" is a very common password.");


    [Fact]
    public void UntaggedWarningIsShownVerbatim()
    {
        var control = new PasswordStrength { Localizer = _ => "translated" };
        var custom = new PasswordStrengthResult
        {
            Score = 10,
            Level = PasswordStrengthLevel.Weak,
            Rules = [],
            Warning = "custom"
        };

        control.LocalizedWarning(custom).ShouldBe("custom");
    }
}
