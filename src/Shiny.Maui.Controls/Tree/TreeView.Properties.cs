using System.Collections;
using System.Windows.Input;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.Tree;

public partial class TreeView
{
    // ------------- Data -------------
    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource), typeof(IEnumerable), typeof(TreeView), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TreeView), () =>
            {
                ((TreeView)b).OnItemsSourceChanged();
            }));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly BindableProperty ItemTemplateProperty = BindableProperty.Create(
        nameof(ItemTemplate), typeof(DataTemplate), typeof(TreeView), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TreeView), () =>
            {
                ((TreeView)b).Rebuild();
            }));

    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public static readonly BindableProperty RootLoaderProperty = BindableProperty.Create(
        nameof(RootLoader), typeof(Func<Task<IEnumerable<object>>>), typeof(TreeView), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TreeView), () =>
            {
                ((TreeView)b).OnRootLoaderChanged();
            }));

    /// <summary>
    /// Optional async loader for the root items. When set, ItemsSource is ignored and the
    /// tree shows a loading state on first render until the loader completes.
    /// </summary>
    public Func<Task<IEnumerable<object>>>? RootLoader
    {
        get => (Func<Task<IEnumerable<object>>>?)GetValue(RootLoaderProperty);
        set => SetValue(RootLoaderProperty, value);
    }

    public static readonly BindableProperty ChildrenSelectorProperty = BindableProperty.Create(
        nameof(ChildrenSelector), typeof(Func<object, IEnumerable<object>?>), typeof(TreeView), null);

    /// <summary>
    /// Synchronous children selector. Use for fully-materialized hierarchies. Ignored when
    /// <see cref="ChildrenLoader"/> is set.
    /// </summary>
    public Func<object, IEnumerable<object>?>? ChildrenSelector
    {
        get => (Func<object, IEnumerable<object>?>?)GetValue(ChildrenSelectorProperty);
        set => SetValue(ChildrenSelectorProperty, value);
    }

    public static readonly BindableProperty ChildrenLoaderProperty = BindableProperty.Create(
        nameof(ChildrenLoader), typeof(Func<object, Task<IEnumerable<object>>>), typeof(TreeView), null);

    /// <summary>
    /// Async children loader invoked the first time a node is expanded. Results are cached
    /// on the node; call <see cref="TreeView.Refresh(object)"/> to invalidate.
    /// </summary>
    public Func<object, Task<IEnumerable<object>>>? ChildrenLoader
    {
        get => (Func<object, Task<IEnumerable<object>>>?)GetValue(ChildrenLoaderProperty);
        set => SetValue(ChildrenLoaderProperty, value);
    }

    public static readonly BindableProperty HasChildrenSelectorProperty = BindableProperty.Create(
        nameof(HasChildrenSelector), typeof(Func<object, bool>), typeof(TreeView), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TreeView), () =>
            {
                ((TreeView)b).Rebuild();
            }));

    /// <summary>
    /// Predicate that returns true when the item may have children. Items returning false
    /// render as leaves with no expand chevron. Defaults to true when a ChildrenLoader is
    /// set, or to "any items returned by ChildrenSelector" when sync.
    /// </summary>
    public Func<object, bool>? HasChildrenSelector
    {
        get => (Func<object, bool>?)GetValue(HasChildrenSelectorProperty);
        set => SetValue(HasChildrenSelectorProperty, value);
    }

    public static readonly BindableProperty CanExpandSelectorProperty = BindableProperty.Create(
        nameof(CanExpandSelector), typeof(Func<object, bool>), typeof(TreeView), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TreeView), () =>
            {
                ((TreeView)b).Rebuild();
            }));

    public Func<object, bool>? CanExpandSelector
    {
        get => (Func<object, bool>?)GetValue(CanExpandSelectorProperty);
        set => SetValue(CanExpandSelectorProperty, value);
    }

    public static readonly BindableProperty CanSelectSelectorProperty = BindableProperty.Create(
        nameof(CanSelectSelector), typeof(Func<object, bool>), typeof(TreeView), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TreeView), () =>
            {
                ((TreeView)b).Rebuild();
            }));

    public Func<object, bool>? CanSelectSelector
    {
        get => (Func<object, bool>?)GetValue(CanSelectSelectorProperty);
        set => SetValue(CanSelectSelectorProperty, value);
    }

    // ------------- Selection -------------
    public static readonly BindableProperty SelectionModeProperty = BindableProperty.Create(
        nameof(SelectionMode), typeof(TreeSelectionMode), typeof(TreeView), TreeSelectionMode.Single,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TreeView), () =>
            {
                ((TreeView)b).OnSelectionModeChanged();
            }));

    /// <summary>
    /// Selection behaviour. <see cref="TreeSelectionMode.Multiple"/> renders a checkbox on
    /// every row (see <see cref="ShowSelectionCheckBoxes"/> to turn that off).
    /// </summary>
    public TreeSelectionMode SelectionMode
    {
        get => (TreeSelectionMode)GetValue(SelectionModeProperty);
        set => SetValue(SelectionModeProperty, value);
    }

    public static readonly BindableProperty ShowSelectionCheckBoxesProperty = BindableProperty.Create(
        nameof(ShowSelectionCheckBoxes), typeof(bool), typeof(TreeView), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TreeView), () =>
            {
                ((TreeView)b).Rebuild();
            }));

    /// <summary>
    /// Show a checkbox on each row while <see cref="SelectionMode"/> is
    /// <see cref="TreeSelectionMode.Multiple"/>. Defaults to true. The checkbox mirrors the
    /// row's selection state — tapping anywhere on the row (including the box) toggles it.
    /// </summary>
    public bool ShowSelectionCheckBoxes
    {
        get => (bool)GetValue(ShowSelectionCheckBoxesProperty);
        set => SetValue(ShowSelectionCheckBoxesProperty, value);
    }

    public static readonly BindableProperty CheckBoxColorProperty = BindableProperty.Create(
        nameof(CheckBoxColor), typeof(Color), typeof(TreeView), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TreeView), () =>
            {
                ((TreeView)b).Rebuild();
            }));

    /// <summary>Multi-select checkbox color. When unset, binds the Primary theme token.</summary>
    public Color? CheckBoxColor
    {
        get => (Color?)GetValue(CheckBoxColorProperty);
        set => SetValue(CheckBoxColorProperty, value);
    }

    public static readonly BindableProperty SelectedItemProperty = BindableProperty.Create(
        nameof(SelectedItem), typeof(object), typeof(TreeView), null,
        defaultBindingMode: BindingMode.TwoWay,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(TreeView), () =>
            {
                ((TreeView)b).OnSelectedItemPropertyChanged(n);
            }));

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public static readonly BindableProperty SelectedItemsProperty = BindableProperty.Create(
        nameof(SelectedItems), typeof(IList<object>), typeof(TreeView), null,
        defaultBindingMode: BindingMode.TwoWay);

    public IList<object>? SelectedItems
    {
        get => (IList<object>?)GetValue(SelectedItemsProperty);
        set => SetValue(SelectedItemsProperty, value);
    }

    // ------------- Icons -------------
    public static readonly BindableProperty ExpandedIconProperty = BindableProperty.Create(
        nameof(ExpandedIcon), typeof(ImageSource), typeof(TreeView), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TreeView), () =>
            {
                ((TreeView)b).RefreshChevrons();
            }));

    /// <summary>Icon shown when a node is expanded. Falls back to a built-in ▼ glyph.</summary>
    public ImageSource? ExpandedIcon
    {
        get => (ImageSource?)GetValue(ExpandedIconProperty);
        set => SetValue(ExpandedIconProperty, value);
    }

    public static readonly BindableProperty CollapsedIconProperty = BindableProperty.Create(
        nameof(CollapsedIcon), typeof(ImageSource), typeof(TreeView), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TreeView), () =>
            {
                ((TreeView)b).RefreshChevrons();
            }));

    /// <summary>Icon shown when a node is collapsed. Falls back to a built-in ▶ glyph.</summary>
    public ImageSource? CollapsedIcon
    {
        get => (ImageSource?)GetValue(CollapsedIconProperty);
        set => SetValue(CollapsedIconProperty, value);
    }

    public static readonly BindableProperty RetryIconProperty = BindableProperty.Create(
        nameof(RetryIcon), typeof(ImageSource), typeof(TreeView), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TreeView), () =>
            {
                ((TreeView)b).RefreshChevrons();
            }));

    /// <summary>Icon shown when a lazy load fails. Tapping it retries. Falls back to ⟳.</summary>
    public ImageSource? RetryIcon
    {
        get => (ImageSource?)GetValue(RetryIconProperty);
        set => SetValue(RetryIconProperty, value);
    }

    public static readonly BindableProperty ChevronColorProperty = BindableProperty.Create(
        nameof(ChevronColor), typeof(Color), typeof(TreeView), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TreeView), () =>
            {
                ((TreeView)b).RefreshChevrons();
            }));

    /// <summary>Chevron/glyph color. When unset, binds the OnSurfaceVariant theme token.</summary>
    public Color? ChevronColor
    {
        get => (Color?)GetValue(ChevronColorProperty);
        set => SetValue(ChevronColorProperty, value);
    }

    public static readonly BindableProperty ChevronSizeProperty = BindableProperty.Create(
        nameof(ChevronSize), typeof(double), typeof(TreeView), 16d,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TreeView), () =>
            {
                ((TreeView)b).Rebuild();
            }));

    public double ChevronSize
    {
        get => (double)GetValue(ChevronSizeProperty);
        set => SetValue(ChevronSizeProperty, value);
    }

    // ------------- Layout -------------
    public static readonly BindableProperty IndentSizeProperty = BindableProperty.Create(
        nameof(IndentSize), typeof(double), typeof(TreeView), 20d,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TreeView), () =>
            {
                ((TreeView)b).Rebuild();
            }));

    public double IndentSize
    {
        get => (double)GetValue(IndentSizeProperty);
        set => SetValue(IndentSizeProperty, value);
    }

    // ------------- Virtualization -------------
    public static readonly BindableProperty IsVirtualizedProperty = BindableProperty.Create(
        nameof(IsVirtualized), typeof(bool), typeof(TreeView), false,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TreeView), () =>
            {
                ((TreeView)b).OnIsVirtualizedChanged();
            }));

    /// <summary>
    /// Only build the rows in and around the viewport. Every row is then <see cref="RowHeight"/>
    /// tall (and <see cref="RowSpacing"/> is ignored), and the tree must be given a bounded height
    /// - a <c>*</c> grid row, not a <see cref="ScrollView"/> or <see cref="VerticalStackLayout"/>,
    /// which would let it grow to its full height and realize every row anyway.
    /// </summary>
    public bool IsVirtualized
    {
        get => (bool)GetValue(IsVirtualizedProperty);
        set => SetValue(IsVirtualizedProperty, value);
    }

    public static readonly BindableProperty RowHeightProperty = BindableProperty.Create(
        nameof(RowHeight), typeof(double), typeof(TreeView), 40d,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TreeView), () =>
            {
                ((TreeView)b).Rebuild();
            }));

    /// <summary>The fixed height of every row while <see cref="IsVirtualized"/>. Ignored otherwise.</summary>
    public double RowHeight
    {
        get => (double)GetValue(RowHeightProperty);
        set => SetValue(RowHeightProperty, value);
    }

    public static readonly BindableProperty RowSpacingProperty = BindableProperty.Create(
        nameof(RowSpacing), typeof(double), typeof(TreeView), 0d,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(TreeView), () =>
            {
                ((TreeView)b).rowLayout.Spacing = (double)n;
            }));

    public double RowSpacing
    {
        get => (double)GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    public static readonly BindableProperty RowPaddingProperty = BindableProperty.Create(
        nameof(RowPadding), typeof(Thickness), typeof(TreeView), new Thickness(8, 6),
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TreeView), () =>
            {
                ((TreeView)b).Rebuild();
            }));

    public Thickness RowPadding
    {
        get => (Thickness)GetValue(RowPaddingProperty);
        set => SetValue(RowPaddingProperty, value);
    }

    // ------------- Guide lines -------------
    public static readonly BindableProperty ShowGuideLinesProperty = BindableProperty.Create(
        nameof(ShowGuideLines), typeof(bool), typeof(TreeView), false,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TreeView), () =>
            {
                ((TreeView)b).Rebuild();
            }));

    public bool ShowGuideLines
    {
        get => (bool)GetValue(ShowGuideLinesProperty);
        set => SetValue(ShowGuideLinesProperty, value);
    }

    public static readonly BindableProperty GuideLineColorProperty = BindableProperty.Create(
        nameof(GuideLineColor), typeof(Color), typeof(TreeView), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TreeView), () =>
            {
                ((TreeView)b).Rebuild();
            }));

    /// <summary>Guideline color. When unset, binds the OutlineVariant theme token.</summary>
    public Color? GuideLineColor
    {
        get => (Color?)GetValue(GuideLineColorProperty);
        set => SetValue(GuideLineColorProperty, value);
    }

    // ------------- Visuals -------------
    public static readonly BindableProperty SelectedBackgroundColorProperty = BindableProperty.Create(
        nameof(SelectedBackgroundColor), typeof(Color), typeof(TreeView), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TreeView), () =>
            {
                ((TreeView)b).RefreshSelectionVisuals();
            }));

    /// <summary>Selected row background. When unset, binds the SecondaryContainer theme token.</summary>
    public Color? SelectedBackgroundColor
    {
        get => (Color?)GetValue(SelectedBackgroundColorProperty);
        set => SetValue(SelectedBackgroundColorProperty, value);
    }

    public static readonly BindableProperty RowBackgroundColorProperty = BindableProperty.Create(
        nameof(RowBackgroundColor), typeof(Color), typeof(TreeView), Colors.Transparent,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TreeView), () =>
            {
                ((TreeView)b).RefreshSelectionVisuals();
            }));

    public Color RowBackgroundColor
    {
        get => (Color)GetValue(RowBackgroundColorProperty);
        set => SetValue(RowBackgroundColorProperty, value);
    }

    // ------------- Drag/drop -------------
    public static readonly BindableProperty EnableDragDropProperty = BindableProperty.Create(
        nameof(EnableDragDrop), typeof(bool), typeof(TreeView), false,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(TreeView), () =>
            {
                ((TreeView)b).Rebuild();
            }));

    /// <summary>
    /// When true, rows become drag sources and drop targets. The TreeView never mutates
    /// the bound data; subscribe to <see cref="ItemDropped"/> (or bind
    /// <see cref="ItemDroppedCommand"/>) to perform the reorder yourself.
    /// </summary>
    public bool EnableDragDrop
    {
        get => (bool)GetValue(EnableDragDropProperty);
        set => SetValue(EnableDragDropProperty, value);
    }

    // ------------- Commands -------------
    public static readonly BindableProperty ItemSelectedCommandProperty = BindableProperty.Create(
        nameof(ItemSelectedCommand), typeof(ICommand), typeof(TreeView));

    public ICommand? ItemSelectedCommand
    {
        get => (ICommand?)GetValue(ItemSelectedCommandProperty);
        set => SetValue(ItemSelectedCommandProperty, value);
    }

    public static readonly BindableProperty ItemExpandedCommandProperty = BindableProperty.Create(
        nameof(ItemExpandedCommand), typeof(ICommand), typeof(TreeView));

    public ICommand? ItemExpandedCommand
    {
        get => (ICommand?)GetValue(ItemExpandedCommandProperty);
        set => SetValue(ItemExpandedCommandProperty, value);
    }

    public static readonly BindableProperty ItemCollapsedCommandProperty = BindableProperty.Create(
        nameof(ItemCollapsedCommand), typeof(ICommand), typeof(TreeView));

    public ICommand? ItemCollapsedCommand
    {
        get => (ICommand?)GetValue(ItemCollapsedCommandProperty);
        set => SetValue(ItemCollapsedCommandProperty, value);
    }

    public static readonly BindableProperty LoadFailedCommandProperty = BindableProperty.Create(
        nameof(LoadFailedCommand), typeof(ICommand), typeof(TreeView));

    public ICommand? LoadFailedCommand
    {
        get => (ICommand?)GetValue(LoadFailedCommandProperty);
        set => SetValue(LoadFailedCommandProperty, value);
    }

    public static readonly BindableProperty ItemDroppedCommandProperty = BindableProperty.Create(
        nameof(ItemDroppedCommand), typeof(ICommand), typeof(TreeView));

    public ICommand? ItemDroppedCommand
    {
        get => (ICommand?)GetValue(ItemDroppedCommandProperty);
        set => SetValue(ItemDroppedCommandProperty, value);
    }
}
