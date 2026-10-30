#if IOS || MACCATALYST || MACOS
using CoreAnimation;
using CoreFoundation;
using CoreGraphics;
using Foundation;
using Microsoft.Maui.Graphics.Platform;
using ObjCRuntime;
using Shiny.Controls.MotionIcons;
#if MACOS
using PlatformView = AppKit.NSView;
#else
using PlatformView = UIKit.UIView;
#endif

namespace Shiny.Maui.Controls.MotionIcons;

/// <summary>
/// Runs a motion icon's play on Core Animation, so the render server animates it and the app does
/// no work per frame.
/// </summary>
/// <remarks>
/// <para>Compiled into core's iOS asset, and linked into <c>Shiny.Maui.Controls.Desktop</c> for
/// macOS (AppKit) and Mac Catalyst, which only get core's <c>net10.0</c> asset.</para>
/// <para>The layer tree mirrors <see cref="MotionSceneBuilder"/>'s scene exactly: one root layer for
/// the icon as a whole, one shape layer per part, each the size of the viewBox with its anchor on
/// the part's origin, transformed translate-then-rotate-then-scale. That is what makes a part land on
/// the same pixels whichever renderer draws it.</para>
/// <para>Keys are not translated into Core Animation timing functions. Each animated property is
/// sampled once per display frame across a cycle with <see cref="MotionSampler"/> — the very code
/// the managed timeline evaluates — and handed over as linear keyframes. Every ease, elastic and
/// bounce included, comes out the same as the managed path at 60fps, without a second
/// implementation of any curve to drift.</para>
/// </remarks>
sealed class CoreAnimationMotionPlayer : INativeMotionPlayer
{
    const double SamplesPerSecond = 60d;
    const string AnimationKey = "shiny.motion";

    readonly PlatformView host;
    readonly NativeMotionRequest request;
    readonly double cycle;
    readonly double beginTime;
    double iterations;
    CALayer? stage;
    Clock? clock;
    SizeWatcher? watcher;
    bool paused;
    bool disposed;
    bool relayoutQueued;

    CoreAnimationMotionPlayer(PlatformView host, NativeMotionRequest request)
    {
        this.host = host;
        this.request = request;
        this.iterations = request.Iterations;
        this.cycle = request.Spec.Duration.TotalSeconds / request.Rate;

#if MACOS
        host.WantsLayer = true;
#endif
        this.Build();
        this.beginTime = this.LocalTime();
        this.Animate();

        // A view usually starts playing before its platform view has been given a frame, and MAUI's
        // SizeChanged has already fired by then, so the size it settles at has to be watched here.
        // Not with KVO on host.Layer: the binding's observer only weakly holds the layer's managed
        // wrapper, and once a GC collects it Dispose skips RemoveObserver and frees the observer
        // anyway — the layer keeps a dangling observer, which iOS 15 crashes on (#60).
        this.watcher = new SizeWatcher(this, host.Bounds);
        host.AddSubview(this.watcher);
    }

    /// <summary>Creates a player for a loaded view, or null to leave the play on the managed ticker.</summary>
    public static INativeMotionPlayer? TryCreate(MotionIconView view, NativeMotionRequest request)
    {
        if (request.Rate <= 0d || view.Handler?.PlatformView is not PlatformView host || host.Layer is null)
            return null;

        return new CoreAnimationMotionPlayer(host, request);
    }

    public event EventHandler? Finished;

    public TimeSpan Position => TimeSpan.FromSeconds(Math.Max(0d, this.LocalTime() - this.beginTime) * this.request.Rate);

    public void Pause()
    {
        if (this.paused || this.stage is not { } layer)
            return;

        var now = layer.ConvertTimeFromLayer(CAAnimation.CurrentMediaTime(), null);
        layer.Speed = 0f;
        layer.TimeOffset = now;
        this.paused = true;
    }

    public void Resume()
    {
        if (!this.paused || this.stage is not { } layer)
            return;

        // Apple's pause/resume recipe (QA1673): hand the frozen time back as a begin time, so the
        // layer's clock picks up exactly where it stopped rather than jumping to "now".
        var frozen = layer.TimeOffset;
        layer.Speed = 1f;
        layer.TimeOffset = 0d;
        layer.BeginTime = 0d;
        layer.BeginTime = layer.ConvertTimeFromLayer(CAAnimation.CurrentMediaTime(), null) - frozen;
        this.paused = false;
    }

    public void FinishCycle()
    {
        if (this.stage is null)
            return;

        // Re-adding the animations with the same begin time and fewer repeats is seamless: the
        // render server only sees the end of the active duration move, not a restart.
        var cyclesDone = Math.Max(0d, this.LocalTime() - this.beginTime) / this.cycle;
        var remaining = Math.Max(1d, Math.Floor(cyclesDone) + 1d);
        if (remaining >= this.iterations)
            return;

        this.iterations = remaining;
        this.Animate();
    }

    public void Relayout()
    {
        if (this.disposed || this.relayoutQueued)
            return;

        // Never rebuild layers inside the layout pass that reported the change.
        this.relayoutQueued = true;
        DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            this.relayoutQueued = false;
            this.RelayoutNow();
        });
    }

    void RelayoutNow()
    {
        // Layout passes report the same bounds over and over; only a real change is worth a rebuild.
        if (this.disposed || this.stage is not { } current || current.Bounds.Size == this.host.Bounds.Size)
            return;

        this.Build();
        this.Animate();
    }

    public void Dispose()
    {
        if (this.disposed)
            return;

        this.disposed = true;
        this.clock = null;

        this.watcher?.RemoveFromSuperview();
        this.watcher?.Dispose();
        this.watcher = null;

        // Take the animations off before the layers, so no stop callback is left pending on them.
        this.root?.RemoveAnimation(AnimationKey);
        foreach (var (layer, _) in this.parts)
            layer.RemoveAnimation(AnimationKey);

        this.stage?.RemoveFromSuperLayer();
        this.stage = null;
        this.root = null;
        this.parts.Clear();
    }

    double LocalTime()
        => this.stage?.ConvertTimeFromLayer(CAAnimation.CurrentMediaTime(), null) ?? CAAnimation.CurrentMediaTime();

    // ---------------------------------------------------------------------------------------------
    // Layers
    // ---------------------------------------------------------------------------------------------

    void Build()
    {
        CATransaction.Begin();
        CATransaction.DisableActions = true;
        try
        {
            var previous = this.stage;
            var layer = new CALayer
            {
                Frame = this.host.Bounds,
                // The artwork is authored y-down. UIKit layers already are; an AppKit view that is
                // not flipped has a y-up layer, so the stage flips itself to match.
#if MACOS
                GeometryFlipped = !this.host.IsFlipped,
#endif
                ContentsScale = this.Scale()
            };

            // A rebuild (resize, cycle-end stop) has to keep the clock it replaces, or a paused icon
            // would start moving and a playing one would jump back to its first frame.
            if (previous != null)
            {
                layer.Speed = previous.Speed;
                layer.TimeOffset = previous.TimeOffset;
                layer.BeginTime = previous.BeginTime;
                previous.RemoveFromSuperLayer();
            }

            this.host.Layer!.AddSublayer(layer);
            this.stage = layer;
            this.BuildArtwork(layer);
        }
        finally
        {
            CATransaction.Commit();
        }
    }

    CALayer? root;
    readonly List<(CAShapeLayer Layer, MotionIconPart Part)> parts = [];
    double fit;

    void BuildArtwork(CALayer stage)
    {
        var icon = this.request.Icon;
        var box = icon.ViewBox;
        var bounds = stage.Bounds;
        var size = Math.Min(bounds.Width, bounds.Height);
        this.fit = size / box;

        var rootOrigin = this.request.Spec.RootOrigin ?? icon.Center;
        var rootAnchor = new CGPoint(rootOrigin.X / box, rootOrigin.Y / box);
        var root = new CALayer
        {
            Bounds = new CGRect(0, 0, size, size),
            AnchorPoint = rootAnchor,
            // Letterboxed into the middle, as SceneStretch.Uniform does.
            Position = new CGPoint(
                (bounds.Width - size) / 2 + rootAnchor.X * size,
                (bounds.Height - size) / 2 + rootAnchor.Y * size)
        };
        stage.AddSublayer(root);
        this.root = root;
        this.parts.Clear();

        var builder = new PathBuilder();
        var scale = this.Scale();
        foreach (var part in icon.Parts)
        {
            var origin = icon.OriginOf(part);
            var anchor = new CGPoint(origin.X / box, origin.Y / box);
            var shape = new CAShapeLayer
            {
                Bounds = new CGRect(0, 0, size, size),
                AnchorPoint = anchor,
                Position = new CGPoint(anchor.X * size, anchor.Y * size),
                Path = Parse(builder, part.Path).AsCGPath(0, 0, (float)this.fit, (float)this.fit),
                FillColor = this.Resolve(part.Fill),
                StrokeColor = this.Resolve(part.Stroke),
                LineWidth = (nfloat)(this.request.StrokeWidth * part.StrokeScale * this.fit),
                LineCap = part.LineCap switch
                {
                    MotionLineCap.Butt => CAShapeLayer.CapButt,
                    MotionLineCap.Square => CAShapeLayer.CapSquare,
                    _ => CAShapeLayer.CapRound
                },
                LineJoin = part.LineJoin switch
                {
                    MotionLineJoin.Miter => CAShapeLayer.JoinMiter,
                    MotionLineJoin.Bevel => CAShapeLayer.JoinBevel,
                    _ => CAShapeLayer.JoinRound
                },
                ContentsScale = scale
            };
            root.AddSublayer(shape);
            this.parts.Add((shape, part));
        }
    }

    nfloat Scale()
    {
#if MACOS
        return this.host.Window?.BackingScaleFactor ?? AppKit.NSScreen.MainScreen?.BackingScaleFactor ?? 2;
#else
        return this.host.Window?.Screen.Scale ?? UIKit.UIScreen.MainScreen.Scale;
#endif
    }

    CGColor? Resolve(IconPaint paint) => paint.Kind switch
    {
        IconPaintKind.Current => this.request.Color.AsCGColor(),
        IconPaintKind.Accent => (this.request.Accent ?? this.request.Color).AsCGColor(),
        IconPaintKind.Fixed => Color.FromArgb(paint.Value).AsCGColor(),
        _ => null
    };

    static PathF Parse(PathBuilder builder, string data)
    {
        // Same rule as the managed scene: bad artwork leaves a gap, never a crash.
        try
        {
            return builder.BuildPath(data);
        }
        catch (Exception)
        {
            return new PathF();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Motion
    // ---------------------------------------------------------------------------------------------

    void Animate()
    {
        if (this.root is not { } root)
            return;

        var spec = this.request.Spec;
        var samples = Math.Max(2, (int)Math.Ceiling(spec.Duration.TotalSeconds * SamplesPerSecond));
        var keyTimes = new NSNumber[samples + 1];
        for (var i = 0; i <= samples; i++)
            keyTimes[i] = NSNumber.FromDouble((double)i / samples);

        // The root always carries a group, even an empty one: it is the clock a finite play's end is
        // read from.
        this.clock = double.IsPositiveInfinity(this.iterations) ? null : new Clock(this);
        this.Add(root, this.TransformAndOpacity(null, keyTimes, samples), this.clock);

        foreach (var (layer, part) in this.parts)
        {
            var animations = this.TransformAndOpacity(part.Id, keyTimes, samples);
            this.AddShapeTracks(animations, part, keyTimes, samples);
            this.Add(layer, animations, null);
        }
    }

    void Add(CALayer layer, List<CAAnimation> animations, Clock? delegateClock)
    {
        if (animations.Count == 0 && delegateClock is null)
        {
            layer.RemoveAnimation(AnimationKey);
            return;
        }

        foreach (var animation in animations)
            animation.Duration = this.cycle;

        var group = new CAAnimationGroup
        {
            Animations = [.. animations],
            Duration = this.cycle,
            RepeatCount = double.IsPositiveInfinity(this.iterations) ? float.PositiveInfinity : (float)this.iterations,
            BeginTime = this.beginTime,
            // FillMode.None, as on the managed path and the web: when the play ends the icon is the
            // artwork as drawn, which is what the layers' own (unanimated) values already are.
            RemovedOnCompletion = true,
            FillMode = CAFillMode.Removed
        };
        if (delegateClock != null)
            group.Delegate = delegateClock;

        layer.AddAnimation(group, AnimationKey);
    }

    List<CAAnimation> TransformAndOpacity(string? partId, NSNumber[] keyTimes, int samples)
    {
        var result = new List<CAAnimation>();
        var spec = this.request.Spec;

        var tx = Track(spec, partId, MotionChannel.TranslateX);
        var ty = Track(spec, partId, MotionChannel.TranslateY);
        var rotate = Track(spec, partId, MotionChannel.Rotate);
        var uniform = Track(spec, partId, MotionChannel.Scale);
        var sx = Track(spec, partId, MotionChannel.ScaleX) ?? uniform;
        var sy = Track(spec, partId, MotionChannel.ScaleY) ?? uniform;

        if (tx != null || ty != null || rotate != null || sx != null || sy != null)
        {
            var values = new NSObject[samples + 1];
            for (var i = 0; i <= samples; i++)
            {
                var o = (double)i / samples;
                // Row-vector order: scale, then rotate, then translate — the managed layer's order,
                // about the same anchor.
                var t = CATransform3D.MakeScale((nfloat)Value(sx, o, 1d), (nfloat)Value(sy, o, 1d), 1)
                    .Concat(CATransform3D.MakeRotation((nfloat)(Value(rotate, o, 0d) * Math.PI / 180d), 0, 0, 1))
                    .Concat(CATransform3D.MakeTranslation((nfloat)(Value(tx, o, 0d) * this.fit), (nfloat)(Value(ty, o, 0d) * this.fit), 0));
                values[i] = NSValue.FromCATransform3D(t);
            }
            result.Add(Keyframes("transform", values, keyTimes));
        }

        if (Track(spec, partId, MotionChannel.Opacity) is { } opacity)
            result.Add(Keyframes("opacity", Numbers(opacity, samples, static v => v), keyTimes));

        return result;
    }

    void AddShapeTracks(List<CAAnimation> animations, MotionIconPart part, NSNumber[] keyTimes, int samples)
    {
        var spec = this.request.Spec;

        if (Track(spec, part.Id, MotionChannel.StrokeWidth) is { } width)
        {
            var scale = this.request.StrokeWidth * part.StrokeScale * this.fit;
            animations.Add(Keyframes("lineWidth", Numbers(width, samples, v => v * scale), keyTimes));
        }

        if (Track(spec, part.Id, MotionChannel.Trim) is { } trim)
            animations.Add(Keyframes("strokeEnd", Numbers(trim, samples, static v => v), keyTimes));

        foreach (var track in spec.ColorTracks)
        {
            if (track.PartId != null && !String.Equals(track.PartId, part.Id, StringComparison.Ordinal))
                continue;

            var values = new NSObject[samples + 1];
            for (var i = 0; i <= samples; i++)
                values[i] = Runtime.GetNSObject(this.ColorAt(track, (double)i / samples).AsCGColor().Handle)!;

            var key = track.Channel is MotionPaintChannel.Fill ? "fillColor" : "strokeColor";
            animations.Add(Keyframes(key, values, keyTimes));
        }
    }

    Color ColorAt(MotionColorTrack track, double offset)
    {
        // A null key is the host's icon colour, which here is a known value — so it blends, as the
        // managed timeline does with the same pair.
        var (from, to, progress) = MotionSampler.ColorAt(track.Keys, offset);
        var a = from is null ? this.request.Color : Color.FromArgb(from);
        var b = to is null ? this.request.Color : Color.FromArgb(to);
        var t = (float)progress;
        return new Color(
            a.Red + (b.Red - a.Red) * t,
            a.Green + (b.Green - a.Green) * t,
            a.Blue + (b.Blue - a.Blue) * t,
            a.Alpha + (b.Alpha - a.Alpha) * t);
    }

    static CAKeyFrameAnimation Keyframes(string keyPath, NSObject[] values, NSNumber[] keyTimes)
    {
        var animation = CAKeyFrameAnimation.FromKeyPath(keyPath);
        animation.Values = values;
        animation.KeyTimes = keyTimes;
        animation.CalculationMode = CAAnimation.AnimationLinear;
        return animation;
    }

    static NSObject[] Numbers(MotionTrack track, int samples, Func<double, double> convert)
    {
        var values = new NSObject[samples + 1];
        for (var i = 0; i <= samples; i++)
            values[i] = NSNumber.FromDouble(convert(MotionSampler.ValueAt(track.Keys, (double)i / samples)));
        return values;
    }

    static double Value(MotionTrack? track, double offset, double fallback)
        => track is null ? fallback : MotionSampler.ValueAt(track.Keys, offset);

    static MotionTrack? Track(MotionSpec spec, string? partId, MotionChannel channel)
    {
        foreach (var track in spec.Tracks)
        {
            if (track.Channel == channel && String.Equals(track.PartId, partId, StringComparison.Ordinal))
                return track;
        }
        return null;
    }

    void OnClockStopped(Clock stopped)
    {
        // A rebuild replaces the clock, and the old one reports in as it goes; only the current
        // one running out is the end of the play.
        if (this.disposed || !ReferenceEquals(stopped, this.clock))
            return;

        this.Finished?.Invoke(this, EventArgs.Empty);
    }

    sealed class Clock(CoreAnimationMotionPlayer owner) : CAAnimationDelegate
    {
        // Weak: Core Animation retains an animation's delegate, and the player must not outlive the view.
        readonly WeakReference<CoreAnimationMotionPlayer> owner = new(owner);

        public override void AnimationStopped(CAAnimation anim, bool finished)
        {
            if (!finished || !this.owner.TryGetTarget(out var player))
                return;

            // Not from inside Core Animation's own stop callback.
            DispatchQueue.MainQueue.DispatchAsync(() => player.OnClockStopped(this));
        }
    }

#if MACOS
    sealed class SizeWatcher : AppKit.NSView
    {
        readonly WeakReference<CoreAnimationMotionPlayer> owner;

        public SizeWatcher(CoreAnimationMotionPlayer owner, CGRect frame) : base(frame)
        {
            this.owner = new(owner);
            this.AutoresizingMask = AppKit.NSViewResizingMask.WidthSizable | AppKit.NSViewResizingMask.HeightSizable;
        }

        public override AppKit.NSView? HitTest(CGPoint aPoint) => null;

        public override void Layout()
        {
            base.Layout();

            if (this.owner.TryGetTarget(out var player))
                player.Relayout();
        }
    }
#else
    sealed class SizeWatcher : UIKit.UIView
    {
        readonly WeakReference<CoreAnimationMotionPlayer> owner;

        public SizeWatcher(CoreAnimationMotionPlayer owner, CGRect frame) : base(frame)
        {
            this.owner = new(owner);
            this.UserInteractionEnabled = false;
            this.AutoresizingMask = UIKit.UIViewAutoresizing.FlexibleDimensions;
        }

        public override void LayoutSubviews()
        {
            base.LayoutSubviews();

            if (this.owner.TryGetTarget(out var player))
                player.Relayout();
        }
    }
#endif
}
#endif
