using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Shiny.Blazor.Controls.Camera.Media;

/// <summary>
/// Draws <see cref="IMediaService"/>'s modal camera — capture, recording and scanning. Render exactly one in your
/// layout (next to <c>&lt;DialogHost /&gt;</c>); it shows nothing until a service call opens a session.
/// </summary>
public partial class MediaHost : IDisposable
{
    [Inject] MediaService Media { get; set; } = default!;

    /// <summary>Extra class on the modal root, for restyling the chrome.</summary>
    [Parameter] public string? CssClass { get; set; }

    ElementReference root;
    CameraView? camera;
    string? focusedKey;
    Timer? elapsedTimer;

    protected override void OnInitialized()
    {
        this.Media.AttachHost();
        this.Media.Changed += this.OnMediaChanged;
    }


    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // move focus into the modal once per session, so Escape works and screen readers land inside it
        if (this.Media.ActiveSession is { } session && session.Key != this.focusedKey)
        {
            this.focusedKey = session.Key;
            try
            {
                await this.root.FocusAsync();
            }
            catch (Exception) { /* element gone mid-render — the next session focuses itself */ }
        }
    }


    void OnMediaChanged()
    {
        // the elapsed readout is the only thing that changes without an event, so tick only while recording
        var recording = this.Media.ActiveSession is { IsRecording: true };
        if (recording && this.elapsedTimer is null)
            this.elapsedTimer = new Timer(_ => this.InvokeAsync(this.StateHasChanged), null, 500, 500);
        else if (!recording && this.elapsedTimer is not null)
        {
            this.elapsedTimer.Dispose();
            this.elapsedTimer = null;
        }

        _ = this.InvokeAsync(this.StateHasChanged);
    }


    void OnStarted(MediaSession session)
    {
        if (this.camera is not null)
            this.Media.OnCameraStarted(session, this.camera);
    }


    void OnKeyDown(MediaSession session, KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
            this.Media.Cancel(session);
    }


    static bool IsReady(MediaSession session) => session.CameraReady.Task.IsCompletedSuccessfully;


    static string DefaultLabel(MediaSession session) => session.Kind switch
    {
        MediaSessionKind.Photo => "Take a photo",
        MediaSessionKind.Video => "Record a video",
        _ => "Scan"
    };


    static string FormatElapsed(DateTimeOffset started)
    {
        var elapsed = DateTimeOffset.UtcNow - started;
        return elapsed.TotalHours >= 1
            ? elapsed.ToString(@"h\:mm\:ss")
            : elapsed.ToString(@"m\:ss");
    }


    public void Dispose()
    {
        this.Media.Changed -= this.OnMediaChanged;
        this.Media.DetachHost();
        this.elapsedTimer?.Dispose();
    }
}
