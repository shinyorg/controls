using System.Runtime.InteropServices;
using AudioToolbox;
using AVFoundation;
using AVKit;
using CoreGraphics;
using CoreImage;
using CoreMedia;
using Foundation;
using MediaToolbox;
using Microsoft.Maui.Handlers;
using ObjCRuntime;
using UIKit;

namespace MediaSpike;

public partial class SpikePlayerHandler : ViewHandler<SpikePlayerView, IosPlayerView>
{
    protected override IosPlayerView CreatePlatformView() => new();

    protected override void ConnectHandler(IosPlayerView platformView)
    {
        base.ConnectHandler(platformView);
        this.VirtualView.Player = platformView;
        this.VirtualView.RaiseReady();
    }
}


/// <summary>
/// SPIKE: AVPlayerLayer-backed view. Effects go through <c>AVPlayerItem.VideoComposition</c> with a CIFilter
/// handler; the audio tap is an MTAudioProcessingTap on the item's AudioMix.
/// </summary>
public class IosPlayerView : UIView, ISpikePlayer
{
    [Export("layerClass")]
    public static Class GetLayerClass() => new(typeof(AVPlayerLayer));

    AVPlayerLayer PlayerLayer => (AVPlayerLayer)this.Layer;

    readonly AVPlayer player = new();
    AVUrlAsset? asset;
    AVPlayerItem? item;
    AVPictureInPictureController? pip;
    PipDelegate? pipDelegate;
    string effect = "None";
    int framesRendered;
    MTAudioProcessingTap? tap;
    AudioStreamBasicDescription tapFormat;
    float[] tapBuffer = [];

    static CIKernel? crtKernel;

    public string[] Effects { get; } = ["None", "Mono", "CRT (Metal kernel)", "CRT (built-in CIFilters)"];
    public Action<float[], int, int, double, double>? AudioTap { get; set; }

    public IosPlayerView()
    {
        this.BackgroundColor = UIColor.Black;
        this.PlayerLayer.Player = this.player;
        this.PlayerLayer.VideoGravity = AVLayerVideoGravity.ResizeAspect;
        AVAudioSession.SharedInstance().SetCategory(AVAudioSessionCategory.Playback);
        AVAudioSession.SharedInstance().SetActive(true);

        if (AVPictureInPictureController.IsPictureInPictureSupported)
        {
            this.pipDelegate = new PipDelegate();
            this.pip = new AVPictureInPictureController(this.PlayerLayer) { Delegate = this.pipDelegate };
        }
        else
        {
            Spike.Log("PiP not supported on this device");
        }
    }

    public void Open(string source)
    {
        var url = source.StartsWith("http")
            ? NSUrl.FromString(source)!
            : NSUrl.FromFilename(NSBundle.MainBundle.PathForResource(Path.GetFileNameWithoutExtension(source), Path.GetExtension(source).TrimStart('.'))!);

        this.asset = AVUrlAsset.Create(url);
        this.item = new AVPlayerItem(this.asset);
        this.ApplyEffect();
        if (this.AudioTap != null)
            this.AttachTap(this.item, this.asset);
        this.player.ReplaceCurrentItemWithPlayerItem(this.item);
        Spike.Log($"opened {source}");
    }

    public void Play() => this.player.Play();
    public void Pause() => this.player.Pause();

    public void SetEffect(string effect)
    {
        this.effect = effect;
        this.ApplyEffect();
    }

    public Task<bool> StartPipAsync()
    {
        if (this.pip == null)
            return Task.FromResult(false);

        Spike.Log($"pip possible={this.pip.PictureInPicturePossible}");
        if (!this.pip.PictureInPicturePossible)
            return Task.FromResult(false);

        this.pip.StartPictureInPicture();
        return Task.FromResult(true);
    }

    // ── effects ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>true = old behaviour (new composition per switch); false = one composition, effect read per frame.</summary>
    public static bool Reassign { get; set; }

    void ApplyEffect()
    {
        if (this.item == null || this.asset == null)
            return;

        if (!Reassign && this.item.VideoComposition != null)
        {
            // stable composition: the handler picks the new effect up on its next frame
            Spike.Log($"effect {this.effect} (stable composition, no reassign) playhead={this.player.CurrentTime.Seconds:F2}");
            return;
        }

        if (this.effect == "None" && Reassign)
        {
            this.item.VideoComposition = null;
            Spike.Log("effect None (composition cleared)");
            return;
        }
        // stable mode: attached once at open (passthrough while None), never re-attached

        this.framesRendered = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var fixedName = this.effect;
        this.item.VideoComposition = AVVideoComposition.CreateVideoComposition(this.asset, request =>
        {
            var name = Reassign ? fixedName : this.effect;
            var t = request.CompositionTime.Seconds;
            var output = Render(name, request.SourceImage, t);
            request.Finish(output, null);

            var n = Interlocked.Increment(ref this.framesRendered);
            if (n == 1 || n % 150 == 0)
                Spike.Log($"composition '{name}' frame {n} t={t:F2} extent={request.SourceImage.Extent.Size} avg {(sw.Elapsed.TotalMilliseconds / n):F2}ms/frame wall");
        });
        Spike.Log($"effect {this.effect} (composition set, renderSize={this.item.VideoComposition?.RenderSize}) playhead={this.player.CurrentTime.Seconds:F2}");
    }

    public string PlayheadStatus => $"playhead={this.player.CurrentTime.Seconds:F2} status={this.player.TimeControlStatus} rate={this.player.Rate}";

    static CIImage Render(string effect, CIImage src, double time) => effect switch
    {
        "Mono" => new CIPhotoEffectMono { InputImage = src }.OutputImage!,
        "CRT (Metal kernel)" => CrtKernel(src, time),
        "CRT (built-in CIFilters)" => CrtBuiltIn(src, time),
        _ => src
    };

    static CIImage CrtKernel(CIImage src, double time)
    {
        if (crtKernel == null)
        {
            var data = NSData.FromFile(NSBundle.MainBundle.PathForResource("crt", "metallib")!);
            crtKernel = CIKernel.FromFunction("crt", data, out var error);
            Spike.Log($"metal CIKernel loaded={crtKernel != null} error={error?.LocalizedDescription ?? "none"}");
            if (crtKernel == null)
                return src;
        }

        var extent = src.Extent;
        return crtKernel.ApplyWithExtent(
            extent,
            (index, rect) => extent,
            [src, NSNumber.FromDouble(time), NSNumber.FromFloat(0.25f), NSNumber.FromFloat(0.45f), NSNumber.FromFloat(2.5f), NSNumber.FromFloat(240f)]
        ) ?? src;
    }

    /// <summary>The "no custom shader" CRT: curvature-ish bump, RGB split, stripe scanlines, bloom, vignette.</summary>
    static CIImage CrtBuiltIn(CIImage src, double time)
    {
        var extent = src.Extent;

        // RGB split: isolate R and B, offset them, add back over G
        CIImage Channel(float r, float g, float b, float dx)
        {
            var m = new CIColorMatrix
            {
                InputImage = src,
                RVector = new CIVector(r, 0, 0, 0),
                GVector = new CIVector(0, g, 0, 0),
                BVector = new CIVector(0, 0, b, 0),
                AVector = new CIVector(0, 0, 0, 1)
            }.OutputImage!;
            return m.ImageByApplyingTransform(CGAffineTransform.MakeTranslation(dx, 0));
        }
        var split = new CIAdditionCompositing
        {
            InputImage = Channel(1, 0, 0, 2.5f),
            BackgroundImage = new CIAdditionCompositing { InputImage = Channel(0, 1, 0, 0), BackgroundImage = Channel(0, 0, 1, -2.5f) }.OutputImage
        }.OutputImage!.ImageByCroppingToRect(extent);

        var bloom = new CIBloom { InputImage = split, Radius = 6, Intensity = 0.6f }.OutputImage!.ImageByCroppingToRect(extent);

        // horizontal scanlines: vertical stripes rotated 90°
        var lineWidth = (float)(extent.Height / 480.0);
        var stripes = new CIStripesGenerator
        {
            Color0 = new CIColor(0, 0, 0, 0.45f),
            Color1 = new CIColor(0, 0, 0, 0),
            Width = lineWidth,
            Sharpness = 0.3f
        }.OutputImage!.ImageByApplyingTransform(CGAffineTransform.MakeRotation((nfloat)(Math.PI / 2))).ImageByCroppingToRect(extent);
        var lined = new CISourceOverCompositing { InputImage = stripes, BackgroundImage = bloom }.OutputImage!;

        var bump = new CIBumpDistortion
        {
            InputImage = lined,
            InputCenter = new CGPoint(extent.GetMidX(), extent.GetMidY()),
            Radius = (float)Math.Max(extent.Width, extent.Height) * 0.75f,
            Scale = 0.18f
        }.OutputImage!.ImageByCroppingToRect(extent);

        var flicker = 0.96f + 0.04f * (float)Math.Sin(time * 110.0);
        var dimmed = new CIColorControls { InputImage = bump, Brightness = flicker - 1f, Saturation = 1.1f, Contrast = 1.05f }.OutputImage!;
        return new CIVignette { InputImage = dimmed, Intensity = 1.2f, Radius = 1.6f }.OutputImage!.ImageByCroppingToRect(extent);
    }

    // ── audio tap ───────────────────────────────────────────────────────────────────────────────

    void AttachTap(AVPlayerItem item, AVAsset asset)
    {
#pragma warning disable CS0618
        var track = asset.TracksWithMediaType(AVMediaTypes.Audio.GetConstant()!).FirstOrDefault();
#pragma warning restore CS0618
        if (track == null)
        {
            Spike.Log("tap: no audio track (not loaded yet / streaming?)");
            return;
        }

        var callbacks = new MTAudioProcessingTapCallbacks(this.TapProcess)
        {
            Prepare = this.TapPrepare,
        };
        this.tap = new MTAudioProcessingTap(callbacks, MTAudioProcessingTapCreationFlags.PostEffects);

        var p = AVMutableAudioMixInputParameters.FromTrack(track);
        p.AudioTapProcessor = this.tap;
        var mix = AVMutableAudioMix.Create();
        mix.InputParameters = [p];
        item.AudioMix = mix;
        Spike.Log("tap: attached MTAudioProcessingTap");
    }

    void TapPrepare(MTAudioProcessingTap tap, nint maxFrames, ref AudioStreamBasicDescription format)
    {
        this.tapFormat = format;
        Spike.Log($"tap: prepare maxFrames={maxFrames} rate={format.SampleRate} ch={format.ChannelsPerFrame} bits={format.BitsPerChannel} flags={format.FormatFlags}");
    }

    void TapProcess(MTAudioProcessingTap tap, nint numberFrames, MTAudioProcessingTapFlags flags, AudioBuffers bufferList, out nint numberFramesOut, out MTAudioProcessingTapFlags flagsOut)
    {
        var status = tap.GetSourceAudio(numberFrames, bufferList, out flagsOut, out var timeRange, out numberFramesOut);
        if (status != MTAudioProcessingTapError.None || this.AudioTap is not { } sink)
            return;

        var frames = (int)numberFramesOut;
        var channels = bufferList.Count;
        if (this.tapBuffer.Length < frames * channels)
            this.tapBuffer = new float[frames * channels];

        // expected format: float32, non-interleaved, one AudioBuffer per channel
        for (var ch = 0; ch < channels; ch++)
        {
            var buf = bufferList[ch];
            Marshal.Copy(buf.Data, this.tapBuffer, ch * frames, Math.Min(frames, buf.DataByteSize / 4));
        }
        sink(this.tapBuffer, frames, channels, this.tapFormat.SampleRate, timeRange.Start.Seconds);
    }

    class PipDelegate : AVPictureInPictureControllerDelegate
    {
        public override void DidStartPictureInPicture(AVPictureInPictureController c) => Spike.Log("pip: DidStart");
        public override void DidStopPictureInPicture(AVPictureInPictureController c) => Spike.Log("pip: DidStop");
        public override void FailedToStartPictureInPicture(AVPictureInPictureController c, NSError error) => Spike.Log($"pip: FAILED {error.LocalizedDescription}");
    }
}
