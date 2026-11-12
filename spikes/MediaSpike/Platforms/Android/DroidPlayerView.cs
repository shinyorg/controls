using Android.Content;
using Android.Opengl;
using Android.Widget;
using AndroidX.Media3.Common;
using AndroidX.Media3.Common.Util;
using AndroidX.Media3.Effect;
using AndroidX.Media3.ExoPlayer;
using AndroidX.Media3.ExoPlayer.Audio;
using AndroidX.Media3.UI;
using Java.Nio;
using Microsoft.Maui.Handlers;
using Size = AndroidX.Media3.Common.Util.Size;

namespace MediaSpike;

public partial class SpikePlayerHandler : ViewHandler<SpikePlayerView, DroidPlayerView>
{
    protected override DroidPlayerView CreatePlatformView() => new(this.Context);

    protected override void ConnectHandler(DroidPlayerView platformView)
    {
        base.ConnectHandler(platformView);
        this.VirtualView.Player = platformView;
        this.VirtualView.RaiseReady();
    }
}


/// <summary>SPIKE: ExoPlayer + PlayerView. Effects via setVideoEffects; audio tap via TeeAudioProcessor.</summary>
public class DroidPlayerView : FrameLayout, ISpikePlayer
{
    readonly IExoPlayer player;
    readonly PlayerView view;
    readonly TapSink sink;

    public string[] Effects { get; } = ["None", "Mono", "CRT (GLSL)"];
    public Action<float[], int, int, double, double>? AudioTap { get; set; }

    public DroidPlayerView(Context context) : base(context)
    {
        this.sink = new TapSink(this);
        this.player = new ExoPlayerBuilder(context, new TapRenderersFactory(context, this.sink)).Build()!;
        this.view = new PlayerView(context) { Player = this.player, UseController = false };
        this.AddView(this.view, new LayoutParams(LayoutParams.MatchParent, LayoutParams.MatchParent));
    }

    public string PlayheadStatus => $"playhead={this.player.CurrentPosition / 1000.0:F2} state={this.player.PlaybackState} playing={this.player.IsPlaying}";

    /// <summary>true = install a passthrough effect before prepare so the effects pipeline exists for live switching.</summary>
    public static bool Preset { get; set; }

    public void Open(string source)
    {
        var uri = source.StartsWith("http") ? source : "asset:///" + source;
        if (Preset)
        {
            this.player.SetVideoEffects([new Contrast(0f)]);
            Spike.Log("preset passthrough effect (Contrast 0) before prepare");
        }
        this.player.SetMediaItem(MediaItem.FromUri(uri)!);
        this.player.Prepare();
        Spike.Log($"opened {uri}");
    }

    public void Play() => this.player.Play();
    public void Pause() => this.player.Pause();

    public void SetEffect(string effect)
    {
        var list = new List<IEffect>();
        if (Preset && effect == "None")
            list.Add(new Contrast(0f)); // keep the pipeline alive rather than tearing it down
        switch (effect)
        {
            case "Mono": list.Add(RgbFilter.CreateGrayscaleFilter()!); break;
            case "CRT (GLSL)": list.Add(new CrtEffect()); break;
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // the question: can effects be changed on a playing player without re-preparing?
            this.player.SetVideoEffects(list);
            Spike.Log($"effect {effect} set in {sw.Elapsed.TotalMilliseconds:F1}ms {this.PlayheadStatus}");
        }
        catch (Exception ex)
        {
            Spike.Log($"effect {effect} FAILED: {ex.GetType().Name} {ex.Message}");
        }
    }

    public Task<bool> StartPipAsync() => Task.FromResult(false);

    internal void OnPcm(float[] samples, int frames, int channels, double rate, double pts) => this.AudioTap?.Invoke(samples, frames, channels, rate, pts);


    /// <summary>
    /// Implements every Java default method on the library interfaces (CLAUDE.md rule): GlEffect.isNoOp and Effect.getDurationAfterEffectApplied.
    /// </summary>
    class CrtEffect : Java.Lang.Object, IGlEffect
    {
        public IGlShaderProgram ToGlShaderProgram(Context? context, bool useHdr) => new CrtShaderProgram();
        public bool IsNoOp(int inputWidth, int inputHeight) => false;
        // Effect (base interface) default method — Java returns the duration unchanged
        public long GetDurationAfterEffectApplied(long durationUs) => durationUs;
    }


    class CrtShaderProgram : BaseGlShaderProgram
    {
        const string Vertex = """
            attribute vec4 aFramePosition;
            varying vec2 vTexSamplingCoord;
            void main() {
                gl_Position = aFramePosition;
                vTexSamplingCoord = aFramePosition.xy * 0.5 + 0.5;
            }
            """;

        const string Fragment = """
            precision highp float;
            uniform sampler2D uTexSampler;
            uniform float uTime;
            uniform float uCurvature;
            uniform float uScanline;
            uniform float uAberration;
            uniform float uLines;
            uniform vec2 uSize;
            varying vec2 vTexSamplingCoord;
            void main() {
                vec2 cc = vTexSamplingCoord - 0.5;
                vec2 d = 0.5 + cc * (1.0 + uCurvature * dot(cc, cc));
                if (d.x < 0.0 || d.y < 0.0 || d.x > 1.0 || d.y > 1.0) { gl_FragColor = vec4(0.0, 0.0, 0.0, 1.0); return; }
                vec2 off = vec2(uAberration / uSize.x, 0.0);
                vec3 col = vec3(texture2D(uTexSampler, d + off).r, texture2D(uTexSampler, d).g, texture2D(uTexSampler, d - off).b);
                float line = 0.5 + 0.5 * sin(d.y * uLines * 6.2831853);
                col *= mix(1.0, line, uScanline);
                float v = 16.0 * d.x * d.y * (1.0 - d.x) * (1.0 - d.y);
                col *= pow(v, 0.3);
                col *= 0.96 + 0.04 * sin(uTime * 110.0);
                col *= vec3(0.95, 1.05, 0.95);
                gl_FragColor = vec4(col, 1.0);
            }
            """;

        readonly GlProgram program;
        int width, height;
        long frames;

        public CrtShaderProgram() : base(false, 1)
        {
            this.program = new GlProgram(Vertex, Fragment);
            this.program.SetBufferAttribute("aFramePosition", GlUtil.GetNormalizedCoordinateBounds()!, GlUtil.HomogeneousCoordinateVectorSize);
            Spike.Log("CRT GlProgram compiled");
        }

        public override Size Configure(int inputWidth, int inputHeight)
        {
            this.width = inputWidth;
            this.height = inputHeight;
            Spike.Log($"CRT configure {inputWidth}x{inputHeight}");
            return new Size(inputWidth, inputHeight);
        }

        public override void DrawFrame(int inputTexId, long presentationTimeUs)
        {
            this.program.Use();
            this.program.SetSamplerTexIdUniform("uTexSampler", inputTexId, 0);
            this.program.SetFloatUniform("uTime", presentationTimeUs / 1_000_000f);
            this.program.SetFloatUniform("uCurvature", 0.25f);
            this.program.SetFloatUniform("uScanline", 0.45f);
            this.program.SetFloatUniform("uAberration", 2.5f);
            this.program.SetFloatUniform("uLines", 240f);
            this.program.SetFloatsUniform("uSize", [this.width, this.height]);
            this.program.BindAttributesAndUniforms();
            GLES20.GlDrawArrays(GLES20.GlTriangleStrip, 0, 4);
            if (++this.frames % 150 == 1)
                Spike.Log($"CRT drawFrame #{this.frames} pts={presentationTimeUs / 1e6:F2}");
        }

        public override void Release()
        {
            base.Release();
            this.program.Delete();
        }
    }


    /// <summary>Puts a TeeAudioProcessor in the audio sink so every decoded PCM buffer is copied out.</summary>
    class TapRenderersFactory(Context context, TeeAudioProcessor.IAudioBufferSink sink) : DefaultRenderersFactory(context)
    {
        protected override IAudioSink? BuildAudioSink(Context? context, bool enableFloatOutput, bool enableAudioTrackPlaybackParams)
            => new DefaultAudioSink.Builder(context!)
                .SetAudioProcessors([new TeeAudioProcessor(sink)])!
                .Build();
    }


    class TapSink(DroidPlayerView owner) : Java.Lang.Object, TeeAudioProcessor.IAudioBufferSink
    {
        int rate, channels, encoding;
        long framesSinceFlush;
        byte[] bytes = [];
        float[] planar = [];
        bool logged;

        public void Flush(int sampleRateHz, int channelCount, int encoding)
        {
            this.rate = sampleRateHz;
            this.channels = channelCount;
            this.encoding = encoding;
            this.framesSinceFlush = 0;
            Spike.Log($"tee: flush rate={sampleRateHz} ch={channelCount} encoding={encoding} (2=PCM16, 4=FLOAT)");
        }

        public void HandleBuffer(ByteBuffer buffer)
        {
            var n = buffer.Remaining();
            if (n == 0 || this.channels == 0)
                return;
            if (this.bytes.Length < n)
                this.bytes = new byte[n];
            buffer.Get(this.bytes, 0, n);

            // PCM16 interleaved -> planar float
            var frames = n / 2 / this.channels;
            if (this.planar.Length < frames * this.channels)
                this.planar = new float[frames * this.channels];
            for (var i = 0; i < frames; i++)
                for (var ch = 0; ch < this.channels; ch++)
                {
                    var o = (i * this.channels + ch) * 2;
                    this.planar[ch * frames + i] = (short)(this.bytes[o] | (this.bytes[o + 1] << 8)) / 32768f;
                }

            // no timestamps on the sink: position is frames counted since the last flush (seek/format change)
            var pts = this.framesSinceFlush / (double)this.rate;
            this.framesSinceFlush += frames;
            if (!this.logged)
            {
                this.logged = true;
                Spike.Log($"tee: first buffer bytes={n} frames={frames}");
            }
            owner.OnPcm(this.planar, frames, this.channels, this.rate, pts);
        }
    }
}
