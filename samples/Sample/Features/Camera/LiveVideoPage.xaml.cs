using System.Diagnostics;
using Shiny.Controls.Analysis;
using Shiny.Controls.Camera;
using Shiny.Maui.Controls.Analysis;
using Shiny.Maui.Controls.Camera;

namespace Sample.Features.Camera;

/// <summary>
/// <see cref="VideoEncoder"/> and <see cref="RemoteVideoView"/> wired back to back: the two ends of a video
/// call with no transport in between.
/// </summary>
public partial class LiveVideoPage : ContentPage
{
    // the two profiles a call switches between (Wi-Fi and BLE)
    static readonly (string Name, int MaxDimension, double FrameRate, int Bitrate, double JpegQuality)[] Profiles =
    [
        ("Wi-Fi: 640 px, 15 fps", 640, 15, 800_000, 0.6),
        ("BLE: 160 px, 3 fps", 160, 3, 64_000, 0.4)
    ];

    readonly List<VideoCodec> codecs;
    readonly Stopwatch window = Stopwatch.StartNew();
    int frames;
    long bytes;

    public LiveVideoPage()
    {
        this.InitializeComponent();
        SampleSourceCode.Attach(this);

        // only offer what both ends of this device can do
        this.codecs = Enum.GetValues<VideoCodec>()
            .Where(c => VideoEncoder.IsSupported(c) && RemoteVideoView.CanDecode(c))
            .ToList();
        foreach (var codec in this.codecs)
            this.CodecPicker.Items.Add(codec.ToString());
        foreach (var profile in Profiles)
            this.ProfilePicker.Items.Add(profile.Name);

        this.CodecPicker.SelectedIndex = this.codecs.Count > 0 ? 0 : -1;
        this.ProfilePicker.SelectedIndex = 0;

        // the encoder raises off the UI thread; Render is safe from any thread, the label is not
        this.Encoder.FrameEncoded += this.OnFrameEncoded;
        this.Remote.KeyFrameNeeded += (_, _) => this.Encoder.RequestKeyFrame();
    }

    void OnFrameEncoded(EncodedVideoFrame frame)
    {
        this.Remote.Render(frame);

        Interlocked.Increment(ref this.frames);
        Interlocked.Add(ref this.bytes, frame.Data.Length);
        if (this.window.ElapsedMilliseconds < 1000)
            return;

        var seconds = this.window.Elapsed.TotalSeconds;
        var fps = Interlocked.Exchange(ref this.frames, 0) / seconds;
        var kbps = Interlocked.Exchange(ref this.bytes, 0) * 8 / seconds / 1000;
        this.window.Restart();

        var text = $"{frame.Codec} {frame.Width}x{frame.Height}, rotation {frame.Rotation}, {fps:0.0} fps, {kbps:0} kbps";
        this.Dispatcher.Dispatch(() => this.Stats.Text = text);
    }

    void OnCodecChanged(object? sender, EventArgs e)
    {
        if (this.CodecPicker.SelectedIndex >= 0)
            this.Encoder.Codec = this.codecs[this.CodecPicker.SelectedIndex];
    }

    void OnProfileChanged(object? sender, EventArgs e)
    {
        if (this.ProfilePicker.SelectedIndex < 0)
            return;

        var profile = Profiles[this.ProfilePicker.SelectedIndex];
        this.Encoder.MaxDimension = profile.MaxDimension;
        this.Encoder.FrameRate = profile.FrameRate;
        this.Encoder.Bitrate = profile.Bitrate;
        this.Encoder.JpegQuality = profile.JpegQuality;
    }

    void OnFlipClicked(object? sender, EventArgs e)
        => this.Camera.Facing = this.Camera.Facing == CameraFacing.Front ? CameraFacing.Back : CameraFacing.Front;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        this.Camera.IsActive = true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        this.Camera.IsActive = false;
        this.Remote.Clear();
    }
}
