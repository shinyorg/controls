using Android.Content;
using Android.Opengl;
using AndroidX.Media3.Common;
using AndroidX.Media3.Common.Util;
using AndroidX.Media3.Effect;
using AndroidX.Media3.ExoPlayer;
using AndroidX.Media3.ExoPlayer.Audio;
using Java.Nio;
using MediaItem = AndroidX.Media3.Common.MediaItem;
using Media3Size = AndroidX.Media3.Common.Util.Size;

namespace Shiny.Maui.Controls.Media;

// Frame and audio taps for analysis. Audio is a TeeAudioProcessor inside the audio sink; frames are a GL effect in
// Media3's video effects chain that reads a scaled-down copy of each frame back from the GPU.
partial class AndroidMediaPlayerBackend
{
    // longest edge of the frames handed to a frame analyzer — enough for OCR on burned-in text, cheap to read back
    const int AnalysisMaxEdge = 1280;

    readonly AnalysisAudioTee audioTee;
    IMediaAnalysisSink? analysisSink;
    MediaItem? currentItem;
    ReadbackEffect? readback;
    bool effectsPrepared;

    public void SetAnalysisSink(IMediaAnalysisSink? sink)
    {
        this.analysisSink = sink;

        // ⚠️ Media3 builds its frame-processing pipeline only at prepare: video effects set on a player prepared
        // without any are accepted and silently never run. So frames switched on mid-playback reopen the item at
        // the current position, once; after that the effect stays and only its readback is gated.
        if (sink is { FramesEnabled: true } && !this.effectsPrepared && this.currentItem is not null)
            this.ReprepareWithEffects();
    }

    public IAsyncEnumerable<AudioFrame>? DecodeAudio(MediaSource source, AudioAnalysisFormat format, CancellationToken ct)
    {
        var uri = ResolveUri(source);
        return uri is null ? null : AndroidAudioDecoder.DecodeAsync(this.context, uri, format, ct);
    }

    // Before every prepare: install the readback effect when a frame analyzer is attached.
    void ApplyAnalysisEffects()
    {
        if (this.analysisSink is { FramesEnabled: true })
        {
            this.readback ??= new ReadbackEffect(this);
            this.player.SetVideoEffects([this.readback]);
            this.effectsPrepared = true;
        }
        else if (this.effectsPrepared)
        {
            this.player.SetVideoEffects([]);
            this.effectsPrepared = false;
        }
    }

    void ReprepareWithEffects()
    {
        var item = this.currentItem;
        if (item is null)
            return;

        var position = Math.Max(0, this.player.CurrentPosition);
        this.ApplyAnalysisEffects();
        this.player.SetMediaItem(item, position);
        this.player.Prepare();
    }

    internal bool WantsFrame() => this.analysisSink?.WantsFrame() == true;

    internal void OnReadbackFrame(VideoFrame frame)
    {
        var sink = this.analysisSink;
        if (sink is null)
            frame.Dispose();
        else
            sink.OnFrame(frame);
    }


    /// <summary>Puts the analysis tee in the audio sink, ahead of the speed/pitch stage.</summary>
    sealed class AnalysisRenderersFactory(Context context, TeeAudioProcessor.IAudioBufferSink tee) : DefaultRenderersFactory(context)
    {
        protected override IAudioSink? BuildAudioSink(Context? context, bool enableFloatOutput, bool enableAudioTrackPlaybackParams)
            => new DefaultAudioSink.Builder(context!)
                .SetAudioProcessors([new TeeAudioProcessor(tee)])!
                .SetEnableFloatOutput(enableFloatOutput)!
                .SetEnableAudioTrackPlaybackParams(enableAudioTrackPlaybackParams)!
                .Build();
    }


    /// <summary>
    /// Receives every decoded PCM buffer on ExoPlayer's playback thread. No timestamps come with them; the control's
    /// pipeline counts samples from the last discontinuity.
    /// </summary>
    sealed class AnalysisAudioTee(AndroidMediaPlayerBackend owner) : Java.Lang.Object, TeeAudioProcessor.IAudioBufferSink
    {
        const int EncodingPcm16 = 2;  // C.ENCODING_PCM_16BIT
        const int EncodingFloat = 4;  // C.ENCODING_PCM_FLOAT

        int rate;
        int channels;
        int encoding;
        byte[] bytes = [];
        float[] samples = [];

        public void Flush(int sampleRateHz, int channelCount, int encoding)
        {
            this.rate = sampleRateHz;
            this.channels = channelCount;
            this.encoding = encoding;
        }

        public void HandleBuffer(ByteBuffer buffer)
        {
            var sink = owner.analysisSink;
            if (sink is null || !sink.WantsAudio() || this.channels <= 0 || this.rate <= 0)
                return;

            var n = buffer.Remaining();
            if (n == 0)
                return;

            if (this.bytes.Length < n)
                this.bytes = new byte[n];
            buffer.Get(this.bytes, 0, n);

            int count;
            if (this.encoding == EncodingFloat)
            {
                count = n / 4;
                if (this.samples.Length < count)
                    this.samples = new float[count];
                System.Buffer.BlockCopy(this.bytes, 0, this.samples, 0, count * 4);
            }
            else if (this.encoding == EncodingPcm16)
            {
                count = n / 2;
                if (this.samples.Length < count)
                    this.samples = new float[count];
                for (var i = 0; i < count; i++)
                    this.samples[i] = (short)(this.bytes[i * 2] | (this.bytes[i * 2 + 1] << 8)) / 32768f;
            }
            else
            {
                return; // 8/24/32-bit integer PCM is not produced by the default decoders
            }

            sink.OnAudio(this.samples.AsSpan(0, count - count % this.channels), this.rate, this.channels);
        }
    }


    /// <summary>
    /// A pass-through GL effect that, when the frame analyzer wants a frame, also draws the frame into a small
    /// off-screen target and reads it back. Implements every Java default method on its library interfaces (see the
    /// Android rule in CLAUDE.md): <c>GlEffect.isNoOp</c> and <c>Effect.getDurationAfterEffectApplied</c>.
    /// </summary>
    sealed class ReadbackEffect(AndroidMediaPlayerBackend owner) : Java.Lang.Object, IGlEffect
    {
        public IGlShaderProgram ToGlShaderProgram(Context? context, bool useHdr) => new ReadbackShaderProgram(owner);

        public bool IsNoOp(int inputWidth, int inputHeight) => false;

        public long GetDurationAfterEffectApplied(long durationUs) => durationUs;
    }


    sealed class ReadbackShaderProgram : BaseGlShaderProgram
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
            precision mediump float;
            uniform sampler2D uTexSampler;
            varying vec2 vTexSamplingCoord;
            void main() {
                gl_FragColor = texture2D(uTexSampler, vTexSamplingCoord);
            }
            """;

        // Media3 runs renderers from a large position offset; frame timestamps arrive with it applied
        const long RendererOffsetUs = 1_000_000_000_000L;

        // GL_VIEWPORT — the binding's GLES20.GlViewport is the method, not the constant
        const int GlViewportParameter = 0x0BA2;

        readonly AndroidMediaPlayerBackend owner;
        readonly GlProgram program;
        int width;
        int height;
        int analysisWidth;
        int analysisHeight;
        int analysisTexture = -1;
        int analysisFbo = -1;
        ByteBuffer? pixels;
        readonly int[] savedFbo = new int[1];
        readonly int[] savedViewport = new int[4];

        public ReadbackShaderProgram(AndroidMediaPlayerBackend owner) : base(false, 1)
        {
            this.owner = owner;
            this.program = new GlProgram(Vertex, Fragment);
            this.program.SetBufferAttribute("aFramePosition", GlUtil.GetNormalizedCoordinateBounds()!, GlUtil.HomogeneousCoordinateVectorSize);
        }

        public override Media3Size Configure(int inputWidth, int inputHeight)
        {
            this.width = inputWidth;
            this.height = inputHeight;

            var scale = Math.Min(1d, (double)AnalysisMaxEdge / Math.Max(inputWidth, inputHeight));
            this.analysisWidth = Math.Max(2, (int)(inputWidth * scale) & ~1);
            this.analysisHeight = Math.Max(2, (int)(inputHeight * scale) & ~1);
            this.ReleaseTarget();
            return new Media3Size(inputWidth, inputHeight);
        }

        public override void DrawFrame(int inputTexId, long presentationTimeUs)
        {
            // the visible frame, unchanged
            this.Draw(inputTexId);

            if (!this.owner.WantsFrame())
                return;

            try
            {
                this.owner.OnReadbackFrame(this.ReadBack(inputTexId, presentationTimeUs));
            }
            catch
            {
                // a failed readback costs one analysis frame, never the picture
            }
        }

        void Draw(int inputTexId)
        {
            this.program.Use();
            this.program.SetSamplerTexIdUniform("uTexSampler", inputTexId, 0);
            this.program.BindAttributesAndUniforms();
            GLES20.GlDrawArrays(GLES20.GlTriangleStrip, 0, 4);
        }

        VideoFrame ReadBack(int inputTexId, long presentationTimeUs)
        {
            if (this.analysisFbo < 0)
            {
                this.analysisTexture = GlUtil.CreateTexture(this.analysisWidth, this.analysisHeight, false);
                this.analysisFbo = GlUtil.CreateFboForTexture(this.analysisTexture);
                this.pixels = ByteBuffer.AllocateDirect(this.analysisWidth * this.analysisHeight * 4)!.Order(ByteOrder.NativeOrder()!);
            }

            // the base class has the output target focused; draw the small copy, read it, then put that back
            GLES20.GlGetIntegerv(GLES20.GlFramebufferBinding, this.savedFbo, 0);
            GLES20.GlGetIntegerv(GlViewportParameter, this.savedViewport, 0);
            try
            {
                GlUtil.FocusFramebufferUsingCurrentContext(this.analysisFbo, this.analysisWidth, this.analysisHeight);
                this.Draw(inputTexId);

                var buffer = this.pixels!;
                buffer.Rewind();
                GLES20.GlReadPixels(0, 0, this.analysisWidth, this.analysisHeight, GLES20.GlRgba, GLES20.GlUnsignedByte, buffer);
            }
            finally
            {
                GLES20.GlBindFramebuffer(GLES20.GlFramebuffer, this.savedFbo[0]);
                GLES20.GlViewport(this.savedViewport[0], this.savedViewport[1], this.savedViewport[2], this.savedViewport[3]);
            }

            // GL rows run bottom-up; frames are top row first
            var stride = this.analysisWidth * 4;
            var raw = new byte[stride * this.analysisHeight];
            var rgba = new byte[raw.Length];
            this.pixels!.Rewind();
            this.pixels.Get(raw, 0, raw.Length);
            for (var y = 0; y < this.analysisHeight; y++)
                System.Buffer.BlockCopy(raw, (this.analysisHeight - 1 - y) * stride, rgba, y * stride, stride);

            var us = presentationTimeUs >= RendererOffsetUs ? presentationTimeUs - RendererOffsetUs : presentationTimeUs;
            return new AndroidRgbaFrame(rgba, this.analysisWidth, this.analysisHeight)
            {
                Timestamp = TimeSpan.FromTicks(Math.Max(0, us) * 10)
            };
        }

        void ReleaseTarget()
        {
            if (this.analysisFbo >= 0)
            {
                GlUtil.DeleteFbo(this.analysisFbo);
                this.analysisFbo = -1;
            }
            if (this.analysisTexture >= 0)
            {
                GlUtil.DeleteTexture(this.analysisTexture);
                this.analysisTexture = -1;
            }
            this.pixels = null;
        }

        public override void Release()
        {
            base.Release();
            this.ReleaseTarget();
            this.program.Delete();
        }
    }
}
