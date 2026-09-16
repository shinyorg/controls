using System.Collections;
using System.Collections.Specialized;
using System.Windows.Input;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.Collections;

public abstract class CollectionControlBase : View
{
    IDisposable? collectionSubscription;

    protected CollectionControlBase()
        // This level owns no children of its own - OnItemsSourceChanged only touches a field and
        // the handler - so it is ready the moment its own constructor ends. Scoping it here is
        // what lets the concrete grids inherit the guard without writing anything themselves.
        => StyleGuard.MarkReady(this, typeof(CollectionControlBase));

    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(CollectionControlBase),
        null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CollectionControlBase), () =>
            {
                ((CollectionControlBase)b).OnItemsSourceChanged((IEnumerable?)o, (IEnumerable?)n);
            }));

    public static readonly BindableProperty ItemTemplateProperty = BindableProperty.Create(
        nameof(ItemTemplate),
        typeof(DataTemplate),
        typeof(CollectionControlBase));

    public static readonly BindableProperty ItemTemplateSelectorProperty = BindableProperty.Create(
        nameof(ItemTemplateSelector),
        typeof(DataTemplateSelector),
        typeof(CollectionControlBase));

    public static readonly BindableProperty HeaderTemplateProperty = BindableProperty.Create(
        nameof(HeaderTemplate),
        typeof(DataTemplate),
        typeof(CollectionControlBase));

    public static readonly BindableProperty FooterTemplateProperty = BindableProperty.Create(
        nameof(FooterTemplate),
        typeof(DataTemplate),
        typeof(CollectionControlBase));

    public static readonly BindableProperty EmptyViewTemplateProperty = BindableProperty.Create(
        nameof(EmptyViewTemplate),
        typeof(DataTemplate),
        typeof(CollectionControlBase));

    public static readonly BindableProperty ItemSelectedCommandProperty = BindableProperty.Create(
        nameof(ItemSelectedCommand),
        typeof(ICommand),
        typeof(CollectionControlBase));

    public static readonly BindableProperty LoadMoreCommandProperty = BindableProperty.Create(
        nameof(LoadMoreCommand),
        typeof(ICommand),
        typeof(CollectionControlBase));

    public static readonly BindableProperty LoadMoreThresholdProperty = BindableProperty.Create(
        nameof(LoadMoreThreshold),
        typeof(int),
        typeof(CollectionControlBase),
        5);

    public static readonly BindableProperty ItemSpacingProperty = BindableProperty.Create(
        nameof(ItemSpacing),
        typeof(double),
        typeof(CollectionControlBase),
        0.0);

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public DataTemplateSelector? ItemTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(ItemTemplateSelectorProperty);
        set => SetValue(ItemTemplateSelectorProperty, value);
    }

    public DataTemplate? HeaderTemplate
    {
        get => (DataTemplate?)GetValue(HeaderTemplateProperty);
        set => SetValue(HeaderTemplateProperty, value);
    }

    public DataTemplate? FooterTemplate
    {
        get => (DataTemplate?)GetValue(FooterTemplateProperty);
        set => SetValue(FooterTemplateProperty, value);
    }

    public DataTemplate? EmptyViewTemplate
    {
        get => (DataTemplate?)GetValue(EmptyViewTemplateProperty);
        set => SetValue(EmptyViewTemplateProperty, value);
    }

    public ICommand? ItemSelectedCommand
    {
        get => (ICommand?)GetValue(ItemSelectedCommandProperty);
        set => SetValue(ItemSelectedCommandProperty, value);
    }

    public ICommand? LoadMoreCommand
    {
        get => (ICommand?)GetValue(LoadMoreCommandProperty);
        set => SetValue(LoadMoreCommandProperty, value);
    }

    public int LoadMoreThreshold
    {
        get => (int)GetValue(LoadMoreThresholdProperty);
        set => SetValue(LoadMoreThresholdProperty, value);
    }

    public double ItemSpacing
    {
        get => (double)GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    public event EventHandler<CollectionItemEventArgs>? ItemSelected;
    public event EventHandler? LoadMoreRequested;
    public event EventHandler<CollectionItemEventArgs>? ItemAppearing;
    public event EventHandler<CollectionItemEventArgs>? ItemDisappearing;

    internal DataTemplate ResolveTemplate(object item)
    {
        if (ItemTemplateSelector is { } selector)
            return selector.SelectTemplate(item, this);

        if (ItemTemplate is { } template)
            return template;

        return new DataTemplate(() =>
        {
            var label = new Label();
            label.SetBinding(Label.TextProperty, ".");
            return label;
        });
    }

    internal View CreateItemView(object item)
    {
        var template = ResolveTemplate(item);
        var content = template.CreateContent();
        var view = content as View ?? (content as ViewCell)?.View
            ?? throw new InvalidOperationException("DataTemplate must produce a View or ViewCell.");
        view.BindingContext = item;
        return view;
    }

    internal void RecycleItemView(View view, object newItem)
    {
        view.BindingContext = newItem;
    }

    internal void RaiseItemSelected(object item, int index)
    {
        var args = new CollectionItemEventArgs(item, index);
        ItemSelected?.Invoke(this, args);
        if (ItemSelectedCommand?.CanExecute(item) == true)
            ItemSelectedCommand.Execute(item);
    }

    internal void RaiseLoadMoreRequested()
    {
        LoadMoreRequested?.Invoke(this, EventArgs.Empty);
        if (LoadMoreCommand?.CanExecute(null) == true)
            LoadMoreCommand.Execute(null);
    }

    internal void RaiseItemAppearing(object item, int index)
    {
        ItemAppearing?.Invoke(this, new CollectionItemEventArgs(item, index));
    }

    internal void RaiseItemDisappearing(object item, int index)
    {
        ItemDisappearing?.Invoke(this, new CollectionItemEventArgs(item, index));
    }

    internal IList<object> GetItemsList()
    {
        if (ItemsSource is null)
            return [];

        if (ItemsSource is IList<object> list)
            return list;

        var result = new List<object>();
        foreach (var item in ItemsSource)
            result.Add(item);
        return result;
    }

    void OnItemsSourceChanged(IEnumerable? oldValue, IEnumerable? newValue)
    {
        // Weak: ItemsSource is usually a view model's collection, which outlives the page.
        collectionSubscription?.Dispose();
        collectionSubscription = WeakEventSubscription.CollectionChanged(newValue, OnCollectionChanged);

        OnItemsSourceUpdated(CollectionChangedArgs.Reset);
    }

    void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(() => OnCollectionChanged(sender, e));
            return;
        }

        var args = e.Action switch
        {
            NotifyCollectionChangedAction.Add => new CollectionChangedArgs(
                CollectionChangeType.Add, e.NewStartingIndex, e.NewItems?.Count ?? 0),
            NotifyCollectionChangedAction.Remove => new CollectionChangedArgs(
                CollectionChangeType.Remove, e.OldStartingIndex, e.OldItems?.Count ?? 0),
            NotifyCollectionChangedAction.Replace => new CollectionChangedArgs(
                CollectionChangeType.Replace, e.NewStartingIndex, e.NewItems?.Count ?? 0),
            NotifyCollectionChangedAction.Move => new CollectionChangedArgs(
                CollectionChangeType.Move, e.OldStartingIndex, 1, e.NewStartingIndex),
            _ => CollectionChangedArgs.Reset
        };
        OnItemsSourceUpdated(args);
    }

    protected virtual void OnItemsSourceUpdated(CollectionChangedArgs args)
    {
        Handler?.UpdateValue(nameof(ItemsSource));
    }
}

public enum CollectionChangeType
{
    Reset,
    Add,
    Remove,
    Replace,
    Move
}

public class CollectionChangedArgs
{
    public static readonly CollectionChangedArgs Reset = new(CollectionChangeType.Reset, 0, 0);

    public CollectionChangedArgs(CollectionChangeType type, int startIndex, int count, int newIndex = -1)
    {
        Type = type;
        StartIndex = startIndex;
        Count = count;
        NewIndex = newIndex;
    }

    public CollectionChangeType Type { get; }
    public int StartIndex { get; }
    public int Count { get; }
    public int NewIndex { get; }
}
