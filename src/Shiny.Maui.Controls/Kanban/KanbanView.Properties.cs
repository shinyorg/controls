using System.Collections;
using System.Globalization;
using System.Windows.Input;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.Kanban;

public partial class KanbanView
{
    // Every property that changes what is on screen routes through one of three verbs, so the
    // rebuild story stays legible: Bucket re-buckets the cards and re-renders from scratch, Render
    // rebuilds the visual tree from the board it already has, and Chrome only restyles what is
    // already there. Getting that wrong is how a control ends up re-bucketing a thousand cards
    // because someone changed a corner radius.
    static BindableProperty Bucket<T>(string name, T? defaultValue = default) =>
        Declare(name, defaultValue, static v => v.RebuildBoard());

    static BindableProperty Render<T>(string name, T? defaultValue = default) =>
        Declare(name, defaultValue, static v => v.RenderBoard());

    static BindableProperty Chrome<T>(string name, T? defaultValue = default) =>
        Declare(name, defaultValue, static v => v.ApplyThemeChrome());

    static BindableProperty Inert<T>(string name, T? defaultValue = default) =>
        Declare<T>(name, defaultValue, null);

    static BindableProperty Declare<T>(string name, T? defaultValue, Action<KanbanView>? changed) =>
        BindableProperty.Create(
            name,
            typeof(T),
            typeof(KanbanView),
            defaultValue,
            propertyChanged: changed is null
                ? null
                : (b, _, _) => StyleGuard.WhenReady(b, typeof(KanbanView), () => changed((KanbanView)b))
        );


    // =============================================================================================
    // Data
    // =============================================================================================

    public static readonly BindableProperty CardsProperty = BindableProperty.Create(
        nameof(Cards), typeof(IEnumerable), typeof(KanbanView),
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(KanbanView), () => ((KanbanView)b).OnItemsChanged(o, n)));

    public static readonly BindableProperty ColumnsProperty = BindableProperty.Create(
        nameof(Columns), typeof(IEnumerable), typeof(KanbanView),
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(KanbanView), () => ((KanbanView)b).OnItemsChanged(o, n)));

    public static readonly BindableProperty SwimlanesProperty = BindableProperty.Create(
        nameof(Swimlanes), typeof(IEnumerable), typeof(KanbanView),
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(KanbanView), () => ((KanbanView)b).OnItemsChanged(o, n)));

    /// <summary>
    /// The cards. Items must be <see cref="KanbanCard"/>; map your own type onto one and hang the
    /// original off <see cref="KanbanCard.Item"/>. An <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/>
    /// is watched for changes, as is each card's own <c>PropertyChanged</c>.
    /// </summary>
    public IEnumerable? Cards
    {
        get => (IEnumerable?)this.GetValue(CardsProperty);
        set => this.SetValue(CardsProperty, value);
    }

    /// <summary>The columns, left to right. Items must be <see cref="KanbanColumn"/>.</summary>
    public IEnumerable? Columns
    {
        get => (IEnumerable?)this.GetValue(ColumnsProperty);
        set => this.SetValue(ColumnsProperty, value);
    }

    /// <summary>
    /// The swimlane bands, top to bottom. Items must be <see cref="KanbanSwimlane"/>. Ignored unless
    /// <see cref="SwimlaneMode"/> is <see cref="KanbanSwimlaneMode.Grouped"/>.
    /// </summary>
    public IEnumerable? Swimlanes
    {
        get => (IEnumerable?)this.GetValue(SwimlanesProperty);
        set => this.SetValue(SwimlanesProperty, value);
    }


    // =============================================================================================
    // Behaviour
    // =============================================================================================

    public static readonly BindableProperty SwimlaneModeProperty = Bucket(nameof(SwimlaneMode), KanbanSwimlaneMode.None);
    public static readonly BindableProperty WipBehaviorProperty = Bucket(nameof(WipBehavior), KanbanWipBehavior.Warn);
    public static readonly BindableProperty AllowDragDropProperty = Render(nameof(AllowDragDrop), true);
    public static readonly BindableProperty IsReadOnlyProperty = Render(nameof(IsReadOnly), false);
    public static readonly BindableProperty AllowColumnCollapseProperty = Render(nameof(AllowColumnCollapse), true);
    public static readonly BindableProperty AllowSwimlaneCollapseProperty = Render(nameof(AllowSwimlaneCollapse), true);
    public static readonly BindableProperty AddCardModeProperty = Render(nameof(AddCardMode), KanbanAddCardMode.None);

    /// <summary>Whether cards are grouped into swimlane bands. Defaults to <see cref="KanbanSwimlaneMode.None"/>.</summary>
    public KanbanSwimlaneMode SwimlaneMode
    {
        get => (KanbanSwimlaneMode)this.GetValue(SwimlaneModeProperty);
        set => this.SetValue(SwimlaneModeProperty, value);
    }

    /// <summary>
    /// What a full column does to an incoming card. <see cref="KanbanWipBehavior.Warn"/> - the
    /// default - styles the column and lets the drop land; <see cref="KanbanWipBehavior.Block"/>
    /// refuses it and raises <see cref="DropRejected"/>.
    /// </summary>
    public KanbanWipBehavior WipBehavior
    {
        get => (KanbanWipBehavior)this.GetValue(WipBehaviorProperty);
        set => this.SetValue(WipBehaviorProperty, value);
    }

    /// <summary>Cards can be dragged. Defaults to true.</summary>
    public bool AllowDragDrop
    {
        get => (bool)this.GetValue(AllowDragDropProperty);
        set => this.SetValue(AllowDragDropProperty, value);
    }

    /// <summary>Nothing can be dragged, collapsed or added. Wins over every other permission.</summary>
    public bool IsReadOnly
    {
        get => (bool)this.GetValue(IsReadOnlyProperty);
        set => this.SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>Column headers get a collapse chevron. Defaults to true.</summary>
    public bool AllowColumnCollapse
    {
        get => (bool)this.GetValue(AllowColumnCollapseProperty);
        set => this.SetValue(AllowColumnCollapseProperty, value);
    }

    /// <summary>Swimlane headings get a collapse chevron. Defaults to true.</summary>
    public bool AllowSwimlaneCollapse
    {
        get => (bool)this.GetValue(AllowSwimlaneCollapseProperty);
        set => this.SetValue(AllowSwimlaneCollapseProperty, value);
    }

    /// <summary>
    /// The add-card affordance at the foot of each column. Defaults to
    /// <see cref="KanbanAddCardMode.None"/> - the board never invents a card of its own, so this
    /// only ever raises <see cref="AddCardRequested"/> for you to handle.
    /// </summary>
    public KanbanAddCardMode AddCardMode
    {
        get => (KanbanAddCardMode)this.GetValue(AddCardModeProperty);
        set => this.SetValue(AddCardModeProperty, value);
    }


    // =============================================================================================
    // Layout
    // =============================================================================================

    public static readonly BindableProperty ColumnWidthProperty = Render(nameof(ColumnWidth), 280d);
    public static readonly BindableProperty CollapsedColumnWidthProperty = Render(nameof(CollapsedColumnWidth), 52d);
    public static readonly BindableProperty ColumnSpacingProperty = Render(nameof(ColumnSpacing), 12d);
    public static readonly BindableProperty CardSpacingProperty = Render(nameof(CardSpacing), 8d);
    public static readonly BindableProperty ColumnPaddingProperty = Render(nameof(ColumnPadding), new Thickness(8));
    public static readonly BindableProperty MinColumnHeightProperty = Render(nameof(MinColumnHeight), 120d);

    /// <summary>Width of one column in device-independent units. Defaults to 280.</summary>
    public double ColumnWidth
    {
        get => (double)this.GetValue(ColumnWidthProperty);
        set => this.SetValue(ColumnWidthProperty, value);
    }

    /// <summary>Width of a collapsed column's spine. Defaults to 52.</summary>
    public double CollapsedColumnWidth
    {
        get => (double)this.GetValue(CollapsedColumnWidthProperty);
        set => this.SetValue(CollapsedColumnWidthProperty, value);
    }

    /// <summary>Gap between columns. Defaults to 12.</summary>
    public double ColumnSpacing
    {
        get => (double)this.GetValue(ColumnSpacingProperty);
        set => this.SetValue(ColumnSpacingProperty, value);
    }

    /// <summary>Gap between cards within a column. Defaults to 8.</summary>
    public double CardSpacing
    {
        get => (double)this.GetValue(CardSpacingProperty);
        set => this.SetValue(CardSpacingProperty, value);
    }

    /// <summary>Padding inside a column's card well. Defaults to 8 all round.</summary>
    public Thickness ColumnPadding
    {
        get => (Thickness)this.GetValue(ColumnPaddingProperty);
        set => this.SetValue(ColumnPaddingProperty, value);
    }

    /// <summary>
    /// How tall an empty column stays. Defaults to 120 - a well with no height is a drop target the
    /// user cannot hit.
    /// </summary>
    public double MinColumnHeight
    {
        get => (double)this.GetValue(MinColumnHeightProperty);
        set => this.SetValue(MinColumnHeightProperty, value);
    }


    // =============================================================================================
    // Display
    // =============================================================================================

    public static readonly BindableProperty ShowWipLimitsProperty = Render(nameof(ShowWipLimits), true);
    public static readonly BindableProperty ColumnCountDisplayProperty = Render(nameof(ColumnCountDisplay), KanbanColumnCount.CountAndLimit);
    public static readonly BindableProperty ShowLabelsProperty = Render(nameof(ShowLabels), true);
    public static readonly BindableProperty ShowAssigneeProperty = Render(nameof(ShowAssignee), true);
    public static readonly BindableProperty ShowDueDateProperty = Render(nameof(ShowDueDate), true);
    public static readonly BindableProperty DescriptionLineLimitProperty = Render(nameof(DescriptionLineLimit), 2);
    public static readonly BindableProperty DueSoonWindowProperty = Render(nameof(DueSoonWindow), TimeSpan.FromDays(2));
    public static readonly BindableProperty DueDateFormatProperty = Render<string>(nameof(DueDateFormat), "d MMM");
    public static readonly BindableProperty EmptyColumnTextProperty = Render<string>(nameof(EmptyColumnText), "Nothing here");

    /// <summary>
    /// An over-limit column is styled as over-limit - a tinted header and its count in the error
    /// colour. Independent of <see cref="WipBehavior"/>, which decides whether the drop lands.
    /// </summary>
    public bool ShowWipLimits
    {
        get => (bool)this.GetValue(ShowWipLimitsProperty);
        set => this.SetValue(ShowWipLimitsProperty, value);
    }

    /// <summary>What the column header shows beside its title. Defaults to <c>3 / 5</c>.</summary>
    public KanbanColumnCount ColumnCountDisplay
    {
        get => (KanbanColumnCount)this.GetValue(ColumnCountDisplayProperty);
        set => this.SetValue(ColumnCountDisplayProperty, value);
    }

    /// <summary>Label chips are drawn across the top of the default card. Defaults to true.</summary>
    public bool ShowLabels
    {
        get => (bool)this.GetValue(ShowLabelsProperty);
        set => this.SetValue(ShowLabelsProperty, value);
    }

    /// <summary>The assignee avatar and name are drawn in the default card's footer. Defaults to true.</summary>
    public bool ShowAssignee
    {
        get => (bool)this.GetValue(ShowAssigneeProperty);
        set => this.SetValue(ShowAssigneeProperty, value);
    }

    /// <summary>The due date is drawn in the default card's footer. Defaults to true.</summary>
    public bool ShowDueDate
    {
        get => (bool)this.GetValue(ShowDueDateProperty);
        set => this.SetValue(ShowDueDateProperty, value);
    }

    /// <summary>How many lines of <see cref="KanbanCard.Description"/> the default card shows. Zero hides it.</summary>
    public int DescriptionLineLimit
    {
        get => (int)this.GetValue(DescriptionLineLimitProperty);
        set => this.SetValue(DescriptionLineLimitProperty, value);
    }

    /// <summary>How far ahead a due date counts as "due soon" and is tinted. Defaults to two days.</summary>
    public TimeSpan DueSoonWindow
    {
        get => (TimeSpan)this.GetValue(DueSoonWindowProperty);
        set => this.SetValue(DueSoonWindowProperty, value);
    }

    /// <summary>Format string for the footer's due date. Defaults to <c>d MMM</c>.</summary>
    public string DueDateFormat
    {
        get => (string)this.GetValue(DueDateFormatProperty);
        set => this.SetValue(DueDateFormatProperty, value);
    }

    /// <summary>Placeholder in an empty column. Set to an empty string for a bare well.</summary>
    public string EmptyColumnText
    {
        get => (string)this.GetValue(EmptyColumnTextProperty);
        set => this.SetValue(EmptyColumnTextProperty, value);
    }

    public static readonly BindableProperty CultureProperty = Render<CultureInfo>(nameof(Culture));

    /// <summary>
    /// Culture for the due date and the column counts. Null takes
    /// <see cref="CultureInfo.CurrentCulture"/> at render time.
    /// </summary>
    public CultureInfo? Culture
    {
        get => (CultureInfo?)this.GetValue(CultureProperty);
        set => this.SetValue(CultureProperty, value);
    }


    // =============================================================================================
    // Templates
    // =============================================================================================

    public static readonly BindableProperty CardTemplateProperty = Render<DataTemplate>(nameof(CardTemplate));
    public static readonly BindableProperty ColumnHeaderTemplateProperty = Render<DataTemplate>(nameof(ColumnHeaderTemplate));
    public static readonly BindableProperty SwimlaneHeaderTemplateProperty = Render<DataTemplate>(nameof(SwimlaneHeaderTemplate));
    public static readonly BindableProperty EmptyColumnTemplateProperty = Render<DataTemplate>(nameof(EmptyColumnTemplate));

    /// <summary>
    /// Replaces the whole card. The template's binding context is the <see cref="KanbanCard"/>.
    /// </summary>
    /// <remarks>
    /// The drag still belongs to the board: it wraps whatever the template produces, so a custom
    /// card neither needs nor gets its own gesture. A card that puts its own tap handler on a
    /// button inside itself is fine - the drag only starts once the finger has moved.
    /// </remarks>
    public DataTemplate? CardTemplate
    {
        get => (DataTemplate?)this.GetValue(CardTemplateProperty);
        set => this.SetValue(CardTemplateProperty, value);
    }

    /// <summary>Replaces the column header. Binding context is the <see cref="KanbanColumn"/>.</summary>
    public DataTemplate? ColumnHeaderTemplate
    {
        get => (DataTemplate?)this.GetValue(ColumnHeaderTemplateProperty);
        set => this.SetValue(ColumnHeaderTemplateProperty, value);
    }

    /// <summary>Replaces the swimlane heading. Binding context is the <see cref="KanbanSwimlane"/>.</summary>
    public DataTemplate? SwimlaneHeaderTemplate
    {
        get => (DataTemplate?)this.GetValue(SwimlaneHeaderTemplateProperty);
        set => this.SetValue(SwimlaneHeaderTemplateProperty, value);
    }

    /// <summary>Replaces the placeholder in an empty column. Binding context is the <see cref="KanbanColumn"/>.</summary>
    public DataTemplate? EmptyColumnTemplate
    {
        get => (DataTemplate?)this.GetValue(EmptyColumnTemplateProperty);
        set => this.SetValue(EmptyColumnTemplateProperty, value);
    }


    // =============================================================================================
    // Selection and commands
    // =============================================================================================

    public static readonly BindableProperty SelectedCardProperty = BindableProperty.Create(
        nameof(SelectedCard), typeof(KanbanCard), typeof(KanbanView), null,
        defaultBindingMode: BindingMode.TwoWay,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(KanbanView), () => ((KanbanView)b).ApplySelection()));

    public static readonly BindableProperty CardTappedCommandProperty = Inert<ICommand>(nameof(CardTappedCommand));
    public static readonly BindableProperty CardMovedCommandProperty = Inert<ICommand>(nameof(CardMovedCommand));
    public static readonly BindableProperty AddCardCommandProperty = Inert<ICommand>(nameof(AddCardCommand));

    /// <summary>The selected card, or null. Two-way by default.</summary>
    public KanbanCard? SelectedCard
    {
        get => (KanbanCard?)this.GetValue(SelectedCardProperty);
        set => this.SetValue(SelectedCardProperty, value);
    }

    /// <summary>Invoked with the <see cref="KanbanCard"/> when one is tapped.</summary>
    public ICommand? CardTappedCommand
    {
        get => (ICommand?)this.GetValue(CardTappedCommandProperty);
        set => this.SetValue(CardTappedCommandProperty, value);
    }

    /// <summary>Invoked with the <see cref="KanbanMove"/> after a card has been moved.</summary>
    public ICommand? CardMovedCommand
    {
        get => (ICommand?)this.GetValue(CardMovedCommandProperty);
        set => this.SetValue(CardMovedCommandProperty, value);
    }

    /// <summary>Invoked with the <see cref="KanbanAddCardEventArgs"/> when a new card is asked for.</summary>
    public ICommand? AddCardCommand
    {
        get => (ICommand?)this.GetValue(AddCardCommandProperty);
        set => this.SetValue(AddCardCommandProperty, value);
    }


    // =============================================================================================
    // Events
    // =============================================================================================

    /// <summary>Raised before a card moves. Cancel to refuse the drop.</summary>
    public event EventHandler<KanbanCardMovingEventArgs>? CardMoving;

    /// <summary>Raised after a card has moved and the model has been rewritten.</summary>
    public event EventHandler<KanbanCardMovedEventArgs>? CardMoved;

    /// <summary>Raised when a drop is refused - by a WIP limit, a locked card, or a closed column.</summary>
    public event EventHandler<KanbanDropRejectedEventArgs>? DropRejected;

    /// <summary>Raised when a card is tapped.</summary>
    public event EventHandler<KanbanCardEventArgs>? CardTapped;

    /// <summary>Raised when a column is collapsed or expanded.</summary>
    public event EventHandler<KanbanColumnEventArgs>? ColumnCollapseChanged;

    /// <summary>Raised when the user asks for a new card. The board does not create one.</summary>
    public event EventHandler<KanbanAddCardEventArgs>? AddCardRequested;

    /// <summary>Raised after the board has been re-bucketed, before it is rendered.</summary>
    public event EventHandler<KanbanBoardBuiltEventArgs>? BoardBuilt;
}
