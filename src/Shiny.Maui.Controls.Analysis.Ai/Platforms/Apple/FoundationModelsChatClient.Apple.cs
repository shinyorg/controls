using System.Buffers;
using System.Runtime.InteropServices;

namespace Shiny.Maui.Controls.Analysis.Ai;

// [DllImport] over Native/ShinyFoundationModels.xcframework, a C ABI written in Swift because Foundation Models
// is a Swift-only framework with no .NET binding. Callbacks arrive on a Swift concurrency thread.
public sealed unsafe partial class FoundationModelsChatClient
{
    const string Lib = "@rpath/ShinyFoundationModels.framework/ShinyFoundationModels";

    [DllImport(Lib)] static extern int sfm_availability();
    [DllImport(Lib)] static extern void sfm_prewarm(byte* instructions);
    [DllImport(Lib)] static extern void sfm_cancel(long handle);
    [DllImport(Lib)]
    static extern long sfm_respond(
        byte* requestJson,
        byte** images,
        int* imageLengths,
        int imageCount,
        IntPtr ctx,
        delegate* unmanaged<IntPtr, byte*, void> onDelta,
        delegate* unmanaged<IntPtr, byte*, int, byte*, void> onDone
    );

    private static partial FoundationModelsAvailability GetAvailabilityCore()
    {
        var code = sfm_availability();
        return Enum.IsDefined(typeof(FoundationModelsAvailability), code)
            ? (FoundationModelsAvailability)code
            : FoundationModelsAvailability.Unknown;
    }

    private static partial void PrewarmCore(string? instructions)
    {
        if (instructions is null)
        {
            sfm_prewarm(null);
            return;
        }

        var utf8 = Utf8(instructions);
        fixed (byte* p = utf8)
            sfm_prewarm(p);
    }

    sealed class Call(Action<string>? onSnapshot)
    {
        public readonly TaskCompletionSource<string> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly Action<string>? OnSnapshot = onSnapshot;
    }

    private partial Task<string> RespondCoreAsync(FoundationModelsRequest request, Action<string>? onSnapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var call = new Call(onSnapshot);
        var handle = GCHandle.Alloc(call);
        var json = Utf8(request.ToJson());

        // the bridge copies the image bytes before sfm_respond returns, so pinning for the call is enough
        var count = request.Images.Count;
        var pins = new MemoryHandle[count];
        var pointers = new IntPtr[count];
        var lengths = new int[count];
        long native;
        try
        {
            for (var i = 0; i < count; i++)
            {
                pins[i] = request.Images[i].Pin();
                pointers[i] = (IntPtr)pins[i].Pointer;
                lengths[i] = request.Images[i].Length;
            }

            fixed (byte* j = json)
            fixed (IntPtr* p = pointers)
            fixed (int* l = lengths)
            {
                native = sfm_respond(j, (byte**)p, l, count, GCHandle.ToIntPtr(handle), &OnDelta, &OnDone);
            }
        }
        finally
        {
            foreach (var pin in pins)
                pin.Dispose();
        }

        if (cancellationToken.CanBeCanceled && native != 0)
        {
            var registration = cancellationToken.Register(() => sfm_cancel(native));
            call.Completion.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
        }

        return cancellationToken.CanBeCanceled
            ? call.Completion.Task.WaitAsync(cancellationToken)
            : call.Completion.Task;
    }

    [UnmanagedCallersOnly]
    static void OnDelta(IntPtr ctx, byte* text)
    {
        if (GCHandle.FromIntPtr(ctx).Target is Call { OnSnapshot: { } onSnapshot })
            onSnapshot(Marshal.PtrToStringUTF8((IntPtr)text) ?? String.Empty);
    }

    // called exactly once per request, which is where the GCHandle is released
    [UnmanagedCallersOnly]
    static void OnDone(IntPtr ctx, byte* result, int errorKind, byte* error)
    {
        var handle = GCHandle.FromIntPtr(ctx);
        var call = (Call)handle.Target!;
        handle.Free();

        if (errorKind == 0)
        {
            call.Completion.TrySetResult(Marshal.PtrToStringUTF8((IntPtr)result) ?? String.Empty);
            return;
        }

        var message = Marshal.PtrToStringUTF8((IntPtr)error) ?? "Foundation Models request failed";
        if (errorKind == 5) // cancelled
            call.Completion.TrySetCanceled();
        else
            call.Completion.TrySetException(new FoundationModelsException(ToKind(errorKind), message));
    }

    static FoundationModelsErrorKind ToKind(int code) => Enum.IsDefined(typeof(FoundationModelsErrorKind), code)
        ? (FoundationModelsErrorKind)code
        : FoundationModelsErrorKind.Other;

    static byte[] Utf8(string value)
    {
        var bytes = new byte[System.Text.Encoding.UTF8.GetByteCount(value) + 1]; // NUL-terminated
        System.Text.Encoding.UTF8.GetBytes(value, bytes);
        return bytes;
    }
}
