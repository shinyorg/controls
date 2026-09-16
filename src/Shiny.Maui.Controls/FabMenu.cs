using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

[ContentProperty(nameof(Items))]
public class FabMenu : ContentView
{
    const uint DefaultAnimationDuration = 200;
    const double ItemStaggerMs = 35;
    const double ItemTravelDistance = 12;
    const double ItemCollapsedScale = 0.85;
    const double DefaultIconRotation = 45;

    readonly Grid rootGrid;
    readonly BoxView backdrop;
    readonly VerticalStackLayout stack;
    readonly VerticalStackLayout itemsLayout;
    readonly Fab mainFab;
    readonly TapGestureRecognizer backdropTap;

    bool isAnimating;


    public FabMenu()
    {
        backdrop = new BoxView
        {
            Opacity = 0,
            IsVisible = false
        };
        backdropTap = new TapGestureRecognizer();
        backdropTap.Tapped += OnBackdropTapped;
        backdrop.GestureRecognizers.Add(backdropTap);

        itemsLayout = new VerticalStackLayout
        {
            Spacing = 10,
            HorizontalOptions = LayoutOptions.End
        };

        mainFab = new Fab();
        mainFab.Clicked += OnMainFabClicked;

        stack = new VerticalStackLayout
        {
            Spacing = 14,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.End
        };
        stack.Add(itemsLayout);
        stack.Add(mainFab);

        rootGrid = new Grid();
        rootGrid.Children.Add(backdrop);
        rootGrid.Children.Add(stack);

        Content = rootGrid;
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        InputTransparent = false;

        // Assign Items last — the ItemsProperty change handler calls RebuildItemsLayout(),
        // which requires itemsLayout to already be constructed.
        Items = new ObservableCollection<FabMenuItem>();

        ApplyBackdropColor();
        ApplyMenuAlignment();

        // Last line: replays any callback that fired before the children existed.
        StyleGuard.MarkReady(this, typeof(FabMenu));
    }




    /// <summary>Explicit backdrop colour when set, otherwise the theme scrim.</summary>
    void ApplyBackdropColor()
    {
        if (this.BackdropColor is Color c)
            backdrop.Color = c;
        else
            backdrop.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.Scrim);
    }




    // ------- Items / ItemsSource -------

    public static readonly BindableProperty ItemsProperty = BindableProperty.Create(
        nameof(Items),
        typeof(IList<FabMenuItem>),
        typeof(FabMenu),
        null,
        propertyChanged: (b, o, n) =>
        {
            // The collection subscription has to happen whenever the property changes -
            // it touches no children, so it is not gated. Only the rebuild is.
            var menu = (FabMenu)b;
            // Weak: a bound Items collection can outlive the page.
            menu.itemsSubscription?.Dispose();
            menu.itemsSubscription = WeakEventSubscription.CollectionChanged(n, menu.OnItemsCollectionChanged);

            StyleGuard.WhenReady<FabMenu>(b, m => m.RebuildItemsLayout());
        });
    IDisposable? itemsSubscription;

    public IList<FabMenuItem> Items
    {
        get => (IList<FabMenuItem>)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }


    // ------- IsOpen -------

    public static readonly BindableProperty IsOpenProperty = BindableProperty.Create(
        nameof(IsOpen),
        typeof(bool),
        typeof(FabMenu),
        false,
        defaultBindingMode: BindingMode.TwoWay,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<FabMenu>(b, menu => _ = menu.AnimateToStateAsync((bool)n)));
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }


    // ------- Main Fab pass-throughs -------

    public static readonly BindableProperty IconProperty = BindableProperty.Create(
        nameof(Icon),
        typeof(ImageSource),
        typeof(FabMenu),
        null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<FabMenu>(b, menu => menu.mainFab.Icon = n as ImageSource));
    public ImageSource? Icon
    {
        get => (ImageSource?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text),
        typeof(string),
        typeof(FabMenu),
        null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<FabMenu>(b, menu => menu.mainFab.Text = n as string));
    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly BindableProperty FabBackgroundColorProperty = BindableProperty.Create(
        nameof(FabBackgroundColor),
        typeof(Color),
        typeof(FabMenu),
        null,
        // Forward to the inner Fab; null lets the Fab fall back to its own theme default (Primary).
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<FabMenu>(b, menu => menu.mainFab.FabBackgroundColor = n as Color));
    public Color? FabBackgroundColor
    {
        get => (Color?)GetValue(FabBackgroundColorProperty);
        set => SetValue(FabBackgroundColorProperty, value);
    }

    public static readonly BindableProperty BorderColorProperty = BindableProperty.Create(
        nameof(BorderColor),
        typeof(Color),
        typeof(FabMenu),
        null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<FabMenu>(b, menu => { if (n is Color c) menu.mainFab.BorderColor = c; }));
    public Color? BorderColor
    {
        get => (Color?)GetValue(BorderColorProperty);
        set => SetValue(BorderColorProperty, value);
    }

    public static readonly BindableProperty BorderThicknessProperty = BindableProperty.Create(
        nameof(BorderThickness),
        typeof(double),
        typeof(FabMenu),
        0.0,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<FabMenu>(b, menu => menu.mainFab.BorderThickness = (double)n));
    public double BorderThickness
    {
        get => (double)GetValue(BorderThicknessProperty);
        set => SetValue(BorderThicknessProperty, value);
    }

    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor),
        typeof(Color),
        typeof(FabMenu),
        null,
        // Forward to the inner Fab; null lets the Fab fall back to its own theme default (OnPrimary).
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<FabMenu>(b, menu => menu.mainFab.TextColor = n as Color));
    public Color? TextColor
    {
        get => (Color?)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }


    // ------- Behaviour -------

    public static readonly BindableProperty HasBackdropProperty = BindableProperty.Create(
        nameof(HasBackdrop),
        typeof(bool),
        typeof(FabMenu),
        true);
    public bool HasBackdrop
    {
        get => (bool)GetValue(HasBackdropProperty);
        set => SetValue(HasBackdropProperty, value);
    }

    public static readonly BindableProperty CloseOnBackdropTapProperty = BindableProperty.Create(
        nameof(CloseOnBackdropTap),
        typeof(bool),
        typeof(FabMenu),
        true);
    public bool CloseOnBackdropTap
    {
        get => (bool)GetValue(CloseOnBackdropTapProperty);
        set => SetValue(CloseOnBackdropTapProperty, value);
    }

    public static readonly BindableProperty CloseOnItemTapProperty = BindableProperty.Create(
        nameof(CloseOnItemTap),
        typeof(bool),
        typeof(FabMenu),
        true);
    public bool CloseOnItemTap
    {
        get => (bool)GetValue(CloseOnItemTapProperty);
        set => SetValue(CloseOnItemTapProperty, value);
    }

    public static readonly BindableProperty BackdropColorProperty = BindableProperty.Create(
        nameof(BackdropColor),
        typeof(Color),
        typeof(FabMenu),
        null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<FabMenu>(b, menu => menu.ApplyBackdropColor()));
    public Color? BackdropColor
    {
        get => (Color?)GetValue(BackdropColorProperty);
        set => SetValue(BackdropColorProperty, value);
    }

    public static readonly BindableProperty BackdropOpacityProperty = BindableProperty.Create(
        nameof(BackdropOpacity),
        typeof(double),
        typeof(FabMenu),
        0.4);
    public double BackdropOpacity
    {
        get => (double)GetValue(BackdropOpacityProperty);
        set => SetValue(BackdropOpacityProperty, value);
    }

    public static readonly BindableProperty AnimationDurationProperty = BindableProperty.Create(
        nameof(AnimationDuration),
        typeof(uint),
        typeof(FabMenu),
        DefaultAnimationDuration);
    public uint AnimationDuration
    {
        get => (uint)GetValue(AnimationDurationProperty);
        set => SetValue(AnimationDurationProperty, value);
    }


    public static readonly BindableProperty UseFeedbackProperty = BindableProperty.Create(
        nameof(UseFeedback),
        typeof(bool),
        typeof(FabMenu),
        true);
    public bool UseFeedback
    {
        get => (bool)GetValue(UseFeedbackProperty);
        set => SetValue(UseFeedbackProperty, value);
    }


    // ------- Fab Size / Appearance -------

    public static readonly BindableProperty FabSizeProperty = BindableProperty.Create(
        nameof(FabSize),
        typeof(double),
        typeof(FabMenu),
        56.0,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<FabMenu>(b, menu =>
        {
            menu.mainFab.Size = (double)n;
            // The items inset themselves against the main FAB's width - re-run that math.
            menu.ApplyItemAxis();
        }));
    public double FabSize
    {
        get => (double)GetValue(FabSizeProperty);
        set => SetValue(FabSizeProperty, value);
    }

    public static readonly BindableProperty HasShadowProperty = BindableProperty.Create(
        nameof(HasShadow),
        typeof(bool),
        typeof(FabMenu),
        true,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<FabMenu>(b, menu => menu.mainFab.HasShadow = (bool)n));
    public bool HasShadow
    {
        get => (bool)GetValue(HasShadowProperty);
        set => SetValue(HasShadowProperty, value);
    }

    public static readonly BindableProperty MenuAlignmentProperty = BindableProperty.Create(
        nameof(MenuAlignment),
        typeof(LayoutOptions),
        typeof(FabMenu),
        LayoutOptions.End,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<FabMenu>(b, menu => menu.ApplyMenuAlignment()));
    public LayoutOptions MenuAlignment
    {
        get => (LayoutOptions)GetValue(MenuAlignmentProperty);
        set => SetValue(MenuAlignmentProperty, value);
    }

    /// <summary>
    /// Degrees the main FAB rotates while the menu is open - the classic "+" turning into an "×".
    /// Set to 0 to disable. Ignored when the main FAB has <see cref="Text"/>, since a rotated label
    /// reads as broken rather than deliberate.
    /// </summary>
    public static readonly BindableProperty IconRotationProperty = BindableProperty.Create(
        nameof(IconRotation),
        typeof(double),
        typeof(FabMenu),
        DefaultIconRotation);
    public double IconRotation
    {
        get => (double)GetValue(IconRotationProperty);
        set => SetValue(IconRotationProperty, value);
    }


    public event EventHandler<FabMenuItem>? ItemTapped;


    public void Open() => IsOpen = true;
    public void Close() => IsOpen = false;
    public void Toggle() => IsOpen = !IsOpen;


    // ------- Internals -------

    void ApplyMenuAlignment()
    {
        stack.HorizontalOptions = MenuAlignment;
        itemsLayout.HorizontalOptions = MenuAlignment;
        mainFab.HorizontalOptions = MenuAlignment;
        ApplyItemAxis();
    }

    /// <summary>Keeps every item's icon chip centred on the main FAB's vertical axis.</summary>
    void ApplyItemAxis()
    {
        var leading = MenuAlignment.Alignment == LayoutAlignment.Start;
        foreach (var item in itemsLayout.Children.OfType<FabMenuItem>())
            item.ApplyAxis(FabSize, leading);
    }

    void OnMainFabClicked(object? sender, EventArgs e)
    {
        if (UseFeedback)
            FeedbackHelper.Execute(this, "Toggled");
        Toggle();
    }

    void OnBackdropTapped(object? sender, TappedEventArgs e)
    {
        if (CloseOnBackdropTap)
            Close();
    }

    void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RebuildItemsLayout();

    void RebuildItemsLayout()
    {
        // detach old handlers
        foreach (var child in itemsLayout.Children.OfType<FabMenuItem>())
            child.Clicked -= OnMenuItemClicked;

        itemsLayout.Clear();

        if (Items is null)
            return;

        var leading = MenuAlignment.Alignment == LayoutAlignment.Start;

        foreach (var item in Items)
        {
            item.Clicked -= OnMenuItemClicked;
            item.Clicked += OnMenuItemClicked;

            item.ApplyAxis(FabSize, leading);

            // Prime initial state based on IsOpen
            item.Opacity = IsOpen ? 1 : 0;
            item.TranslationY = IsOpen ? 0 : ItemTravelDistance;
            item.Scale = IsOpen ? 1 : ItemCollapsedScale;
            item.IsVisible = IsOpen;

            itemsLayout.Add(item);
        }
    }

    void OnMenuItemClicked(object? sender, EventArgs e)
    {
        if (sender is FabMenuItem item)
        {
            ItemTapped?.Invoke(this, item);
            if (CloseOnItemTap)
                Close();
        }
    }


    async Task AnimateToStateAsync(bool open)
    {
        if (isAnimating)
            return;
        isAnimating = true;
        try
        {
            var duration = AnimationDuration;
            var items = itemsLayout.Children.OfType<FabMenuItem>().ToList();
            var rotation = RotateMainFab() ? IconRotation : 0;

            if (open)
            {
                // show backdrop
                if (HasBackdrop)
                {
                    backdrop.IsVisible = true;
                    backdrop.Opacity = 0;
                }

                // prime items
                foreach (var item in items)
                {
                    item.IsVisible = true;
                    item.Opacity = 0;
                    item.TranslationY = ItemTravelDistance;
                    item.Scale = ItemCollapsedScale;
                }

                var tasks = new List<Task>();
                if (HasBackdrop)
                    tasks.Add(backdrop.FadeToAsync(BackdropOpacity, duration, Easing.CubicOut));
                if (rotation != 0)
                    tasks.Add(mainFab.RotateToAsync(rotation, duration, Easing.CubicInOut));

                // Stagger from bottom to top (closest to the main Fab animates first)
                for (var i = items.Count - 1; i >= 0; i--)
                {
                    var item = items[i];
                    var delay = (items.Count - 1 - i) * ItemStaggerMs;
                    tasks.Add(AnimateItemAsync(item, 1, 0, 1, duration, delay, Easing.CubicOut));
                }

                await Task.WhenAll(tasks);
            }
            else
            {
                var tasks = new List<Task>();
                if (HasBackdrop)
                    tasks.Add(backdrop.FadeToAsync(0, duration, Easing.CubicIn));
                if (mainFab.Rotation != 0)
                    tasks.Add(mainFab.RotateToAsync(0, duration, Easing.CubicInOut));

                // Animate top to bottom on close
                for (var i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    var delay = i * ItemStaggerMs;
                    tasks.Add(AnimateItemAsync(item, 0, ItemTravelDistance, ItemCollapsedScale, duration, delay, Easing.CubicIn));
                }

                await Task.WhenAll(tasks);

                foreach (var item in items)
                    item.IsVisible = false;

                if (HasBackdrop)
                    backdrop.IsVisible = false;
            }
        }
        finally
        {
            isAnimating = false;
        }
    }

    /// <summary>A rotated label reads as broken, so the spin is icon-only-FAB territory.</summary>
    bool RotateMainFab()
        => IconRotation != 0 && string.IsNullOrEmpty(Text);

    static async Task AnimateItemAsync(View item, double targetOpacity, double targetTranslationY, double targetScale, uint duration, double delayMs, Easing easing)
    {
        if (delayMs > 0)
            await Task.Delay((int)delayMs);

        await Task.WhenAll(
            item.FadeToAsync(targetOpacity, duration, easing),
            item.TranslateToAsync(0, targetTranslationY, duration, easing),
            item.ScaleToAsync(targetScale, duration, easing)
        );
    }
}
