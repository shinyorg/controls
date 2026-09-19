using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.Maui.Controls;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.Cells;

public class EntryCell : CellBase, IKeyboardAccessoryHost
{
    /// <summary>
    /// Children are built by CellBase's constructor (BuildLayout -> the virtual
    /// CreateAccessoryView override above), so by the time this body runs they exist.
    /// Marking ready here replays any property an implicit Style applied before
    /// construction - see StyleGuard.
    /// </summary>
    public EntryCell() => StyleGuard.MarkReady(this, typeof(EntryCell));

    static readonly Style CleanEntryStyle = new(typeof(BorderlessEntry))
    {
        Setters =
        {
            new Setter { Property = Entry.BackgroundColorProperty, Value = Colors.Transparent },
            new Setter { Property = VisualElement.HeightRequestProperty, Value = 40d },
        }
    };

    BorderlessEntry entry = default!;

    public static readonly BindableProperty ValueTextProperty = BindableProperty.Create(
        nameof(ValueText), typeof(string), typeof(EntryCell), string.Empty,
        BindingMode.TwoWay,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(EntryCell), () =>
            {
                ((EntryCell)b).OnValueTextChanged((string)n);
            }));

    public static readonly BindableProperty ValueTextColorProperty = BindableProperty.Create(
        nameof(ValueTextColor), typeof(Color), typeof(EntryCell), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(EntryCell), () =>
            {
                ((EntryCell)b).UpdateEntryColor();
            }));

    public static readonly BindableProperty ValueTextFontSizeProperty = BindableProperty.Create(
        nameof(ValueTextFontSize), typeof(double), typeof(EntryCell), -1d,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(EntryCell), () =>
            {
                ((EntryCell)b).UpdateEntryFontSize();
            }));

    public static readonly BindableProperty ValueTextFontFamilyProperty = BindableProperty.Create(
        nameof(ValueTextFontFamily), typeof(string), typeof(EntryCell), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(EntryCell), () =>
            {
                ((EntryCell)b).UpdateEntryFontFamily();
            }));

    public static readonly BindableProperty ValueTextFontAttributesProperty = BindableProperty.Create(
        nameof(ValueTextFontAttributes), typeof(FontAttributes?), typeof(EntryCell), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(EntryCell), () =>
            {
                ((EntryCell)b).UpdateEntryFontAttributes();
            }));

    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder), typeof(string), typeof(EntryCell), string.Empty,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(EntryCell), () =>
            {
                ((EntryCell)b).entry.Placeholder = (string)n;
            }));

    public static readonly BindableProperty PlaceholderColorProperty = BindableProperty.Create(
        nameof(PlaceholderColor), typeof(Color), typeof(EntryCell), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(EntryCell), () =>
            { if (n is Color c) ((EntryCell)b).entry.PlaceholderColor = c; }));

    public static readonly BindableProperty KeyboardProperty = BindableProperty.Create(
        nameof(Keyboard), typeof(Keyboard), typeof(EntryCell), Keyboard.Default,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(EntryCell), () =>
            {
                ((EntryCell)b).entry.Keyboard = (Keyboard)n;
            }));

    public static readonly BindableProperty IsPasswordProperty = BindableProperty.Create(
        nameof(IsPassword), typeof(bool), typeof(EntryCell), false,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(EntryCell), () =>
            {
                ((EntryCell)b).entry.IsPassword = (bool)n;
            }));

    public static readonly BindableProperty MaxLengthProperty = BindableProperty.Create(
        nameof(MaxLength), typeof(int), typeof(EntryCell), -1,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(EntryCell), () =>
            {
            var cell = (EntryCell)b;
            cell.entry.MaxLength = (int)n > 0 ? (int)n : int.MaxValue;
        }));

    public static readonly BindableProperty TextAlignmentProperty = BindableProperty.Create(
        nameof(TextAlignment), typeof(TextAlignment), typeof(EntryCell), TextAlignment.End,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(EntryCell), () =>
            {
                ((EntryCell)b).entry.HorizontalTextAlignment = (TextAlignment)n;
            }));

    public static readonly BindableProperty CompletedCommandProperty = BindableProperty.Create(
        nameof(CompletedCommand), typeof(ICommand), typeof(EntryCell), null);

    /// <summary>
    /// Input mask — <c>#</c> is a digit slot, every other character is a literal inserted as the user
    /// types. Same engine as <see cref="Shiny.Maui.Controls.TextEntry"/>: <see cref="ValueText"/> holds
    /// the raw digits, <see cref="FormattedValueText"/> holds what is on screen, and the keyboard and
    /// max length are set from the mask.
    /// </summary>
    public static readonly BindableProperty MaskProperty = BindableProperty.Create(
        nameof(Mask), typeof(string), typeof(EntryCell), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(EntryCell), () =>
            {
                ((EntryCell)b).OnMaskChanged();
            }));

    /// <summary>The masked display value. Empty when no <see cref="Mask"/> is set.</summary>
    public static readonly BindableProperty FormattedValueTextProperty = BindableProperty.Create(
        nameof(FormattedValueText), typeof(string), typeof(EntryCell), string.Empty);

    /// <summary>
    /// A bar docked to the top of the soft keyboard while this cell has focus (iOS + Android).
    /// </summary>
    public static readonly BindableProperty AccessoryProperty = BindableProperty.Create(
        nameof(Accessory), typeof(KeyboardAccessoryView), typeof(EntryCell), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(EntryCell), () =>
            {
                ((EntryCell)b).SyncAccessory();
            }));

    /// <summary>A stock accessory bar, used when <see cref="Accessory"/> is not set.</summary>
    public static readonly BindableProperty AccessoryPresetProperty = BindableProperty.Create(
        nameof(AccessoryPreset), typeof(KeyboardAccessoryPreset), typeof(EntryCell), KeyboardAccessoryPreset.None,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(EntryCell), () =>
            {
                ((EntryCell)b).ResetPresetAccessory();
            }));

    /// <summary>Groups cells for accessory prev/next navigation.</summary>
    public static readonly BindableProperty FieldGroupProperty = BindableProperty.Create(
        nameof(FieldGroup), typeof(string), typeof(EntryCell), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(EntryCell), () =>
            {
                // The group has to sit on the element the navigator collects, which is the inner input.
                ((EntryCell)b).entry.SetValue(KeyboardField.GroupProperty, n);
            }));

    /// <summary>
    /// When false, autofill, autocorrect, predictive text and spell check are all switched off for
    /// this cell — the settings-form fields most likely to be rewritten by the OS.
    /// </summary>
    public static readonly BindableProperty IsAutoCompleteEnabledProperty = BindableProperty.Create(
        nameof(IsAutoCompleteEnabled), typeof(bool), typeof(EntryCell), true,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(EntryCell), () =>
            {
            var cell = (EntryCell)b;
            var enabled = (bool)n;
            cell.entry.IsAutoCompleteEnabled = enabled;
            cell.entry.IsSpellCheckEnabled = enabled;
            cell.entry.IsTextPredictionEnabled = enabled;
        }));

    public string ValueText
    {
        get => (string)GetValue(ValueTextProperty);
        set => SetValue(ValueTextProperty, value);
    }

    public Color? ValueTextColor
    {
        get => (Color?)GetValue(ValueTextColorProperty);
        set => SetValue(ValueTextColorProperty, value);
    }

    public double ValueTextFontSize
    {
        get => (double)GetValue(ValueTextFontSizeProperty);
        set => SetValue(ValueTextFontSizeProperty, value);
    }

    public string? ValueTextFontFamily
    {
        get => (string?)GetValue(ValueTextFontFamilyProperty);
        set => SetValue(ValueTextFontFamilyProperty, value);
    }

    public FontAttributes? ValueTextFontAttributes
    {
        get => (FontAttributes?)GetValue(ValueTextFontAttributesProperty);
        set => SetValue(ValueTextFontAttributesProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public Color? PlaceholderColor
    {
        get => (Color?)GetValue(PlaceholderColorProperty);
        set => SetValue(PlaceholderColorProperty, value);
    }

    public Keyboard Keyboard
    {
        get => (Keyboard)GetValue(KeyboardProperty);
        set => SetValue(KeyboardProperty, value);
    }

    public bool IsPassword
    {
        get => (bool)GetValue(IsPasswordProperty);
        set => SetValue(IsPasswordProperty, value);
    }

    public int MaxLength
    {
        get => (int)GetValue(MaxLengthProperty);
        set => SetValue(MaxLengthProperty, value);
    }

    public TextAlignment TextAlignment
    {
        get => (TextAlignment)GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    public ICommand? CompletedCommand
    {
        get => (ICommand?)GetValue(CompletedCommandProperty);
        set => SetValue(CompletedCommandProperty, value);
    }

    public string? Mask
    {
        get => (string?)GetValue(MaskProperty);
        set => SetValue(MaskProperty, value);
    }

    public string FormattedValueText
    {
        get => (string)GetValue(FormattedValueTextProperty);
        private set => SetValue(FormattedValueTextProperty, value);
    }

    public KeyboardAccessoryView? Accessory
    {
        get => (KeyboardAccessoryView?)GetValue(AccessoryProperty);
        set => SetValue(AccessoryProperty, value);
    }

    public KeyboardAccessoryPreset AccessoryPreset
    {
        get => (KeyboardAccessoryPreset)GetValue(AccessoryPresetProperty);
        set => SetValue(AccessoryPresetProperty, value);
    }

    public string? FieldGroup
    {
        get => (string?)GetValue(FieldGroupProperty);
        set => SetValue(FieldGroupProperty, value);
    }

    public bool IsAutoCompleteEnabled
    {
        get => (bool)GetValue(IsAutoCompleteEnabledProperty);
        set => SetValue(IsAutoCompleteEnabledProperty, value);
    }

    public event EventHandler? Completed;

    // ---- Keyboard accessory ---------------------------------------------------------------

    bool suppressTextChanged;
    KeyboardAccessoryBinder? accessoryBinder;
    KeyboardAccessoryView? presetAccessory;

    KeyboardAccessoryBinder AccessoryBinder => accessoryBinder ??= new KeyboardAccessoryBinder(this, entry, this);

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

    // The cell is the control, but the input inside it is what the navigator collects and what has to
    // lose focus - so both members point past the wrapper.
    VisualElement IKeyboardAccessoryHost.NavigationElement => entry;

    void IKeyboardAccessoryHost.DismissKeyboard() => entry.Unfocus();

    protected override View? CreateAccessoryView()
    {
        entry = new BorderlessEntry
        {
            Style = CleanEntryStyle,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
            HorizontalTextAlignment = TextAlignment.End,
            MinimumWidthRequest = 120
        };
        entry.TextChanged += (s, e) =>
        {
            if (suppressTextChanged)
                return;

            if (!string.IsNullOrEmpty(Mask))
            {
                suppressTextChanged = true;
                var masked = MaskedInput.Apply(entry.Text, Mask);

                ValueText = masked.Raw;
                FormattedValueText = masked.Formatted;
                entry.Text = masked.Formatted;
                suppressTextChanged = false;

                // The literals shift everything right, so the caret has to be put back after the
                // text is replaced rather than left where the keystroke landed.
                Dispatcher.Dispatch(() => entry.CursorPosition = masked.CursorPosition);
            }
            else if (ValueText != e.NewTextValue)
            {
                ValueText = e.NewTextValue;
            }
        };
        entry.Focused += (s, e) =>
        {
            // The entry fills the row with its text right-aligned, so a tap anywhere left of the value
            // puts the caret at offset 0 on Android - backspace then deletes nothing, and a full
            // MaxLength blocks typing too. Deferred because the platform places the caret at the tap
            // point after focus is taken.
            Dispatcher.Dispatch(() =>
            {
                if (entry.IsFocused && entry.SelectionLength == 0)
                    entry.CursorPosition = entry.Text?.Length ?? 0;
            });
        };
        entry.Completed += (s, e) =>
        {
            Completed?.Invoke(this, EventArgs.Empty);
            if (CompletedCommand?.CanExecute(ValueText) == true)
                CompletedCommand.Execute(ValueText);
        };
        return entry;
    }

    public void SetFocus() => entry?.Focus();

    void OnValueTextChanged(string newValue)
    {
        if (entry is null || suppressTextChanged)
            return;

        // With a mask, ValueText is the raw value and the field shows the formatted one.
        if (!string.IsNullOrEmpty(Mask))
        {
            var masked = MaskedInput.Apply(newValue, Mask);
            FormattedValueText = masked.Formatted;

            if (entry.Text != masked.Formatted)
            {
                suppressTextChanged = true;
                entry.Text = masked.Formatted;
                suppressTextChanged = false;
            }
            return;
        }

        if (entry.Text != newValue)
            entry.Text = newValue;
    }

    void OnMaskChanged()
    {
        if (string.IsNullOrEmpty(Mask))
        {
            // Mask removed - the field goes back to showing the raw value.
            entry.MaxLength = MaxLength > 0 ? MaxLength : int.MaxValue;
            FormattedValueText = string.Empty;

            suppressTextChanged = true;
            entry.Text = ValueText;
            suppressTextChanged = false;
            return;
        }

        // Same defaults TextEntry applies: a digit mask only accepts digits, and the field can never
        // hold more than the mask renders.
        entry.Keyboard = Keyboard.Numeric;
        entry.MaxLength = Mask!.Length;
        OnValueTextChanged(ValueText);
    }

    void UpdateEntryColor()
    {
        var color = ValueTextColor ?? ParentTableView?.CellValueTextColor;
        if (color != null)
            entry.TextColor = color;
        else
            entry.ClearValue(Entry.TextColorProperty);
    }

    void UpdateEntryFontSize()
        => entry.FontSize = ResolveDouble(ValueTextFontSize, ParentTableView?.CellValueTextFontSize ?? -1, 16);

    void UpdateEntryFontFamily()
        => entry.FontFamily = ResolveFontFamily(ValueTextFontFamily, ParentTableView?.CellValueTextFontFamily);

    void UpdateEntryFontAttributes()
        => entry.FontAttributes = ResolveFontAttributes(ValueTextFontAttributes, ParentTableView?.CellValueTextFontAttributes);

    protected override void OnTapped()
    {
        entry.Focus();
    }
}