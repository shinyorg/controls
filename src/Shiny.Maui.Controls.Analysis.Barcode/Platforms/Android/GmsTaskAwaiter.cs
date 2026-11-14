using Android.Gms.Tasks;

namespace Shiny.Maui.Controls.Analysis.Barcode;

// Bridges a Google Play Services Task to a .NET Task<Java.Lang.Object?>.
sealed class GmsTaskAwaiter : Java.Lang.Object, IOnSuccessListener, IOnFailureListener
{
    readonly TaskCompletionSource<Java.Lang.Object?> tcs = new();

    public Task<Java.Lang.Object?> Task => this.tcs.Task;

    public void OnSuccess(Java.Lang.Object? result) => this.tcs.TrySetResult(result);

    public void OnFailure(Java.Lang.Exception e) => this.tcs.TrySetException(new InvalidOperationException(e.Message, e));

    public static Task<Java.Lang.Object?> AwaitAsync(global::Android.Gms.Tasks.Task task)
    {
        var awaiter = new GmsTaskAwaiter();
        task.AddOnSuccessListener(awaiter);
        task.AddOnFailureListener(awaiter);
        return awaiter.Task;
    }
}
