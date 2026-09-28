namespace Shiny.Controls.Office.Spreadsheet.View;

/// <summary>The kinds of input a <see cref="SheetDialog"/> field can be.</summary>
public enum SheetFieldKind
{
    /// <summary>A line of text.</summary>
    Text,

    /// <summary>Several lines of text — a note.</summary>
    MultilineText,

    /// <summary>A number, typed as text and parsed by the dialog.</summary>
    Number,

    /// <summary>One of <see cref="SheetField.Options"/>, as a dropdown.</summary>
    Choice,

    /// <summary>A checkbox.</summary>
    Check,

    /// <summary>A colour from <see cref="SheetField.Palette"/>, or none.</summary>
    Color,

    /// <summary>A list of checkboxes — a filter's values.</summary>
    Checklist,

    /// <summary>A selectable list, one line picked — the Insert Function and Name Manager lists.</summary>
    List,

    /// <summary>Text to read, not to edit.</summary>
    Label
}

/// <summary>One line of a <see cref="SheetFieldKind.Checklist"/>.</summary>
public sealed class SheetChecklistItem(string text, bool isChecked)
{
    public string Text { get; } = text;
    public bool IsChecked { get; set; } = isChecked;
}

/// <summary>
/// One input in a <see cref="SheetDialog"/>. Plain state: the host renders it, writes what the user
/// entered back into it, and calls <see cref="SheetDialog.OnFieldChanged"/>.
/// </summary>
public sealed class SheetField(string key, string label, SheetFieldKind kind)
{
    public string Key { get; } = key;
    public string Label { get; set; } = label;
    public SheetFieldKind Kind { get; } = kind;

    /// <summary>The value of a text or number field.</summary>
    public string Text { get; set; } = string.Empty;

    public string? Placeholder { get; set; }

    /// <summary>The choices of a <see cref="SheetFieldKind.Choice"/> or the lines of a <see cref="SheetFieldKind.List"/>.</summary>
    public IReadOnlyList<string> Options { get; set; } = [];

    /// <summary>A second line under each list entry — a function's description, a name's reference.</summary>
    public IReadOnlyList<string>? Details { get; set; }

    public int SelectedIndex { get; set; } = -1;

    public string? SelectedOption => this.SelectedIndex >= 0 && this.SelectedIndex < this.Options.Count ? this.Options[this.SelectedIndex] : null;

    public bool IsChecked { get; set; }

    /// <summary>The value of a colour field. Null is "no colour" or "automatic".</summary>
    public ArgbColor? Color { get; set; }

    public List<SheetChecklistItem> Items { get; set; } = [];

    public bool IsVisible { get; set; } = true;
    public bool IsEnabled { get; set; } = true;

    /// <summary>The number in a <see cref="SheetFieldKind.Number"/> field, or null when it is not one.</summary>
    public double? Number
        => double.TryParse(this.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var value)
            ? value
            : double.TryParse(this.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value)
                ? value
                : null;

    /// <summary>The colours a colour field offers — Excel's standard row plus black, white and greys.</summary>
    public static IReadOnlyList<(string Name, ArgbColor Color)> Palette { get; } =
    [
        ("Black", new ArgbColor(255, 0, 0, 0)),
        ("White", new ArgbColor(255, 255, 255, 255)),
        ("Gray", new ArgbColor(255, 0x80, 0x80, 0x80)),
        ("Light Gray", new ArgbColor(255, 0xD9, 0xD9, 0xD9)),
        ("Dark Red", new ArgbColor(255, 0xC0, 0x00, 0x00)),
        ("Red", new ArgbColor(255, 0xFF, 0x00, 0x00)),
        ("Orange", new ArgbColor(255, 0xFF, 0xC0, 0x00)),
        ("Yellow", new ArgbColor(255, 0xFF, 0xFF, 0x00)),
        ("Light Green", new ArgbColor(255, 0x92, 0xD0, 0x50)),
        ("Green", new ArgbColor(255, 0x00, 0xB0, 0x50)),
        ("Light Blue", new ArgbColor(255, 0x00, 0xB0, 0xF0)),
        ("Blue", new ArgbColor(255, 0x00, 0x70, 0xC0)),
        ("Dark Blue", new ArgbColor(255, 0x00, 0x20, 0x60)),
        ("Purple", new ArgbColor(255, 0x70, 0x30, 0xA0)),
        ("Blue, Accent 1", new ArgbColor(255, 0x44, 0x72, 0xC4)),
        ("Orange, Accent 2", new ArgbColor(255, 0xED, 0x7D, 0x31)),
        ("Gold, Accent 4", new ArgbColor(255, 0xFF, 0xC0, 0x00)),
        ("Green, Accent 6", new ArgbColor(255, 0x70, 0xAD, 0x47))
    ];
}

/// <summary>A page of fields. A dialog with one tab shows no tab strip.</summary>
public sealed class SheetDialogTab(string title, IReadOnlyList<SheetField> fields)
{
    public string Title { get; } = title;
    public IReadOnlyList<SheetField> Fields { get; } = fields;
}

/// <summary>A dialog button, and what it does.</summary>
/// <param name="Text">Its label.</param>
/// <param name="Action">
/// Runs when pressed. Return true to close the dialog; false to keep it open — after setting
/// <see cref="SheetDialog.Error"/>, usually. Null closes without doing anything: a Cancel.
/// </param>
public sealed record SheetDialogButton(string Text, Func<SheetDialog, bool>? Action = null)
{
    /// <summary>The button Enter presses and the one drawn in the accent.</summary>
    public bool IsPrimary { get; init; }

    /// <summary>The button Escape presses.</summary>
    public bool IsCancel => this.Action is null;
}

/// <summary>
/// A dialog described as data — Format Cells, Data Validation, Name Manager, a filter — which each host
/// renders with one generic component.
/// </summary>
/// <remarks>
/// <para>
/// Every dialog the spreadsheet needs is defined once, here in the kernel, with its validation and the
/// command it runs; MAUI and Blazor each contribute a renderer and nothing else. That is the same
/// arrangement as the grid itself — one controller, two thin hosts — and it is what keeps the two
/// dialogs from ever disagreeing about what a field means.
/// </para>
/// <para>
/// A host shows the dialog when <see cref="SpreadsheetController.DialogRequested"/> fires, writes input
/// back into the fields as the user types, calls <see cref="OnFieldChanged"/>, and redraws on
/// <see cref="Changed"/> — a search box narrowing a list is a field change that rewrites another field.
/// Pressing a button is <see cref="Press"/>; if it returns true the dialog closes, and if
/// <see cref="Next"/> is then set, that one opens in its place.
/// </para>
/// </remarks>
public sealed class SheetDialog
{
    public SheetDialog(string title, IReadOnlyList<SheetDialogTab> tabs, IReadOnlyList<SheetDialogButton> buttons)
    {
        this.Title = title;
        this.Tabs = tabs;
        this.Buttons = buttons;
    }

    public SheetDialog(string title, IReadOnlyList<SheetField> fields, IReadOnlyList<SheetDialogButton> buttons)
        : this(title, [new SheetDialogTab(title, fields)], buttons)
    {
    }

    public string Title { get; }
    public IReadOnlyList<SheetDialogTab> Tabs { get; }
    public IReadOnlyList<SheetDialogButton> Buttons { get; }

    /// <summary>Why the last press was refused, shown above the buttons.</summary>
    public string? Error { get; set; }

    /// <summary>The dialog to open after this one closes — Name Manager's New opens the name editor.</summary>
    public SheetDialog? Next { get; set; }

    /// <summary>Called when a field changes, to update the ones that depend on it.</summary>
    public Action<SheetDialog, SheetField>? FieldChanged { get; init; }

    /// <summary>Raised when the dialog changed itself and the host should redraw it.</summary>
    public event EventHandler? Changed;

    /// <summary>A preferred width in device-independent pixels, for dialogs with a list or several tabs.</summary>
    public double Width { get; init; } = 380;

    public IEnumerable<SheetField> Fields => this.Tabs.SelectMany(x => x.Fields);

    public SheetField this[string key] => this.Fields.First(x => x.Key == key);

    public SheetField? Find(string key) => this.Fields.FirstOrDefault(x => x.Key == key);

    public SheetDialogButton? Primary => this.Buttons.FirstOrDefault(x => x.IsPrimary);

    /// <summary>The host reports an edit.</summary>
    public void OnFieldChanged(SheetField field)
    {
        this.FieldChanged?.Invoke(this, field);
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Presses a button. True when the dialog should close.</summary>
    public bool Press(SheetDialogButton button)
    {
        ArgumentNullException.ThrowIfNull(button);

        this.Error = null;
        this.Next = null;

        if (button.Action is null)
            return true;

        bool close;
        try
        {
            close = button.Action(this);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // A command refusing its input — a duplicate name, a reference that does not parse — is a
            // message for the dialog, not a crash.
            this.Error = ex.Message;
            close = false;
        }

        if (!close)
            this.Changed?.Invoke(this, EventArgs.Empty);

        return close;
    }

    /// <summary>A message box: a title, a line of text and an OK.</summary>
    public static SheetDialog Message(string title, string message)
        => new(title, [new SheetField("message", message, SheetFieldKind.Label)], [new SheetDialogButton("OK", _ => true) { IsPrimary = true }]);
}

/// <summary>One line of a popup menu — a context menu, or the list a validated cell drops down.</summary>
public sealed record SheetMenuItem(string Text, Action? Action = null)
{
    public static readonly SheetMenuItem Separator = new(string.Empty) { IsSeparator = true };

    public bool IsSeparator { get; init; }
    public bool IsEnabled { get; init; } = true;
    public bool IsChecked { get; init; }

    /// <summary>The keyboard shortcut, shown right-aligned — <c>Ctrl+C</c>.</summary>
    public string? Shortcut { get; init; }

    public IReadOnlyList<SheetMenuItem>? Children { get; init; }

    public bool HasChildren => this.Children is { Count: > 0 };
}

/// <summary>A popup menu, and where on the grid it should appear.</summary>
/// <param name="X">Left edge, in the host's coordinates — already multiplied by the zoom.</param>
/// <param name="Y">Top edge, likewise.</param>
public sealed record SheetMenuRequest(IReadOnlyList<SheetMenuItem> Items, double X, double Y)
{
    public string? Title { get; init; }
}
