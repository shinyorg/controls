using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

public class SecurityPin : ContentView
{
    /// <summary>Appended to the pin's <see cref="Element.AutomationId"/> to name the hidden entry that takes the keystrokes.</summary>
    public const string EntryAutomationIdSuffix = "_Entry";

    /// <summary>Names the row of cells, which carries the tap that brings the keyboard up.</summary>
    public const string CellsAutomationIdSuffix = "_Cells";

    /// <summary>Names each cell, with its position: <c>&lt;id&gt;_Cell_0</c>.</summary>
    public const string CellAutomationIdSuffix = "_Cell";

    readonly Grid rootGrid;
    readonly HorizontalStackLayout cellsLayout;
    readonly Entry hiddenEntry;
    readonly List<Border> cellBorders = new();
    readonly List<Label> cellLabels = new();

    bool isUpdatingValue;

    public SecurityPin()
    {
        hiddenEntry = new Entry
        {
            Opacity = 0,
            BackgroundColor = Colors.Transparent,
            TextColor = Colors.Transparent,
            HeightRequest = 1,
            WidthRequest = 1,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            IsSpellCheckEnabled = false,
            IsTextPredictionEnabled = false,
            Keyboard = Keyboard.Numeric
        };
        hiddenEntry.TextChanged += OnHiddenEntryTextChanged;
        hiddenEntry.Completed += OnHiddenEntryCompleted;

        // The active cell is drawn from IsFocused, so gaining or losing focus has to repaint - without
        // it the cell kept its focused colours after focus moved on.
        hiddenEntry.Focused += (_, _) => RefreshCells();
        hiddenEntry.Unfocused += (_, _) => RefreshCells();

        cellsLayout = new HorizontalStackLayout
        {
            Spacing = DefaultCellSpacing,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => hiddenEntry.Focus();
        cellsLayout.GestureRecognizers.Add(tap);

        rootGrid = new Grid
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        rootGrid.Children.Add(hiddenEntry);
        rootGrid.Children.Add(cellsLayout);

        Content = rootGrid;
        BuildCells();

        // Through SetValue, not the AutomationId setter: that throws the second time. See AutomationIds.
        AutomationIds.Link(this, hiddenEntry, EntryAutomationIdSuffix);
        AutomationIds.Link(this, cellsLayout, CellsAutomationIdSuffix);
        AutomationIds.Track(this, NameCells);

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(SecurityPin));
    }

    const double DefaultCellSize = 50;
    const double DefaultCellSpacing = 8;
    const int DefaultLength = 4;


    public event EventHandler<SecurityPinCompletedEventArgs>? Completed;


    public static readonly BindableProperty LengthProperty = BindableProperty.Create(
        nameof(Length),
        typeof(int),
        typeof(SecurityPin),
        DefaultLength,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SecurityPin), () =>
            {
                ((SecurityPin)b).BuildCells();
            })
    );
    public int Length
    {
        get => (int)GetValue(LengthProperty);
        set => SetValue(LengthProperty, value);
    }

    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value),
        typeof(string),
        typeof(SecurityPin),
        string.Empty,
        defaultBindingMode: BindingMode.TwoWay,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(SecurityPin), () =>
            {
                ((SecurityPin)b).OnValueChangedExternally((string?)n);
            })
    );
    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly BindableProperty KeyboardProperty = BindableProperty.Create(
        nameof(Keyboard),
        typeof(Keyboard),
        typeof(SecurityPin),
        Keyboard.Numeric,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(SecurityPin), () =>
            {
                ((SecurityPin)b).hiddenEntry.Keyboard = (Keyboard)n;
            })
    );
    public Keyboard Keyboard
    {
        get => (Keyboard)GetValue(KeyboardProperty);
        set => SetValue(KeyboardProperty, value);
    }

    public static readonly BindableProperty HideCharacterProperty = BindableProperty.Create(
        nameof(HideCharacter),
        typeof(string),
        typeof(SecurityPin),
        null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SecurityPin), () =>
            {
                ((SecurityPin)b).RefreshCells();
            })
    );
    public string? HideCharacter
    {
        get => (string?)GetValue(HideCharacterProperty);
        set => SetValue(HideCharacterProperty, value);
    }

    public static readonly BindableProperty CellSizeProperty = BindableProperty.Create(
        nameof(CellSize),
        typeof(double),
        typeof(SecurityPin),
        DefaultCellSize,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SecurityPin), () =>
            {
                ((SecurityPin)b).ApplyCellStyle();
            })
    );
    public double CellSize
    {
        get => (double)GetValue(CellSizeProperty);
        set => SetValue(CellSizeProperty, value);
    }

    public static readonly BindableProperty CellSpacingProperty = BindableProperty.Create(
        nameof(CellSpacing),
        typeof(double),
        typeof(SecurityPin),
        DefaultCellSpacing,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(SecurityPin), () =>
            {
                ((SecurityPin)b).cellsLayout.Spacing = (double)n;
            })
    );
    public double CellSpacing
    {
        get => (double)GetValue(CellSpacingProperty);
        set => SetValue(CellSpacingProperty, value);
    }

    public static readonly BindableProperty CellCornerRadiusProperty = BindableProperty.Create(
        nameof(CellCornerRadius),
        typeof(double),
        typeof(SecurityPin),
        ThemeTokens.Unset,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SecurityPin), () =>
            {
                ((SecurityPin)b).ApplyCellStyle();
            })
    );
    public double CellCornerRadius
    {
        get => (double)GetValue(CellCornerRadiusProperty);
        set => SetValue(CellCornerRadiusProperty, value);
    }

    public static readonly BindableProperty CellBorderColorProperty = BindableProperty.Create(
        nameof(CellBorderColor),
        typeof(Color),
        typeof(SecurityPin),
        null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SecurityPin), () =>
            {
                ((SecurityPin)b).ApplyCellStyle();
            })
    );
    public Color? CellBorderColor
    {
        get => (Color?)GetValue(CellBorderColorProperty);
        set => SetValue(CellBorderColorProperty, value);
    }

    public static readonly BindableProperty CellFocusedBorderColorProperty = BindableProperty.Create(
        nameof(CellFocusedBorderColor),
        typeof(Color),
        typeof(SecurityPin),
        null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SecurityPin), () =>
            {
                ((SecurityPin)b).ApplyCellStyle();
            })
    );
    public Color? CellFocusedBorderColor
    {
        get => (Color?)GetValue(CellFocusedBorderColorProperty);
        set => SetValue(CellFocusedBorderColorProperty, value);
    }

    public static readonly BindableProperty CellBackgroundColorProperty = BindableProperty.Create(
        nameof(CellBackgroundColor),
        typeof(Color),
        typeof(SecurityPin),
        null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SecurityPin), () =>
            {
                ((SecurityPin)b).ApplyCellStyle();
            })
    );
    public Color? CellBackgroundColor
    {
        get => (Color?)GetValue(CellBackgroundColorProperty);
        set => SetValue(CellBackgroundColorProperty, value);
    }

    public static readonly BindableProperty CellFocusedBackgroundColorProperty = BindableProperty.Create(
        nameof(CellFocusedBackgroundColor),
        typeof(Color),
        typeof(SecurityPin),
        null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SecurityPin), () =>
            {
                ((SecurityPin)b).ApplyCellStyle();
            })
    );
    public Color? CellFocusedBackgroundColor
    {
        get => (Color?)GetValue(CellFocusedBackgroundColorProperty);
        set => SetValue(CellFocusedBackgroundColorProperty, value);
    }

    public static readonly BindableProperty CellTextColorProperty = BindableProperty.Create(
        nameof(CellTextColor),
        typeof(Color),
        typeof(SecurityPin),
        null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SecurityPin), () =>
            {
                ((SecurityPin)b).ApplyCellStyle();
            })
    );
    public Color? CellTextColor
    {
        get => (Color?)GetValue(CellTextColorProperty);
        set => SetValue(CellTextColorProperty, value);
    }

    public static readonly BindableProperty CellFocusedTextColorProperty = BindableProperty.Create(
        nameof(CellFocusedTextColor),
        typeof(Color),
        typeof(SecurityPin),
        null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SecurityPin), () =>
            {
                ((SecurityPin)b).ApplyCellStyle();
            })
    );
    /// <summary>Character colour of the cell awaiting the next character. Unset keeps <see cref="CellTextColor"/>.</summary>
    public Color? CellFocusedTextColor
    {
        get => (Color?)GetValue(CellFocusedTextColorProperty);
        set => SetValue(CellFocusedTextColorProperty, value);
    }

    public static readonly BindableProperty CellBorderThicknessProperty = BindableProperty.Create(
        nameof(CellBorderThickness),
        typeof(double),
        typeof(SecurityPin),
        ThemeTokens.Unset,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SecurityPin), () =>
            {
                ((SecurityPin)b).ApplyCellStyle();
            })
    );
    /// <summary>Stroke width of the cells. Unset follows the theme's thin border token.</summary>
    public double CellBorderThickness
    {
        get => (double)GetValue(CellBorderThicknessProperty);
        set => SetValue(CellBorderThicknessProperty, value);
    }

    public static readonly BindableProperty CellFocusedBorderThicknessProperty = BindableProperty.Create(
        nameof(CellFocusedBorderThickness),
        typeof(double),
        typeof(SecurityPin),
        ThemeTokens.Unset,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SecurityPin), () =>
            {
                ((SecurityPin)b).ApplyCellStyle();
            })
    );
    /// <summary>Stroke width of the cell awaiting the next character. Unset keeps <see cref="CellBorderThickness"/>.</summary>
    public double CellFocusedBorderThickness
    {
        get => (double)GetValue(CellFocusedBorderThicknessProperty);
        set => SetValue(CellFocusedBorderThicknessProperty, value);
    }

    public static readonly BindableProperty CellBorderStyleProperty = BindableProperty.Create(
        nameof(CellBorderStyle),
        typeof(SecurityPinBorderStyle),
        typeof(SecurityPin),
        SecurityPinBorderStyle.Solid,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SecurityPin), () =>
            {
                ((SecurityPin)b).ApplyCellStyle();
            })
    );
    public SecurityPinBorderStyle CellBorderStyle
    {
        get => (SecurityPinBorderStyle)GetValue(CellBorderStyleProperty);
        set => SetValue(CellBorderStyleProperty, value);
    }

    public static readonly BindableProperty CellFocusedBorderStyleProperty = BindableProperty.Create(
        nameof(CellFocusedBorderStyle),
        typeof(SecurityPinBorderStyle?),
        typeof(SecurityPin),
        null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SecurityPin), () =>
            {
                ((SecurityPin)b).ApplyCellStyle();
            })
    );
    /// <summary>Stroke style of the cell awaiting the next character. Unset keeps <see cref="CellBorderStyle"/>.</summary>
    public SecurityPinBorderStyle? CellFocusedBorderStyle
    {
        get => (SecurityPinBorderStyle?)GetValue(CellFocusedBorderStyleProperty);
        set => SetValue(CellFocusedBorderStyleProperty, value);
    }

    public static readonly BindableProperty HighlightCurrentCellProperty = BindableProperty.Create(
        nameof(HighlightCurrentCell),
        typeof(bool),
        typeof(SecurityPin),
        true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SecurityPin), () =>
            {
                ((SecurityPin)b).ApplyCellStyle();
            })
    );
    /// <summary>
    /// While the pin has focus, the cell awaiting the next character takes the <c>CellFocused…</c>
    /// appearance. Turn it off to draw every cell alike.
    /// </summary>
    public bool HighlightCurrentCell
    {
        get => (bool)GetValue(HighlightCurrentCellProperty);
        set => SetValue(HighlightCurrentCellProperty, value);
    }

    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(
        nameof(FontSize),
        typeof(double),
        typeof(SecurityPin),
        ThemeTokens.Unset,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(SecurityPin), () =>
            {
                var pin = (SecurityPin)b;
                foreach (var label in pin.cellLabels)
                    label.SetTokenOrValue(Label.FontSizeProperty, (double)n, ShinyThemeKeys.Type.HeadlineSmallSize);
            })
    );
    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }


    public static readonly BindableProperty UseFeedbackProperty = BindableProperty.Create(
        nameof(UseFeedback),
        typeof(bool),
        typeof(SecurityPin),
        true);
    public bool UseFeedback
    {
        get => (bool)GetValue(UseFeedbackProperty);
        set => SetValue(UseFeedbackProperty, value);
    }


    public new void Focus() => hiddenEntry.Focus();
    public new void Unfocus() => hiddenEntry.Unfocus();
    public void Clear() => UpdateValue(string.Empty);


    // Cells are rebuilt when Length changes, so they are renamed with them.
    void NameCells()
    {
        for (var i = 0; i < cellBorders.Count; i++)
            AutomationIds.Set(cellBorders[i], AutomationIds.For(this, CellAutomationIdSuffix, i));
    }

    void BuildCells()
    {
        cellsLayout.Clear();
        cellBorders.Clear();
        cellLabels.Clear();

        var count = Math.Max(1, Length);
        for (var i = 0; i < count; i++)
        {
            var label = new Label
            {
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
            label.SetTokenOrValue(Label.FontSizeProperty, FontSize, ShinyThemeKeys.Type.HeadlineSmallSize);
            ThemeProbe.Tint(label, Label.TextColorProperty, CellTextColor, ShinyThemeKeys.Color.OnSurface);

            var border = new Border
            {
                WidthRequest = CellSize,
                HeightRequest = CellSize,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                {
                    CornerRadius = new CornerRadius(CellCornerRadius)
                },
                Content = label
            }.WithStrokeThickness(ShinyThemeKeys.Border.Thin);

            cellLabels.Add(label);
            cellBorders.Add(border);
            cellsLayout.Add(border);
        }

        ApplyCellStyle();
        RefreshCells();
        NameCells();

        // Keep the Entry's MaxLength in sync with pin length
        hiddenEntry.MaxLength = count;

        // If current value is longer than new length, truncate
        if (!string.IsNullOrEmpty(Value) && Value.Length > count)
            UpdateValue(Value.Substring(0, count));
    }

    void ApplyCellStyle()
    {
        for (var i = 0; i < cellBorders.Count; i++)
        {
            var border = cellBorders[i];
            border.WidthRequest = CellSize;
            border.HeightRequest = CellSize;
            var shape = new Microsoft.Maui.Controls.Shapes.RoundRectangle();
            shape.SetCornerTokenOrValue(CellCornerRadius, ShinyThemeKeys.Shape.CornerSmallRadius);
            border.StrokeShape = shape;

            ApplyActiveCellStyle(i);
        }
    }

    /// <remarks>
    /// Every branch writes, colour or token. Leaving an unset colour unwritten drew no outline and no
    /// fill at all - the cells were simply not there unless an app-wide implicit Border style happened
    /// to stroke them - and a cell that had been focused kept its focused colour after focus moved on.
    /// </remarks>
    void ApplyActiveCellStyle(int index)
    {
        var border = cellBorders[index];
        var isCurrent = HighlightCurrentCell && index == CurrentIndex && hiddenEntry.IsFocused;

        // The focused cell takes the primary colour unless the consumer coloured the cells themselves
        // and said nothing about focus, in which case it keeps theirs - as it always has.
        if (isCurrent && (CellFocusedBorderColor is not null || CellBorderColor is null))
            ThemeBrush.Apply(border, Border.StrokeProperty, CellFocusedBorderColor, ShinyThemeKeys.Brush.Primary);
        else
            ThemeBrush.Apply(border, Border.StrokeProperty, CellBorderColor, ShinyThemeKeys.Brush.Outline);

        var fill = isCurrent ? CellFocusedBackgroundColor ?? CellBackgroundColor : CellBackgroundColor;
        ThemeProbe.Tint(border, VisualElement.BackgroundColorProperty, fill, ShinyThemeKeys.Color.SurfaceContainerLow);

        var ink = isCurrent ? CellFocusedTextColor ?? CellTextColor : CellTextColor;
        ThemeProbe.Tint(cellLabels[index], Label.TextColorProperty, ink, ShinyThemeKeys.Color.OnSurface);

        var style = isCurrent ? CellFocusedBorderStyle ?? CellBorderStyle : CellBorderStyle;
        var thickness = isCurrent && ThemeTokens.IsSet(CellFocusedBorderThickness)
            ? CellFocusedBorderThickness
            : CellBorderThickness;

        // Cleared first: a local value outranks a dynamic resource, so a cell that had been given the
        // focused (or None) width would otherwise keep it when it goes back to the theme token.
        border.ClearValue(Border.StrokeThicknessProperty);
        if (style == SecurityPinBorderStyle.None)
            border.StrokeThickness = 0;
        else
            border.SetTokenOrValue(Border.StrokeThicknessProperty, thickness, ShinyThemeKeys.Border.Thin);

        // StrokeDashArray is measured in multiples of the stroke width, so the pattern scales with it.
        if (style == SecurityPinBorderStyle.Dashed)
            border.StrokeDashArray = new DoubleCollection { 4, 2 };
        else if (style == SecurityPinBorderStyle.Dotted)
            border.StrokeDashArray = new DoubleCollection { 1, 1.5 };
        else
            border.ClearValue(Border.StrokeDashArrayProperty);
    }

    int CurrentIndex => Math.Min(Value?.Length ?? 0, cellBorders.Count - 1);

    void RefreshCells()
    {
        var value = Value ?? string.Empty;
        var hide = HideCharacter;
        var shouldHide = !string.IsNullOrEmpty(hide);

        for (var i = 0; i < cellLabels.Count; i++)
        {
            if (i < value.Length)
                cellLabels[i].Text = shouldHide ? hide : value[i].ToString();
            else
                cellLabels[i].Text = string.Empty;
        }

        for (var i = 0; i < cellBorders.Count; i++)
            ApplyActiveCellStyle(i);
    }

    void OnHiddenEntryTextChanged(object? sender, TextChangedEventArgs e)
    {
        var newText = e.NewTextValue ?? string.Empty;
        var max = Math.Max(1, Length);
        if (newText.Length > max)
            newText = newText.Substring(0, max);

        UpdateValue(newText);

        if (newText.Length >= max)
        {
            if (UseFeedback)
                FeedbackHelper.Execute(this, nameof(Completed), "LongPress");

            Completed?.Invoke(this, new SecurityPinCompletedEventArgs(newText));
        }
        else if (newText.Length > (e.OldTextValue?.Length ?? 0))
        {
            if (UseFeedback)
                FeedbackHelper.Execute(this, "DigitEntered");
        }
    }

    void OnHiddenEntryCompleted(object? sender, EventArgs e)
    {
        if ((Value?.Length ?? 0) >= Length)
            Completed?.Invoke(this, new SecurityPinCompletedEventArgs(Value ?? string.Empty));
    }

    void OnValueChangedExternally(string? newValue)
    {
        if (isUpdatingValue)
            return;

        var value = newValue ?? string.Empty;
        var max = Math.Max(1, Length);
        if (value.Length > max)
            value = value.Substring(0, max);

        if (hiddenEntry.Text != value)
        {
            isUpdatingValue = true;
            hiddenEntry.Text = value;
            isUpdatingValue = false;
        }

        RefreshCells();
    }

    void UpdateValue(string value)
    {
        isUpdatingValue = true;
        if (hiddenEntry.Text != value)
            hiddenEntry.Text = value;
        if (Value != value)
            Value = value;
        isUpdatingValue = false;

        RefreshCells();
    }
}

public class SecurityPinCompletedEventArgs(string value) : EventArgs
{
    public string Value { get; } = value;
}

/// <summary>How a <see cref="SecurityPin"/> cell's outline is drawn.</summary>
public enum SecurityPinBorderStyle
{
    Solid,
    Dashed,
    Dotted,
    None
}
