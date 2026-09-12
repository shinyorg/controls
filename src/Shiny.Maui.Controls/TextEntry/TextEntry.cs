using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using Shiny.Maui.Controls.Themes;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

[ContentProperty(nameof(RightTools))]
public partial class TextEntry : ContentView, IKeyboardAccessoryHost
{
    // Floating-label animation. The floated label is an M3 outlined "notch": it rides up onto the
    // top border line and paints the field background behind itself, so it never shares vertical
    // space with the text being typed.
    const double PlaceholderScaledSize = 0.75;
    const uint AnimationDuration = 150;
    const double NotchHorizontalPadding = 4;

    // Focus glow
    const float FocusGlowRadius = 8f;
    const float FocusGlowOpacity = 0.35f;

    // Sizing
    const double ClassicMinHeight = 38;
    const double FloatingMinHeight = 56;
    const double EntryHorizontalPadding = 12;
    const double InlineToolEntryPadding = 4;
    const double ClassicVerticalPadding = 6;
    const double FloatingVerticalPadding = 8;

    readonly Border outerBorder;
    readonly Microsoft.Maui.Controls.Shapes.RoundRectangle borderShape;
    readonly Grid contentGrid;
    readonly Grid entryArea;
    readonly HorizontalStackLayout leftToolsLayout;
    readonly HorizontalStackLayout rightToolsLayout;
    readonly BoxView leftSeparator;
    readonly BoxView rightSeparator;
    readonly Label placeholderLabel;
    readonly BorderlessEntry entry;
    readonly Label hintLabel;
    readonly Grid rootGrid;
    readonly Shadow focusGlow;

    NotifyCollectionChangedEventHandler? leftToolsChangedHandler;
    NotifyCollectionChangedEventHandler? rightToolsChangedHandler;

    bool suppressTextChanged;
    bool isPlaceholderUp;
    double placeholderRestY;
    double placeholderFloatY;

    // Internal event for tools (like ClearButtonTool) to observe text changes
    internal event EventHandler? InternalTextChanged;

    public TextEntry()
    {
        placeholderLabel = new Label
        {
            VerticalOptions = LayoutOptions.Start,
            HorizontalOptions = LayoutOptions.Start,
            LineBreakMode = LineBreakMode.TailTruncation,
            Padding = new Thickness(NotchHorizontalPadding, 0),
            InputTransparent = true,
            IsVisible = false,
            AnchorX = 0,   // scale from the left edge so the notch stays put horizontally
            AnchorY = 0.5  // ...and around its own centre so it stays welded to the border line
        }.WithFontSize(ShinyThemeKeys.Type.BodyLargeSize);

        entry = new BorderlessEntry
        {
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
            BackgroundColor = Colors.Transparent
        }.WithFontSize(ShinyThemeKeys.Type.BodyLargeSize);
        entry.SetDynamicResource(Entry.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
        entry.SetDynamicResource(Entry.PlaceholderColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        entry.TextChanged += OnEntryTextChanged;
        entry.Focused += OnEntryFocused;
        entry.Unfocused += OnEntryUnfocused;
        entry.Completed += OnEntryCompleted;

        entryArea = new Grid
        {
            Padding = new Thickness(EntryHorizontalPadding, ClassicVerticalPadding),
            Children = { entry }
        };

        leftToolsLayout = new HorizontalStackLayout
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Fill
        };
        leftToolsLayout.SizeChanged += (_, _) => UpdatePlaceholderGeometry();

        rightToolsLayout = new HorizontalStackLayout
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Fill
        };

        leftSeparator = new BoxView
        {
            WidthRequest = 1,
            VerticalOptions = LayoutOptions.Fill,
            IsVisible = false
        };

        rightSeparator = new BoxView
        {
            WidthRequest = 1,
            VerticalOptions = LayoutOptions.Fill,
            IsVisible = false
        };

        contentGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),  // left tools
                new ColumnDefinition(GridLength.Auto),  // left separator
                new ColumnDefinition(GridLength.Star),  // entry area
                new ColumnDefinition(GridLength.Auto),  // right separator
                new ColumnDefinition(GridLength.Auto)   // right tools
            },
            ColumnSpacing = 0,
            Padding = 0
        };
        contentGrid.Add(leftToolsLayout, 0, 0);
        contentGrid.Add(leftSeparator, 1, 0);
        contentGrid.Add(entryArea, 2, 0);
        contentGrid.Add(rightSeparator, 3, 0);
        contentGrid.Add(rightToolsLayout, 4, 0);

        borderShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerSmallRadius);
        focusGlow = new Shadow
        {
            Brush = new SolidColorBrush(Colors.Transparent),
            Offset = Point.Zero,
            Radius = FocusGlowRadius,
            Opacity = 0f
        };
        outerBorder = new Border
        {
            StrokeShape = borderShape,
            Padding = 0,
            Content = contentGrid,
            MinimumHeightRequest = ClassicMinHeight,
            Shadow = focusGlow
        }.WithStrokeThickness(ShinyThemeKeys.Border.Thin);
        outerBorder.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Surface);
        outerBorder.SizeChanged += (_, _) => UpdatePlaceholderGeometry();

        hintLabel = new Label
        {
            Margin = new Thickness(2, 4, 2, 0),
            IsVisible = false
        }.WithFontSize(ShinyThemeKeys.Type.BodySmallSize);

        rootGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            }
        };
        rootGrid.Add(outerBorder, 0, 0);
        // The floating label overlays the border rather than living inside the field, which is what
        // lets it sit ON the top stroke when floated instead of on top of the text being typed.
        rootGrid.Add(placeholderLabel, 0, 0);
        rootGrid.Add(hintLabel, 0, 1);
        placeholderLabel.SizeChanged += (_, _) => UpdatePlaceholderGeometry();

        Content = rootGrid;

        // Initialize tool collections
        LeftTools = new ObservableCollection<TextEntryTool>();
        RightTools = new ObservableCollection<TextEntryTool>();

        // Apply default variant (Classic) so the placeholder lives on the native entry.
        ApplyVariant();
        ApplyToolStyle();

        // Seed resting border/separator + placeholder colors (explicit-or-theme-token).
        ApplyBorderState();
        ApplyPlaceholderRestColor();

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(TextEntry));
    }

    // ---- Theme token defaults for each logical color (used when no explicit Color is set) ----
    const string PlaceholderColorToken = ShinyThemeKeys.Color.OnSurfaceVariant;
    const string FocusedPlaceholderColorToken = ShinyThemeKeys.Color.Primary;
    const string BorderColorToken = ShinyThemeKeys.Color.Outline;
    const string FocusedBorderColorToken = ShinyThemeKeys.Color.Primary;
    const string ErrorColorToken = ShinyThemeKeys.Color.Error;
    const string HintColorToken = ShinyThemeKeys.Color.OnSurfaceVariant;
    const string ToolAddonColorToken = ShinyThemeKeys.Color.SurfaceContainerHigh;

    // Applies an explicit color when set, otherwise binds the theme token, to the border stroke
    // (a Brush, so we drive a SolidColorBrush's Color) and the tool separators.
    void ApplyStroke(Color? explicitColor, string token)
    {
        if (explicitColor is Color c)
        {
            outerBorder.Stroke = c;
            leftSeparator.Color = c;
            rightSeparator.Color = c;
        }
        else
        {
            ThemeBrush.Apply(outerBorder, Border.StrokeProperty, token.AsBrush());
            leftSeparator.SetDynamicResource(BoxView.ColorProperty, token);
            rightSeparator.SetDynamicResource(BoxView.ColorProperty, token);
        }
    }

    // Re-applies the border/separator color for the current state (error > focused > resting).
    void ApplyBorderState()
    {
        if (HasError)
            ApplyStroke(ErrorColor, ErrorColorToken);
        else if (entry.IsFocused)
            ApplyStroke(FocusedBorderColor, FocusedBorderColorToken);
        else
            ApplyStroke(BorderColor, BorderColorToken);
    }

    // Resting (down) placeholder color.
    void ApplyPlaceholderRestColor()
    {
        if (PlaceholderColor is Color c)
            placeholderLabel.TextColor = c;
        else
            placeholderLabel.SetDynamicResource(Label.TextColorProperty, PlaceholderColorToken);
    }

    // Floated (up) placeholder color. M3 only accents the label while the field actually has focus -
    // a floated-but-unfocused label stays muted, otherwise every filled field on a form shouts.
    void ApplyPlaceholderFloatColor()
    {
        if (HasError)
        {
            if (ErrorColor is Color ec)
                placeholderLabel.TextColor = ec;
            else
                placeholderLabel.SetDynamicResource(Label.TextColorProperty, ErrorColorToken);
        }
        else if (!entry.IsFocused)
        {
            ApplyPlaceholderRestColor();
        }
        else if (FocusedPlaceholderColor is Color fc)
        {
            placeholderLabel.TextColor = fc;
        }
        else
        {
            placeholderLabel.SetDynamicResource(Label.TextColorProperty, FocusedPlaceholderColorToken);
        }
    }

    void ApplyPlaceholderStateColor()
    {
        if (isPlaceholderUp)
            ApplyPlaceholderFloatColor();
        else
            ApplyPlaceholderRestColor();
    }

    // The floated label has to mask the border stroke it sits on, so it paints the same colour as the
    // field. A transparent field background can't mask anything, so fall back to the surface token.
    // Only the floated state paints - at rest the label is over the field and a fill would be a block
    // of colour on a field that might be transparent by design.
    void ApplyNotchBackground()
    {
        if (!isPlaceholderUp || Variant != TextEntryVariant.Floating)
        {
            placeholderLabel.RemoveDynamicResource(VisualElement.BackgroundColorProperty);
            placeholderLabel.BackgroundColor = Colors.Transparent;
            return;
        }

        if (EntryBackgroundColor is Color c && c.Alpha > 0)
        {
            placeholderLabel.RemoveDynamicResource(VisualElement.BackgroundColorProperty);
            placeholderLabel.BackgroundColor = c;
        }
        else
        {
            placeholderLabel.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Surface);
        }
    }

    // Hint label color for the current state.
    void ApplyHintColor(bool error)
    {
        if (error)
        {
            if (ErrorColor is Color ec)
                hintLabel.TextColor = ec;
            else
                hintLabel.SetDynamicResource(Label.TextColorProperty, ErrorColorToken);
        }
        else if (HintColor is Color hc)
        {
            hintLabel.TextColor = hc;
        }
        else
        {
            hintLabel.SetDynamicResource(Label.TextColorProperty, HintColorToken);
        }
    }

    void ApplyVariant()
    {
        ApplyEntryAreaPadding();

        if (Variant == TextEntryVariant.Classic)
        {
            placeholderLabel.IsVisible = false;
            entry.Placeholder = Placeholder;
            outerBorder.MinimumHeightRequest = ClassicMinHeight;
            rootGrid.Padding = 0;
            ApplyNotchBackground();
        }
        else
        {
            placeholderLabel.IsVisible = true;
            placeholderLabel.Text = Placeholder;
            entry.Placeholder = string.Empty;
            outerBorder.MinimumHeightRequest = FloatingMinHeight;

            // Snap the placeholder to the correct rest position for the current text.
            isPlaceholderUp = !string.IsNullOrEmpty(entry.Text) || entry.IsFocused;
            placeholderLabel.Scale = isPlaceholderUp ? PlaceholderScaledSize : 1;
            ApplyPlaceholderStateColor();
            ApplyNotchBackground();
            UpdatePlaceholderGeometry();
        }
    }

    // In Inline tool mode the icons already provide the visual inset, so the field's own padding
    // shrinks on that side - otherwise a leading icon sits a full 24pt away from the text.
    void ApplyEntryAreaPadding()
    {
        var vertical = Variant == TextEntryVariant.Classic ? ClassicVerticalPadding : FloatingVerticalPadding;
        var inline = ToolStyle == TextEntryToolStyle.Inline;
        var left = inline && leftToolsLayout.IsVisible ? InlineToolEntryPadding : EntryHorizontalPadding;
        var right = inline && rightToolsLayout.IsVisible ? InlineToolEntryPadding : EntryHorizontalPadding;

        var padding = new Thickness(left, vertical, right, vertical);
        if (entryArea.Padding != padding)
            entryArea.Padding = padding;
    }

    /// <summary>
    /// Recomputes where the floating label rests and where it floats to. Both depend on measured
    /// sizes (the label's own height, the field's height, and how much room the leading tools take),
    /// so this runs on every relevant SizeChanged as well as on variant changes.
    /// </summary>
    /// <param name="snap">
    /// Move the label to the target straight away. False while an animation is about to run - snapping
    /// first would put the label at its destination and leave nothing to animate.
    /// </param>
    void UpdatePlaceholderGeometry(bool snap = true)
    {
        if (Variant != TextEntryVariant.Floating)
            return;

        var labelHeight = placeholderLabel.Height;
        var fieldHeight = outerBorder.Height;
        if (labelHeight <= 0 || fieldHeight <= 0)
        {
            // The label's height and the field's height land in either order on the first layout
            // pass, and this only runs from their own SizeChanged - so whichever arrives first bails
            // here, and the second one may already have been raised. That leaves the label sitting
            // unpositioned at the grid origin until something else forces a re-layout (resizing the
            // window fixes it, which is exactly the tell). Retry on the next tick instead.
            this.QueueGeometryRetry(snap);
            return;
        }

        this.geometryRetries = 0;

        // Rest: vertically centred in the field. Float: centred on the top border stroke.
        placeholderRestY = (fieldHeight - labelHeight) / 2;
        placeholderFloatY = -labelHeight / 2;

        // Reserve the half-label that pokes above the border so the notch is never clipped by an
        // ancestor and never overlaps whatever sits above this control.
        var topPadding = labelHeight / 2;
        if (Math.Abs(rootGrid.Padding.Top - topPadding) > 0.5)
            rootGrid.Padding = new Thickness(0, topPadding, 0, 0);

        var leftInset = leftToolsLayout.IsVisible ? leftToolsLayout.Width : 0;
        var left = Math.Max(0, leftInset + entryArea.Padding.Left - NotchHorizontalPadding);
        if (Math.Abs(placeholderLabel.Margin.Left - left) > 0.5)
            placeholderLabel.Margin = new Thickness(left, 0, 0, 0);

        var available = outerBorder.Width - left - EntryHorizontalPadding;
        if (available > 0 && Math.Abs(placeholderLabel.MaximumWidthRequest - available) > 0.5)
            placeholderLabel.MaximumWidthRequest = available;

        if (snap)
            placeholderLabel.TranslationY = isPlaceholderUp ? placeholderFloatY : placeholderRestY;
    }

    int geometryRetries;
    bool geometryRetryQueued;

    /// <summary>
    /// Re-runs <see cref="UpdatePlaceholderGeometry"/> on the next tick, a bounded number of times,
    /// so a first pass that ran before both sizes were measured is not the last word. Bounded because
    /// a control that is never given a size (collapsed, or off an inactive tab) would otherwise
    /// re-queue forever; once it is sized its own SizeChanged brings us back here anyway.
    /// </summary>
    void QueueGeometryRetry(bool snap)
    {
        const int MaxGeometryRetries = 3;
        if (this.geometryRetryQueued || this.geometryRetries >= MaxGeometryRetries)
            return;

        this.geometryRetryQueued = true;
        this.geometryRetries++;
        this.Dispatcher.Dispatch(() =>
        {
            this.geometryRetryQueued = false;
            this.UpdatePlaceholderGeometry(snap);
        });
    }

    void ApplyPlaceholder(string text)
    {
        placeholderLabel.Text = text;
        if (Variant == TextEntryVariant.Classic)
            entry.Placeholder = text;
    }

    // The glow is a single Shadow instance created with the control and never swapped out.
    // Assigning Border.Shadow re-applies the platform shadow, and on Android that tears the
    // border's view down far enough to clear focus from the entry inside it - so a tap would
    // focus the entry and lose it again in the same frame, making the control impossible to
    // type into. Only the brush and opacity are touched from here on.
    void ShowGlow(Color? color, string token)
    {
        // Shadow.Brush is Brush-typed too, and a Shadow assigned to an element is parented by it, so
        // it sits in the resource chain like any other child.
        ThemeBrush.Apply(focusGlow, Shadow.BrushProperty, color, token.AsBrush());

        focusGlow.Opacity = FocusGlowOpacity;
    }

    void HideGlow() => focusGlow.Opacity = 0f;

    void OnEntryTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (suppressTextChanged) return;

        suppressTextChanged = true;

        if (!string.IsNullOrEmpty(Mask))
        {
            var masked = MaskedInput.Apply(entry.Text, Mask);

            Text = masked.Raw;
            FormattedText = masked.Formatted;
            entry.Text = masked.Formatted;

            // Set cursor position after formatting
            Dispatcher.Dispatch(() => entry.CursorPosition = masked.CursorPosition);
        }
        else
        {
            Text = entry.Text ?? string.Empty;
        }

        suppressTextChanged = false;

        InternalTextChanged?.Invoke(this, EventArgs.Empty);
        TextChanged?.Invoke(this, new TextChangedEventArgs(e.OldTextValue, Text));
        if (TextChangedCommand?.CanExecute(Text) == true)
            TextChangedCommand.Execute(Text);

        UpdateCharacterCount();
    }

    void OnEntryFocused(object? sender, FocusEventArgs e)
    {
        if (Variant == TextEntryVariant.Floating)
        {
            AnimatePlaceholder(true);
            ApplyPlaceholderStateColor();
        }

        if (HasError)
            ApplyStroke(ErrorColor, ErrorColorToken);
        else
            ApplyStroke(FocusedBorderColor, FocusedBorderColorToken);

        outerBorder.StrokeThickness = FocusedBorderThickness;
        if (HasError)
            ShowGlow(ErrorColor, ErrorColorToken);
        else
            ShowGlow(FocusedBorderColor, FocusedBorderColorToken);
    }

    void OnEntryUnfocused(object? sender, FocusEventArgs e)
    {
        if (Variant == TextEntryVariant.Floating)
        {
            if (string.IsNullOrEmpty(entry.Text))
                AnimatePlaceholder(false);
            ApplyPlaceholderStateColor();
        }

        if (HasError)
            ApplyStroke(ErrorColor, ErrorColorToken);
        else
            ApplyStroke(BorderColor, BorderColorToken);

        outerBorder.StrokeThickness = BorderThickness;
        if (HasError)
            ShowGlow(ErrorColor, ErrorColorToken);
        else
            HideGlow();
    }

    void OnEntryCompleted(object? sender, EventArgs e)
    {
        Completed?.Invoke(this, e);
        if (CompletedCommand?.CanExecute(Text) == true)
            CompletedCommand.Execute(Text);
    }

    async void AnimatePlaceholder(bool up)
    {
        if (Variant != TextEntryVariant.Floating) return;
        if (up == isPlaceholderUp) return;
        isPlaceholderUp = up;

        // The notch mask goes on before the label leaves the field and only comes off once it is
        // fully back inside it, so the stroke is never visible through a half-scaled label.
        if (up)
            ApplyNotchBackground();

        UpdatePlaceholderGeometry(snap: false);

        await Task.WhenAll(
            placeholderLabel.TranslateToAsync(0, up ? placeholderFloatY : placeholderRestY, AnimationDuration, Easing.CubicOut),
            placeholderLabel.ScaleToAsync(up ? PlaceholderScaledSize : 1, AnimationDuration, Easing.CubicOut)
        );

        if (!up && !isPlaceholderUp)
            ApplyNotchBackground();

        ApplyPlaceholderStateColor();
    }

    void UpdateCharacterCount()
    {
        if (!ShowCharacterCount || MaxLength <= 0) return;
        var count = entry.Text?.Length ?? 0;
        // Show in hint when no error
        if (!HasError)
        {
            hintLabel.Text = $"{count}/{MaxLength}";
            ApplyHintColor(count >= MaxLength);
            hintLabel.IsVisible = true;
        }
    }

    void SyncHint()
    {
        if (HasError && !string.IsNullOrEmpty(HintText))
        {
            hintLabel.Text = HintText;
            ApplyHintColor(true);
            hintLabel.IsVisible = true;
        }
        else if (!string.IsNullOrEmpty(HintText))
        {
            hintLabel.Text = HintText;
            ApplyHintColor(false);
            hintLabel.IsVisible = true;
        }
        else if (ShowCharacterCount && MaxLength > 0)
        {
            UpdateCharacterCount();
        }
        else
        {
            hintLabel.IsVisible = false;
        }

        // Border color: error > focused > resting.
        if (HasError)
            ApplyStroke(ErrorColor, ErrorColorToken);
        else if (entry.IsFocused)
            ApplyStroke(FocusedBorderColor, FocusedBorderColorToken);
        else
            ApplyStroke(BorderColor, BorderColorToken);

        if (HasError)
            ShowGlow(ErrorColor, ErrorColorToken);
        else if (entry.IsFocused)
            ShowGlow(FocusedBorderColor, FocusedBorderColorToken);
        else
            HideGlow();

        ApplyPlaceholderStateColor();
    }

    // ---- Tools ----------------------------------------------------------------------------

    void OnToolsChanged(IList<TextEntryTool>? oldTools, IList<TextEntryTool>? newTools, HorizontalStackLayout layout)
    {
        var isLeft = layout == leftToolsLayout;
        ref var handler = ref isLeft ? ref leftToolsChangedHandler : ref rightToolsChangedHandler;

        if (oldTools is INotifyCollectionChanged oldNcc && handler is not null)
            oldNcc.CollectionChanged -= handler;

        DetachTools(oldTools);
        RebuildTools(newTools, layout);
        AttachTools(newTools);

        if (newTools is INotifyCollectionChanged ncc)
        {
            handler = (_, _) =>
            {
                DetachTools(newTools);
                RebuildTools(newTools, layout);
                AttachTools(newTools);
            };
            ncc.CollectionChanged += handler;
        }
        else
        {
            handler = null;
        }
    }

    void RebuildTools(IList<TextEntryTool>? tools, HorizontalStackLayout layout)
    {
        layout.Children.Clear();
        layout.IsVisible = tools is { Count: > 0 };

        if (layout.IsVisible)
        {
            foreach (var tool in tools!)
            {
                tool.ParentEntry = this;
                tool.ApplyToolStyle(ToolStyle);
                layout.Children.Add(tool);
            }
        }

        ApplyToolStyle();
    }

    // Paints the tool rails for the current ToolStyle. Inline is the default: no addon block, no
    // separator - the icons sit on the field itself.
    void ApplyToolStyle()
    {
        var addon = ToolStyle == TextEntryToolStyle.Addon;

        foreach (var layout in new[] { leftToolsLayout, rightToolsLayout })
        {
            if (addon)
            {
                layout.SetDynamicResource(VisualElement.BackgroundColorProperty, ToolAddonColorToken);
            }
            else
            {
                // Drop the token binding as well as the colour - leaving it attached would repaint the
                // addon surface behind inline tools the next time the theme changed.
                layout.RemoveDynamicResource(VisualElement.BackgroundColorProperty);
                layout.BackgroundColor = Colors.Transparent;
            }
        }

        leftSeparator.IsVisible = addon && leftToolsLayout.IsVisible;
        rightSeparator.IsVisible = addon && rightToolsLayout.IsVisible;

        foreach (var tool in leftToolsLayout.Children.OfType<TextEntryTool>())
            tool.ApplyToolStyle(ToolStyle);
        foreach (var tool in rightToolsLayout.Children.OfType<TextEntryTool>())
            tool.ApplyToolStyle(ToolStyle);

        ApplyEntryAreaPadding();
        UpdatePlaceholderGeometry();
    }

    void AttachTools(IList<TextEntryTool>? tools)
    {
        if (tools is null) return;
        foreach (var tool in tools)
        {
            if (tool is ITextEntryAwareTool aware)
                aware.Attach(this);
        }
    }

    void DetachTools(IList<TextEntryTool>? tools)
    {
        if (tools is null) return;
        foreach (var tool in tools)
        {
            if (tool is ITextEntryAwareTool aware)
                aware.Detach();
        }
    }

    // ---- Mask -----------------------------------------------------------------------------

    void OnMaskChanged()
    {
        if (!string.IsNullOrEmpty(Mask))
        {
            entry.Keyboard = Keyboard.Numeric;
            entry.MaxLength = Mask.Length;

            // Reformat existing text
            if (!string.IsNullOrEmpty(Text))
            {
                suppressTextChanged = true;
                var formatted = TextEntryMaskHelper.ApplyMask(Text, Mask);
                FormattedText = formatted;
                entry.Text = formatted;
                suppressTextChanged = false;
            }
        }
        else
        {
            // Mask removed - restore raw text to entry
            entry.MaxLength = MaxLength;
            suppressTextChanged = true;
            entry.Text = Text;
            FormattedText = string.Empty;
            suppressTextChanged = false;
        }
    }

    void ApplyMaskToEntry()
    {
        if (string.IsNullOrEmpty(Mask)) return;

        var formatted = TextEntryMaskHelper.ApplyMask(Text, Mask);
        FormattedText = formatted;

        suppressTextChanged = true;
        entry.Text = formatted;
        suppressTextChanged = false;

        if (!string.IsNullOrEmpty(formatted) && !isPlaceholderUp)
            AnimatePlaceholder(true);
        else if (string.IsNullOrEmpty(formatted) && !entry.IsFocused && isPlaceholderUp)
            AnimatePlaceholder(false);
    }

    // ---- Input assistance -----------------------------------------------------------------

    // Autofill, autocorrect and predictive text are three separate platform switches. Turning
    // AutoComplete off kills all of them, because the reason anyone reaches for it - serials, codes,
    // SKUs, usernames - is broken by any one of the three.
    void ApplyInputAssistance()
    {
        var auto = IsAutoCompleteEnabled;
        entry.IsSpellCheckEnabled = auto && IsSpellCheckEnabled;
        entry.IsTextPredictionEnabled = auto && IsTextPredictionEnabled;
        entry.IsAutoCompleteEnabled = auto;
    }

    // ---- Keyboard accessory ---------------------------------------------------------------

    KeyboardAccessoryBinder? accessoryBinder;
    KeyboardAccessoryView? presetAccessory;

    // Created on first use, so a field with no accessory costs nothing. The binder subscribes to the
    // inner entry's handler/focus events itself, so creating it late is safe - anything it missed is
    // re-applied by the SetBar below.
    KeyboardAccessoryBinder AccessoryBinder => accessoryBinder ??= new KeyboardAccessoryBinder(this, entry, this);

    // An explicit Accessory always wins; a preset is materialized once and cached, because on iOS a
    // UIView can only have one superview and rebuilding the bar per focus would churn it.
    void SyncAccessory()
    {
        var bar = Accessory;
        if (bar is null && AccessoryPreset != KeyboardAccessoryPreset.None)
            bar = presetAccessory ??= KeyboardAccessoryView.FromPreset(AccessoryPreset);

        if (bar is null && accessoryBinder is null)
            return;

        AccessoryBinder.SetBar(bar);
    }

    void ResetPresetAccessory()
    {
        presetAccessory = null;
        SyncAccessory();
    }

    VisualElement IKeyboardAccessoryHost.NavigationElement => this;

    // Unfocus() is shadowed on this type - the interface call has to reach the shadowing member, not
    // VisualElement's, or it would unfocus the wrapper and leave the keyboard up.
    void IKeyboardAccessoryHost.DismissKeyboard() => Unfocus();

    // Public API
    public event EventHandler<TextChangedEventArgs>? TextChanged;
    public event EventHandler? Completed;

    public new bool Focus() => entry.Focus();
    public new void Unfocus() => entry.Unfocus();

    /// <summary>True while the inner text input holds focus.</summary>
    public bool IsInputFocused => entry.IsFocused;
}
