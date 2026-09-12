using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

/// <summary>
/// A bound set of chips: one per item in <see cref="ItemsSource"/>, selectable singly or in any number,
/// reported back through <see cref="SelectedItem"/> and <see cref="SelectedItems"/>.
/// </summary>
/// <remarks>
/// <para>
/// The difference from <see cref="ButtonGroup"/> is where the segments come from and what they select.
/// A button group's segments are written out in markup and its selection is an <em>index</em>, which is
/// the right shape for "Day / Week / Month" and the wrong one for a list that arrives from a service. A
/// chip group's chips come from a collection and its selection is the <em>items themselves</em>, so a
/// view model binds the objects it already has rather than translating indexes back into them. It also
/// wraps: a dozen filters flow onto as many lines as they need instead of becoming one unreadably long
/// segmented control.
/// </para>
/// <para>
/// The difference from <see cref="TagEntry"/> is who supplies the values. A tag field is the user's own
/// words; a chip group is a known list they choose from.
/// </para>
/// </remarks>
/// <example>
/// <code language="xaml">
/// &lt;shiny:ChipGroup ItemsSource="{Binding Categories}"
///                  SelectedItem="{Binding Category}"
///                  DisplayMemberPath="Name" /&gt;
/// </code>
/// </example>
public partial class ChipGroup : ContentView
{
    readonly TagWrapLayout wrap;
    readonly List<ChipView> chips = new();
    readonly List<object> items = new();
    readonly List<object> selection = new();

    /// <summary>
    /// Resolves <see cref="ItemDisplayBinding"/> for one item at a time. It is never in the visual tree:
    /// a binding needs a binding context and nothing else, and a target per chip is not an option —
    /// MAUI refuses to apply one <c>BindingBase</c> instance to two targets, so the consumer's binding
    /// is applied once here and each item is pushed through it.
    /// </summary>
    readonly Label displayProbe = new();
    BindingBase? appliedDisplayBinding;

    INotifyCollectionChanged? itemsNotifier;
    INotifyCollectionChanged? selectedNotifier;
    bool syncing;
    bool rebuilding;

    public ChipGroup()
    {
        this.wrap = new TagWrapLayout
        {
            // A chip group's last chip is a chip like any other: there is no editor to hand the rest of
            // the line to, which is the only reason that switch exists.
            FillTrailingChild = false,
            HorizontalSpacing = 8,
            VerticalSpacing = 8
        };

        this.Content = this.wrap;
        this.HorizontalOptions = LayoutOptions.Fill;

        // Assigned after the children exist, for the reason TagEntry gives: the property-changed handler
        // touches them. Created here rather than in a defaultValueCreator, because a lazily-created
        // default never raises propertyChanged and the CollectionChanged hook wired there never runs.
        this.SelectedItems = new ObservableCollection<object>();

        // Last line: replays any styled property that was applied before the children existed.
        // See StyleGuard.
        StyleGuard.MarkReady(this, typeof(ChipGroup));
    }


    // ---------------------------------------------------------------------------------------------
    // Public surface
    // ---------------------------------------------------------------------------------------------

    /// <summary>Raised whenever the selection changes, however it changed.</summary>
    public event EventHandler<ChipSelectionChangedEventArgs>? SelectionChanged;

    /// <summary>Raised for every tap on a chip, including one that changes nothing.</summary>
    public event EventHandler<ChipTappedEventArgs>? ChipTapped;

    /// <summary>Raised before a chip is removed. Cancel it to keep the chip.</summary>
    public event EventHandler<ChipRemovingEventArgs>? ChipRemoving;

    /// <summary>Raised after a chip is removed.</summary>
    public event EventHandler<ChipRemovedEventArgs>? ChipRemoved;

    /// <summary>The items the group currently has chips for, in order.</summary>
    public IReadOnlyList<object> Items => this.items.ToList();

    /// <summary>Whether <paramref name="item"/> is currently selected.</summary>
    public bool IsSelected(object item) => this.IndexIn(this.selection, item) >= 0;

    /// <summary>Selects <paramref name="item"/>. Returns false when it is not in the source, or the selection is capped.</summary>
    public bool Select(object item) => this.ApplySelection(item, true);

    /// <summary>Deselects <paramref name="item"/>. Returns false when it was not selected.</summary>
    public bool Deselect(object item) => this.ApplySelection(item, false);

    /// <summary>Clears the selection entirely.</summary>
    public void ClearSelection()
    {
        if (this.selection.Count == 0)
            return;

        this.selection.Clear();
        this.AfterSelectionChanged();
    }

    /// <summary>The chips currently on screen, in order. Internal, so a test can tap one the way a user does.</summary>
    internal IReadOnlyList<ChipView> Chips => this.chips;


    // ---------------------------------------------------------------------------------------------
    // Items
    // ---------------------------------------------------------------------------------------------

    internal void OnItemsSourceChanged(IEnumerable? oldValue, IEnumerable? newValue)
    {
        if (this.itemsNotifier is not null)
            this.itemsNotifier.CollectionChanged -= this.OnItemsCollectionChanged;

        this.itemsNotifier = newValue as INotifyCollectionChanged;
        if (this.itemsNotifier is not null)
            this.itemsNotifier.CollectionChanged += this.OnItemsCollectionChanged;

        this.RebuildChips();
    }


    void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => this.RebuildChips();


    internal void RebuildChips()
    {
        if (this.rebuilding)
            return;

        this.rebuilding = true;
        try
        {
            foreach (var chip in this.chips)
            {
                chip.Tapped -= this.OnChipTapped;
                chip.RemoveRequested -= this.OnChipRemoveRequested;
                this.wrap.Remove(chip);
            }
            this.chips.Clear();
            this.items.Clear();

            if (this.ItemsSource is not null)
            {
                foreach (var item in this.ItemsSource)
                {
                    // A null has nothing to draw and nothing to select: two of them would be the same
                    // chip twice over, and neither could be told apart in a selection.
                    if (item is not null)
                        this.items.Add(item);
                }
            }

            // Computed once per rebuild rather than once per chip: DisplayMemberPath makes a fresh
            // Binding, and applying a new one for every item would re-bind the probe all the way down
            // the list for no gain.
            this.ApplyDisplayBinding(
                this.ItemDisplayBinding
                ?? (String.IsNullOrWhiteSpace(this.DisplayMemberPath) ? null : new Binding(this.DisplayMemberPath))
            );

            foreach (var item in this.items)
            {
                var chip = new ChipView();
                chip.Bind(item, this.DisplayFor(item));

                if (this.ItemTemplate?.CreateContent() is View templated)
                {
                    templated.BindingContext = item;
                    chip.SetCustomContent(templated);
                }

                chip.Tapped += this.OnChipTapped;
                chip.RemoveRequested += this.OnChipRemoveRequested;

                this.chips.Add(chip);
                this.wrap.Add(chip);
            }

            // Anything selected that is no longer in the source goes: leaving it would report a
            // selection with no chip to show for it, which a view model cannot act on and the user
            // cannot undo.
            var pruned = this.selection.RemoveAll(x => this.IndexIn(this.items, x) < 0);

            this.ApplyLayout();
            this.RefreshChips();

            if (pruned > 0)
                this.AfterSelectionChanged();
        }
        finally
        {
            this.rebuilding = false;
        }
    }


    void ApplyDisplayBinding(BindingBase? binding)
    {
        if (ReferenceEquals(binding, this.appliedDisplayBinding))
            return;

        this.appliedDisplayBinding = binding;

        if (binding is null)
            this.displayProbe.RemoveBinding(Label.TextProperty);
        else
            this.displayProbe.SetBinding(Label.TextProperty, binding);
    }


    string DisplayFor(object item)
    {
        if (this.appliedDisplayBinding is null)
            return item.ToString() ?? String.Empty;

        this.displayProbe.BindingContext = item;
        return this.displayProbe.Text ?? String.Empty;
    }


    internal void RefreshChips()
    {
        foreach (var chip in this.chips)
        {
            chip.IsSelected = chip.Item is not null && this.IndexIn(this.selection, chip.Item) >= 0;
            chip.ShowCheck = this.ShowSelectionCheck && this.SelectionMode != ChipSelectionMode.None;
            chip.CanRemove = this.AllowRemove && !this.IsReadOnly;

            chip.ChipBackgroundColor = this.ChipBackgroundColor;
            chip.ChipTextColor = this.ChipTextColor;
            chip.SelectedChipBackgroundColor = this.SelectedChipBackgroundColor;
            chip.SelectedChipTextColor = this.SelectedChipTextColor;
            chip.ChipBorderColor = this.ChipBorderColor;
            chip.ChipCornerRadius = this.ChipCornerRadius;

            chip.Refresh();
        }
    }


    void ApplyLayout()
    {
        this.wrap.Wrap = this.Wrap;
        this.wrap.HorizontalSpacing = this.HorizontalSpacing;
        this.wrap.VerticalSpacing = this.VerticalSpacing;
        this.wrap.InvalidateMeasure();
    }


    // ---------------------------------------------------------------------------------------------
    // Selection
    // ---------------------------------------------------------------------------------------------

    void OnChipTapped(object? sender, EventArgs e)
    {
        if (this.IsReadOnly || !this.IsEnabled || sender is not ChipView chip || chip.Item is null)
            return;

        var item = chip.Item;
        var index = this.IndexIn(this.items, item);
        var wasSelected = this.IndexIn(this.selection, item) >= 0;
        var changed = false;

        switch (this.SelectionMode)
        {
            case ChipSelectionMode.Single:
                if (wasSelected)
                {
                    // Re-tapping the answer is a no-op unless deselection is allowed: a picker that can
                    // be emptied by tapping its own answer again is a picker with no answer.
                    if (this.AllowDeselect)
                    {
                        this.selection.Clear();
                        changed = true;
                    }
                }
                else
                {
                    this.selection.Clear();
                    this.selection.Add(item);
                    changed = true;
                }
                break;

            case ChipSelectionMode.Multiple:
                if (wasSelected)
                {
                    this.selection.RemoveAt(this.IndexIn(this.selection, item));
                    changed = true;
                }
                else if (this.MaxSelectionCount <= 0 || this.selection.Count < this.MaxSelectionCount)
                {
                    this.selection.Add(item);
                    changed = true;
                }
                break;
        }

        if (changed)
        {
            this.AfterSelectionChanged();

            // Only a selection the user just made draws itself in; one restored from a binding is
            // simply already there, and a group animating every check on load reads as a glitch.
            if (chip.IsSelected)
                chip.PlayCheck();
        }

        var args = new ChipTappedEventArgs(item, index, this.IndexIn(this.selection, item) >= 0);
        this.ChipTapped?.Invoke(this, args);

        var command = this.ChipTappedCommand;
        if (command?.CanExecute(item) == true)
            command.Execute(item);
    }


    bool ApplySelection(object item, bool selected)
    {
        if (this.SelectionMode == ChipSelectionMode.None || this.IndexIn(this.items, item) < 0)
            return false;

        var at = this.IndexIn(this.selection, item);

        if (selected)
        {
            if (at >= 0)
                return false;

            if (this.SelectionMode == ChipSelectionMode.Single)
                this.selection.Clear();
            else if (this.MaxSelectionCount > 0 && this.selection.Count >= this.MaxSelectionCount)
                return false;

            this.selection.Add(item);
        }
        else
        {
            if (at < 0)
                return false;

            this.selection.RemoveAt(at);
        }

        this.AfterSelectionChanged();
        return true;
    }


    /// <summary>Puts the selection back into source order, repaints, publishes and announces it.</summary>
    void AfterSelectionChanged()
    {
        this.selection.Sort((a, b) => this.IndexIn(this.items, a).CompareTo(this.IndexIn(this.items, b)));

        this.RefreshChips();
        this.PublishSelection();
        this.RaiseSelectionChanged();
    }


    void PublishSelection()
    {
        this.syncing = true;
        try
        {
            var first = this.selection.Count == 0 ? null : this.selection[0];
            if (!Equals(this.SelectedItem, first))
                this.SelectedItem = first;

            this.WriteSelectedItems();
        }
        finally
        {
            this.syncing = false;
        }
    }


    /// <summary>
    /// Mirrors the selection into the bound list <em>in place</em>. Replacing the instance would be
    /// easier and would break every view model that holds onto its own <c>ObservableCollection</c> and
    /// watches it.
    /// </summary>
    void WriteSelectedItems()
    {
        var list = this.SelectedItems;

        if (list is null)
        {
            this.SelectedItems = new ObservableCollection<object>(this.selection);
            return;
        }

        // A fixed-size or read-only list (an array, say) is a snapshot the consumer handed us rather
        // than a collection to keep. There is nowhere to write, and throwing over it would turn a
        // reasonable binding into a crash.
        if (list.IsReadOnly || list.IsFixedSize)
            return;

        if (SameContents(list, this.selection))
            return;

        list.Clear();
        foreach (var item in this.selection)
            list.Add(item);
    }


    internal void OnSelectedItemChanged(object? value)
    {
        if (this.syncing || this.SelectionMode == ChipSelectionMode.None)
            return;

        // Writing SelectedItem is "select exactly this one", in either mode - which is what makes the
        // property readable and writable on the same terms a single-selection group uses it.
        this.selection.Clear();
        if (value is not null && this.IndexIn(this.items, value) >= 0)
            this.selection.Add(value);

        this.RefreshChips();

        // Deliberately not the full publish: the SelectedItem write that would end it lands back in
        // this same handler, and MAUI defers a same-property SetValue - so the guard would already be
        // released by the time it arrived, and the two would take turns rewriting each other.
        this.syncing = true;
        try
        {
            this.WriteSelectedItems();
        }
        finally
        {
            this.syncing = false;
        }

        this.RaiseSelectionChanged();
    }


    internal void OnSelectedItemsChanged(IList? oldValue, IList? newValue)
    {
        if (this.selectedNotifier is not null)
            this.selectedNotifier.CollectionChanged -= this.OnSelectedItemsCollectionChanged;

        this.selectedNotifier = newValue as INotifyCollectionChanged;
        if (this.selectedNotifier is not null)
            this.selectedNotifier.CollectionChanged += this.OnSelectedItemsCollectionChanged;

        if (this.syncing)
            return;

        this.AdoptSelectedItems();
    }


    void OnSelectedItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (this.syncing)
            return;

        this.AdoptSelectedItems();
    }


    /// <summary>Takes the bound list as the truth — a view model has just set or changed it.</summary>
    void AdoptSelectedItems()
    {
        var list = this.SelectedItems;

        this.selection.Clear();

        if (list is not null && this.SelectionMode != ChipSelectionMode.None)
        {
            foreach (var item in list)
            {
                if (item is null || this.IndexIn(this.selection, item) >= 0)
                    continue;

                this.selection.Add(item);

                if (this.SelectionMode == ChipSelectionMode.Single)
                    break;
            }
        }

        this.selection.Sort((a, b) => this.IndexIn(this.items, a).CompareTo(this.IndexIn(this.items, b)));
        this.RefreshChips();

        this.syncing = true;
        try
        {
            var first = this.selection.Count == 0 ? null : this.selection[0];
            if (!Equals(this.SelectedItem, first))
                this.SelectedItem = first;
        }
        finally
        {
            this.syncing = false;
        }

        this.RaiseSelectionChanged();
    }


    void RaiseSelectionChanged()
    {
        var args = new ChipSelectionChangedEventArgs(this.selection.ToList());
        this.SelectionChanged?.Invoke(this, args);

        var command = this.SelectionChangedCommand;
        var parameter = this.SelectionChangedCommandParameter ?? args.SelectedItem;
        if (command?.CanExecute(parameter) == true)
            command.Execute(parameter);
    }


    // ---------------------------------------------------------------------------------------------
    // Removal
    // ---------------------------------------------------------------------------------------------

    void OnChipRemoveRequested(object? sender, EventArgs e)
    {
        if (this.IsReadOnly || sender is not ChipView chip || chip.Item is null)
            return;

        var item = chip.Item;
        var index = this.IndexIn(this.items, item);

        var removing = new ChipRemovingEventArgs(item, index);
        this.ChipRemoving?.Invoke(this, removing);
        if (removing.Cancel)
            return;

        // Taken out of the source when the source is a list that can be written to. When it is not -
        // a LINQ projection, an array - the event is the only signal and the view model owns the
        // removal, which is also how a chip that needs a server round-trip first is handled.
        if (this.ItemsSource is IList list && !list.IsReadOnly && !list.IsFixedSize)
        {
            var at = list.IndexOf(item);
            if (at >= 0)
                list.RemoveAt(at);
        }

        var wasSelected = this.selection.RemoveAll(x => Equals(x, item)) > 0;

        // A plain IList raises nothing, so the chips are rebuilt here; an ObservableCollection has
        // already done it and this is a no-op second pass.
        this.RebuildChips();

        if (wasSelected)
            this.AfterSelectionChanged();

        this.ChipRemoved?.Invoke(this, new ChipRemovedEventArgs(item, index));

        var command = this.ChipRemovedCommand;
        if (command?.CanExecute(item) == true)
            command.Execute(item);
    }


    // ---------------------------------------------------------------------------------------------
    // Plumbing
    // ---------------------------------------------------------------------------------------------

    int IndexIn(IList<object> list, object item)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (Equals(list[i], item))
                return i;
        }

        return -1;
    }


    static bool SameContents(IList list, IList<object> selection)
    {
        if (list.Count != selection.Count)
            return false;

        for (var i = 0; i < selection.Count; i++)
        {
            if (!Equals(list[i], selection[i]))
                return false;
        }

        return true;
    }


    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        if (propertyName == nameof(this.IsEnabled))
            StyleGuard.WhenReady<ChipGroup>(this, static x => x.wrap.Opacity = x.IsEnabled ? 1 : x.DisabledOpacity);
    }
}
