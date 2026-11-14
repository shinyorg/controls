using Microsoft.AspNetCore.Components;

namespace Shiny.Blazor.Controls.Analysis;

/// <summary>
/// Shows captions from an <see cref="ICaptionSource"/> (a speech caption analyzer) as a subtitle bar. Put it in the
/// same positioned container as a <c>MediaElement</c> or <c>CameraView</c>; it pins itself to the bottom centre.
/// </summary>
/// <remarks>
/// Bind <see cref="Position"/> to the player's position for captions made ahead of playback and it shows the cue
/// being spoken; for live captions it shows the newest text and clears after <see cref="LiveHoldTime"/> without new
/// speech. The bar draws its own background, so wrap it in no border of your own.
/// </remarks>
public partial class CaptionView : IDisposable
{
    string? text;
    ICaptionSource? subscribed;
    CancellationTokenSource? hold;

    /// <summary>The captions to show.</summary>
    [Parameter] public ICaptionSource? Source { get; set; }

    /// <summary>The media position, for timed captions.</summary>
    [Parameter] public TimeSpan Position { get; set; }

    /// <summary>Timed, live, or decided by the source. Default <see cref="CaptionDisplayMode.Auto"/>.</summary>
    [Parameter] public CaptionDisplayMode Mode { get; set; } = CaptionDisplayMode.Auto;

    /// <summary>In live mode, how long a caption stays up with no new speech. Default 4 seconds.</summary>
    [Parameter] public TimeSpan LiveHoldTime { get; set; } = TimeSpan.FromSeconds(4);

    /// <summary>In live mode, the most characters shown; older text is cut at a word boundary. Default 140.</summary>
    [Parameter] public int MaxLiveCharacters { get; set; } = 140;

    /// <summary>Extra CSS classes on the bar.</summary>
    [Parameter] public string? CssClass { get; set; }

    /// <summary>Inline style on the bar (override <c>--shiny-captions-*</c> custom properties here).</summary>
    [Parameter] public string? Style { get; set; }

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(this.subscribed, this.Source))
        {
            if (this.subscribed is not null)
                this.subscribed.CaptionsChanged -= this.OnCaptionsChanged;
            this.subscribed = this.Source;
            if (this.subscribed is not null)
                this.subscribed.CaptionsChanged += this.OnCaptionsChanged;
        }
        this.text = this.Resolve();
    }

    bool IsTimedMode => this.Mode switch
    {
        CaptionDisplayMode.Timed => true,
        CaptionDisplayMode.Live => false,
        _ => this.Source?.IsTimed == true
    };

    string? Resolve()
    {
        var source = this.Source;
        if (source is null)
            return null;

        if (this.IsTimedMode)
            return source.Cues.CueAt(this.Position)?.Text;

        var cues = source.Cues;
        var last = cues.Count > 0 ? cues[^1].Text : null;
        var partial = source.PartialText;
        var combined = (last, partial) switch
        {
            (null, null) => null,
            ({ } l, null) => l,
            (null, { } p) => p,
            ({ } l, { } p) => l + " " + p
        };
        return combined is null ? null : Tail(combined.Trim(), this.MaxLiveCharacters);
    }

    static string Tail(string value, int max)
    {
        if (max <= 0 || value.Length <= max)
            return value;

        var cut = value.IndexOf(' ', value.Length - max);
        return "…" + (cut < 0 ? value[^max..] : value[(cut + 1)..]);
    }

    void OnCaptionsChanged(object? sender, EventArgs e) => _ = this.InvokeAsync(() =>
    {
        this.text = this.Resolve();
        this.StateHasChanged();
        if (!this.IsTimedMode)
            this.RestartHold();
    });

    void RestartHold()
    {
        this.hold?.Cancel();
        this.hold = new CancellationTokenSource();
        var token = this.hold.Token;
        _ = Task.Delay(this.LiveHoldTime, token).ContinueWith(t =>
        {
            if (t.IsCanceled)
                return;
            _ = this.InvokeAsync(() =>
            {
                this.text = null;
                this.StateHasChanged();
            });
        }, TaskScheduler.Default);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        this.hold?.Cancel();
        if (this.subscribed is not null)
            this.subscribed.CaptionsChanged -= this.OnCaptionsChanged;
    }
}
