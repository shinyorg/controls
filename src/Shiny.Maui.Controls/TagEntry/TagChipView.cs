using Microsoft.Maui.Controls.Shapes;
using Shiny.Controls.MotionIcons;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.MotionIcons;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

/// <summary>
/// One committed tag inside a <see cref="TagEntry"/> — a label and the affordance that removes it.
/// </summary>
/// <remarks>
/// Deliberately not a <see cref="PillView"/>. A pill is a status badge: it carries no interaction, and
/// giving it one would mean every pill in every app grew a hit target it does not want. This is the pill
/// visuals plus a remove button, and the remove button is the only part that takes a tap — the chip body
/// is not a control, so tapping the word does nothing rather than something surprising.
/// </remarks>
class TagChipView : ContentView
{
    const double RemoveIconSize = 12;
    const double RemoveTouchSize = 24;

    readonly Border border;
    readonly Label label;
    readonly ContentView removeHost;
    readonly MotionIconView removeIcon;
    readonly HorizontalStackLayout row;
    readonly Grid rootGrid;

    public TagChipView()
    {
        this.label = new Label
        {
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        }.WithFontSize(ShinyThemeKeys.Type.LabelLargeSize);

        this.removeIcon = new MotionIconView
        {
            Icon = "close",
            // Manual: the chip drives playback from its own gesture, so a tap anywhere in the touch
            // target animates rather than only one that lands on the 12px glyph.
            Trigger = MotionTrigger.Manual,
            RepeatCount = 1,
            StrokeWidth = 2,
            WidthRequest = RemoveIconSize,
            HeightRequest = RemoveIconSize,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };

        this.removeHost = new ContentView
        {
            Content = this.removeIcon,
            WidthRequest = RemoveTouchSize,
            HeightRequest = RemoveTouchSize,
            VerticalOptions = LayoutOptions.Center
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += this.OnRemoveTapped;
        this.removeHost.GestureRecognizers.Add(tap);

        SemanticProperties.SetDescription(this.removeHost, "Remove tag");

        this.row = new HorizontalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children = { this.label, this.removeHost }
        };

        this.rootGrid = new Grid();
        this.rootGrid.Add(this.row);

        this.border = new Border
        {
            Padding = new Thickness(10, 4, 4, 4),
            StrokeThickness = 0,
            Stroke = null,
            Content = this.rootGrid,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center
        }.Neutralize();

        this.ApplyCornerRadius();
        this.ApplyColors();

        this.Content = this.border;
        this.HorizontalOptions = LayoutOptions.Start;
        this.VerticalOptions = LayoutOptions.Center;

        StyleGuard.MarkReady(this, typeof(TagChipView));
    }


    /// <summary>The tag this chip stands for.</summary>
    public string Tag { get; private set; } = String.Empty;

    /// <summary>Raised when the remove affordance is tapped.</summary>
    public event EventHandler? RemoveRequested;

    /// <summary>Whether the remove affordance is shown at all — a read-only field keeps its chips crisp but inert.</summary>
    public bool CanRemove
    {
        get => this.removeHost.IsVisible;
        set => this.removeHost.IsVisible = value;
    }

    public Color? ChipBackgroundColor { get; set; }
    public Color? ChipTextColor { get; set; }
    public double ChipCornerRadius { get; set; } = ThemeTokens.Unset;

    /// <summary>Replaces the label with the caller's own view, from <see cref="TagEntry.ChipTemplate"/>.</summary>
    public void SetCustomContent(View? content)
    {
        if (this.custom is not null)
        {
            this.rootGrid.Remove(this.custom);
            this.custom = null;
        }

        if (content is null)
        {
            this.row.IsVisible = true;
            return;
        }

        // The row still carries the remove button, so the custom content replaces the label only.
        this.label.IsVisible = false;
        this.custom = content;
        this.row.Insert(0, content);
    }

    View? custom;


    public void Bind(string tag)
    {
        this.Tag = tag;
        this.label.Text = tag;
        SemanticProperties.SetDescription(this, tag);
    }


    public void Refresh()
    {
        this.ApplyCornerRadius();
        this.ApplyColors();
    }


    void OnRemoveTapped(object? sender, TappedEventArgs e)
    {
        this.removeIcon.Play();
        this.RemoveRequested?.Invoke(this, EventArgs.Empty);
    }


    void ApplyCornerRadius()
    {
        var shape = new RoundRectangle();
        shape.SetCornerTokenOrValue(this.ChipCornerRadius, ShinyThemeKeys.Shape.CornerSmallRadius);
        this.border.StrokeShape = shape;
    }


    void ApplyColors()
    {
        ThemeProbe.Tint(this.border, VisualElement.BackgroundColorProperty, this.ChipBackgroundColor, ShinyThemeKeys.Color.SecondaryContainer);

        if (this.ChipTextColor is Color explicitText)
        {
            this.label.TextColor = explicitText;
            this.removeIcon.Color = explicitText;
        }
        else if (this.ChipBackgroundColor is Color fill)
        {
            // A caller-picked fill has no matching "on" token, so the readable ink is computed from it
            // the way PillView computes a pill's.
            var ink = PillView.GetContrastTextColor(fill);
            this.label.TextColor = ink;
            this.removeIcon.Color = ink;
        }
        else
        {
            this.label.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSecondaryContainer);
            this.removeIcon.SetDynamicResource(MotionIconView.ColorProperty, ShinyThemeKeys.Color.OnSecondaryContainer);
        }
    }
}
