using System.Collections.Specialized;
using System.ComponentModel;

namespace Shiny.Maui.Controls.Infrastructure;

/// <summary>
/// Subscribes a control to a collection or model object it does not own without letting that
/// object keep the control alive.
/// </summary>
/// <remarks>
/// <para>
/// An <c>ItemsSource</c> is almost always a view model's <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/>,
/// and a view model routinely outlives the page it is shown on — a singleton, a shared store, a tab
/// that is navigated away from. A plain <c>+=</c> puts the control in the collection's invocation
/// list, which roots the control, its page and everything bound under it for as long as the view
/// model lives. MAUI never disposes views, so "unsubscribe in Dispose" never runs.
/// </para>
/// <para>
/// The publisher holds only a small proxy; the proxy holds the handler weakly; the subscription
/// (owned by the control) holds the handler strongly. When the control is collected the handler goes
/// with it, and the proxy removes itself on the next event. Disposing the subscription detaches
/// immediately, which is what a control does when the property is reassigned.
/// </para>
/// </remarks>
static class WeakEventSubscription
{
    public static IDisposable? CollectionChanged(object? source, NotifyCollectionChangedEventHandler handler)
        => source is INotifyCollectionChanged ncc ? new CollectionSubscription(ncc, handler) : null;

    public static IDisposable? PropertyChanged(object? source, PropertyChangedEventHandler handler)
        => source is INotifyPropertyChanged npc ? new PropertySubscription(npc, handler) : null;


    sealed class CollectionSubscription : IDisposable
    {
        // Strong: this object is owned by the subscriber, so the handler lives exactly as long as it.
        // Never read - holding it is the whole point.
#pragma warning disable IDE0052
        readonly NotifyCollectionChangedEventHandler handler;
#pragma warning restore IDE0052
        Proxy? proxy;

        public CollectionSubscription(INotifyCollectionChanged source, NotifyCollectionChangedEventHandler handler)
        {
            this.handler = handler;
            this.proxy = new Proxy(source, handler);
        }

        public void Dispose()
        {
            this.proxy?.Detach();
            this.proxy = null;
        }

        sealed class Proxy
        {
            readonly WeakReference<NotifyCollectionChangedEventHandler> target;
            INotifyCollectionChanged? source;

            public Proxy(INotifyCollectionChanged source, NotifyCollectionChangedEventHandler handler)
            {
                this.target = new WeakReference<NotifyCollectionChangedEventHandler>(handler);
                this.source = source;
                source.CollectionChanged += this.OnChanged;
            }

            void OnChanged(object? sender, NotifyCollectionChangedEventArgs e)
            {
                if (this.target.TryGetTarget(out var handler))
                    handler(sender, e);
                else
                    this.Detach();
            }

            public void Detach()
            {
                if (this.source is null)
                    return;

                this.source.CollectionChanged -= this.OnChanged;
                this.source = null;
            }
        }
    }


    sealed class PropertySubscription : IDisposable
    {
#pragma warning disable IDE0052
        readonly PropertyChangedEventHandler handler;
#pragma warning restore IDE0052
        Proxy? proxy;

        public PropertySubscription(INotifyPropertyChanged source, PropertyChangedEventHandler handler)
        {
            this.handler = handler;
            this.proxy = new Proxy(source, handler);
        }

        public void Dispose()
        {
            this.proxy?.Detach();
            this.proxy = null;
        }

        sealed class Proxy
        {
            readonly WeakReference<PropertyChangedEventHandler> target;
            INotifyPropertyChanged? source;

            public Proxy(INotifyPropertyChanged source, PropertyChangedEventHandler handler)
            {
                this.target = new WeakReference<PropertyChangedEventHandler>(handler);
                this.source = source;
                source.PropertyChanged += this.OnChanged;
            }

            void OnChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (this.target.TryGetTarget(out var handler))
                    handler(sender, e);
                else
                    this.Detach();
            }

            public void Detach()
            {
                if (this.source is null)
                    return;

                this.source.PropertyChanged -= this.OnChanged;
                this.source = null;
            }
        }
    }
}
