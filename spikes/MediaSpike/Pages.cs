namespace MediaSpike;

public class MainPage : ContentPage
{
    public MainPage()
    {
        this.Title = "Media spike";
        var effects = new Button { Text = "Video effects" };
        effects.Clicked += (_, _) => this.Navigation.PushAsync(new EffectsPage(null));
        var transcribe = new Button { Text = "Audio tap + transcription" };
        transcribe.Clicked += (_, _) => this.Navigation.PushAsync(new TranscribePage(null));
        this.Content = new VerticalStackLayout { Padding = 20, Spacing = 12, Children = { effects, transcribe } };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var mode = Spike.Mode;
        if (mode == null)
            return;

        Spike.Mode = null;
        Spike.Log($"autorun {mode}");
        await Task.Delay(500);
        if (mode.StartsWith("effects:") || mode.StartsWith("pip:"))
            await this.Navigation.PushAsync(new EffectsPage(mode));
        else
            await this.Navigation.PushAsync(new TranscribePage(mode));
    }
}


public class LogView : ScrollView
{
    readonly Label label = new() { FontSize = 11, FontFamily = "Courier" };

    public LogView()
    {
        this.Content = this.label;
        Spike.Logged += msg =>
        {
            this.label.Text += msg + "\n";
            _ = this.ScrollToAsync(this.label, ScrollToPosition.End, false);
        };
    }
}


/// <summary>Spike 1: effects applied to playing video, switched live, and whether they survive PiP.</summary>
public class EffectsPage : ContentPage
{
    readonly SpikePlayerView player = new() { HeightRequest = 240 };
    readonly string? autorun;

    public EffectsPage(string? autorun)
    {
        this.autorun = autorun;
        this.Title = "Effects";
        var picker = new Picker { Title = "Effect" };
        var play = new Button { Text = "Play" };
        var pip = new Button { Text = "PiP" };
        var source = new Picker { ItemsSource = new[] { "speech.mp4", "https://media.w3.org/2010/05/sintel/trailer.mp4" }, SelectedIndex = 0 };

        this.player.PlayerReady += (_, _) =>
        {
            picker.ItemsSource = this.player.Player!.Effects;
            this.player.Player.Open((string)source.SelectedItem);
            _ = this.RunAutorun();
        };
        picker.SelectedIndexChanged += (_, _) => this.player.Player?.SetEffect((string)picker.SelectedItem);
        source.SelectedIndexChanged += (_, _) => this.player.Player?.Open((string)source.SelectedItem);
        play.Clicked += (_, _) => this.player.Player?.Play();
        pip.Clicked += async (_, _) => Spike.Log($"pip started: {await this.player.Player!.StartPipAsync()}");

        this.Content = new Grid
        {
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star) },
            Children =
            {
                this.player,
                new HorizontalStackLayout { Spacing = 8, Padding = 8, Children = { picker, source, play, pip } }.Row(1),
                new LogView().Row(2)
            }
        };
    }

    async Task RunAutorun()
    {
        if (this.autorun == null)
            return;

        // effects:<name>  — set effect, play
        // pip:<name>      — set effect, play, enter PiP
        // effects:cycle   — play, then switch every effect live, 2s apart
        var parts = this.autorun.Split(':', 2);
        var p = this.player.Player!;
        p.Play();
        if (parts[1].StartsWith("cycle"))
        {
#if IOS
            IosPlayerView.Reassign = parts[1] == "cycle-reassign";
            var ios = (IosPlayerView)p;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var timer = Application.Current!.Dispatcher.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(500);
            timer.Tick += (_, _) => Spike.Log($"  wall={watch.Elapsed.TotalSeconds:F1} {ios.PlayheadStatus}");
            timer.Start();
#elif ANDROID
            var droid = (DroidPlayerView)p;
            if (parts[1] == "cycle-preset" && !DroidPlayerView.Preset)
            {
                DroidPlayerView.Preset = true;
                p.Open("speech.mp4");
                p.Play();
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var timer = Application.Current!.Dispatcher.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(500);
            timer.Tick += (_, _) => Spike.Log($"  wall={watch.Elapsed.TotalSeconds:F1} {droid.PlayheadStatus}");
            timer.Start();
#endif
            foreach (var e in p.Effects)
            {
                await Task.Delay(2000);
                Spike.Log($"live switch -> {e}");
                p.SetEffect(e);
            }
            return;
        }
        p.SetEffect(parts[1]);
        if (parts[0] == "pip")
        {
            await Task.Delay(2500);
            Spike.Log($"pip started: {await p.StartPipAsync()}");
        }
    }
}


/// <summary>Spike 2/3: audio tap off the player, fed live into a transcriber; and ahead-of-time file transcription.</summary>
public class TranscribePage : ContentPage
{
    readonly SpikePlayerView player = new() { HeightRequest = 200 };
    readonly ProgressBar level = new();
    readonly Label transcript = new() { FontSize = 15 };
    readonly string? autorun;
    long tapBuffers;
    double lastLevelUpdate;

    public TranscribePage(string? autorun)
    {
        this.autorun = autorun;
        this.Title = "Transcribe";
        var file = new Button { Text = "Transcribe file" };
        var live = new Button { Text = "Play + live" };
        file.Clicked += (_, _) => this.TranscribeFile();
        live.Clicked += (_, _) => this.PlayLive();

        this.player.PlayerReady += (_, _) =>
        {
            this.player.Player!.Open("speech.mp4");
            if (this.autorun == "file") this.TranscribeFile();
            else if (this.autorun == "live") this.PlayLive();
            else if (this.autorun == "tap") this.PlayTapOnly();
        };

        this.Content = new Grid
        {
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star) },
            Children =
            {
                this.player,
                new HorizontalStackLayout { Spacing = 8, Padding = 8, Children = { file, live } }.Row(1),
                this.level.Row(2),
                this.transcript.Row(3),
                new LogView().Row(4)
            }
        };
    }

    void OnTap(float[] samples, int frames, int channels, double rate, double pts)
    {
        var n = Interlocked.Increment(ref this.tapBuffers);
        if (n == 1)
            Spike.Log($"tap: first buffer frames={frames} ch={channels} rate={rate} pts={pts:F3}");

        if (pts - this.lastLevelUpdate > 0.1)
        {
            this.lastLevelUpdate = pts;
            double sum = 0;
            for (var i = 0; i < frames; i++) sum += samples[i] * samples[i];
            var rms = Math.Sqrt(sum / Math.Max(1, frames));
            MainThread.BeginInvokeOnMainThread(() => this.level.Progress = Math.Min(1, rms * 4));
        }
    }

    void PlayTapOnly()
    {
        var p = this.player.Player!;
        p.AudioTap = this.OnTap;
        p.Open("speech.mp4");
        p.Play();
    }

    void TranscribeFile()
    {
#if IOS
        var path = Foundation.NSBundle.MainBundle.PathForResource("speech", "mp4")!;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Spike.Log($"file: available={SpeechBridge.IsAvailable()} path={path}");
        var session = new SpeechBridge.Session();
        session.Segment += s =>
        {
            if (!s.IsFinal) return;
            Spike.Log($"file seg [{s.Start:F2}-{s.End:F2}] {s.Text}");
            Spike.Log($"   words {s.WordsJson}");
            MainThread.BeginInvokeOnMainThread(() => this.transcript.Text += s.Text + " ");
        };
        session.Done += err => Spike.Log($"file done in {sw.Elapsed.TotalSeconds:F2}s (media 10.86s) error={err ?? "none"}");
        session.TranscribeFile(path, "en-US");
#else
        Spike.Log("file transcription: Android path not part of this spike");
#endif
    }

    void PlayLive()
    {
#if IOS
        Spike.Log($"live: available={SpeechBridge.IsAvailable()}");
        var session = new SpeechBridge.Session();
        var pushed = 0L;
        var dropped = 0L;
        session.Segment += s =>
        {
            if (s.IsFinal)
            {
                Spike.Log($"live seg [{s.Start:F2}-{s.End:F2}] {s.Text}");
                MainThread.BeginInvokeOnMainThread(() => this.transcript.Text += s.Text + " ");
            }
        };
        session.Done += err => Spike.Log($"live done pushed={pushed} dropped(warming)={dropped} error={err ?? "none"}");
        session.StartLive("en-US");

        var p = this.player.Player!;
        p.AudioTap = (samples, frames, ch, rate, pts) =>
        {
            this.OnTap(samples, frames, ch, rate, pts);
            if (session.Push(samples, frames, ch, rate)) pushed++; else dropped++;
        };
        p.Open("speech.mp4");
        // give the analyzer a moment to load assets before audio starts
        Task.Delay(1500).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
        {
            Spike.Log("live: play");
            p.Play();
        }));
        Task.Delay(14000).ContinueWith(_ =>
        {
            Spike.Log("live: finish");
            session.FinishLive();
        });
#else
        this.PlayTapOnly();
#endif
    }
}


static class LayoutExt
{
    public static T Row<T>(this T view, int row) where T : BindableObject
    {
        Grid.SetRow(view, row);
        return view;
    }
}
