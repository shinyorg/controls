namespace Shiny.Controls.Office.Spreadsheet.View;

/// <summary>The modifier keys held with a key press.</summary>
[Flags]
public enum SheetKeyModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,

    /// <summary>Cmd on a Mac. Treated as Control, which is what a Mac user means by it.</summary>
    Meta = 8
}

public sealed partial class SpreadsheetController
{
    /// <summary>
    /// Handles a key pressed on the grid — Excel's shortcuts, one table for both hosts.
    /// </summary>
    /// <param name="key">
    /// The key as a browser's <c>KeyboardEvent.key</c> names it: <c>"ArrowUp"</c>, <c>"a"</c>,
    /// <c>"F2"</c>, <c>"Delete"</c>, <c>" "</c>, <c>"`"</c>. A MAUI host maps its platform keys onto these.
    /// </param>
    /// <returns>True when the key did something, so the host should stop the platform acting on it too.</returns>
    public bool HandleKey(string key, SheetKeyModifiers modifiers = SheetKeyModifiers.None)
    {
        ArgumentNullException.ThrowIfNull(key);

        var ctrl = (modifiers & (SheetKeyModifiers.Control | SheetKeyModifiers.Meta)) != 0;
        var shift = (modifiers & SheetKeyModifiers.Shift) != 0;
        var alt = (modifiers & SheetKeyModifiers.Alt) != 0;
        var lower = key.Length == 1 ? key.ToLowerInvariant() : key;

        switch (lower)
        {
            case "ArrowUp" when alt:
                return false;

            case "ArrowDown" when alt:
                this.OpenListDropdown();
                return true;

            case "ArrowUp": this.Move(MoveDirection.Up, shift, ctrl); return true;
            case "ArrowDown": this.Move(MoveDirection.Down, shift, ctrl); return true;
            case "ArrowLeft": this.Move(MoveDirection.Left, shift, ctrl); return true;
            case "ArrowRight": this.Move(MoveDirection.Right, shift, ctrl); return true;

            case "Tab": this.Advance(byRow: false, backwards: shift); return true;
            case "Enter": this.Advance(byRow: true, backwards: shift); return true;

            case "Home":
                this.GoTo(ctrl ? new CellRef(0, 0) : new CellRef(this.Metrics.FrozenPane.Column, this.Selection.Active.Row));
                return true;

            case "End" when ctrl:
                if (this.sheet.UsedRange is { } used)
                    this.GoTo(used.BottomRight);

                return true;

            case "PageUp" when ctrl: this.StepSheet(-1); return true;
            case "PageDown" when ctrl: this.StepSheet(1); return true;
            case "PageUp": this.Page(-1, shift); return true;
            case "PageDown": this.Page(1, shift); return true;

            case "Delete":
            case "Backspace":
                this.ClearSelection();
                return true;

            case "F2" when shift: this.ShowDialog(SpreadsheetDialogs.Note(this)); return true;
            case "F2": this.BeginEdit(); return true;
            case "F3" when shift: this.ShowDialog(SpreadsheetDialogs.InsertFunction(this)); return true;
            case "F3" when ctrl: this.ShowDialog(SpreadsheetDialogs.NameManager(this)); return true;
            case "F5": this.ShowDialog(SpreadsheetDialogs.GoTo(this)); return true;
            case "F9": this.CalculateNow(); return true;
            case "F10" when shift: this.OpenContextMenuAtSelection(); return true;
            case "ContextMenu": this.OpenContextMenuAtSelection(); return true;

            case "Escape":
                this.CancelEdit();
                this.ClearClipboard();
                this.SelectedChartId = null;
                return true;

            case " " when ctrl && shift: this.Selection.SelectAll(); this.RaiseChanged(); return true;
            case " " when ctrl: this.SelectEntireColumn(); return true;
            case " " when shift: this.SelectEntireRow(); return true;

            case "=" when alt: this.ApplyAutoFunction(AutoFunction.Sum); return true;
        }

        if (!ctrl)
        {
            // A printable character starts an edit and becomes its first keystroke.
            if (key.Length == 1 && !alt)
            {
                this.BeginEdit(key);
                return true;
            }

            return false;
        }

        switch (lower)
        {
            case "c": this.Copy(); return true;
            case "x": this.Cut(); return true;
            case "v": this.Paste(); return true;
            case "z" when shift: this.Redo(); return true;
            case "z": this.Undo(); return true;
            case "y": this.Redo(); return true;

            case "b": this.ToggleBold(); return true;
            case "i": this.ToggleItalic(); return true;
            case "u": this.ToggleUnderline(); return true;
            case "5": this.ToggleStrikethrough(); return true;

            case "a": this.SelectAllSmart(); return true;
            case "d": this.FillDown(); return true;
            case "r": this.FillRight(); return true;
            case "g": this.ShowDialog(SpreadsheetDialogs.GoTo(this)); return true;
            case "k": this.ShowDialog(SpreadsheetDialogs.Hyperlink(this)); return true;
            case "l" when shift: this.ToggleAutoFilter(); return true;
            case "1": this.ShowDialog(SpreadsheetDialogs.FormatCells(this)); return true;
            case "9": this.SetRowsHidden(true); return true;
            case "0": this.SetColumnsHidden(true); return true;

            // Excel's shortcuts on the shifted digits. A browser reports the shifted character, so both
            // spellings are listed.
            case ";": this.InsertCurrentDate(); return true;
            case ":": this.InsertCurrentTime(); return true;
            case "`" or "~": this.ToggleShowFormulas(); return true;
            case "$" or "4" when shift: this.SetNumberFormat(NumberFormatPreset.Currency); return true;
            case "%" when shift: this.SetNumberFormat(NumberFormatPreset.Percent); return true;
            case "&" or "7" when shift: this.ApplyBorders(Commands.BorderPreset.Outside); return true;
            case "_" or "-" when shift: this.ApplyBorders(Commands.BorderPreset.None); return true;
            case "!" when shift: this.SetNumberFormat(NumberFormatPreset.Number); return true;
            case "#" when shift: this.SetNumberFormat(NumberFormatPreset.ShortDate); return true;
            case "@" when shift: this.SetNumberFormat(NumberFormatPreset.Time); return true;
        }

        return false;
    }

    /// <summary>Moves a screen's worth of rows, the way Page Up and Page Down do.</summary>
    void Page(int direction, bool extend)
    {
        var (first, last) = this.Viewport.VisibleRows();
        var rows = Math.Max(1, last - first);
        var target = Math.Clamp(this.Selection.Active.Row + direction * rows, 0, CellRef.MaxRow);
        var cell = new CellRef(this.Selection.Active.Column, target);

        if (extend)
            this.Selection.ExtendTo(cell);
        else
            this.Selection.MoveTo(cell);

        this.Viewport.ScrollBy(0, direction * this.Metrics.Rows.SizeOfRange(first, last));
        this.Viewport.ScrollIntoView(cell);
        this.RaiseChanged();
    }
}
