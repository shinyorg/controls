using System.Collections.ObjectModel;
using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

public partial class PasswordStrength
{
    // ---------------------------------------------------------------------------------------------
    // Meter construction. Rebuilt only when MeterStyle changes; everything else repaints in place.
    // ---------------------------------------------------------------------------------------------

    void BuildMeter()
    {
        meterHost.Children.Clear();
        meterHost.ColumnDefinitions.Clear();
        segments.Clear();
        barTrack = null;
        barFill = null;
        barColumns = null;

        if (this.MeterStyle == PasswordStrengthMeterStyle.Bar)
        {
            // The fill is sized by two star columns rather than a width request, so it tracks the
            // control's width without anyone having to measure it.
            barColumns = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(0, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(100, GridUnitType.Star))
                }
            };

            barFill = new Border
            {
                Stroke = null,
                StrokeThickness = 0,
                Padding = 0,
                HorizontalOptions = LayoutOptions.Fill
            };
            barColumns.Add(barFill, 0);

            barTrack = new Border
            {
                Stroke = null,
                StrokeThickness = 0,
                Padding = 0,
                Content = barColumns
            };

            meterHost.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            meterHost.Add(barTrack, 0);
        }
        else
        {
            for (var i = 0; i < SegmentCount; i++)
            {
                var segment = new Border
                {
                    Stroke = null,
                    StrokeThickness = 0,
                    Padding = 0
                };
                segments.Add(segment);
                meterHost.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                meterHost.Add(segment, i);
            }
        }
    }


    void RebuildMeter()
    {
        this.BuildMeter();
        this.ApplyAppearance();

        if (this.Result is not null)
            this.ApplyMeter(this.Result);
    }


    // ---------------------------------------------------------------------------------------------
    // Static appearance — everything that does not depend on the current score
    // ---------------------------------------------------------------------------------------------

    void ApplyAppearance()
    {
        entry.Placeholder = this.Placeholder;
        entry.Variant = this.Variant;

        entry.RightTools ??= new ObservableCollection<TextEntryTool>();
        if (!entry.RightTools.Contains(visibilityTool))
            entry.RightTools.Add(visibilityTool);

        visibilityTool.IsVisible = this.ShowVisibilityToggle;
        this.ApplyToolContent();

        meterRow.IsVisible = this.ShowMeter || this.ShowStrengthLabel;
        meterHost.IsVisible = this.ShowMeter;
        strengthLabel.IsVisible = this.ShowStrengthLabel;
        rulesLayout.IsVisible = this.ShowRules;

        meterHost.ColumnSpacing = this.MeterStyle == PasswordStrengthMeterStyle.Bar ? 0 : this.SegmentSpacing;
        meterHost.HeightRequest = this.MeterHeight;

        foreach (var segment in segments)
            this.StyleMeterPiece(segment);

        if (barTrack is not null)
            this.StyleMeterPiece(barTrack);

        if (barFill is not null)
            this.StyleMeterPiece(barFill);

        if (this.Result is not null)
        {
            this.ApplyMeter(this.Result);
            this.ApplyRules(this.Result);
            this.ApplyWarning(this.Result);
        }
    }


    void StyleMeterPiece(Border border)
    {
        border.HeightRequest = this.MeterHeight;
        border.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(this.MeterCornerRadius) };
        this.PaintTrack(border);
    }


    void ApplyToolContent()
    {
        var icon = isRevealed ? this.HidePasswordIcon : this.ShowPasswordIcon;
        visibilityTool.Icon = icon;

        // The word is the fallback rather than a glyph: an emoji eye renders at a different size on
        // every platform and some Android fonts have no eye at all.
        visibilityTool.Text = icon is null
            ? this.Localize(
                isRevealed ? PasswordStrengthTextKey.HidePassword : PasswordStrengthTextKey.ShowPassword,
                isRevealed ? "Hide" : "Show")
            : null;
    }


    // ---------------------------------------------------------------------------------------------
    // Score-driven repaint
    // ---------------------------------------------------------------------------------------------

    void ApplyMeter(PasswordStrengthResult result)
    {
        strengthLabel.Text = result.Level == PasswordStrengthLevel.None
            ? string.Empty
            : this.LevelText(result.Level);

        this.PaintLevel(strengthLabel, Label.TextColorProperty, result.Level, dimWhenNone: true);

        if (barColumns is not null && barFill is not null)
        {
            var filled = Math.Clamp(result.Score, 0, 100);
            barColumns.ColumnDefinitions[0].Width = new GridLength(filled, GridUnitType.Star);
            barColumns.ColumnDefinitions[1].Width = new GridLength(100 - filled, GridUnitType.Star);

            if (result.Level == PasswordStrengthLevel.None)
                this.PaintTrack(barFill);
            else
                this.PaintLevel(barFill, VisualElement.BackgroundColorProperty, result.Level);

            return;
        }

        // Segments light up one per level, so the meter reads the same way at a glance whether or
        // not the caller is showing the caption.
        var lit = (int)result.Level;
        for (var i = 0; i < segments.Count; i++)
        {
            if (i < lit)
                this.PaintLevel(segments[i], VisualElement.BackgroundColorProperty, result.Level);
            else
                this.PaintTrack(segments[i]);
        }
    }


    void ApplyRules(PasswordStrengthResult result)
    {
        if (rulesLayout.Children.Count != result.Rules.Count)
        {
            rulesLayout.Children.Clear();
            foreach (var _ in result.Rules)
                rulesLayout.Children.Add(this.CreateRuleRow());
        }

        for (var i = 0; i < result.Rules.Count; i++)
        {
            var rule = result.Rules[i];
            var row = (Grid)rulesLayout.Children[i];
            var glyph = (Label)row.Children[0];
            var text = (Label)row.Children[1];

            glyph.Text = rule.IsSatisfied ? SatisfiedGlyph : UnsatisfiedGlyph;
            glyph.FontSize = this.RuleFontSize;
            text.FontSize = this.RuleFontSize;
            text.Text = this.RuleText(rule);

            if (rule.IsSatisfied)
            {
                this.PaintLevel(glyph, Label.TextColorProperty, PasswordStrengthLevel.Strong);
                this.PaintLevel(text, Label.TextColorProperty, PasswordStrengthLevel.Strong);
            }
            else
            {
                this.PaintRuleText(glyph);
                this.PaintRuleText(text);
            }
        }
    }


    Grid CreateRuleRow()
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(GlyphWidth)),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 6
        };

        grid.Add(new Label { HorizontalTextAlignment = TextAlignment.Center }, 0);
        grid.Add(new Label { LineBreakMode = LineBreakMode.WordWrap }, 1);
        return grid;
    }


    void ApplyWarning(PasswordStrengthResult result)
    {
        if (!this.ShowWarning)
            return;

        var warning = this.WarningText(result);
        entry.HintText = warning;
        entry.HasError = warning is not null;
    }


    // ---------------------------------------------------------------------------------------------
    // Colour. An explicit value wins; null binds the theme token so a theme swap repaints live.
    // ---------------------------------------------------------------------------------------------

    void PaintTrack(VisualElement element)
    {
        if (this.TrackColor is { } color)
            element.BackgroundColor = color;
        else
            element.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHighest);
    }


    void PaintRuleText(Label label)
    {
        if (this.RuleTextColor is { } color)
            label.TextColor = color;
        else
            label.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
    }


    void PaintLevel(VisualElement element, BindableProperty property, PasswordStrengthLevel level, bool dimWhenNone = false)
    {
        var (explicitColor, token) = level switch
        {
            PasswordStrengthLevel.Weak => (this.WeakColor, ShinyThemeKeys.Color.Critical),
            PasswordStrengthLevel.Fair => (this.FairColor, ShinyThemeKeys.Color.Caution),
            PasswordStrengthLevel.Good => (this.GoodColor, ShinyThemeKeys.Color.Warning),
            PasswordStrengthLevel.Strong => (this.StrongColor, ShinyThemeKeys.Color.Success),
            _ => (dimWhenNone ? null : this.TrackColor, ShinyThemeKeys.Color.OnSurfaceVariant)
        };

        if (explicitColor is { } color)
            element.SetValue(property, color);
        else
            element.SetDynamicResource(property, token);
    }


    // ---------------------------------------------------------------------------------------------
    // Wording
    // ---------------------------------------------------------------------------------------------

    string LevelText(PasswordStrengthLevel level) => level switch
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


    /// <summary>
    /// The warning in the current wording. A warning the evaluator tagged with a key goes through the
    /// localizer like every other string; untagged text from a custom evaluator is shown as written.
    /// </summary>
    string? WarningText(PasswordStrengthResult result)
        => result is { Warning: { } warning, WarningKey: { } key }
            ? this.Localize(key, warning, value: result.WarningValue)
            : result.Warning;


    string Localize(PasswordStrengthTextKey key, string fallback, int argument = 0, string? value = null)
        => this.Localizer?.Invoke(new PasswordStrengthText(key, fallback, argument) { Value = value }) ?? fallback;
}
