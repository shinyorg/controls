using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using TvTableView = Shiny.Maui.Controls.TableView;
using TvTableSection = Shiny.Maui.Controls.Sections.TableSection;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Cells;

public class RadioCell : CellBase
{
    /// <summary>Appended to the cell's <see cref="Element.AutomationId"/> to name its radio button.</summary>
    public const string RadioAutomationIdSuffix = "_Radio";

    /// <summary>
    /// Children are built by CellBase's constructor (BuildLayout -> the virtual
    /// CreateAccessoryView override above), so by the time this body runs they exist.
    /// Marking ready here replays any property an implicit Style applied before
    /// construction - see StyleGuard.
    /// </summary>
    public RadioCell()
    {
        AutomationIds.Link(this, radioButton, RadioAutomationIdSuffix);
        // The table re-applies this when it hosts the cell; doing it here too means a cell used on
        // its own follows the theme from the start, not only after its accent is first changed.
        this.ApplyAccentStyles();
        StyleGuard.MarkReady(this, typeof(RadioCell));
    }

    RadioButton radioButton = default!;

    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value), typeof(object), typeof(RadioCell), null);

    public static readonly BindableProperty AccentColorProperty = BindableProperty.Create(
        nameof(AccentColor), typeof(Color), typeof(RadioCell), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(RadioCell), () =>
            {
                ((RadioCell)b).UpdateAccentColor();
            }));

    // Attached property for section-level or tableview-level selected value
    public static readonly BindableProperty SelectedValueProperty = BindableProperty.CreateAttached(
        "SelectedValue", typeof(object), typeof(RadioCell), null,
        BindingMode.TwoWay,
        // Attached, so the target is the section or table the radio group lives on, never a
        // RadioCell - RadioCell's level would never be marked on it. What the handler needs
        // built is the target's cell collection, so gate on the target's own level. Anything
        // else carrying the value has no cells to update and needs no gate at all.
        propertyChanged: (b, o, n) =>
        {
            if (b is TvTableView)
                StyleGuard.WhenReady(b, typeof(TvTableView), () => OnSelectedValueChanged(b, o, n));
            else if (b is TvTableSection)
                StyleGuard.WhenReady(b, typeof(TvTableSection), () => OnSelectedValueChanged(b, o, n));
        });

    public object? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Color? AccentColor
    {
        get => (Color?)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public static object? GetSelectedValue(BindableObject obj) => obj.GetValue(SelectedValueProperty);
    public static void SetSelectedValue(BindableObject obj, object? value) => obj.SetValue(SelectedValueProperty, value);

    protected override View? CreateAccessoryView()
    {
        radioButton = new RadioButton
        {
            VerticalOptions = LayoutOptions.Center
        };
        radioButton.CheckedChanged += OnRadioCheckedChanged;
        return radioButton;
    }

    void OnRadioCheckedChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (!e.Value) return;

        // Write to Section scope
        if (ParentSection != null)
            SetSelectedValue(ParentSection, Value);

        // Also write to TableView scope (global radio groups)
        if (ParentTableView != null)
            SetSelectedValue(ParentTableView, Value);
    }

    /// <summary>
    /// Checks this cell if its group already holds its value. The group's <c>SelectedValue</c> usually
    /// arrives (from a binding) before the section has rendered its cells, and the change handler below
    /// only reaches cells that already exist - so the renderer calls this as it attaches each cell.
    /// A section-level value wins over a table-level one, as it does when the user taps.
    /// </summary>
    internal void SyncFromGroup()
    {
        object? groupValue = null;
        if (this.ParentSection is { } section && section.IsSet(SelectedValueProperty))
            groupValue = GetSelectedValue(section);
        else if (this.ParentTableView is { } table && table.IsSet(SelectedValueProperty))
            groupValue = GetSelectedValue(table);
        else
            return;

        var shouldCheck = Equals(this.Value, groupValue);
        if (this.radioButton.IsChecked != shouldCheck)
            this.radioButton.IsChecked = shouldCheck;
    }

    static void OnSelectedValueChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is TvTableSection section)
        {
            foreach (var cell in section.GetVisibleCells())
            {
                if (cell is RadioCell radioCell)
                    radioCell.radioButton.IsChecked = Equals(radioCell.Value, newValue);
            }
        }
        else if (bindable is TvTableView tableView)
        {
            foreach (var sec in tableView.GetAllSections())
            {
                foreach (var cell in sec.GetVisibleCells())
                {
                    if (cell is RadioCell radioCell)
                        radioCell.radioButton.IsChecked = Equals(radioCell.Value, newValue);
                }
            }
        }
    }

    protected override void ApplyAccentStyles() => this.UpdateAccentColor();

    void UpdateAccentColor()
        => ThemeProbe.Tint(radioButton, RadioButton.BorderColorProperty, AccentColor ?? ParentTableView?.CellAccentColor, ShinyThemeKeys.Color.Primary);


    protected override void OnTapped()
    {
        radioButton.IsChecked = true;
    }
}