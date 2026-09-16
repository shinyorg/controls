using Microsoft.Extensions.Logging;
using Shiny.Maui.Controls.QuickEntry;

namespace Shiny.Maui.Controls.Desktop.QuickEntry;

/// <summary>
/// Draws the screen-edge glow in a transparent, click-through OS window so it rims the whole display
/// rather than the app's own page.
/// </summary>
/// <remarks>
/// macOS and Linux/X11 only. Windows registers <c>WindowsScreenGlow</c> instead — a WinUI 3 window
/// has no per-pixel alpha, so the same frames are rendered with GDI+ into layered Win32 windows.
/// </remarks>
sealed class DesktopScreenGlowPresenter : IScreenGlowPresenter, IDisposable
{
    readonly ILogger<DesktopScreenGlowPresenter>? logger;
    readonly SemaphoreSlim gate = new(1, 1);

    Window? window;
    ScreenGlowView? view;
    object? platformWindow;
    bool visible;

    public DesktopScreenGlowPresenter(ILogger<DesktopScreenGlowPresenter>? logger = null)
        => this.logger = logger;

    public QuickEntryPresentation Kind => QuickEntryPresentation.Desktop;

    public bool IsSupported => ScreenGlowPlatform.IsSupported;

    public async Task ShowAsync(ScreenGlowOptions options)
    {
        if (!ScreenGlowPlatform.IsSupported)
            return;

        await this.EnsureWindowAsync(options).ConfigureAwait(true);
        if (this.platformWindow == null || this.view == null)
            return;

        this.visible = true;
        this.view.Opacity = 0;
        this.view.Start();
        ScreenGlowPlatform.Show(this.platformWindow);
        await this.view.FadeToAsync(1, (uint)options.FadeDuration.TotalMilliseconds).ConfigureAwait(true);
    }

    public async Task HideAsync(ScreenGlowOptions options)
    {
        if (this.platformWindow == null || this.view == null)
            return;

        this.visible = false;
        await this.view.FadeToAsync(0, (uint)options.FadeDuration.TotalMilliseconds).ConfigureAwait(true);

        // A Show that landed during the fade must not be undone by this Hide.
        if (this.visible)
            return;

        this.view.Stop();
        ScreenGlowPlatform.Hide(this.platformWindow);
    }

    async Task EnsureWindowAsync(ScreenGlowOptions options)
    {
        if (this.platformWindow != null)
            return;

        await this.gate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (this.platformWindow != null)
                return;

            var app = Application.Current
                ?? throw new InvalidOperationException("The screen glow needs a running MAUI application.");

            this.view = new ScreenGlowView(options) { Opacity = 0, InputTransparent = true };
            var page = new ContentPage
            {
                BackgroundColor = Colors.Transparent,
                Padding = 0,
                Content = this.view
            };

            var size = ScreenGlowPlatform.GetScreenSize();
            var w = new Window(page)
            {
                Title = "Screen Glow",
                Width = size.Width,
                Height = size.Height,
                X = 0,
                Y = 0
            };
            this.window = w;

            var ready = new TaskCompletionSource();
            void OnHandlerChanged(object? sender, EventArgs e)
            {
                if (w.Handler?.PlatformView is { } native)
                {
                    w.HandlerChanged -= OnHandlerChanged;
                    this.platformWindow = native;
                    ready.TrySetResult();
                }
            }
            w.HandlerChanged += OnHandlerChanged;

            app.OpenWindow(w);
            OnHandlerChanged(w, EventArgs.Empty);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await ready.Task.WaitAsync(timeout.Token).ConfigureAwait(true);

            ScreenGlowPlatform.Initialize(this.platformWindow!);
            ScreenGlowPlatform.Hide(this.platformWindow!);
        }
        catch (OperationCanceledException)
        {
            this.logger?.LogError("Timed out waiting for the screen glow window handler.");

            // Close the window that never produced a handler. Without this it stayed in
            // Application.Windows, and since platformWindow is still null the next show opened
            // another one - a fresh screen-sized window leaked per attempt. The quick entry
            // presenter already tears down the same way.
            this.Teardown();
        }
        finally
        {
            this.gate.Release();
        }
    }

    public void Teardown()
    {
        this.view?.Stop();

        var native = this.platformWindow;
        this.platformWindow = null;
        if (native != null)
            ScreenGlowPlatform.Teardown(native);

        if (this.window != null)
        {
            try { Application.Current?.CloseWindow(this.window); }
            catch (Exception ex) { this.logger?.LogDebug(ex, "Closing the screen glow window failed"); }
            this.window = null;
        }
    }

    public void Dispose()
    {
        this.Teardown();
        this.gate.Dispose();
    }
}
