namespace Shiny.Maui.Controls.Office;

/// <summary>
/// The hidden entry the canvas editors type through: an <see cref="Entry"/> that also reports a
/// Backspace pressed while it is empty.
/// </summary>
/// <remarks>
/// The editors clear the entry after every keystroke, so it is empty whenever Backspace arrives - and
/// deleting from an empty text field changes no text, raises no <c>TextChanged</c>, and reaches nothing
/// cross-platform. On iOS and Mac Catalyst that made Backspace (soft keyboard and hardware alike) do
/// nothing at all. The Apple handler (<c>OfficeTextInputHandler</c>, registered by
/// <c>UseShinyOffice</c>) overrides <c>deleteBackward</c> and raises <see cref="EmptyBackspace"/>.
/// </remarks>
public class OfficeTextInput : Entry
{
    /// <summary>Backspace was pressed while the entry held no text.</summary>
    public event EventHandler? EmptyBackspace;

    /// <summary>Raises <see cref="EmptyBackspace"/>. The seam the platform handler (and a test) calls.</summary>
    public void SendEmptyBackspace() => this.EmptyBackspace?.Invoke(this, EventArgs.Empty);
}
