using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls.FileDrop;

/// <summary>The one implementation of <see cref="IFileDropService"/>.</summary>
public sealed class FileDropService : IFileDropService
{
    readonly IJSRuntime js;
    readonly IFileDropDelegate? handler;
    readonly ILogger<FileDropService>? logger;
    readonly string id = Guid.NewGuid().ToString("N");

    IJSObjectReference? module;
    DotNetObjectReference<FileDropService>? self;
    Task? starting;
    int generation;

    public FileDropService(
        IJSRuntime js,
        FileDropOptions options,
        IFileDropDelegate? handler = null,
        ILogger<FileDropService>? logger = null
    )
    {
        this.js = js;
        this.Options = options;
        this.handler = handler;
        this.logger = logger;
    }

    public FileDropOptions Options { get; }

    public bool IsSupported => true;

    public bool IsRunning => this.module != null;

    public bool IsEnabled { get; set; } = true;

    public event EventHandler<FileDragEventArgs>? DragEnter;
    public event EventHandler<FileDragEventArgs>? DragOver;
    public event EventHandler<FileDragEventArgs>? DragLeave;
    public event EventHandler<FileDropEventArgs>? Dropped;

    public Task StartAsync()
    {
        if (this.module != null)
            return Task.CompletedTask;

        // One start at a time: two overlapping calls each used to create a DotNetObjectReference, and
        // the one overwritten was tracked by the JS runtime and never disposed.
        return this.starting ??= this.StartCoreAsync();
    }


    async Task StartCoreAsync()
    {
        var generation = this.generation;
        try
        {
            var loaded = await this.js
                .InvokeAsync<IJSObjectReference>("import", "./_content/Shiny.Blazor.Controls/file-drop.js")
                .ConfigureAwait(false);

            // Stopped while the import was in flight (the host was disposed). StopAsync found nothing
            // to detach, so attaching now would leave four window listeners - and the service they
            // call back into - alive for the rest of the page.
            if (generation != this.generation)
            {
                await loaded.ReleaseLateAsync().ConfigureAwait(false);
                return;
            }

            var self = DotNetObjectReference.Create(this);
            this.self = self;
            this.module = loaded;
            await loaded.InvokeVoidAsync("attach", this.id, self).ConfigureAwait(false);
        }
        finally
        {
            this.starting = null;
        }
    }

    public async Task StopAsync()
    {
        this.generation++;
        var current = this.module;
        this.module = null;

        if (current != null)
        {
            try
            {
                await current.InvokeVoidAsync("detach", this.id).ConfigureAwait(false);
                await current.DisposeAsync().ConfigureAwait(false);
            }
            catch (JSDisconnectedException)
            {
                // The circuit went away before teardown. There is nothing left to detach from.
            }
        }

        this.self?.Dispose();
        this.self = null;
    }

    public async Task ReleaseAsync(IEnumerable<DroppedFile> files)
    {
        if (this.module == null)
            return;

        var keys = files
            .Where(x => x.IsMetadataKnown)
            .Select(x => x.Key)
            .ToArray();

        if (keys.Length == 0)
            return;

        try
        {
            await this.module.InvokeVoidAsync("release", this.id, keys).ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
        }
    }

    // -------------------------------------------------------------------------------------
    // Called from file-drop.js.

    [JSInvokable]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(FileDropItem))]
    public void OnDragEnter(FileDropPayload payload) => this.Raise(this.DragEnter, payload);

    [JSInvokable]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(FileDropItem))]
    public void OnDragOver(FileDropPayload payload) => this.Raise(this.DragOver, payload);

    [JSInvokable]
    public void OnDragLeave()
    {
        if (this.IsEnabled)
            this.DragLeave?.Invoke(this, new FileDragEventArgs([], 0, 0, 0));
    }

    [JSInvokable]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(FileDropItem))]
    public async Task OnDrop(FileDropPayload payload)
    {
        if (!this.IsEnabled)
            return;

        var (accepted, rejected) = this.Convert(payload);
        if (accepted.Count == 0)
        {
            this.logger?.LogDebug("A drop of {Count} file(s) was refused — none matched the configured filters.", payload.Files.Length);

            // A refused drop still ends the drag, and the browser sends no dragleave after a drop.
            // Without this an overlay bound to the drag state would stay up for good.
            this.DragLeave?.Invoke(this, new FileDragEventArgs([], rejected, payload.X, payload.Y));

            await this.ReleaseAsync(this.Convert(payload, filtered: false).Accepted).ConfigureAwait(false);
            return;
        }

        var args = new FileDropEventArgs(accepted, rejected, payload.X, payload.Y);

        try
        {
            if (this.handler != null)
            {
                var context = new FileDropContext(args);
                await this.handler.OnFilesDropped(context).ConfigureAwait(false);

                if (!context.Handled)
                    this.Dropped?.Invoke(this, args);
            }
            else
            {
                this.Dropped?.Invoke(this, args);
            }
        }
        finally
        {
            // Always, even when a handler threw: the alternative is a page that leaks every file
            // anyone ever dropped on it into JS memory.
            if (this.Options.ReleaseFilesAfterHandling)
                await this.ReleaseAsync(accepted).ConfigureAwait(false);
        }
    }

    void Raise(EventHandler<FileDragEventArgs>? handler, FileDropPayload payload)
    {
        if (handler == null || !this.IsEnabled)
            return;

        var (accepted, rejected) = this.Convert(payload);
        handler.Invoke(this, new FileDragEventArgs(accepted, rejected, payload.X, payload.Y));
    }

    (IReadOnlyList<DroppedFile> Accepted, int Rejected) Convert(FileDropPayload payload, bool filtered = true)
    {
        var accepted = new List<DroppedFile>(payload.Files.Length);
        var rejected = 0;

        foreach (var item in payload.Files)
        {
            var file = new DroppedFile(
                item.Key,
                item.Name,
                item.Size,
                item.ContentType,
                DateTimeOffset.FromUnixTimeMilliseconds(item.LastModified),
                (max, ct) => this.OpenAsync(item.Key, max, ct)
            );

            if (!filtered)
                accepted.Add(file);
            else if (!this.Options.Accepts(file))
                rejected++;
            else if (this.Options.MaxFiles > 0 && accepted.Count >= this.Options.MaxFiles)
                rejected++;
            else
                accepted.Add(file);
        }

        return (accepted, rejected);
    }

    async Task<Stream> OpenAsync(string key, long maxAllowedSize, CancellationToken cancellationToken)
    {
        if (this.module == null)
            throw new InvalidOperationException("The file drop service is not running.");

        var reference = await this.module
            .InvokeAsync<IJSStreamReference>("read", cancellationToken, this.id, key)
            .ConfigureAwait(false);

        return await reference
            .OpenReadStreamAsync(maxAllowedSize, cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => new(this.StopAsync());
}


/// <summary>What <c>file-drop.js</c> sends for a drag or a drop.</summary>
/// <remarks>
/// A named type rather than an anonymous one, and its element type is kept alive explicitly by the
/// <c>DynamicDependency</c> on each <c>JSInvokable</c> above: the annotation that flows through
/// <see cref="FileDropPayload"/> stops at the array and never reaches <see cref="FileDropItem"/>,
/// which trims the properties away in a published WebAssembly build and nowhere else.
/// </remarks>
public sealed class FileDropPayload
{
    public FileDropItem[] Files { get; set; } = [];
    public double X { get; set; }
    public double Y { get; set; }
}


/// <summary>One file in a <see cref="FileDropPayload"/>.</summary>
public sealed class FileDropItem
{
    /// <summary>Identifies the browser-side File. Empty for the placeholders sent during a drag.</summary>
    public string Key { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary><c>-1</c> during a drag, when the browser will not reveal it.</summary>
    public long Size { get; set; } = -1;

    public string ContentType { get; set; } = "";

    /// <summary>Unix milliseconds, as the browser reports it.</summary>
    public long LastModified { get; set; }
}
