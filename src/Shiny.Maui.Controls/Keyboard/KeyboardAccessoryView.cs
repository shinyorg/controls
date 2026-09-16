using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

/// <summary>
/// A bar docked to the top edge of the soft keyboard while a <see cref="TextEntry"/> has focus.
///
/// <para>
/// On iOS this is the real <c>UIResponder.InputAccessoryView</c>, so it rides the keyboard exactly.
/// Android has no such API — the IME is a different process — so the same bar is rendered in the
/// activity's own content view and driven by the IME window insets. Every other head has no soft
/// keyboard, and the bar is never shown.
/// </para>
/// </summary>
[ContentProperty(nameof(Items))]
public class KeyboardAccessoryView : ContentView
{
    const double DefaultBarHeight = 44;

    readonly Grid root;
    readonly Grid itemsGrid;
    readonly BoxView topLine;

    IDisposable? itemsSubscription;

    public KeyboardAccessoryView()
    {
        topLine = new BoxView { HeightRequest = 1, VerticalOptions = LayoutOptions.Start };
        topLine.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.OutlineVariant);

        itemsGrid = new Grid { ColumnSpacing = 0, VerticalOptions = LayoutOptions.Fill };

        root = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) }
        };
        root.Add(topLine, 0, 0);
        root.Add(itemsGrid, 0, 1);

        Content = root;
        HeightRequest = DefaultBarHeight;
        SetDynamicResource(BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHigh);

        Items = new ObservableCollection<View>();

        StyleGuard.MarkReady(this, typeof(KeyboardAccessoryView));
    }

    /// <summary>
    /// The items on the bar, left to right. A <see cref="KeyboardAccessorySpacer"/> takes whatever
    /// room is left over, which is how you push items apart.
    /// </summary>
    public static readonly BindableProperty ItemsProperty = BindableProperty.Create(
        nameof(Items), typeof(IList<View>), typeof(KeyboardAccessoryView), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(KeyboardAccessoryView), () =>
        {
            ((KeyboardAccessoryView)b).OnItemsChanged(o as IList<View>, n as IList<View>);
        }));
    public IList<View>? Items
    {
        get => (IList<View>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    /// <summary>
    /// Replaces the whole item row with your own layout. Set this instead of <see cref="Items"/>
    /// when the bar needs to be something other than a row of buttons.
    /// </summary>
    public static readonly BindableProperty BarContentProperty = BindableProperty.Create(
        nameof(BarContent), typeof(View), typeof(KeyboardAccessoryView), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(KeyboardAccessoryView), () =>
        {
            ((KeyboardAccessoryView)b).RebuildItems();
        }));
    public View? BarContent
    {
        get => (View?)GetValue(BarContentProperty);
        set => SetValue(BarContentProperty, value);
    }

    public static readonly BindableProperty BarHeightProperty = BindableProperty.Create(
        nameof(BarHeight), typeof(double), typeof(KeyboardAccessoryView), DefaultBarHeight,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(KeyboardAccessoryView), () =>
        {
            ((KeyboardAccessoryView)b).HeightRequest = (double)n;
        }));
    public double BarHeight
    {
        get => (double)GetValue(BarHeightProperty);
        set => SetValue(BarHeightProperty, value);
    }

    public static readonly BindableProperty BarBackgroundColorProperty = BindableProperty.Create(
        nameof(BarBackgroundColor), typeof(Color), typeof(KeyboardAccessoryView), null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(KeyboardAccessoryView), () =>
        {
            var bar = (KeyboardAccessoryView)b;
            if (n is Color c)
                bar.BackgroundColor = c;
            else
                bar.SetDynamicResource(BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHigh);
        }));
    public Color? BarBackgroundColor
    {
        get => (Color?)GetValue(BarBackgroundColorProperty);
        set => SetValue(BarBackgroundColorProperty, value);
    }

    public static readonly BindableProperty BarBorderColorProperty = BindableProperty.Create(
        nameof(BarBorderColor), typeof(Color), typeof(KeyboardAccessoryView), null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(KeyboardAccessoryView), () =>
        {
            var bar = (KeyboardAccessoryView)b;
            if (n is Color c)
                bar.topLine.Color = c;
            else
                bar.topLine.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.OutlineVariant);
        }));
    public Color? BarBorderColor
    {
        get => (Color?)GetValue(BarBorderColorProperty);
        set => SetValue(BarBorderColorProperty, value);
    }

    public static readonly BindableProperty ItemSpacingProperty = BindableProperty.Create(
        nameof(ItemSpacing), typeof(double), typeof(KeyboardAccessoryView), 4.0,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(KeyboardAccessoryView), () =>
        {
            ((KeyboardAccessoryView)b).itemsGrid.ColumnSpacing = (double)n;
        }));
    public double ItemSpacing
    {
        get => (double)GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    /// <summary>
    /// The field the bar is serving right now — a <see cref="TextEntry"/>, an
    /// <see cref="Cells.EntryCell"/>, or anything else implementing
    /// <see cref="IKeyboardAccessoryHost"/>. Null while nothing is focused.
    /// </summary>
    public IKeyboardAccessoryHost? CurrentOwner { get; private set; }

    internal void DetachHost(IKeyboardAccessoryHost host)
    {
        if (ReferenceEquals(CurrentOwner, host))
            SetCurrentOwner(null);
    }

    internal void NotifyFocusChanged(IKeyboardAccessoryHost host, bool focused)
    {
        if (focused)
            SetCurrentOwner(host);
        else if (ReferenceEquals(CurrentOwner, host))
            SetCurrentOwner(null);
    }

    void SetCurrentOwner(IKeyboardAccessoryHost? host)
    {
        CurrentOwner = host;
        foreach (var item in EnumerateItems().OfType<KeyboardAccessoryItem>())
            item.OnOwnerChanged(host);
    }

    // Items set through BarContent are nested inside whatever layout the caller built, so the flat
    // Items list is not enough to find them - and an item that is never found is an item whose Owner
    // stays null, which reads as a dead Done button.
    IEnumerable<View> EnumerateItems()
        => BarContent is View custom
            ? Flatten(custom)
            : Items ?? Enumerable.Empty<View>();

    static IEnumerable<View> Flatten(View view)
    {
        yield return view;

        IEnumerable<View> children = view switch
        {
            // A KeyboardAccessoryItem is itself a ContentView, so stop at one rather than walking the
            // glyph and label it is built from.
            KeyboardAccessoryItem => Enumerable.Empty<View>(),
            Layout layout => layout.Children.OfType<View>(),
            ScrollView { Content: View scrolled } => [scrolled],
            ContentView { Content: View content } => [content],
            Border { Content: View bordered } => [bordered],
            _ => Enumerable.Empty<View>()
        };

        foreach (var child in children)
        {
            foreach (var descendant in Flatten(child))
                yield return descendant;
        }
    }

    void OnItemsChanged(IList<View>? oldItems, IList<View>? newItems)
    {
        itemsSubscription?.Dispose();

        RebuildItems();

        // Weak: a bound Items collection can outlive the page.
        itemsSubscription = WeakEventSubscription.CollectionChanged(newItems, (_, _) => RebuildItems());
    }

    // A spacer takes a Star column and everything else takes Auto - that is the whole layout.
    void RebuildItems()
    {
        itemsGrid.Children.Clear();
        itemsGrid.ColumnDefinitions.Clear();

        if (BarContent is View custom)
        {
            itemsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            itemsGrid.Add(custom, 0, 0);

            foreach (var nested in Flatten(custom).OfType<KeyboardAccessoryItem>())
            {
                nested.Bar = this;
                nested.OnOwnerChanged(CurrentOwner);
            }
            return;
        }

        var column = 0;
        foreach (var item in EnumerateItems())
        {
            var width = item is KeyboardAccessorySpacer ? GridLength.Star : GridLength.Auto;
            itemsGrid.ColumnDefinitions.Add(new ColumnDefinition(width));

            if (item is KeyboardAccessoryItem accessoryItem)
            {
                accessoryItem.Bar = this;
                accessoryItem.OnOwnerChanged(CurrentOwner);
            }

            itemsGrid.Add(item, column, 0);
            column++;
        }
    }

    /// <summary>Builds one of the stock bars.</summary>
    public static KeyboardAccessoryView FromPreset(KeyboardAccessoryPreset preset)
    {
        var bar = new KeyboardAccessoryView();
        var items = bar.Items!;

        switch (preset)
        {
            case KeyboardAccessoryPreset.Done:
                items.Add(new KeyboardAccessorySpacer());
                items.Add(new KeyboardDismissItem());
                break;

            case KeyboardAccessoryPreset.Navigation:
                items.Add(new KeyboardNavigationItem { Direction = KeyboardNavigationDirection.Previous });
                items.Add(new KeyboardNavigationItem { Direction = KeyboardNavigationDirection.Next });
                items.Add(new KeyboardAccessorySpacer());
                break;

            case KeyboardAccessoryPreset.NavigationAndDone:
                items.Add(new KeyboardNavigationItem { Direction = KeyboardNavigationDirection.Previous });
                items.Add(new KeyboardNavigationItem { Direction = KeyboardNavigationDirection.Next });
                items.Add(new KeyboardAccessorySpacer());
                items.Add(new KeyboardDismissItem());
                break;
        }

        return bar;
    }
}
