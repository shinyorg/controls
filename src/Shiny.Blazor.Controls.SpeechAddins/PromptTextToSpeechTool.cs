using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Shiny.Blazor.Controls.QuickEntry;

namespace Shiny.Blazor.Controls.SpeechAddins;

/// <summary>
/// A <see cref="PromptTool"/> that reads the prompt's answer aloud. Drop it into
/// <c>PromptView.TrailingTools</c>.
/// </summary>
/// <remarks>
/// <para>
/// It speaks <see cref="PromptView.Response"/> — the plain-text answer. An answer pushed through
/// <c>ResponseContent</c> alone is markup with no text to read, so set <see cref="TextSelector"/> to
/// pull the words out of whatever you rendered.
/// </para>
/// <para>
/// The engine is the browser's own <c>speechSynthesis</c>, reached through this package's JS module —
/// the same arrangement <c>TextEntrySpeechToTextButton</c> uses for recognition. Nothing to register:
/// the tool resolves <see cref="IJSRuntime"/> from the services the prompt hands it.
/// </para>
/// </remarks>
public class PromptTextToSpeechTool : PromptTool, IAsyncDisposable
{
    const string ModulePath = "./_content/Shiny.Blazor.Controls.SpeechAddins/shinyTextToSpeech.js";
    const string IdleGlyph = "\U0001F50A"; // speaker
    const string StopGlyph = "⏹";     // stop

    IJSObjectReference? module;
    DotNetObjectReference<PromptTextToSpeechTool>? selfRef;
    bool isSpeaking;

    public PromptTextToSpeechTool()
    {
        this.Icon = IdleGlyph;
        this.Title = "Read aloud";
    }

    /// <summary>Glyph shown while speaking. Clicking it stops.</summary>
    public string SpeakingIcon { get; set; } = StopGlyph;

    /// <summary>CSS colour while speaking.</summary>
    public string SpeakingColor { get; set; } = "#F44336";

    /// <summary>BCP 47 language tag (e.g. "en-US"). Null uses the browser default.</summary>
    public string? Culture { get; set; }

    /// <summary>The name of the voice to use, as the browser reports it. Null uses the default.</summary>
    public string? VoiceName { get; set; }

    public double SpeechRate { get; set; } = 1.0;

    public double Pitch { get; set; } = 1.0;

    public double Volume { get; set; } = 1.0;

    /// <summary>Speak the answer as soon as it arrives, rather than waiting for a click.</summary>
    public bool AutoSpeak { get; set; }

    /// <summary>Hide the tool while there is nothing to read. Default true.</summary>
    public bool HideWhenEmpty { get; set; } = true;

    /// <summary>
    /// Chooses what to read. Null (the default) reads <see cref="PromptView.Response"/> — set this
    /// when the answer only exists as <c>ResponseContent</c> markup.
    /// </summary>
    public Func<PromptView, string?>? TextSelector { get; set; }

    /// <summary>Whether the tool is currently speaking.</summary>
    public bool IsSpeaking => this.isSpeaking;

    public override bool IsVisible
    {
        get => base.IsVisible && (!this.HideWhenEmpty || !String.IsNullOrWhiteSpace(this.ResolveText()));
        set => base.IsVisible = value;
    }

    protected override void OnAttached()
    {
        if (this.Prompt is not null)
            this.Prompt.ResponseChanged += this.OnResponseChanged;
    }

    protected override void OnDetached()
    {
        if (this.Prompt is not null)
            this.Prompt.ResponseChanged -= this.OnResponseChanged;

        // Nothing else ever calls DisposeAsync on a tool - it is a plain object in a list - so the JS module and
        // the .NET reference registered with the circuit's JS runtime are released here. Both are recreated
        // lazily if the tool is docked again.
        _ = this.DisposeAsync().AsTask();
    }

    protected override Task OnClickAsync()
        => this.isSpeaking ? this.StopAsync() : this.SpeakAsync();

    /// <summary>Read the current answer aloud. Same path as clicking the tool.</summary>
    public async Task SpeakAsync()
    {
        var text = this.ResolveText();
        if (String.IsNullOrWhiteSpace(text))
            return;

        var js = this.Services?.GetService<IJSRuntime>();
        if (js is null)
            return;

        try
        {
            this.module ??= await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            if (!await this.module.InvokeAsync<bool>("isSupported"))
                return;

            this.selfRef ??= DotNetObjectReference.Create(this);

            await this.SetSpeakingAsync(true);
            await this.module.InvokeVoidAsync(
                "speak",
                this.selfRef,
                text,
                this.Culture,
                this.VoiceName,
                this.SpeechRate,
                this.Pitch,
                this.Volume
            );
        }
        catch (JSDisconnectedException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"PromptTextToSpeechTool: {ex.Message}");
            await this.SetSpeakingAsync(false);
        }
    }

    /// <summary>Stop reading, if it is reading.</summary>
    public async Task StopAsync()
    {
        if (this.module is null)
            return;

        try
        {
            await this.module.InvokeVoidAsync("stop");
        }
        catch (JSDisconnectedException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"PromptTextToSpeechTool: {ex.Message}");
        }

        // cancel() raises onerror rather than onend in some browsers, and neither is guaranteed once
        // the circuit is gone — so the glyph is reset here rather than waiting to be told.
        await this.SetSpeakingAsync(false);
    }

    [JSInvokable]
    public Task OnSpeechEnd() => this.SetSpeakingAsync(false);

    [JSInvokable]
    public Task OnSpeechError(string error)
    {
        Console.WriteLine($"PromptTextToSpeechTool: {error}");
        return this.SetSpeakingAsync(false);
    }

    async Task SetSpeakingAsync(bool speaking)
    {
        if (this.isSpeaking == speaking)
            return;

        this.isSpeaking = speaking;
        this.Icon = speaking ? this.SpeakingIcon : IdleGlyph;
        this.ToolColor = speaking ? this.SpeakingColor : null;
        await this.RefreshAsync();
    }

    void OnResponseChanged(object? sender, EventArgs e)
    {
        // A new answer invalidates whatever was being read for the old one.
        _ = this.RestartAsync();
    }

    async Task RestartAsync()
    {
        await this.StopAsync();

        if (this.AutoSpeak && !String.IsNullOrWhiteSpace(this.ResolveText()))
            await this.SpeakAsync();
    }

    string? ResolveText()
    {
        if (this.Prompt is null)
            return null;

        return this.TextSelector is { } selector
            ? selector(this.Prompt)
            : this.Prompt.Response;
    }

    public async ValueTask DisposeAsync()
    {
        var module = this.module;
        var selfRef = this.selfRef;
        this.module = null;
        this.selfRef = null;

        if (module is not null)
        {
            try
            {
                await module.InvokeVoidAsync("stop");
                await module.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
            catch { }
        }

        selfRef?.Dispose();

        if (this.isSpeaking)
        {
            this.isSpeaking = false;
            this.Icon = IdleGlyph;
            this.ToolColor = null;
        }
    }
}
