namespace Shiny.Blazor.Controls.Toast;

/// <summary>
/// A snackbar that stays up while work runs. Dispose it when the work is done.
/// </summary>
public interface ILoadingSnackbar : IDisposable
{
    /// <summary>The message. Setting it updates the snackbar in place.</summary>
    string Text { get; set; }

    /// <summary>Progress from 0 to 1 along the top line. Null (the default) is indeterminate.</summary>
    double? Progress { get; set; }

    /// <summary>Completes when the snackbar closes, with the reason.</summary>
    Task<SnackbarResult> Closed { get; }
}
