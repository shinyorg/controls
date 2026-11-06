using System.Windows.Input;
using Shiny.Maui.Controls.Themes;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

public partial class TextEntry
{
    // Text
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(TextEntry), string.Empty,
        BindingMode.TwoWay,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
            var te = (TextEntry)b;
            if (te.suppressTextChanged) return;

            if (!string.IsNullOrEmpty(te.Mask))
            {
                te.ApplyMaskToEntry();
            }
            else
            {
                te.suppressTextChanged = true;
                te.entry.Text = (string)n;
                te.suppressTextChanged = false;

                // The write above was suppressed, so OnEntryTextChanged never saw it.
                te.SyncPlaceholderToText();
            }

            te.InternalTextChanged?.Invoke(te, EventArgs.Empty);
        }));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    // Placeholder
    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder), typeof(string), typeof(TextEntry), string.Empty,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
                ((TextEntry)b).ApplyPlaceholder((string)n);
            }));
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }

    // PlaceholderColor — muted placeholder/text → on-surface-variant token (was #6C757D)
    public static readonly BindableProperty PlaceholderColorProperty = BindableProperty.Create(
        nameof(PlaceholderColor), typeof(Color), typeof(TextEntry), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
            var te = (TextEntry)b;
            if (!te.isPlaceholderUp)
                te.ApplyPlaceholderRestColor();
        }));
    public Color? PlaceholderColor { get => (Color?)GetValue(PlaceholderColorProperty); set => SetValue(PlaceholderColorProperty, value); }

    // FocusedPlaceholderColor — accent → primary token (was #0D6EFD)
    public static readonly BindableProperty FocusedPlaceholderColorProperty = BindableProperty.Create(
        nameof(FocusedPlaceholderColor), typeof(Color), typeof(TextEntry), null);
    public Color? FocusedPlaceholderColor { get => (Color?)GetValue(FocusedPlaceholderColorProperty); set => SetValue(FocusedPlaceholderColorProperty, value); }

    // Variant — picks Classic (.form-control) or Floating (.form-floating)
    public static readonly BindableProperty VariantProperty = BindableProperty.Create(
        nameof(Variant), typeof(TextEntryVariant), typeof(TextEntry), TextEntryVariant.Classic,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
                ((TextEntry)b).ApplyVariant();
            }));
    public TextEntryVariant Variant { get => (TextEntryVariant)GetValue(VariantProperty); set => SetValue(VariantProperty, value); }

    // Border — resting/valid border → outline token (was #CED4DA)
    public static readonly BindableProperty BorderColorProperty = BindableProperty.Create(
        nameof(BorderColor), typeof(Color), typeof(TextEntry), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
            var te = (TextEntry)b;
            if (!te.entry.IsFocused && !te.HasError)
                te.ApplyBorderState();
        }));
    public Color? BorderColor { get => (Color?)GetValue(BorderColorProperty); set => SetValue(BorderColorProperty, value); }

    // Focused border → primary token (was #86B7FE)
    public static readonly BindableProperty FocusedBorderColorProperty = BindableProperty.Create(
        nameof(FocusedBorderColor), typeof(Color), typeof(TextEntry), null);
    public Color? FocusedBorderColor { get => (Color?)GetValue(FocusedBorderColorProperty); set => SetValue(FocusedBorderColorProperty, value); }

    public static readonly BindableProperty BorderThicknessProperty = BindableProperty.Create(
        nameof(BorderThickness), typeof(double), typeof(TextEntry), 1.0,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
            var te = (TextEntry)b;
            if (!te.entry.IsFocused)
                te.outerBorder.StrokeThickness = (double)n;
        }));
    public double BorderThickness { get => (double)GetValue(BorderThicknessProperty); set => SetValue(BorderThicknessProperty, value); }

    public static readonly BindableProperty FocusedBorderThicknessProperty = BindableProperty.Create(
        nameof(FocusedBorderThickness), typeof(double), typeof(TextEntry), 2.0);
    public double FocusedBorderThickness { get => (double)GetValue(FocusedBorderThicknessProperty); set => SetValue(FocusedBorderThicknessProperty, value); }

    // Bootstrap radius is .375rem = 6px
    public static readonly BindableProperty CornerRadiusProperty = BindableProperty.Create(
        nameof(CornerRadius), typeof(CornerRadius), typeof(TextEntry), new CornerRadius(6),
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
                ((TextEntry)b).borderShape.CornerRadius = (CornerRadius)n;
            }));
    public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }

    // Entry surface — defaults to the surface token (was white)
    public static readonly BindableProperty EntryBackgroundColorProperty = BindableProperty.Create(
        nameof(EntryBackgroundColor), typeof(Color), typeof(TextEntry), null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
            var te = (TextEntry)b;
            ThemeProbe.Tint(te.outerBorder, VisualElement.BackgroundColorProperty, n as Color, ShinyThemeKeys.Color.Surface);

            // The floating label masks the border stroke with this colour, so it has to follow.
            if (te.isPlaceholderUp)
                te.ApplyNotchBackground();
        }));
    public Color? EntryBackgroundColor { get => (Color?)GetValue(EntryBackgroundColorProperty); set => SetValue(EntryBackgroundColorProperty, value); }

    // Font — Bootstrap base 1rem = 16
    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(
        nameof(FontSize), typeof(double), typeof(TextEntry), 16.0,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
            var te = (TextEntry)b;
            var s = (double)n;
            te.entry.FontSize = s;
            te.placeholderLabel.FontSize = s;
        }));
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

    public static readonly BindableProperty FontFamilyProperty = BindableProperty.Create(
        nameof(FontFamily), typeof(string), typeof(TextEntry), null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
            var te = (TextEntry)b;
            te.entry.FontFamily = n as string;
            te.placeholderLabel.FontFamily = n as string;
        }));
    public string? FontFamily { get => (string?)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }

    public static readonly BindableProperty FontAttributesProperty = BindableProperty.Create(
        nameof(FontAttributes), typeof(FontAttributes), typeof(TextEntry), FontAttributes.None,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
                ((TextEntry)b).entry.FontAttributes = (FontAttributes)n;
            }));
    public FontAttributes FontAttributes { get => (FontAttributes)GetValue(FontAttributesProperty); set => SetValue(FontAttributesProperty, value); }

    // TextColor — entry body text → on-surface token (was #212529)
    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor), typeof(Color), typeof(TextEntry), null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
            var te = (TextEntry)b;
            ThemeProbe.Tint(te.entry, Entry.TextColorProperty, n as Color, ShinyThemeKeys.Color.OnSurface);
        }));
    public Color? TextColor { get => (Color?)GetValue(TextColorProperty); set => SetValue(TextColorProperty, value); }

    // Entry behavior
    public static readonly BindableProperty IsReadOnlyProperty = BindableProperty.Create(
        nameof(IsReadOnly), typeof(bool), typeof(TextEntry), false,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
                ((TextEntry)b).entry.IsReadOnly = (bool)n;
            }));
    public bool IsReadOnly { get => (bool)GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }

    public static readonly BindableProperty IsPasswordProperty = BindableProperty.Create(
        nameof(IsPassword), typeof(bool), typeof(TextEntry), false,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
                ((TextEntry)b).entry.IsPassword = (bool)n;
            }));
    public bool IsPassword { get => (bool)GetValue(IsPasswordProperty); set => SetValue(IsPasswordProperty, value); }

    public static readonly BindableProperty ReturnTypeProperty = BindableProperty.Create(
        nameof(ReturnType), typeof(ReturnType), typeof(TextEntry), ReturnType.Default,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
                ((TextEntry)b).entry.ReturnType = (ReturnType)n;
            }));
    public ReturnType ReturnType { get => (ReturnType)GetValue(ReturnTypeProperty); set => SetValue(ReturnTypeProperty, value); }

    public static readonly BindableProperty KeyboardProperty = BindableProperty.Create(
        nameof(Keyboard), typeof(Keyboard), typeof(TextEntry), Keyboard.Default,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
            if (n is Keyboard k)
                ((TextEntry)b).entry.Keyboard = k;
        }));
    public Keyboard Keyboard { get => (Keyboard)GetValue(KeyboardProperty); set => SetValue(KeyboardProperty, value); }

    public static readonly BindableProperty MaxLengthProperty = BindableProperty.Create(
        nameof(MaxLength), typeof(int), typeof(TextEntry), int.MaxValue,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
                ((TextEntry)b).entry.MaxLength = (int)n;
            }));
    public int MaxLength { get => (int)GetValue(MaxLengthProperty); set => SetValue(MaxLengthProperty, value); }

    // Hint / Validation
    public static readonly BindableProperty HintTextProperty = BindableProperty.Create(
        nameof(HintText), typeof(string), typeof(TextEntry), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
                ((TextEntry)b).SyncHint();
            }));
    public string? HintText { get => (string?)GetValue(HintTextProperty); set => SetValue(HintTextProperty, value); }

    // Hint/helper text → on-surface-variant token (was Grey)
    public static readonly BindableProperty HintColorProperty = BindableProperty.Create(
        nameof(HintColor), typeof(Color), typeof(TextEntry), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
                ((TextEntry)b).SyncHint();
            }));
    public Color? HintColor { get => (Color?)GetValue(HintColorProperty); set => SetValue(HintColorProperty, value); }

    public static readonly BindableProperty HasErrorProperty = BindableProperty.Create(
        nameof(HasError), typeof(bool), typeof(TextEntry), false,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
                ((TextEntry)b).SyncHint();
            }));
    public bool HasError { get => (bool)GetValue(HasErrorProperty); set => SetValue(HasErrorProperty, value); }

    // Error/validation → error token (was #DC3545)
    public static readonly BindableProperty ErrorColorProperty = BindableProperty.Create(
        nameof(ErrorColor), typeof(Color), typeof(TextEntry), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
                ((TextEntry)b).SyncHint();
            }));
    public Color? ErrorColor { get => (Color?)GetValue(ErrorColorProperty); set => SetValue(ErrorColorProperty, value); }

    public static readonly BindableProperty ShowCharacterCountProperty = BindableProperty.Create(
        nameof(ShowCharacterCount), typeof(bool), typeof(TextEntry), false,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
                ((TextEntry)b).SyncHint();
            }));
    public bool ShowCharacterCount { get => (bool)GetValue(ShowCharacterCountProperty); set => SetValue(ShowCharacterCountProperty, value); }

    // Input assistance — autofill / autocorrect / predictive text
    /// <summary>
    /// When false, the platform's autofill and suggestion machinery is switched off for this field:
    /// no autofill dropdown, no autocorrect, no predictive text, no spell check. Use it for serials,
    /// coupon codes, SKUs, usernames — anything the OS "helpfully" rewrites. Defaults to true.
    /// </summary>
    public static readonly BindableProperty IsAutoCompleteEnabledProperty = BindableProperty.Create(
        nameof(IsAutoCompleteEnabled), typeof(bool), typeof(TextEntry), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
                ((TextEntry)b).ApplyInputAssistance();
            }));
    public bool IsAutoCompleteEnabled { get => (bool)GetValue(IsAutoCompleteEnabledProperty); set => SetValue(IsAutoCompleteEnabledProperty, value); }

    /// <summary>Spell checking. Forced off while <see cref="IsAutoCompleteEnabled"/> is false.</summary>
    public static readonly BindableProperty IsSpellCheckEnabledProperty = BindableProperty.Create(
        nameof(IsSpellCheckEnabled), typeof(bool), typeof(TextEntry), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
                ((TextEntry)b).ApplyInputAssistance();
            }));
    public bool IsSpellCheckEnabled { get => (bool)GetValue(IsSpellCheckEnabledProperty); set => SetValue(IsSpellCheckEnabledProperty, value); }

    /// <summary>Predictive text / the keyboard suggestion strip. Forced off while <see cref="IsAutoCompleteEnabled"/> is false.</summary>
    public static readonly BindableProperty IsTextPredictionEnabledProperty = BindableProperty.Create(
        nameof(IsTextPredictionEnabled), typeof(bool), typeof(TextEntry), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
                ((TextEntry)b).ApplyInputAssistance();
            }));
    public bool IsTextPredictionEnabled { get => (bool)GetValue(IsTextPredictionEnabledProperty); set => SetValue(IsTextPredictionEnabledProperty, value); }

    // Tools
    /// <summary>
    /// How docked tools are painted — <see cref="TextEntryToolStyle.Inline"/> (default) puts them on
    /// the field itself, <see cref="TextEntryToolStyle.Addon"/> restores the Bootstrap input-group block.
    /// </summary>
    public static readonly BindableProperty ToolStyleProperty = BindableProperty.Create(
        nameof(ToolStyle), typeof(TextEntryToolStyle), typeof(TextEntry), TextEntryToolStyle.Inline,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
                ((TextEntry)b).ApplyToolStyle();
            }));
    public TextEntryToolStyle ToolStyle { get => (TextEntryToolStyle)GetValue(ToolStyleProperty); set => SetValue(ToolStyleProperty, value); }

    public static readonly BindableProperty LeftToolsProperty = BindableProperty.Create(
        nameof(LeftTools), typeof(IList<TextEntryTool>), typeof(TextEntry), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
                ((TextEntry)b).OnToolsChanged(
            o as IList<TextEntryTool>,
            n as IList<TextEntryTool>,
            ((TextEntry)b).leftToolsLayout);
            }));
    public IList<TextEntryTool>? LeftTools { get => (IList<TextEntryTool>?)GetValue(LeftToolsProperty); set => SetValue(LeftToolsProperty, value); }

    public static readonly BindableProperty RightToolsProperty = BindableProperty.Create(
        nameof(RightTools), typeof(IList<TextEntryTool>), typeof(TextEntry), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
                ((TextEntry)b).OnToolsChanged(
            o as IList<TextEntryTool>,
            n as IList<TextEntryTool>,
            ((TextEntry)b).rightToolsLayout);
            }));
    public IList<TextEntryTool>? RightTools { get => (IList<TextEntryTool>?)GetValue(RightToolsProperty); set => SetValue(RightToolsProperty, value); }

    // Mask
    public static readonly BindableProperty MaskProperty = BindableProperty.Create(
        nameof(Mask), typeof(string), typeof(TextEntry), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
                ((TextEntry)b).OnMaskChanged();
            }));
    public string? Mask { get => (string?)GetValue(MaskProperty); set => SetValue(MaskProperty, value); }

    public static readonly BindableProperty FormattedTextProperty = BindableProperty.Create(
        nameof(FormattedText), typeof(string), typeof(TextEntry), string.Empty);
    public string FormattedText { get => (string)GetValue(FormattedTextProperty); private set => SetValue(FormattedTextProperty, value); }

    // Keyboard accessory
    /// <summary>
    /// A bar docked to the top edge of the soft keyboard while this field has focus. iOS uses the
    /// real <c>UIResponder.InputAccessoryView</c>; Android renders the same bar in-window, driven by
    /// the IME window insets. No-op on every other head — see the docs for the platform matrix.
    /// </summary>
    public static readonly BindableProperty AccessoryProperty = BindableProperty.Create(
        nameof(Accessory), typeof(KeyboardAccessoryView), typeof(TextEntry), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
                ((TextEntry)b).SyncAccessory();
            }));
    public KeyboardAccessoryView? Accessory { get => (KeyboardAccessoryView?)GetValue(AccessoryProperty); set => SetValue(AccessoryProperty, value); }

    /// <summary>
    /// A stock accessory bar, used when <see cref="Accessory"/> is not set. The usual reason to set
    /// this is <see cref="Keyboard.Numeric"/> — the iOS number pad has no return key at all, so
    /// without a Done button there is no way to dismiss it.
    /// </summary>
    public static readonly BindableProperty AccessoryPresetProperty = BindableProperty.Create(
        nameof(AccessoryPreset), typeof(KeyboardAccessoryPreset), typeof(TextEntry), KeyboardAccessoryPreset.None,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TextEntry), () =>
            {
                ((TextEntry)b).ResetPresetAccessory();
            }));
    public KeyboardAccessoryPreset AccessoryPreset { get => (KeyboardAccessoryPreset)GetValue(AccessoryPresetProperty); set => SetValue(AccessoryPresetProperty, value); }

    /// <summary>
    /// Groups fields for accessory prev/next navigation. Fields with the same group navigate to each
    /// other; ungrouped fields navigate across the whole page.
    /// </summary>
    public static readonly BindableProperty FieldGroupProperty = BindableProperty.Create(
        nameof(FieldGroup), typeof(string), typeof(TextEntry), null,
        propertyChanged: (b, _, n) => b.SetValue(KeyboardField.GroupProperty, n));
    public string? FieldGroup { get => (string?)GetValue(FieldGroupProperty); set => SetValue(FieldGroupProperty, value); }

    // Commands
    public static readonly BindableProperty TextChangedCommandProperty = BindableProperty.Create(
        nameof(TextChangedCommand), typeof(ICommand), typeof(TextEntry));
    public ICommand? TextChangedCommand { get => (ICommand?)GetValue(TextChangedCommandProperty); set => SetValue(TextChangedCommandProperty, value); }

    public static readonly BindableProperty CompletedCommandProperty = BindableProperty.Create(
        nameof(CompletedCommand), typeof(ICommand), typeof(TextEntry));
    public ICommand? CompletedCommand { get => (ICommand?)GetValue(CompletedCommandProperty); set => SetValue(CompletedCommandProperty, value); }
}
