using System.Collections;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.Cells;

public class TextPickerCell : CellBase
{
    /// <summary>Appended to the cell's <see cref="Element.AutomationId"/> to name the native picker behind it (the tap target on Android).</summary>
    public const string PickerAutomationIdSuffix = "_Picker";

    /// <summary>
    /// Children are built by CellBase's constructor (BuildLayout -> the virtual
    /// CreateAccessoryView override above), so by the time this body runs they exist.
    /// Marking ready here replays any property an implicit Style applied before
    /// construction - see StyleGuard.
    /// </summary>
    public TextPickerCell()
    {
        AutomationIds.Link(this, valueLabel, ValueAutomationIdSuffix);
        AutomationIds.Link(this, hiddenPicker, PickerAutomationIdSuffix);
        StyleGuard.MarkReady(this, typeof(TextPickerCell));
    }

    Label valueLabel = default!;
    Picker hiddenPicker = default!;
    bool syncingPicker;

    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource), typeof(IList), typeof(TextPickerCell), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TextPickerCell), () =>
            {
                ((TextPickerCell)b).UpdatePickerItems();
            }));

    public static readonly BindableProperty SelectedIndexProperty = BindableProperty.Create(
        nameof(SelectedIndex), typeof(int), typeof(TextPickerCell), -1,
        BindingMode.TwoWay,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TextPickerCell), () =>
            {
                ((TextPickerCell)b).OnSelectedIndexChanged();
            }));

    public static readonly BindableProperty SelectedItemProperty = BindableProperty.Create(
        nameof(SelectedItem), typeof(object), typeof(TextPickerCell), null,
        BindingMode.TwoWay,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TextPickerCell), () =>
            {
                ((TextPickerCell)b).OnSelectedItemChanged();
            }));

    public static readonly BindableProperty DisplayMemberProperty = BindableProperty.Create(
        nameof(DisplayMember), typeof(string), typeof(TextPickerCell), null);

    public static readonly BindableProperty PickerTitleProperty = BindableProperty.Create(
        nameof(PickerTitle), typeof(string), typeof(TextPickerCell), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TextPickerCell), () =>
            {
            var cell = (TextPickerCell)b;
            if (cell.hiddenPicker != null)
                cell.hiddenPicker.Title = (string?)n;
        }));

    public static readonly BindableProperty SelectedCommandProperty = BindableProperty.Create(
        nameof(SelectedCommand), typeof(ICommand), typeof(TextPickerCell), null);

    public static readonly BindableProperty ValueTextColorProperty = BindableProperty.Create(
        nameof(ValueTextColor), typeof(Color), typeof(TextPickerCell), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TextPickerCell), () =>
            {
                ((TextPickerCell)b).UpdateValueColor();
            }));

    public IList? ItemsSource
    {
        get => (IList?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public string? DisplayMember
    {
        get => (string?)GetValue(DisplayMemberProperty);
        set => SetValue(DisplayMemberProperty, value);
    }

    public string? PickerTitle
    {
        get => (string?)GetValue(PickerTitleProperty);
        set => SetValue(PickerTitleProperty, value);
    }

    public ICommand? SelectedCommand
    {
        get => (ICommand?)GetValue(SelectedCommandProperty);
        set => SetValue(SelectedCommandProperty, value);
    }

    public Color? ValueTextColor
    {
        get => (Color?)GetValue(ValueTextColorProperty);
        set => SetValue(ValueTextColorProperty, value);
    }

    protected override View? CreateAccessoryView()
    {
        valueLabel = new Label
        {
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End
        };

        hiddenPicker = new Picker
        {
            Opacity = 0.01,
            Title = PickerTitle
        };
        hiddenPicker.SelectedIndexChanged += (s, e) =>
        {
            // Ignore events we raised ourselves while programmatically re-syncing the picker
            // (RestoreSelection / OnSelectedIndexChanged). Reacting to those would let our own
            // write bounce back through here.
            if (syncingPicker)
                return;

            // The native control raises a spurious SelectedIndex = -1 when its items are
            // repopulated (e.g. a TableView rebuild re-attaches the cell and re-maps ItemsSource).
            // Just ignore it: do NOT write a value back to the picker from inside its own change
            // handler. During repopulation the native control re-snaps to -1 and re-raises, so
            // fighting it here causes an infinite ping-pong that freezes the UI thread. The
            // deferred RestoreSelection (UpdatePickerItems / OnSelectedItemChanged) re-syncs once
            // the items are stable.
            if (hiddenPicker.SelectedIndex < 0)
                return;

            SelectedIndex = hiddenPicker.SelectedIndex;
            SelectedItem = hiddenPicker.SelectedItem;
            UpdateDisplayText();

            if (SelectedCommand?.CanExecute(SelectedItem) == true)
                SelectedCommand.Execute(SelectedItem);
        };
        hiddenPicker.Focused += (s, e) => ApplySelectionHighlight();
        hiddenPicker.Unfocused += (s, e) => ClearSelectionHighlight();

        // Overlay the transparent picker across the entire cell. On Android the overlay
        // tap opens the native dialog directly. On iOS the parent TapGestureRecognizer
        // consumes the touch first, so OnCellTapped explicitly focuses the picker.
        Grid.SetColumn(hiddenPicker, 0);
        Grid.SetColumnSpan(hiddenPicker, 3);
        Grid.SetRow(hiddenPicker, 0);
        Grid.SetRowSpan(hiddenPicker, 2);
        RootGrid.Children.Add(hiddenPicker);

        return valueLabel;
    }

    /// <summary>
    /// On the AppKit head the picker is an NSPopUpButton spread over the cell, and Picker.Focus() only makes
    /// it first responder - it never opens the menu. A click that reaches the button does, so the cell keeps
    /// no recognizer there and the click goes straight to it. (The library has no -macos TFM; the AppKit head
    /// runs the net10.0 build, hence the runtime check. Mac Catalyst reports IsMacCatalyst, not IsMacOS.)
    /// </summary>
    protected override bool NativeControlTakesClicks => OperatingSystem.IsMacOS();

    protected override void OnCellTapped(object? sender, TappedEventArgs e)
    {
        base.OnCellTapped(sender, e);
        hiddenPicker?.Focus();
    }

    void UpdatePickerItems()
    {
        if (hiddenPicker == null || ItemsSource == null) return;

        hiddenPicker.ItemsSource = ItemsSource;
        if (!string.IsNullOrEmpty(DisplayMember))
            hiddenPicker.ItemDisplayBinding = MemberPath.Binding(DisplayMember);

        // Reassigning a Picker's ItemsSource resets its native selection to -1, and the items may
        // also have arrived after the selection was set (e.g. an async-loaded list, or a TableView
        // rebuild recreating the cell). Re-sync from whichever selection the consumer bound -
        // SelectedItem OR SelectedIndex - so a SelectedIndex-only binding doesn't lose its display.
        RestoreSelection();
    }

    // Sync directly from SelectedIndex. This must NOT consult SelectedItem: the picker's own
    // SelectedIndexChanged handler sets SelectedIndex before SelectedItem, so SelectedItem still
    // holds the previous value here and would otherwise revert the user's fresh selection.
    void OnSelectedIndexChanged()
    {
        if (hiddenPicker != null && SelectedIndex >= 0 && hiddenPicker.SelectedIndex != SelectedIndex)
            SetPickerIndex(SelectedIndex);
        UpdateDisplayText();
    }

    // Reflect an externally-set SelectedItem in the picker selection + value label (the picker's own
    // selection events drive the reverse direction).
    void OnSelectedItemChanged()
    {
        RestoreSelection();
    }

    void RestoreSelection()
    {
        if (hiddenPicker == null) return;

        // Prefer SelectedItem when set, otherwise fall back to SelectedIndex. This keeps the native
        // picker (and the value label, which reads hiddenPicker.SelectedItem) in sync regardless of
        // which property the consumer bound.
        var index = -1;
        if (SelectedItem != null && ItemsSource != null)
            index = ItemsSource.IndexOf(SelectedItem);

        if (index < 0 && ItemsSource != null && SelectedIndex >= 0 && SelectedIndex < ItemsSource.Count)
            index = SelectedIndex;

        if (index >= 0 && hiddenPicker.SelectedIndex != index)
            SetPickerIndex(index);

        UpdateDisplayText();
    }

    // Write the native picker selection without re-entering our own SelectedIndexChanged handler.
    void SetPickerIndex(int index)
    {
        syncingPicker = true;
        try
        {
            hiddenPicker.SelectedIndex = index;
        }
        finally
        {
            syncingPicker = false;
        }
    }

    void UpdateDisplayText()
    {
        if (valueLabel == null || hiddenPicker == null) return;

        if (hiddenPicker.SelectedItem != null)
        {
            if (!string.IsNullOrEmpty(DisplayMember))
            {
                MemberPath.TryGetValue(hiddenPicker.SelectedItem, DisplayMember, out var value);
                valueLabel.Text = value?.ToString() ?? hiddenPicker.SelectedItem.ToString();
            }
            else
            {
                valueLabel.Text = hiddenPicker.SelectedItem.ToString();
            }
        }
        else
        {
            valueLabel.Text = string.Empty;
        }
    }

    protected override void ApplyValueTextStyles() => UpdateValueColor();

    void UpdateValueColor()
    {
        var color = ValueTextColor ?? ParentTableView?.CellValueTextColor;
        if (color != null)
            valueLabel.TextColor = color;
        else
            valueLabel.ClearValue(Label.TextColorProperty);
    }
}