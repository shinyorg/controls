using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Shiny.Maui.Controls.DataGrid;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> that can swallow the notifications of a bulk rewrite and
/// raise a single <see cref="NotifyCollectionChangedAction.Reset"/> at the end. The grid rebuilds its
/// rows by clearing and re-adding every one; raised item by item, that is one native list reload per row
/// on handlers that answer every change with a full reload (the macOS AppKit CollectionView does).
/// </summary>
internal sealed class BatchingObservableCollection<T> : ObservableCollection<T>
{
    int depth;
    bool changed;

    /// <summary>Notifications are held until the returned scope (and any outer one) is disposed.</summary>
    public IDisposable Batch()
    {
        this.depth++;
        return new Scope(this);
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (this.depth > 0)
        {
            this.changed = true;
            return;
        }
        base.OnCollectionChanged(e);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (this.depth > 0)
            return;

        base.OnPropertyChanged(e);
    }

    void EndBatch()
    {
        if (--this.depth > 0 || !this.changed)
            return;

        this.changed = false;
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(this.Count)));
        base.OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        base.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    sealed class Scope(BatchingObservableCollection<T> owner) : IDisposable
    {
        bool disposed;

        public void Dispose()
        {
            if (this.disposed)
                return;

            this.disposed = true;
            owner.EndBatch();
        }
    }
}
