using Shiny.Maui.Controls;

namespace Sample.Features.PasswordStrength;

public partial class PasswordStrengthPage : ContentPage
{
    public PasswordStrengthPage()
    {
        InitializeComponent();
        SampleSourceCode.Attach(this);

        // Per-control override. Registering it with SetCustomPasswordStrengthEvaluator would apply
        // it to every field in the app instead, which is what a real app usually wants.
        Delayed.Evaluator = new DelayedPasswordStrengthEvaluator();
    }

    void OnStrengthChanged(object? sender, PasswordStrengthChangedEventArgs e)
        => (BindingContext as PasswordStrengthViewModel)?.OnStrengthChanged(e);

    void OnFrenchToggled(object? sender, ToggledEventArgs e)
        => Localized.Localizer = e.Value ? French : null;

    static string? French(PasswordStrengthText text) => text.Key switch
    {
        PasswordStrengthTextKey.LevelWeak => "Faible",
        PasswordStrengthTextKey.LevelFair => "Moyen",
        PasswordStrengthTextKey.LevelGood => "Bon",
        PasswordStrengthTextKey.LevelStrong => "Fort",
        PasswordStrengthTextKey.ShowPassword => "Voir",
        PasswordStrengthTextKey.HidePassword => "Cacher",
        // Argument carries the number, so the sentence can be rebuilt rather than patched
        PasswordStrengthTextKey.RuleMinimumLength => $"Au moins {text.Argument} caractères",
        PasswordStrengthTextKey.RuleNumber => "Un chiffre",
        PasswordStrengthTextKey.RuleNotCompromised => "Pas un mot de passe courant",
        // Warnings are localized too; Value carries the word the warning is about
        PasswordStrengthTextKey.WarningCompromised => "C'est l'un des mots de passe les plus utilisés.",
        PasswordStrengthTextKey.WarningBlocked => "Ce mot de passe n'est pas autorisé.",
        PasswordStrengthTextKey.WarningUserInput => "Il contient vos informations personnelles, qu'un attaquant connaît déjà.",
        PasswordStrengthTextKey.WarningCommonPassword => $"« {text.Value} » est un mot de passe très courant.",
        _ => null // anything not translated keeps the default
    };
}
