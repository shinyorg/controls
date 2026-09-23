namespace Shiny.Controls.Office.Packaging;

/// <summary>
/// Base for an open OOXML document.
/// </summary>
/// <remarks>
/// <para>
/// The package is opened once and held. Edits are applied surgically to the live XML DOM and the whole
/// package is written back on save. Nothing is ever reconstructed from a parsed model, so parts the
/// editor does not understand — tracked changes, custom XML, macros, pivot caches, embedded objects —
/// survive untouched because they are never read in the first place.
/// </para>
/// <para>
/// The file on disk is copied into memory on open and never held open. That keeps the original intact
/// if the process dies mid-edit, and lets <see cref="SaveAsync"/> write atomically.
/// </para>
/// </remarks>
public abstract class OfficeDocument : IDisposable
{
    MemoryStream? buffer;
    bool disposed;

    protected OfficeDocument(MemoryStream buffer, string? path, IUnsupportedFeatureSink unsupported)
    {
        this.buffer = buffer;
        this.Path = path;
        this.Unsupported = unsupported;
    }

    /// <summary>Where the document was loaded from, or null when it came from a stream.</summary>
    public string? Path { get; private set; }

    public IUnsupportedFeatureSink Unsupported { get; }

    public bool IsDirty { get; private set; }

    public event EventHandler? DirtyChanged;

    /// <summary>The in-memory package bytes. Only valid while the document is open.</summary>
    protected MemoryStream Buffer => this.buffer ?? throw new ObjectDisposedException(this.GetType().Name);

    /// <summary>Reads a file fully into a seekable, writable buffer.</summary>
    protected static async Task<MemoryStream> ReadIntoBufferAsync(string path, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        await using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
            await file.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        buffer.Position = 0;
        return buffer;
    }

    protected static async Task<MemoryStream> ReadIntoBufferAsync(Stream source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;
        return buffer;
    }

    public void MarkDirty()
    {
        if (this.IsDirty)
            return;

        this.IsDirty = true;
        this.DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Flushes pending DOM changes into the package buffer. Implementations must be idempotent.</summary>
    protected abstract void FlushToPackage();

    /// <summary>
    /// A copy of the package to write in place of the live buffer, or null to write the buffer itself.
    /// </summary>
    /// <remarks>
    /// For state that has to stay in the live package — so it can still be undone — but must not reach
    /// the saved file. A deck keeps a deleted slide's part so undoing the delete can bring it back, and
    /// strips it from the copy instead. Called after <see cref="FlushToPackage"/>; the caller disposes it.
    /// </remarks>
    protected virtual MemoryStream? CreateSaveCopy() => null;

    /// <summary>Flushes, then returns the stream to write and whether the caller owns it.</summary>
    MemoryStream PrepareSave(out bool owned)
    {
        this.FlushToPackage();

        var copy = this.CreateSaveCopy();
        owned = copy is not null;

        var stream = copy ?? this.Buffer;
        stream.Position = 0;
        return stream;
    }

    /// <summary>Saves back over <see cref="Path"/>.</summary>
    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (this.Path is null)
            throw new InvalidOperationException("This document has no path; use SaveAsAsync or SaveToAsync.");

        return this.SaveAsAsync(this.Path, cancellationToken);
    }

    /// <summary>
    /// Saves to <paramref name="path"/> and retargets the document at it. The write goes to a sibling
    /// temporary file and is moved into place, so an interrupted save never leaves a half-written document.
    /// </summary>
    public async Task SaveAsAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var source = this.PrepareSave(out var owned);

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        var temp = System.IO.Path.Combine(directory ?? ".", $".{System.IO.Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await source.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
                await file.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch
                {
                    // The save already failed; a leftover temp file is not worth masking that with.
                }
            }

            throw;
        }
        finally
        {
            if (owned)
                source.Dispose();
        }

        this.Path = path;
        this.IsDirty = false;
        this.DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Writes the current package to a stream without changing the document's path or dirty state.</summary>
    public async Task SaveToAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var source = this.PrepareSave(out var owned);

        try
        {
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (owned)
                source.Dispose();
        }
    }

    /// <summary>Returns the current package bytes, flushing pending changes first.</summary>
    public byte[] ToArray()
    {
        var source = this.PrepareSave(out var owned);

        try
        {
            return source.ToArray();
        }
        finally
        {
            if (owned)
                source.Dispose();
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (this.disposed)
            return;

        this.disposed = true;
        if (disposing)
        {
            this.buffer?.Dispose();
            this.buffer = null;
        }
    }

    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }
}
