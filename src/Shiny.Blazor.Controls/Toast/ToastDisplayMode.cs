namespace Shiny.Blazor.Controls.Toast;

public enum ToastDisplayMode
{
    Pill,
    FillHorizontal,

    /// <summary>
    /// A rectangular bar that spans the viewport on a phone and is capped at 600px on a wider screen.
    /// The default for <see cref="SnackbarConfig"/>, and usable on a plain toast too.
    /// </summary>
    Snackbar
}
