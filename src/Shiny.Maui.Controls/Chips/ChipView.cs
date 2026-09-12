using Microsoft.Maui.Controls.Shapes;
using Shiny.Controls.MotionIcons;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.MotionIcons;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

/// <summary>
/// One chip inside a <see cref="ChipGroup"/> — a label, an optional leading check while it is selected,
/// and an optional trailing remove affordance.
/// </summary>
/// <remarks>
/// <para>
/// The sibling of <c>TagChipView</c>, and deliberately a separate class rather than a flag on it. A tag
/// chip's body is inert: only its ✕ takes a tap, because tapping the word of a tag you just typed should
/// do nothing. A chip here is the opposite — the body <em>is</em> the hit target, which is what makes it
/// selectable — and one class doing both would be a chip whose most basic behaviour depends on which
/// control happened to build it.
/// </para>
/// <para>
/// The tap handler hangs off the gesture's <c>Command</c> rather than its <c>Tapped</c> event. A test
/// cannot raise <c>Tapped</c> — there is no public way to — so a control whose whole behaviour is "what
/// happens when a chip is tapped" would have nothing assertable about it.
/// </para>
/// </remarks>
class ChipView : ContentView
{
    const double IconSize = 12;
    const double RemoveTouchSize = 24;

    readonly Border border;
    readonly Grid rootGrid;
    readonly HorizontalStackLayout row;
    readonly MotionIconView check;
    readonly Label label;
    readonly ContentView removeHost;
    readonly MotionIconView removeIcon;

    View? custom;

    public ChipView()
    {
        this.check = new MotionIconView
        {
            Icon = "check",
            Trigger = MotionTrigger.Manual,
            RepeatCount = 1,
            StrokeWidth = 2,
            WidthRequest = IconSize,
            HeightRequest = IconSize,
            IsVisible = false,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };

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
            WidthRequest = IconSize,
            HeightRequest = IconSize,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };

        this.removeHost = new ContentView
        {
            Content = this.removeIcon,
            WidthRequest = RemoveTouchSize,
            HeightRequest = RemoveTouchSize,
            IsVisible = false,
            VerticalOptions = LayoutOptions.Center
        };

        var remove = new TapGestureRecognizer { Command = new Command(this.OnRemoveTapped) };
        this.removeHost.GestureRecognizers.Add(remove);
        SemanticProperties.SetDescription(this.removeHost, "Remove");

        this.row = new HorizontalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children = { this.check, this.label, this.removeHost }
        };

        this.rootGrid = new Grid();
        this.rootGrid.Add(this.row);

        this.border = new Border
        {
            Padding = new Thickness(12, 6),
            Content = this.rootGrid,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center
        }.Neutralize();

        this.Content = this.border;
        this.HorizontalOptions = LayoutOptions.Start;
        this.VerticalOptions = LayoutOptions.Center;

        this.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(this.OnTapped) });

        this.ApplyCornerRadius();
        this.ApplyColors();

        StyleGuard.MarkReady(this, typeof(ChipView));
    }


    /// <summary>The item this chip stands for.</summary>
    public object? Item { get; private set; }

    /// <summary>Raised when the chip body is tapped.</summary>
    public event EventHandler? Tapped;

    /// <summary>Raised when the remove affordance is tapped.</summary>
    public event EventHandler? RemoveRequested;

    public bool IsSelected { get; set; }

    /// <summary>Whether the remove affordance is shown at all.</summary>
    public bool CanRemove
    {
        get => this.removeHost.IsVisible;
        set
        {
            this.removeHost.IsVisible = value;

            // The trailing padding shrinks to make room for the 24px touch target, so a chip with no
            // remove button keeps the symmetrical padding a plain chip should have.
            this.border.Padding = value
                ? new Thickness(12, 6, 4, 6)
                : new Thickness(12, 6);
        }
    }

    /// <summary>Whether the leading check is drawn while the chip is selected.</summary>
    public bool ShowCheck { get; set; } = true;

    public Color? ChipBackgroundColor { get; set; }
    public Color? ChipTextColor { get; set; }
    public Color? SelectedChipBackgroundColor { get; set; }
    public Color? SelectedChipTextColor { get; set; }
    public Color? ChipBorderColor { get; set; }
    public double ChipCornerRadius { get; set; } = ThemeTokens.Unset;

    /// <summary>The label, so a caller's own binding can be applied to it.</summary>
    public Label Label => this.label;

    /// <summary>The remove affordance, which carries its own gesture.</summary>
    public View RemoveTarget => this.removeHost;


    /// <summary>Replaces the label with the caller's own view, from <see cref="ChipGroup.ItemTemplate"/>.</summary>
    public void SetCustomContent(View? content)
    {
        if (this.custom is not null)
        {
            this.row.Remove(this.custom);
            this.custom = null;
        }

        if (content is null)
        {
            this.label.IsVisible = true;
            return;
        }

        // The row still carries the check and the remove button, so the template replaces the label only.
        this.label.IsVisible = false;
        this.custom = content;
        this.row.Insert(1, content);
    }


    public void Bind(object? item, string text)
    {
        this.Item = item;
        this.label.Text = text;
        SemanticProperties.SetDescription(this, text);
    }


    public void Refresh()
    {
        this.ApplyCornerRadius();
        this.ApplyColors();

        this.check.IsVisible = this.IsSelected && this.ShowCheck;
        SemanticProperties.SetHint(this, this.IsSelected ? "Selected" : String.Empty);
    }


    /// <summary>Plays the check's draw-in, for a selection the user just made rather than one restored.</summary>
    public void PlayCheck()
    {
        if (this.check.IsVisible)
            this.check.Play();
    }


    void OnTapped() => this.Tapped?.Invoke(this, EventArgs.Empty);


    void OnRemoveTapped()
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
        if (this.IsSelected)
        {
            var fill = this.SelectedChipBackgroundColor;
            ThemeProbe.Tint(this.border, VisualElement.BackgroundColorProperty, fill, ShinyThemeKeys.Color.SecondaryContainer);
            this.Ink(this.SelectedChipTextColor, fill, ShinyThemeKeys.Color.OnSecondaryContainer);

            // A selected chip is a filled surface. Keeping the outline on it would draw a hairline of
            // the *unselected* colour around the fill, which reads as a chip that is both states at once.
            this.border.StrokeThickness = 0;
        }
        else
        {
            var fill = this.ChipBackgroundColor;

            // No token for the unselected fill: an outlined chip is transparent by design, so the page
            // behind it shows through and a chip group over a card does not paint its own slab.
            ThemeProbe.Tint(this.border, VisualElement.BackgroundColorProperty, fill ?? Colors.Transparent, ShinyThemeKeys.Color.Surface);
            this.Ink(this.ChipTextColor, fill, ShinyThemeKeys.Color.OnSurfaceVariant);

            ThemeBrush.Apply(this.border, Border.StrokeProperty, this.ChipBorderColor, ShinyThemeKeys.Brush.Outline);

            // Cleared first: the selected branch writes 0 as a local value, and a local value outranks
            // the dynamic resource below - so without this a chip that had ever been selected would
            // come back from it with no outline at all.
            this.border.ClearValue(Border.StrokeThicknessProperty);
            this.border.SetDynamicResource(Border.StrokeThicknessProperty, ShinyThemeKeys.Border.Thin);
        }
    }


    /// <summary>
    /// Paints the label and the two glyphs. An explicit fill has no matching "on" token, so the readable
    /// ink is computed from it by luminance the way <see cref="PillView"/> computes a pill's.
    /// </summary>
    /// <remarks>
    /// Every one of these goes through <see cref="ThemeProbe.Tint"/> rather than a plain assignment. A
    /// chip changes state repeatedly, and a colour written as a local value silently outranks the
    /// dynamic resource that would put it back on the theme — so a chip that had been selected once
    /// would keep the selected ink forever.
    /// </remarks>
    void Ink(Color? explicitInk, Color? fill, string token)
    {
        var color = explicitInk ?? (fill is Color surface ? PillView.GetContrastTextColor(surface) : null);

        ThemeProbe.Tint(this.label, Label.TextColorProperty, color, token);
        ThemeProbe.Tint(this.check, MotionIconView.ColorProperty, color, token);
        ThemeProbe.Tint(this.removeIcon, MotionIconView.ColorProperty, color, token);
    }
}
