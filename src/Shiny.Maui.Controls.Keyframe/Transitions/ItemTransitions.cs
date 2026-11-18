using System.Collections.Specialized;
using System.Runtime.CompilerServices;

namespace Shiny.Maui.Controls.Keyframe;

/// <summary>What a collection view does with the platform's own item insert, remove and move animations.</summary>
public enum ItemTransitionMode
{
    /// <summary>Leave them as the platform draws them. The default.</summary>
    Platform,

    /// <summary>Switch them off: items appear, disappear and move without animating. Combine with
    /// <see cref="ItemTransitions.AddProperty"/> to replace the platform's insert with your own.</summary>
    None
}

/// <summary>
/// Item animations for a <c>CollectionView</c> (any <see cref="ItemsView"/>): switch the platform's
/// insert/remove/move animations off, play a <see cref="Keyframes"/> as items first appear — staggered,
/// for the list entrance — or as they are added, and animate an item out before removing it.
/// </summary>
/// <example>
/// <code language="xml">
/// &lt;CollectionView ItemsSource="{Binding Items}"
///                 kf:ItemTransitions.Mode="None"
///                 kf:ItemTransitions.Appear="{StaticResource FadeUp}"
///                 kf:ItemTransitions.Add="{StaticResource PopIn}"
///                 kf:ItemTransitions.Stagger="0:0:0.04" /&gt;
/// </code>
/// <code language="csharp">
/// await ItemTransitions.RemoveAsync(list, item, () => Items.Remove(item));
/// </code>
/// </example>
/// <remarks>
/// <para><b>Appear</b> plays once per item, the first time a view shows it, so scrolling back to an item
/// does not replay it and a recycled view showing a new item does. Items realised in quick succession are
/// staggered. Appear and Add timelines are given a backwards fill automatically, so a staggered item holds
/// its opening pose instead of flashing in its final one.</para>
/// <para><b>Remove</b> can't be intercepted once an item has left the source — the platform tears the
/// view down immediately — so it is a call you make, <see cref="RemoveAsync"/>, that plays the
/// animation on the realised view and then runs your removal. <b>Move</b> is the platform's, or nothing
/// under <see cref="ItemTransitionMode.None"/>.</para>
/// <para><b>Mode None</b> uses <c>SetItemAnimator(null)</c> on Android, collapses the collection view's
/// layer animations on iOS and Mac Catalyst, and clears the item container transitions on Windows. On the
/// AppKit and GTK heads the platform has no item animations to remove.</para>
/// </remarks>
public static class ItemTransitions
{
    /// <summary>Whether the platform's own item animations run.</summary>
    public static readonly BindableProperty ModeProperty =
        BindableProperty.CreateAttached("Mode", typeof(ItemTransitionMode), typeof(ItemTransitions), ItemTransitionMode.Platform,
            propertyChanged: (b, _, _) => Ensure(b));

    /// <summary>Played on each item the first time a view shows it.</summary>
    public static readonly BindableProperty AppearProperty =
        BindableProperty.CreateAttached("Appear", typeof(IAnimationDefinition), typeof(ItemTransitions), null,
            propertyChanged: (b, _, _) => Ensure(b));

    /// <summary>Played on an item added to the source after the view loaded. Falls back to <see cref="AppearProperty"/>.</summary>
    public static readonly BindableProperty AddProperty =
        BindableProperty.CreateAttached("Add", typeof(IAnimationDefinition), typeof(ItemTransitions), null,
            propertyChanged: (b, _, _) => Ensure(b));

    /// <summary>Played by <see cref="RemoveAsync"/> before the item is removed.</summary>
    public static readonly BindableProperty RemoveProperty =
        BindableProperty.CreateAttached("Remove", typeof(IAnimationDefinition), typeof(ItemTransitions), null);

    /// <summary>Delay between items that appear together. 40 ms by default; at most 12 steps are taken.</summary>
    public static readonly BindableProperty StaggerProperty =
        BindableProperty.CreateAttached("Stagger", typeof(TimeSpan), typeof(ItemTransitions), TimeSpan.FromMilliseconds(40));

    const int MaximumStaggerSteps = 12;
    static readonly TimeSpan StaggerWindow = TimeSpan.FromMilliseconds(80);
    static readonly ConditionalWeakTable<ItemsView, Controller> Controllers = [];

    public static ItemTransitionMode GetMode(BindableObject view) => (ItemTransitionMode)view.GetValue(ModeProperty);
    public static void SetMode(BindableObject view, ItemTransitionMode value) => view.SetValue(ModeProperty, value);
    public static IAnimationDefinition? GetAppear(BindableObject view) => (IAnimationDefinition?)view.GetValue(AppearProperty);
    public static void SetAppear(BindableObject view, IAnimationDefinition? value) => view.SetValue(AppearProperty, value);
    public static IAnimationDefinition? GetAdd(BindableObject view) => (IAnimationDefinition?)view.GetValue(AddProperty);
    public static void SetAdd(BindableObject view, IAnimationDefinition? value) => view.SetValue(AddProperty, value);
    public static IAnimationDefinition? GetRemove(BindableObject view) => (IAnimationDefinition?)view.GetValue(RemoveProperty);
    public static void SetRemove(BindableObject view, IAnimationDefinition? value) => view.SetValue(RemoveProperty, value);
    public static TimeSpan GetStagger(BindableObject view) => (TimeSpan)view.GetValue(StaggerProperty);
    public static void SetStagger(BindableObject view, TimeSpan value) => view.SetValue(StaggerProperty, value);

    /// <summary>
    /// Plays the <see cref="RemoveProperty">Remove</see> animation on the item's view, if it is realised
    /// and one is set, then runs <paramref name="remove"/>. Without either, removes straight away.
    /// </summary>
    public static async Task RemoveAsync(ItemsView itemsView, object item, Action remove, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemsView);
        ArgumentNullException.ThrowIfNull(remove);

        var animation = GetRemove(itemsView);
        var view = animation is null ? null : FindRealized(itemsView, item);

        if (animation is not null && view is not null)
        {
            // Hold the last frame: the view is about to be torn down, and releasing would flash it
            // back to its resting pose for a frame first.
            var player = AnimationRuns.Begin(view, animation);
            await WaitAsync(player, cancellationToken);
        }

        remove();
    }

    static Task WaitAsync(Player player, CancellationToken cancellationToken)
    {
        if (player.State is PlaybackState.Finished or PlaybackState.Idle)
            return Task.CompletedTask;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        player.Finished += (_, _) => tcs.TrySetResult();
        cancellationToken.Register(() => tcs.TrySetResult());
        return tcs.Task;
    }

    static VisualElement? FindRealized(ItemsView itemsView, object item)
        => ((IVisualTreeElement)itemsView)
            .GetVisualChildren()
            .OfType<VisualElement>()
            .FirstOrDefault(v => Equals(v.BindingContext, item));

    static void Ensure(BindableObject bindable)
    {
        if (bindable is ItemsView view)
            Controllers.GetValue(view, static v => new Controller(v)).Refresh();
    }

    sealed class Controller
    {
        readonly WeakReference<ItemsView> owner;
        readonly ConditionalWeakTable<object, object> seen = [];
        readonly ConditionalWeakTable<object, object> pendingAdds = [];
        readonly HashSet<VisualElement> hooked = new(ReferenceEqualityComparer.Instance);
        INotifyCollectionChanged? source;
        DateTime lastAppear;
        int staggerStep;

        public Controller(ItemsView view)
        {
            owner = new WeakReference<ItemsView>(view);
            view.ChildAdded += OnChildAdded;
            view.ChildRemoved += OnChildRemoved;
            view.HandlerChanged += (_, _) => ApplyMode();
            view.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == ItemsView.ItemsSourceProperty.PropertyName)
                    HookSource();
            };

            HookSource();
        }

        public void Refresh() => ApplyMode();

        void HookSource()
        {
            if (source is not null)
                source.CollectionChanged -= OnSourceChanged;

            source = owner.TryGetTarget(out var view) ? view.ItemsSource as INotifyCollectionChanged : null;

            if (source is not null)
                source.CollectionChanged += OnSourceChanged;
        }

        void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null)
                return;

            foreach (var item in e.NewItems.Cast<object>().Where(i => i is not null && !i.GetType().IsValueType))
                pendingAdds.AddOrUpdate(item, item);
        }

        void OnChildAdded(object? sender, ElementEventArgs e)
        {
            if (e.Element is not VisualElement child || !hooked.Add(child))
                return;

            child.BindingContextChanged += OnItemBindingContextChanged;
            Realized(child);
        }

        void OnChildRemoved(object? sender, ElementEventArgs e)
        {
            if (e.Element is VisualElement child && hooked.Remove(child))
                child.BindingContextChanged -= OnItemBindingContextChanged;
        }

        void OnItemBindingContextChanged(object? sender, EventArgs e)
        {
            if (sender is VisualElement child)
                Realized(child);
        }

        void Realized(VisualElement child)
        {
            if (!owner.TryGetTarget(out var view) || child.BindingContext is not { } item)
                return;

            var isValue = item.GetType().IsValueType;
            if (!isValue && seen.TryGetValue(item, out _))
                return;

            if (!isValue)
                seen.AddOrUpdate(item, item);

            var added = !isValue && pendingAdds.TryGetValue(item, out _);
            if (added)
                pendingAdds.Remove(item);

            var animation = added ? GetAdd(view) ?? GetAppear(view) : GetAppear(view);
            if (animation is null)
                return;

            var now = DateTime.UtcNow;
            staggerStep = now - lastAppear < StaggerWindow ? Math.Min(staggerStep + 1, MaximumStaggerSteps) : 0;
            lastAppear = now;

            Play(child, animation, GetStagger(view) * staggerStep);
        }

        static void Play(VisualElement child, IAnimationDefinition animation, TimeSpan delay)
        {
            var node = animation.Build(child, name => NameScope.Find(child, name));
            NodeFill.Add(node, FillMode.Backwards);

            var storyboard = new Storyboard().Add(node, delay);
            var player = new Player(storyboard, MauiClock.For(child))
            {
                Rate = animation.Speed,
                ReducedMotion = animation.ReducedMotion
            };
            player.Finished += (_, _) => player.Dispose();
            player.Play();
        }

        void ApplyMode()
        {
            if (!owner.TryGetTarget(out var view) || view.Handler?.PlatformView is not { } platformView)
                return;

            var off = GetMode(view) == ItemTransitionMode.None;
            NativeItemAnimations.Set(platformView, enabled: !off);
        }
    }
}

/// <summary>Turns a collection view's native item animations on or off.</summary>
static class NativeItemAnimations
{
    public static void Set(object platformView, bool enabled)
    {
#if ANDROID
        if (platformView is AndroidX.RecyclerView.Widget.RecyclerView recycler)
            recycler.SetItemAnimator(enabled ? new AndroidX.RecyclerView.Widget.DefaultItemAnimator() : null);
#elif IOS || MACCATALYST
        if (platformView is UIKit.UIView root && Find(root) is { } collectionView)
        {
            // UIKit runs the insert/delete/move animations as layer animations inside the collection
            // view, so collapsing the layer's timescale finishes them instantly. Scrolling is not a
            // layer animation and is unaffected.
            collectionView.Layer.Speed = enabled ? 1f : 1000f;
        }
#elif WINDOWS
        if (platformView is Microsoft.UI.Xaml.Controls.ListViewBase list)
        {
            list.ItemContainerTransitions = enabled
                ? [new Microsoft.UI.Xaml.Media.Animation.AddDeleteThemeTransition(), new Microsoft.UI.Xaml.Media.Animation.ReorderThemeTransition()]
                : [];
        }
#endif
    }

#if IOS || MACCATALYST
    static UIKit.UICollectionView? Find(UIKit.UIView view)
    {
        if (view is UIKit.UICollectionView collectionView)
            return collectionView;

        return view.Subviews.Select(Find).FirstOrDefault(found => found is not null);
    }
#endif
}
