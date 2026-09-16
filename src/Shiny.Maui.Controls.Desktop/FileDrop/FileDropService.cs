using Microsoft.Extensions.Logging;
using Microsoft.Maui.Graphics;

namespace Shiny.Maui.Controls.Desktop.FileDrop;

/// <summary>
/// The one implementation of <see cref="IFileDropService"/>. All of the policy lives here; the
/// <c>FileDropPlatform</c> types under <c>Platforms/</c> only report raw drags.
/// </summary>
sealed class FileDropService : IFileDropService, IFileDropHost, IDisposable
{
    readonly ILogger<FileDropService>? logger;
    readonly IFileDropDelegate? handler;
    readonly Dictionary<Window, Attachment> attachments = new();
    readonly object gate = new();

    public FileDropService(FileDropOptions options, IFileDropDelegate? handler = null, ILogger<FileDropService>? logger = null)
    {
        this.Options = options;
        this.handler = handler;
        this.logger = logger;
    }

    public FileDropOptions Options { get; }

    public bool IsSupported => FileDropPlatform.IsSupported;

    public bool IsEnabled { get; set; } = true;

    public event EventHandler<FileDragEventArgs>? DragEnter;
    public event EventHandler<FileDragEventArgs>? DragOver;
    public event EventHandler<FileDragEventArgs>? DragLeave;
    public event EventHandler<FileDropEventArgs>? Dropped;

    public IDisposable AttachTo(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        lock (this.gate)
        {
            if (this.attachments.TryGetValue(window, out var existing))
                return existing;
        }

        if (!FileDropPlatform.IsSupported)
        {
            this.LogDebug("File drop is not supported on this platform — AttachTo is a no-op.");
            return NullAttachment.Instance;
        }

        // The native window is created with the handler, and on some heads that happens after
        // OpenWindow returns. Attaching to a window that has none yet would silently do nothing,
        // so the attachment waits for HandlerChanged rather than giving up.
        var attachment = new Attachment(this, window);
        lock (this.gate)
            this.attachments[window] = attachment;

        attachment.Start();
        return attachment;
    }

    internal void Detach(Window window)
    {
        lock (this.gate)
            this.attachments.Remove(window);
    }

    /// <summary>Attaches any window that is open and not already attached.</summary>
    internal void AttachOpenWindows()
    {
        var app = Application.Current;
        if (app == null)
            return;

        foreach (var window in app.Windows)
        {
            bool attached;
            lock (this.gate)
                attached = this.attachments.ContainsKey(window);

            if (!attached)
                this.AttachTo(window);
        }
    }

    public bool WouldAccept(IReadOnlyList<DroppedFile> files)
    {
        if (!this.IsEnabled)
            return false;

        foreach (var file in files)
        {
            if (this.Options.Accepts(file))
                return true;
        }

        // A platform that cannot name the payload until the drop lands reports an empty list while
        // the drag is still moving. Refusing that would show the "no drop" cursor for every drag on
        // Mac Catalyst, so an unknown payload is optimistically accepted and filtered on drop.
        return files.Count == 0;
    }

    public void NotifyEnter(Window window, IReadOnlyList<DroppedFile> files, Point position)
        => this.Raise(this.DragEnter, window, files, position);

    public void NotifyOver(Window window, IReadOnlyList<DroppedFile> files, Point position)
        => this.Raise(this.DragOver, window, files, position);

    public void NotifyLeave(Window window)
        => this.Raise(this.DragLeave, window, [], Point.Zero);

    public void NotifyDrop(Window window, IReadOnlyList<DroppedFile> files, Point position)
    {
        if (!this.IsEnabled)
            return;

        var (accepted, rejected) = this.Filter(files);
        if (accepted.Count == 0)
        {
            this.LogDebug($"A drop of {files.Count} file(s) was refused — none matched the configured filters.");

            // A refused drop still ends the drag, and no platform sends a leave after a drop. Without
            // this an overlay bound to the drag state would stay up for good. Raised directly rather
            // than through Raise so the rejected count survives — that is the whole story here.
            var refused = new FileDragEventArgs([], rejected, position, window);
            this.Dispatch(() => this.DragLeave?.Invoke(this, refused));
            return;
        }

        var args = new FileDropEventArgs(accepted, rejected, position, window);

        // The delegate is async and native code is not going to wait for it. Faulting inside it must
        // not take the drop handler down with it, hence the explicit continuation rather than a bare
        // discard on an async void.
        this.DispatchAsync(async () =>
        {
            try
            {
                if (this.handler != null)
                {
                    var context = new FileDropContext(args);
                    await this.handler.OnFilesDropped(context).ConfigureAwait(true);
                    if (context.Handled)
                        return;
                }

                this.Dropped?.Invoke(this, args);
            }
            catch (Exception ex)
            {
                this.LogError("A file drop handler threw", ex);
            }
        });
    }

    void Raise(EventHandler<FileDragEventArgs>? handler, Window window, IReadOnlyList<DroppedFile> files, Point position)
    {
        if (handler == null || !this.IsEnabled)
            return;

        var (accepted, rejected) = this.Filter(files);
        var args = new FileDragEventArgs(accepted, rejected, position, window);

        this.Dispatch(() =>
        {
            try
            {
                handler.Invoke(this, args);
            }
            catch (Exception ex)
            {
                this.LogError("A file drag handler threw", ex);
            }
        });
    }

    (IReadOnlyList<DroppedFile> Accepted, int Rejected) Filter(IReadOnlyList<DroppedFile> files)
    {
        if (files.Count == 0)
            return ([], 0);

        var accepted = new List<DroppedFile>(files.Count);
        var rejected = 0;

        foreach (var file in files)
        {
            if (!this.Options.Accepts(file))
                rejected++;
            else if (this.Options.MaxFiles > 0 && accepted.Count >= this.Options.MaxFiles)
                rejected++;
            else
                accepted.Add(file);
        }

        return (accepted, rejected);
    }

    void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || !dispatcher.IsDispatchRequired)
            action();
        else
            dispatcher.Dispatch(action);
    }

    /// <summary>
    /// Deliberately named apart from <see cref="Dispatch(Action)"/>. As an overload, the natural
    /// call <c>Dispatch(() =&gt; _ = work())</c> binds to this method rather than to the
    /// <see cref="Action"/> one — the lambda body is an expression of type <see cref="Task"/>, so
    /// the <see cref="Func{Task}"/> overload is the better match and the method calls itself until
    /// the stack runs out. A distinct name makes that unrepresentable.
    /// </summary>
    void DispatchAsync(Func<Task> work)
        => this.Dispatch(() => { _ = work(); });

    public void LogError(string message, Exception? ex = null) => this.logger?.LogError(ex, "{Message}", message);

    public void LogDebug(string message) => this.logger?.LogDebug("{Message}", message);

    public void Dispose()
    {
        Attachment[] all;
        lock (this.gate)
        {
            all = this.attachments.Values.ToArray();
            this.attachments.Clear();
        }

        foreach (var attachment in all)
            attachment.Dispose();
    }


    sealed class Attachment : IDisposable
    {
        readonly FileDropService owner;
        readonly Window window;
        IDisposable? native;
        bool disposed;

        public Attachment(FileDropService owner, Window window)
        {
            this.owner = owner;
            this.window = window;
        }

        public void Start()
        {
            // The service is a singleton and keys attachments by Window, so a window closed without
            // anyone disposing its attachment stayed in the map for the life of the app - the Window,
            // its page tree and the native drop target with it. Every auto-attached window leaked that
            // way (a second window, a floating dock panel). Closing the window is what ends it now.
            this.window.Destroying += this.OnDestroying;

            if (this.window.Handler?.PlatformView != null)
            {
                this.AttachNative();
                return;
            }

            this.window.HandlerChanged += this.OnHandlerChanged;
        }

        void OnDestroying(object? sender, EventArgs e) => this.Dispose();

        void OnHandlerChanged(object? sender, EventArgs e)
        {
            if (this.window.Handler?.PlatformView == null)
                return;

            this.window.HandlerChanged -= this.OnHandlerChanged;
            this.AttachNative();
        }

        void AttachNative()
        {
            if (this.disposed || this.native != null)
                return;

            var platformWindow = this.window.Handler?.PlatformView;
            if (platformWindow == null)
                return;

            try
            {
                this.native = FileDropPlatform.Attach(this.owner, this.window, platformWindow);
                this.owner.LogDebug($"File drop attached to '{this.window.Title}'.");
            }
            catch (Exception ex)
            {
                this.owner.LogError("Attaching the native file drop target failed", ex);
            }
        }

        public void Dispose()
        {
            if (this.disposed)
                return;

            this.disposed = true;
            this.window.Destroying -= this.OnDestroying;
            this.window.HandlerChanged -= this.OnHandlerChanged;
            this.native?.Dispose();
            this.native = null;
            this.owner.Detach(this.window);
        }
    }


    sealed class NullAttachment : IDisposable
    {
        public static readonly NullAttachment Instance = new();
        public void Dispose() { }
    }
}
