using System.Collections;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

/// <summary>
/// A stack of <see cref="Expander"/>s that agree on how many of them may be open.
/// </summary>
/// <remarks>
/// Expanders can be written out one by one, generated from <see cref="ItemsSource"/>, or both — the
/// generated ones are appended after whatever was declared in markup. The motion and chrome
/// properties here are <em>defaults</em>: an item that sets the same property itself keeps its own
/// value, so one odd expander in the list stays odd.
/// </remarks>
/// <example>
/// <code language="xaml">
/// &lt;shiny:Accordion SelectionMode="Single" AllowCollapseAll="False" Animation="Height,Fade"&gt;
///     &lt;shiny:Expander HeaderText="Account"&gt;…&lt;/shiny:Expander&gt;
///     &lt;shiny:Expander HeaderText="Billing"&gt;…&lt;/shiny:Expander&gt;
/// &lt;/shiny:Accordion&gt;
/// </code>
/// </example>
public class Accordion : VerticalStackLayout
{
    // Which properties this accordion has pushed onto which item. Without it, the first push would
    // make IsSet(target) true on the item and every later change to the accordion's value would be
    // mistaken for "the item set that itself, leave it alone".
    readonly ConditionalWeakTable<Expander, HashSet<BindableProperty>> pushed = new();
    readonly Dictionary<Expander, object?> generatedData = new();
    readonly List<Expander> generated = new();

    IDisposable? itemsSubscription;
    bool syncing;

    public Accordion()
    {
        this.Spacing = 8;

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(Accordion));
    }


    // ---------------------------------------------------------------------------------------------
    // Properties
    // ---------------------------------------------------------------------------------------------

    public static readonly BindableProperty SelectionModeProperty = BindableProperty.Create(
        nameof(SelectionMode), typeof(AccordionSelectionMode), typeof(Accordion), AccordionSelectionMode.Single,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Accordion), () => ((Accordion)b).EnforceRules(null)));
    /// <summary>One open at a time, or as many as the user likes. Defaults to <see cref="AccordionSelectionMode.Single"/>.</summary>
    public AccordionSelectionMode SelectionMode { get => (AccordionSelectionMode)GetValue(SelectionModeProperty); set => SetValue(SelectionModeProperty, value); }

    public static readonly BindableProperty AllowCollapseAllProperty = BindableProperty.Create(
        nameof(AllowCollapseAll), typeof(bool), typeof(Accordion), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Accordion), () => ((Accordion)b).EnforceRules(null)));
    /// <summary>
    /// When false the accordion refuses to end up with nothing open: the last open item stops
    /// responding to taps, and if the list starts fully closed the first item is opened.
    /// </summary>
    public bool AllowCollapseAll { get => (bool)GetValue(AllowCollapseAllProperty); set => SetValue(AllowCollapseAllProperty, value); }

    public static readonly BindableProperty ExpandedIndexProperty = BindableProperty.Create(
        nameof(ExpandedIndex), typeof(int), typeof(Accordion), -1, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(Accordion), () => ((Accordion)b).OnExpandedIndexChanged((int)n)));
    /// <summary>
    /// Index of the open item, or -1 for none. Two-way, and in <see cref="AccordionSelectionMode.Multiple"/>
    /// it reports the first open item.
    /// </summary>
    public int ExpandedIndex { get => (int)GetValue(ExpandedIndexProperty); set => SetValue(ExpandedIndexProperty, value); }

    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource), typeof(IEnumerable), typeof(Accordion), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(Accordion), () => ((Accordion)b).OnItemsSourceChanged(o as IEnumerable, n as IEnumerable)));
    /// <summary>Data to generate expanders from, using <see cref="ItemTemplate"/> or the header/content templates.</summary>
    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }

    public static readonly BindableProperty ItemTemplateProperty = BindableProperty.Create(
        nameof(ItemTemplate), typeof(DataTemplate), typeof(Accordion), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Accordion), () => ((Accordion)b).RegenerateItems()));
    /// <summary>
    /// Builds a whole <see cref="Expander"/> per item. Use it when an item needs to set expander
    /// properties of its own; otherwise <see cref="HeaderTemplate"/> and <see cref="ContentTemplate"/>
    /// are less to write.
    /// </summary>
    public DataTemplate? ItemTemplate { get => (DataTemplate?)GetValue(ItemTemplateProperty); set => SetValue(ItemTemplateProperty, value); }

    public static readonly BindableProperty HeaderTemplateProperty = BindableProperty.Create(
        nameof(HeaderTemplate), typeof(DataTemplate), typeof(Accordion), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Accordion), () => ((Accordion)b).RegenerateItems()));
    /// <summary>Header for each generated expander.</summary>
    public DataTemplate? HeaderTemplate { get => (DataTemplate?)GetValue(HeaderTemplateProperty); set => SetValue(HeaderTemplateProperty, value); }

    public static readonly BindableProperty ContentTemplateProperty = BindableProperty.Create(
        nameof(ContentTemplate), typeof(DataTemplate), typeof(Accordion), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Accordion), () => ((Accordion)b).RegenerateItems()));
    /// <summary>Content for each generated expander.</summary>
    public DataTemplate? ContentTemplate { get => (DataTemplate?)GetValue(ContentTemplateProperty); set => SetValue(ContentTemplateProperty, value); }

    public static readonly BindableProperty LoadContentOnDemandProperty = BindableProperty.Create(
        nameof(LoadContentOnDemand), typeof(bool), typeof(Accordion), false,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Accordion), () => ((Accordion)b).PushDefaults()));
    /// <summary>Hold each item's content back until it is first opened. See <see cref="Expander.LoadContentOnDemand"/>.</summary>
    public bool LoadContentOnDemand { get => (bool)GetValue(LoadContentOnDemandProperty); set => SetValue(LoadContentOnDemandProperty, value); }

    public static readonly BindableProperty ItemStyleProperty = BindableProperty.Create(
        nameof(ItemStyle), typeof(Style), typeof(Accordion), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Accordion), () => ((Accordion)b).PushDefaults()));
    /// <summary>
    /// A <c>TargetType="shiny:Expander"</c> style applied to every item — the full styling surface,
    /// for anything the shortcuts below do not cover.
    /// </summary>
    public Style? ItemStyle { get => (Style?)GetValue(ItemStyleProperty); set => SetValue(ItemStyleProperty, value); }

    public static readonly BindableProperty ItemExpandedCommandProperty = BindableProperty.Create(
        nameof(ItemExpandedCommand), typeof(ICommand), typeof(Accordion), null);
    /// <summary>Invoked with the item's data (or the expander itself, for markup items) when one opens.</summary>
    public ICommand? ItemExpandedCommand { get => (ICommand?)GetValue(ItemExpandedCommandProperty); set => SetValue(ItemExpandedCommandProperty, value); }


    // -- pass-through defaults ---------------------------------------------------------------------

    public static readonly BindableProperty AnimationProperty = BindableProperty.Create(
        nameof(Animation), typeof(ExpanderAnimation), typeof(Accordion), ExpanderAnimation.Height | ExpanderAnimation.Fade,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Accordion), () => ((Accordion)b).PushDefaults()));
    /// <summary>Default <see cref="Expander.Animation"/> for every item.</summary>
    public ExpanderAnimation Animation { get => (ExpanderAnimation)GetValue(AnimationProperty); set => SetValue(AnimationProperty, value); }

    public static readonly BindableProperty SlideFromProperty = BindableProperty.Create(
        nameof(SlideFrom), typeof(ExpanderSlideFrom), typeof(Accordion), ExpanderSlideFrom.Top,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Accordion), () => ((Accordion)b).PushDefaults()));
    /// <summary>Default <see cref="Expander.SlideFrom"/> for every item.</summary>
    public ExpanderSlideFrom SlideFrom { get => (ExpanderSlideFrom)GetValue(SlideFromProperty); set => SetValue(SlideFromProperty, value); }

    public static readonly BindableProperty AnimationDurationProperty = BindableProperty.Create(
        nameof(AnimationDuration), typeof(uint), typeof(Accordion), 250u,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Accordion), () => ((Accordion)b).PushDefaults()));
    /// <summary>Default <see cref="Expander.AnimationDuration"/> for every item.</summary>
    public uint AnimationDuration { get => (uint)GetValue(AnimationDurationProperty); set => SetValue(AnimationDurationProperty, value); }

    public static readonly BindableProperty AnimationEasingProperty = BindableProperty.Create(
        nameof(AnimationEasing), typeof(Easing), typeof(Accordion), Easing.CubicOut,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Accordion), () => ((Accordion)b).PushDefaults()));
    /// <summary>Default <see cref="Expander.AnimationEasing"/> for every item.</summary>
    public Easing AnimationEasing { get => (Easing)GetValue(AnimationEasingProperty); set => SetValue(AnimationEasingProperty, value); }

    public static readonly BindableProperty ExpandDirectionProperty = BindableProperty.Create(
        nameof(ExpandDirection), typeof(ExpandDirection), typeof(Accordion), global::Shiny.Maui.Controls.ExpandDirection.Down,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Accordion), () => ((Accordion)b).PushDefaults()));
    /// <summary>Default <see cref="Expander.ExpandDirection"/> for every item.</summary>
    public ExpandDirection ExpandDirection { get => (ExpandDirection)GetValue(ExpandDirectionProperty); set => SetValue(ExpandDirectionProperty, value); }

    public static readonly BindableProperty IndicatorModeProperty = BindableProperty.Create(
        nameof(IndicatorMode), typeof(ExpanderIndicatorMode), typeof(Accordion), ExpanderIndicatorMode.Rotate,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Accordion), () => ((Accordion)b).PushDefaults()));
    /// <summary>Default <see cref="Expander.IndicatorMode"/> for every item.</summary>
    public ExpanderIndicatorMode IndicatorMode { get => (ExpanderIndicatorMode)GetValue(IndicatorModeProperty); set => SetValue(IndicatorModeProperty, value); }

    public static readonly BindableProperty IndicatorPositionProperty = BindableProperty.Create(
        nameof(IndicatorPosition), typeof(ExpanderIndicatorPosition), typeof(Accordion), ExpanderIndicatorPosition.End,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Accordion), () => ((Accordion)b).PushDefaults()));
    /// <summary>Default <see cref="Expander.IndicatorPosition"/> for every item.</summary>
    public ExpanderIndicatorPosition IndicatorPosition { get => (ExpanderIndicatorPosition)GetValue(IndicatorPositionProperty); set => SetValue(IndicatorPositionProperty, value); }

    public static readonly BindableProperty BorderColorProperty = BindableProperty.Create(
        nameof(BorderColor), typeof(Color), typeof(Accordion), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Accordion), () => ((Accordion)b).PushDefaults()));
    /// <summary>Default <see cref="Expander.BorderColor"/> for every item.</summary>
    public Color? BorderColor { get => (Color?)GetValue(BorderColorProperty); set => SetValue(BorderColorProperty, value); }

    public static readonly BindableProperty BorderThicknessProperty = BindableProperty.Create(
        nameof(BorderThickness), typeof(double), typeof(Accordion), ThemeTokens.Unset,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Accordion), () => ((Accordion)b).PushDefaults()));
    /// <summary>Default <see cref="Expander.BorderThickness"/> for every item.</summary>
    public double BorderThickness { get => (double)GetValue(BorderThicknessProperty); set => SetValue(BorderThicknessProperty, value); }

    public static readonly BindableProperty CornerRadiusProperty = BindableProperty.Create(
        nameof(CornerRadius), typeof(double), typeof(Accordion), ThemeTokens.Unset,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Accordion), () => ((Accordion)b).PushDefaults()));
    /// <summary>Default <see cref="Expander.CornerRadius"/> for every item.</summary>
    public double CornerRadius { get => (double)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }

    public static readonly BindableProperty HeaderBackgroundColorProperty = BindableProperty.Create(
        nameof(HeaderBackgroundColor), typeof(Color), typeof(Accordion), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Accordion), () => ((Accordion)b).PushDefaults()));
    /// <summary>Default <see cref="Expander.HeaderBackgroundColor"/> for every item.</summary>
    public Color? HeaderBackgroundColor { get => (Color?)GetValue(HeaderBackgroundColorProperty); set => SetValue(HeaderBackgroundColorProperty, value); }

    public static readonly BindableProperty ContentBackgroundColorProperty = BindableProperty.Create(
        nameof(ContentBackgroundColor), typeof(Color), typeof(Accordion), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Accordion), () => ((Accordion)b).PushDefaults()));
    /// <summary>Default <see cref="Expander.ContentBackgroundColor"/> for every item.</summary>
    public Color? ContentBackgroundColor { get => (Color?)GetValue(ContentBackgroundColorProperty); set => SetValue(ContentBackgroundColorProperty, value); }


    // Source property on the accordion -> the property it seeds on each item. Everything in here is
    // a default: an item that carries its own value for the target keeps it.
    static readonly (BindableProperty Source, BindableProperty Target)[] PassThrough =
    [
        (AnimationProperty, Expander.AnimationProperty),
        (SlideFromProperty, Expander.SlideFromProperty),
        (AnimationDurationProperty, Expander.AnimationDurationProperty),
        (AnimationEasingProperty, Expander.AnimationEasingProperty),
        (ExpandDirectionProperty, Expander.ExpandDirectionProperty),
        (IndicatorModeProperty, Expander.IndicatorModeProperty),
        (IndicatorPositionProperty, Expander.IndicatorPositionProperty),
        (BorderColorProperty, Expander.BorderColorProperty),
        (BorderThicknessProperty, Expander.BorderThicknessProperty),
        (CornerRadiusProperty, Expander.CornerRadiusProperty),
        (HeaderBackgroundColorProperty, Expander.HeaderBackgroundColorProperty),
        (ContentBackgroundColorProperty, Expander.ContentBackgroundColorProperty),
        (LoadContentOnDemandProperty, Expander.LoadContentOnDemandProperty)
    ];


    // ---------------------------------------------------------------------------------------------
    // Public surface
    // ---------------------------------------------------------------------------------------------

    /// <summary>The expanders in this accordion, in visual order.</summary>
    public IReadOnlyList<Expander> Items => this.ItemList();

    List<Expander> ItemList() => this.Children.OfType<Expander>().ToList();

    /// <summary>Indexes of every open item.</summary>
    public IReadOnlyList<int> ExpandedIndexes
    {
        get
        {
            var items = this.ItemList();
            var result = new List<int>();
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].IsExpanded)
                    result.Add(i);
            }
            return result;
        }
    }

    /// <summary>Raised when an item opens.</summary>
    public event EventHandler<AccordionItemEventArgs>? ItemExpanded;

    /// <summary>Raised when an item closes.</summary>
    public event EventHandler<AccordionItemEventArgs>? ItemCollapsed;

    /// <summary>Raised whenever any item changes state.</summary>
    public event EventHandler<AccordionItemEventArgs>? ItemExpandedChanged;

    /// <summary>Open every item. Does nothing in <see cref="AccordionSelectionMode.Single"/>.</summary>
    public void ExpandAll()
    {
        if (this.SelectionMode == AccordionSelectionMode.Single)
            return;

        foreach (var item in this.ItemList())
            item.IsExpanded = true;
    }

    /// <summary>Close every item — unless <see cref="AllowCollapseAll"/> is false, which leaves the first one open.</summary>
    public void CollapseAll()
    {
        foreach (var item in this.ItemList())
            item.SetExpandedSilently(false);

        this.EnforceRules(null);
        this.PublishIndex();
    }

    /// <summary>Open the item at <paramref name="index"/>. Returns false when out of range.</summary>
    public bool ExpandItem(int index)
    {
        var items = this.ItemList();
        if (index < 0 || index >= items.Count)
            return false;

        items[index].IsExpanded = true;
        return true;
    }


    // ---------------------------------------------------------------------------------------------
    // Child tracking
    // ---------------------------------------------------------------------------------------------

    protected override void OnChildAdded(Element child)
    {
        base.OnChildAdded(child);

        if (child is Expander expander)
        {
            expander.Owner = this;
            this.PushDefaults(expander);
            this.EnforceRules(null);
            this.PublishIndex();
        }
    }


    protected override void OnChildRemoved(Element child, int oldLogicalIndex)
    {
        base.OnChildRemoved(child, oldLogicalIndex);

        if (child is Expander expander)
        {
            if (ReferenceEquals(expander.Owner, this))
                expander.Owner = null;

            this.pushed.Remove(expander);
            this.generatedData.Remove(expander);
            this.EnforceRules(null);
            this.PublishIndex();
        }
    }


    void PushDefaults()
    {
        foreach (var item in this.ItemList())
            this.PushDefaults(item);
    }


    void PushDefaults(Expander item)
    {
        if (this.ItemStyle != null && item.Style != this.ItemStyle)
            item.Style = this.ItemStyle;

        var seeded = this.pushed.GetValue(item, static _ => new HashSet<BindableProperty>());

        foreach (var (source, target) in PassThrough)
        {
            // Only propagate what this accordion actually carries a value for; the rest stay on the
            // expander's own defaults.
            if (!this.IsSet(source))
                continue;

            // The item set this itself and we have never touched it - leave it alone.
            if (item.IsSet(target) && !seeded.Contains(target))
                continue;

            seeded.Add(target);
            item.SetValue(target, this.GetValue(source));
        }
    }


    // ---------------------------------------------------------------------------------------------
    // ItemsSource
    // ---------------------------------------------------------------------------------------------

    void OnItemsSourceChanged(IEnumerable? oldValue, IEnumerable? newValue)
    {
        // Weak: ItemsSource is usually a view model's collection, which outlives the page.
        this.itemsSubscription?.Dispose();
        this.itemsSubscription = WeakEventSubscription.CollectionChanged(newValue, this.OnItemsCollectionChanged);

        this.RegenerateItems();
    }


    void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => this.RegenerateItems();


    void RegenerateItems()
    {
        foreach (var item in this.generated)
            this.Children.Remove(item);

        this.generated.Clear();

        var source = this.ItemsSource;
        if (source == null)
        {
            this.EnforceRules(null);
            this.PublishIndex();
            return;
        }

        foreach (var data in source)
        {
            var expander = this.BuildItem(data);
            this.generatedData[expander] = data;
            this.generated.Add(expander);
            this.Children.Add(expander);
        }

        this.EnforceRules(null);
        this.PublishIndex();
    }


    Expander BuildItem(object? data)
    {
        // An ItemTemplate that hands back a whole expander is used as-is; anything else is treated as
        // the content, so a plain DataTemplate still works without the caller knowing about Expander.
        if (this.ItemTemplate != null)
        {
            var built = this.ItemTemplate.CreateContent();
            if (built is Expander templated)
            {
                templated.BindingContext = data;
                return templated;
            }

            var wrapper = new Expander { BindingContext = data };
            if (built is View view)
                wrapper.Content = view;
            return wrapper;
        }

        var expander = new Expander { BindingContext = data };

        if (this.HeaderTemplate != null)
            expander.HeaderTemplate = this.HeaderTemplate;
        else
            expander.HeaderText = data?.ToString() ?? String.Empty;

        if (this.ContentTemplate != null)
            expander.ContentTemplate = this.ContentTemplate;

        return expander;
    }


    // ---------------------------------------------------------------------------------------------
    // Coordination
    // ---------------------------------------------------------------------------------------------

    internal void OnItemExpandedChanged(Expander item)
    {
        if (this.syncing)
            return;

        this.EnforceRules(item.IsExpanded ? item : null);
        this.PublishIndex();

        var items = this.ItemList();
        var args = new AccordionItemEventArgs(
            item,
            this.generatedData.TryGetValue(item, out var data) ? data : null,
            items.IndexOf(item),
            item.IsExpanded
        );

        if (item.IsExpanded)
            this.ItemExpanded?.Invoke(this, args);
        else
            this.ItemCollapsed?.Invoke(this, args);

        this.ItemExpandedChanged?.Invoke(this, args);

        if (item.IsExpanded)
        {
            var command = this.ItemExpandedCommand;
            var parameter = args.Data ?? item;
            if (command?.CanExecute(parameter) == true)
                command.Execute(parameter);
        }
    }


    /// <summary>
    /// Bring the items back in line with <see cref="SelectionMode"/> and <see cref="AllowCollapseAll"/>.
    /// <paramref name="winner"/> is the item that just opened, and is the one kept open when single
    /// selection has to pick.
    /// </summary>
    void EnforceRules(Expander? winner)
    {
        if (this.syncing)
            return;

        this.syncing = true;
        try
        {
            var items = this.ItemList();
            if (items.Count == 0)
                return;

            if (this.SelectionMode == AccordionSelectionMode.Single)
            {
                var keep = winner ?? items.FirstOrDefault(x => x.IsExpanded);
                foreach (var item in items)
                {
                    if (!ReferenceEquals(item, keep))
                        item.SetExpandedSilently(false);
                }
            }

            if (!this.AllowCollapseAll && !items.Any(x => x.IsExpanded))
                items[0].SetExpandedSilently(true);

            // The open item loses its close affordance only when closing it would leave nothing open.
            var openCount = items.Count(x => x.IsExpanded);
            foreach (var item in items)
                item.CanCollapse = this.AllowCollapseAll || !item.IsExpanded || openCount > 1;
        }
        finally
        {
            this.syncing = false;
        }
    }


    void OnExpandedIndexChanged(int index)
    {
        if (this.syncing)
            return;

        var items = this.ItemList();
        if (index < 0)
        {
            this.CollapseAll();
            return;
        }

        if (index >= items.Count)
            return;

        items[index].IsExpanded = true;
    }


    void PublishIndex()
    {
        var items = this.ItemList();
        var index = -1;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].IsExpanded)
            {
                index = i;
                break;
            }
        }

        if (this.ExpandedIndex == index)
            return;

        this.syncing = true;
        try
        {
            this.ExpandedIndex = index;
        }
        finally
        {
            this.syncing = false;
        }
    }
}
