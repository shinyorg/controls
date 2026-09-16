using System.Globalization;
using Shiny.Maui.Controls;
using Shiny.Speech;

namespace Shiny.Maui.Controls.SpeechAddins;

/// <summary>
/// A TextEntry tool that listens for speech and backfills the entry text.
/// Shows a listening indicator while recording. Optionally auto-submits on completion.
/// </summary>
public class TextEntrySpeechToTextTool : TextEntryTool, ITextEntryAwareTool
{
    TextEntry? textEntry;
    CancellationTokenSource? cts;
    bool isListening;
    string? originalText;

    public TextEntrySpeechToTextTool()
    {
        Text = "\uD83C\uDF99"; // microphone emoji
        ToolColor = Color.FromArgb("#4CAF50");
        Clicked += OnClicked;
    }

    // ------- Configuration Properties -------

    public static readonly BindableProperty SilenceTimeoutProperty = BindableProperty.Create(
        nameof(SilenceTimeout), typeof(TimeSpan), typeof(TextEntrySpeechToTextTool), TimeSpan.FromSeconds(2));

    public TimeSpan SilenceTimeout
    {
        get => (TimeSpan)GetValue(SilenceTimeoutProperty);
        set => SetValue(SilenceTimeoutProperty, value);
    }

    public static readonly BindableProperty CultureProperty = BindableProperty.Create(
        nameof(Culture), typeof(string), typeof(TextEntrySpeechToTextTool));

    public string? Culture
    {
        get => (string?)GetValue(CultureProperty);
        set => SetValue(CultureProperty, value);
    }

    public static readonly BindableProperty PreferOnDeviceProperty = BindableProperty.Create(
        nameof(PreferOnDevice), typeof(bool), typeof(TextEntrySpeechToTextTool), false);

    public bool PreferOnDevice
    {
        get => (bool)GetValue(PreferOnDeviceProperty);
        set => SetValue(PreferOnDeviceProperty, value);
    }

    public static readonly BindableProperty ListeningTextProperty = BindableProperty.Create(
        nameof(ListeningText), typeof(string), typeof(TextEntrySpeechToTextTool), "\u23F9"); // stop icon

    public string ListeningText
    {
        get => (string)GetValue(ListeningTextProperty);
        set => SetValue(ListeningTextProperty, value);
    }

    public static readonly BindableProperty ListeningColorProperty = BindableProperty.Create(
        nameof(ListeningColor), typeof(Color), typeof(TextEntrySpeechToTextTool),
        Color.FromArgb("#F44336"));

    public Color ListeningColor
    {
        get => (Color)GetValue(ListeningColorProperty);
        set => SetValue(ListeningColorProperty, value);
    }

    // ------- ITextEntryAwareTool -------

    void ITextEntryAwareTool.Attach(TextEntry entry) => textEntry = entry;
    void ITextEntryAwareTool.Detach()
    {
        // A recognition session left running when the entry goes away kept the microphone open (and
        // this tool and its entry reachable) until the recognizer next heard silence.
        StopListening();
        textEntry = null;
    }

    // ------- Core Logic -------

    async void OnClicked(object? sender, EventArgs e)
    {
        if (isListening)
        {
            StopListening();
            return;
        }

        if (textEntry is null)
            return;

        var stt = ResolveService<ISpeechToTextService>();
        if (stt is null || !stt.IsSupported)
            return;

        var access = await stt.RequestAccess();
        if (access != AccessState.Available)
            return;

        isListening = true;
        cts = new CancellationTokenSource();
        SetListeningAppearance(true);

        try
        {
            var options = new SpeechRecognitionOptions
            {
                SilenceTimeout = SilenceTimeout,
                PreferOnDevice = PreferOnDevice,
                Culture = Culture is not null ? CultureInfo.GetCultureInfo(Culture) : null
            };

            var result = await stt.ListenUntilSilence(options, cts.Token);

            if (!string.IsNullOrEmpty(result) && textEntry is not null)
            {
                var existing = textEntry.Text?.Trim();
                textEntry.Text = string.IsNullOrEmpty(existing)
                    ? result
                    : $"{existing} {result}";
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TextEntrySpeechToTextTool: {ex.Message}");
        }
        finally
        {
            isListening = false;
            SetListeningAppearance(false);
        }
    }

    void StopListening()
    {
        cts?.Cancel();
        cts = null;
    }

    void SetListeningAppearance(bool listening)
    {
        if (listening)
        {
            originalText = Text;
            Text = ListeningText;
            ToolColor = ListeningColor;
        }
        else
        {
            Text = originalText;
            ToolColor = Color.FromArgb("#4CAF50");
        }
    }

    static T? ResolveService<T>() where T : class
        => Application.Current?.Handler?.MauiContext?.Services.GetService<T>();
}
