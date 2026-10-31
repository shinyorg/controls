namespace Shiny.Maui.Controls.Toast;

public enum ToastDisplayMode
{
    Pill,
    FillHorizontal,

    /// <summary>
    /// A rectangular bar that spans the page on a phone and is capped at 600 wide on a wider screen.
    /// The default for <see cref="SnackbarConfig"/>, and usable on a plain toast too.
    /// </summary>
    Snackbar
}
