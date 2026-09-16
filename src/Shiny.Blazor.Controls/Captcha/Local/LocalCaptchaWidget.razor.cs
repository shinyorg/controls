using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls.Captchas;

/// <summary>
/// The self-hosted challenge: characters drawn to a canvas (or a small sum), typed back, checked in
/// the browser. Rendered by <see cref="LocalCaptchaProvider"/> — not meant to be placed by hand.
/// </summary>
public partial class LocalCaptchaWidget
{
    [Inject] IJSRuntime JS { get; set; } = null!;

    [Parameter, EditorRequired] public LocalCaptchaOptions Options { get; set; } = new();
    [Parameter, EditorRequired] public CaptchaRenderContext Context { get; set; } = null!;

    IJSObjectReference? module;
    bool disposed;
    ElementReference canvasEl;
    CancellationTokenSource? expiryCts;

    string challenge = "";     // what gets drawn (text mode)
    string question = "";      // what gets read out (math mode)
    string answer = "";        // what we compare against
    string typed = "";
    bool solved;
    bool shake;
    int attempts;
    bool needsRedraw;
    CaptchaTheme? lastTheme;

    // the challenge chrome follows the host's Theme, not just the canvas — a dark widget with a
    // light input around it looks broken
    string ThemeClass => this.Context.Theme switch
    {
        CaptchaTheme.Dark => "is-dark",
        CaptchaTheme.Light => "is-light",
        _ => "is-auto"
    };

    string PromptText => this.Options.Prompt ?? (this.Options.Mode == LocalCaptchaMode.Math
        ? "Answer the question"
        : "Type the characters you see");

    protected override void OnInitialized()
    {
        this.Generate();
        this.Context.OnWidgetReady(this);
    }

    protected override void OnParametersSet()
    {
        // the canvas colours come from the widget's CSS variables, so a theme switch has to redraw
        // it — the chrome around it restyles on its own
        if (this.lastTheme != this.Context.Theme)
        {
            this.lastTheme = this.Context.Theme;
            this.needsRedraw = true;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var loaded = await this.JS.InvokeAsync<IJSObjectReference>(
                "import",
                "./_content/Shiny.Blazor.Controls/captcha.js"
            );
            if (this.disposed) { await loaded.ReleaseLateAsync(); return; }
            this.module = loaded;
            this.needsRedraw = true;
        }

        if (this.needsRedraw && this.module != null && this.Options.Mode == LocalCaptchaMode.Text)
        {
            this.needsRedraw = false;
            await this.DrawAsync();
        }
    }

    async Task DrawAsync()
    {
        if (this.module == null)
            return;

        try
        {
            // a named DTO, not an anonymous type: trimmed/AOT publish strips anonymous-type
            // constructor parameter names, which the JS interop serializer requires
            await this.module.InvokeVoidAsync("drawChallenge", this.canvasEl, this.challenge, new DrawOptions
            {
                Width = this.Options.Width,
                Height = this.Options.Height,
                Dark = this.Context.Theme == CaptchaTheme.Dark,
                FollowSystem = this.Context.Theme == CaptchaTheme.Auto
            });
        }
        catch (JSException)
        {
            // canvas not laid out yet — the next render pass redraws
            this.needsRedraw = true;
        }
    }

    void Generate()
    {
        this.typed = "";
        this.shake = false;
        this.attempts = 0;

        if (this.Options.Mode == LocalCaptchaMode.Math)
        {
            var a = Random.Shared.Next(1, 10);
            var b = Random.Shared.Next(1, 10);
            var add = Random.Shared.Next(2) == 0;

            // subtraction only when it stays positive — negative answers are a typing puzzle, not a
            // human check
            if (!add && b > a)
                (a, b) = (b, a);

            this.question = add ? $"{a} + {b}" : $"{a} − {b}";
            this.answer = (add ? a + b : a - b).ToString();
            this.challenge = this.question;
        }
        else
        {
            var set = string.IsNullOrEmpty(this.Options.CharacterSet)
                ? "ABCDEFGHJKMNPQRSTUVWXYZ23456789"
                : this.Options.CharacterSet;

            var len = Math.Clamp(this.Options.Length, 3, 12);
            var chars = new char[len];
            for (var i = 0; i < len; i++)
                chars[i] = set[Random.Shared.Next(set.Length)];

            this.challenge = new string(chars);
            this.answer = this.challenge;
            this.question = this.challenge;
        }

        this.needsRedraw = true;
    }

    async Task OnRefreshAsync()
    {
        if (this.solved)
            return;

        this.Generate();
        this.StateHasChanged();
        await this.DrawAsync();
    }

    Task OnKeyDownAsync(KeyboardEventArgs e)
        => e.Key == "Enter" ? this.CheckAsync(force: true) : Task.CompletedTask;

    async Task OnTypedAsync(ChangeEventArgs e)
    {
        this.typed = e.Value?.ToString() ?? "";
        this.shake = false;

        // check as soon as they have typed enough characters to be answering, so the common case
        // needs no button press at all
        if (this.typed.Length >= this.answer.Length)
            await this.CheckAsync(force: false);
    }

    async Task CheckAsync(bool force)
    {
        if (this.solved || (!force && this.typed.Length < this.answer.Length))
            return;

        if (string.IsNullOrWhiteSpace(this.typed))
            return;

        var comparison = this.Options.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        if (string.Equals(this.typed.Trim(), this.answer, comparison))
        {
            this.solved = true;
            this.shake = false;
            this.StartExpiry();

            // an opaque, single-use token so the shape matches the hosted providers. It proves the
            // browser solved the challenge and nothing else — there is no server to check it against.
            await this.Context.OnSolved($"local.{Guid.NewGuid():N}");
            return;
        }

        this.attempts++;
        this.shake = true;

        if (this.Options.MaxAttempts > 0 && this.attempts >= this.Options.MaxAttempts)
        {
            this.Generate();
            this.shake = true;
            this.StateHasChanged();
            await this.DrawAsync();
        }
    }

    void StartExpiry()
    {
        this.expiryCts?.Cancel();
        this.expiryCts?.Dispose();
        this.expiryCts = null;

        if (this.Options.ExpirySeconds <= 0)
            return;

        var cts = new CancellationTokenSource();
        this.expiryCts = cts;
        var seconds = this.Options.ExpirySeconds;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), cts.Token);
                await this.InvokeAsync(async () =>
                {
                    this.solved = false;
                    this.Generate();
                    this.StateHasChanged();
                    await this.DrawAsync();
                    await this.Context.OnExpired();
                });
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    /// <inheritdoc />
    public async ValueTask ResetAsync()
    {
        this.expiryCts?.Cancel();
        this.solved = false;
        this.Generate();
        this.StateHasChanged();
        await this.DrawAsync();
    }

    /// <summary>No-op — the local challenge is always visible, so there is nothing to trigger.</summary>
    public ValueTask ExecuteAsync() => ValueTask.CompletedTask;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        this.disposed = true;
        this.Context.OnWidgetReady(null);
        this.expiryCts?.Cancel();
        this.expiryCts?.Dispose();

        if (this.module != null)
        {
            try
            {
                await this.module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }

    sealed class DrawOptions
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Dark { get; set; }
        public bool FollowSystem { get; set; }
    }
}
