namespace MediaSpike;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp() => MauiApp
        .CreateBuilder()
        .UseMauiApp<App>()
        .ConfigureMauiHandlers(h => h.AddHandler<SpikePlayerView, SpikePlayerHandler>())
        .Build();
}


public class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
        => new(new NavigationPage(new MainPage()));
}


public static class Spike
{
    /// <summary>Autorun mode — SPIKE_MODE env var (iOS, via SIMCTL_CHILD_) or the "mode" intent extra (Android).</summary>
    public static string? Mode { get; set; } = Environment.GetEnvironmentVariable("SPIKE_MODE");

    public static event Action<string>? Logged;

    public static void Log(string msg)
    {
        Console.WriteLine($"[SPIKE] {msg}");
        MainThread.BeginInvokeOnMainThread(() => Logged?.Invoke(msg));
    }
}


/// <summary>What the platform player exposes to the pages.</summary>
public interface ISpikePlayer
{
    void Open(string source);
    void Play();
    void Pause();
    void SetEffect(string effect);
    string[] Effects { get; }
    Task<bool> StartPipAsync();

    /// <summary>Planar float PCM from the audio tap: (samples[ch * frames + i], frames, channels, sampleRate, ptsSeconds).</summary>
    Action<float[], int, int, double, double>? AudioTap { get; set; }
}


public class SpikePlayerView : View
{
    public ISpikePlayer? Player { get; internal set; }
    public event EventHandler? PlayerReady;
    internal void RaiseReady() => this.PlayerReady?.Invoke(this, EventArgs.Empty);
}


public partial class SpikePlayerHandler
{
    public SpikePlayerHandler() : base(ViewMapper) { }
}
