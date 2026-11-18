using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Shiny.Maui.Controls.Keyframe;

/// <summary>A per-frame callback with the platform's frame timestamp.</summary>
interface IFrameSource
{
    void Start();
    void Stop();
}

static class FrameSources
{
    public static IFrameSource Create(IDispatcher dispatcher, TimeSpan fallbackInterval, Action<TimeSpan> onFrame)
    {
#if IOS || MACCATALYST
        return new DisplayLinkFrameSource(onFrame);
#elif ANDROID
        return new ChoreographerFrameSource(onFrame);
#elif WINDOWS
        return new RenderingFrameSource(onFrame);
#else
        return new TimerFrameSource(dispatcher, fallbackInterval, onFrame);
#endif
    }
}

/// <summary>The fallback for heads without a vsync signal we can reach: a dispatcher timer timed by a stopwatch.</summary>
sealed class TimerFrameSource : IFrameSource
{
    readonly IDispatcherTimer timer;
    readonly Stopwatch stopwatch = new();

    public TimerFrameSource(IDispatcher dispatcher, TimeSpan interval, Action<TimeSpan> onFrame)
    {
        timer = dispatcher.CreateTimer();
        timer.Interval = interval;
        timer.IsRepeating = true;
        timer.Tick += (_, _) => onFrame(stopwatch.Elapsed);
    }

    public void Start()
    {
        stopwatch.Start();
        timer.Start();
    }

    public void Stop()
    {
        timer.Stop();
        stopwatch.Stop();
    }
}

#if IOS || MACCATALYST
sealed class DisplayLinkFrameSource(Action<TimeSpan> onFrame) : IFrameSource
{
    CoreAnimation.CADisplayLink? link;

    public void Start()
    {
        if (link is not null)
            return;

        link = CoreAnimation.CADisplayLink.Create(() =>
        {
            if (link is { } current)
                onFrame(TimeSpan.FromSeconds(current.Timestamp));
        });

        // Ask for the display's full rate. ProMotion iPhones still cap third-party apps at 60 Hz unless
        // the app's Info.plist sets CADisableMinimumFrameDurationOnPhone; iPads and Macs don't need it.
        var maximum = (float)UIKit.UIScreen.MainScreen.MaximumFramesPerSecond;
        link.PreferredFrameRateRange = new CoreAnimation.CAFrameRateRange { Minimum = 30f, Maximum = maximum, Preferred = maximum };
        link.AddToRunLoop(Foundation.NSRunLoop.Main, Foundation.NSRunLoopMode.Common);
    }

    public void Stop()
    {
        link?.Invalidate();
        link = null;
    }
}
#endif

#if ANDROID
sealed class ChoreographerFrameSource : Java.Lang.Object, Android.Views.Choreographer.IFrameCallback, IFrameSource
{
    readonly Action<TimeSpan> onFrame;
    bool running;

    public ChoreographerFrameSource(Action<TimeSpan> onFrame) => this.onFrame = onFrame;

    public void Start()
    {
        if (running)
            return;

        running = true;
        Android.Views.Choreographer.Instance?.PostFrameCallback(this);
    }

    public void Stop()
    {
        running = false;
        Android.Views.Choreographer.Instance?.RemoveFrameCallback(this);
    }

    public void DoFrame(long frameTimeNanos)
    {
        if (!running)
            return;

        // Re-post first, so a handler that throws cannot stop the clock for every animation on the window.
        Android.Views.Choreographer.Instance?.PostFrameCallback(this);
        onFrame(TimeSpan.FromTicks(frameTimeNanos / 100));
    }
}
#endif

#if WINDOWS
sealed class RenderingFrameSource(Action<TimeSpan> onFrame) : IFrameSource
{
    bool running;

    public void Start()
    {
        if (running)
            return;

        running = true;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnRendering;
    }

    public void Stop()
    {
        if (!running)
            return;

        running = false;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnRendering;
    }

    void OnRendering(object? sender, object e)
    {
        if (e is Microsoft.UI.Xaml.Media.RenderingEventArgs args)
            onFrame(args.RenderingTime);
    }
}
#endif

/// <summary>Wires the engine's reduced-motion question to the platform setting.</summary>
static class PlatformMotion
{
#pragma warning disable CA2255 // A library module initializer is the point: the policy must be live before any animation plays.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Install() => MotionPolicy.IsReducedMotion = IsReducedMotion;

    public static bool IsReducedMotion()
    {
#if IOS || MACCATALYST
        return UIKit.UIAccessibility.IsReduceMotionEnabled;
#elif ANDROID
        // Android has no reduce-motion switch; "Remove animations" zeroes the animator duration scale.
        var resolver = Android.App.Application.Context.ContentResolver;
        return Android.Provider.Settings.Global.GetFloat(resolver, Android.Provider.Settings.Global.AnimatorDurationScale, 1f) == 0f;
#elif WINDOWS
        return !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
#else
        return false;
#endif
    }
}
