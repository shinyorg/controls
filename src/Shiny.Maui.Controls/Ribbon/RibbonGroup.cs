using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;

namespace Shiny.Maui.Controls.Ribbons;

/// <summary>
/// A titled box of related commands inside a <see cref="RibbonTab"/> — Clipboard, Font, Paragraph.
/// </summary>
/// <example>
/// <code language="xaml">
/// &lt;shiny:RibbonGroup Title="Font" ShowDialogLauncher="True" DialogLauncherCommand="{Binding OpenFontDialog}"&gt;
///     &lt;shiny:RibbonToggleButton Text="Bold" Size="Small" IsChecked="{Binding Bold}" /&gt;
///     &lt;shiny:RibbonToggleButton Text="Italic" Size="Small" IsChecked="{Binding Italic}" /&gt;
/// &lt;/shiny:RibbonGroup&gt;
/// </code>
/// </example>
/// <remarks>
/// The group is the unit the ribbon gives up when it runs out of room: a group that does not fit
/// collapses to a single button that opens the whole group in a popup, worst <see cref="Priority"/>
/// first. Items are never dropped individually, because half a group is worse than a closed one.
/// </remarks>
[ContentProperty(nameof(Items))]
public class RibbonGroup : BindableObject
{
    readonly ObservableCollection<RibbonItem> items = new();

    public RibbonGroup() => this.items.CollectionChanged += this.OnItemsChanged;

    /// <summary>Raised when anything the ribbon draws this group from changes, its items included.</summary>
    internal event EventHandler? Changed;

    void RaiseChanged() => this.Changed?.Invoke(this, EventArgs.Empty);

    static BindableProperty Redraw(string name, Type returnType, object? defaultValue = null)
        => BindableProperty.Create(
            name, returnType, typeof(RibbonGroup), defaultValue,
            propertyChanged: (b, _, _) => ((RibbonGroup)b).RaiseChanged()
        );


    public static readonly BindableProperty TitleProperty = Redraw(nameof(Title), typeof(string));
    public static readonly BindableProperty IsVisibleProperty = Redraw(nameof(IsVisible), typeof(bool), true);
    public static readonly BindableProperty IsEnabledProperty = Redraw(nameof(IsEnabled), typeof(bool), true);
    public static readonly BindableProperty ShowDialogLauncherProperty = Redraw(nameof(ShowDialogLauncher), typeof(bool), false);
    public static readonly BindableProperty DialogLauncherCommandProperty = Redraw(nameof(DialogLauncherCommand), typeof(ICommand));
    public static readonly BindableProperty DialogLauncherTooltipProperty = Redraw(nameof(DialogLauncherTooltip), typeof(string));
    public static readonly BindableProperty CanCollapseProperty = Redraw(nameof(CanCollapse), typeof(bool), true);
    public static readonly BindableProperty PriorityProperty = Redraw(nameof(Priority), typeof(int), 0);
    public static readonly BindableProperty CollapsedIconProperty = Redraw(nameof(CollapsedIcon), typeof(ImageSource));
    public static readonly BindableProperty AutomationIdProperty = Redraw(nameof(AutomationId), typeof(string));


    /// <summary>Raised when the small arrow in the group's corner is pressed.</summary>
    public event EventHandler? DialogLauncherClicked;


    /// <summary>The caption under the group. Also the label when the group collapses to a button.</summary>
    public string? Title
    {
        get => (string?)this.GetValue(TitleProperty);
        set => this.SetValue(TitleProperty, value);
    }

    public bool IsVisible
    {
        get => (bool)this.GetValue(IsVisibleProperty);
        set => this.SetValue(IsVisibleProperty, value);
    }

    /// <summary>Dims and deadens every item in the group without each of them having to be bound.</summary>
    public bool IsEnabled
    {
        get => (bool)this.GetValue(IsEnabledProperty);
        set => this.SetValue(IsEnabledProperty, value);
    }

    /// <summary>
    /// Draws the small arrow in the group's bottom corner — the convention for "there is more of this
    /// than fits here", which opens the full dialog.
    /// </summary>
    public bool ShowDialogLauncher
    {
        get => (bool)this.GetValue(ShowDialogLauncherProperty);
        set => this.SetValue(ShowDialogLauncherProperty, value);
    }

    public ICommand? DialogLauncherCommand
    {
        get => (ICommand?)this.GetValue(DialogLauncherCommandProperty);
        set => this.SetValue(DialogLauncherCommandProperty, value);
    }

    /// <summary>Hover text for the launcher arrow. Falls back to "<c>{Title}</c> settings".</summary>
    public string? DialogLauncherTooltip
    {
        get => (string?)this.GetValue(DialogLauncherTooltipProperty);
        set => this.SetValue(DialogLauncherTooltipProperty, value);
    }

    /// <summary>
    /// Whether the group may collapse to a button when the ribbon runs out of room. Set false for a
    /// group that has to stay open — the one holding the control the whole tab is about.
    /// </summary>
    public bool CanCollapse
    {
        get => (bool)this.GetValue(CanCollapseProperty);
        set => this.SetValue(CanCollapseProperty, value);
    }

    /// <summary>
    /// Collapse order. Groups collapse lowest-priority first, so raise this on the ones that should
    /// survive longest. Ties break on position, rightmost first.
    /// </summary>
    public int Priority
    {
        get => (int)this.GetValue(PriorityProperty);
        set => this.SetValue(PriorityProperty, value);
    }

    /// <summary>
    /// The icon on the button this group collapses to. Falls back to the icon of the group's first
    /// visible item, so most groups never need to set it.
    /// </summary>
    public ImageSource? CollapsedIcon
    {
        get => (ImageSource?)this.GetValue(CollapsedIconProperty);
        set => this.SetValue(CollapsedIconProperty, value);
    }

    public string? AutomationId
    {
        get => (string?)this.GetValue(AutomationIdProperty);
        set => this.SetValue(AutomationIdProperty, value);
    }

    /// <summary>The commands in the group, in the order they are drawn.</summary>
    public IList<RibbonItem> Items => this.items;

    /// <summary>The items that are actually drawn.</summary>
    internal IReadOnlyList<RibbonItem> VisibleItems => this.items.Where(x => x.IsVisible).ToList();


    /// <summary>Presses the launcher arrow. The seam a test invokes through.</summary>
    public void InvokeDialogLauncher()
    {
        if (!this.IsEnabled)
            return;

        if (this.DialogLauncherCommand?.CanExecute(null) == true)
            this.DialogLauncherCommand.Execute(null);

        this.DialogLauncherClicked?.Invoke(this, EventArgs.Empty);
    }


    void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (RibbonItem item in e.OldItems)
                item.Changed -= this.OnItemChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (RibbonItem item in e.NewItems)
                item.Changed += this.OnItemChanged;
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var item in this.items)
            {
                item.Changed -= this.OnItemChanged;
                item.Changed += this.OnItemChanged;
            }
        }

        this.RaiseChanged();
    }

    void OnItemChanged(object? sender, EventArgs e) => this.RaiseChanged();


    /// <summary>
    /// Pushes a binding context down to the items and to any view they host.
    /// </summary>
    /// <remarks>
    /// Items are not in the visual tree, so nothing hands them one — a <c>{Binding}</c> on an item
    /// would silently resolve against null and the command would never fire. See the same call on
    /// <see cref="RibbonTab"/> and <see cref="Ribbon"/>, which is where it starts.
    /// </remarks>
    internal void ApplyBindingContext(object? context)
    {
        foreach (var item in this.items)
        {
            SetInheritedBindingContext(item, context);

            if (item is RibbonMenuButton menuButton)
            {
                foreach (var entry in menuButton.Menu)
                    ApplyToEntry(entry, context);
            }
        }
    }

    static void ApplyToEntry(RibbonMenuEntry entry, object? context)
    {
        SetInheritedBindingContext(entry, context);
        foreach (var child in entry.Children)
            ApplyToEntry(child, context);
    }
}
