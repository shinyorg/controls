namespace Shiny.Blazor.Controls.Toast;

/// <summary>Why a snackbar closed.</summary>
public enum SnackbarResult
{
    /// <summary>The user pressed the action button.</summary>
    ActionInvoked,

    /// <summary>The duration ran out.</summary>
    TimedOut,

    /// <summary>The user tapped the body, the handle was disposed, or the token was cancelled.</summary>
    Dismissed,

    /// <summary>A newer toast took its place (<see cref="ToastQueueMode.Replace"/>, or a full stack).</summary>
    Replaced
}
