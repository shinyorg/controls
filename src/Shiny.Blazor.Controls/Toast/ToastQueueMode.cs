namespace Shiny.Blazor.Controls.Toast;

public enum ToastQueueMode
{
    Queue,
    Stack,

    /// <summary>
    /// Dismisses whatever queued toast is showing (it closes with <see cref="SnackbarResult.Replaced"/>)
    /// and shows this one straight away, ahead of anything still waiting. The default for snackbars:
    /// two "Undo" bars waiting their turn read as one undo that never happened.
    /// </summary>
    Replace
}
