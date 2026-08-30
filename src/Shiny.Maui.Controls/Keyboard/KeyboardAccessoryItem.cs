using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

/// <summary>
/// A tappable icon/label on a <see cref="KeyboardAccessoryView"/>. Same shape as
/// <see cref="TextEntryTool"/> — icon, optional text, command.
/// </summary>
public class KeyboardAccessoryItem : IconTextTool
{
    public KeyboardAccessoryItem()
    {
        Padding = new Thickness(12, 0);
        MinimumWidthRequest = 44;

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(KeyboardAccessoryItem));
    }

    /// <summary>The bar this item belongs to. Set when the bar builds its layout.</summary>
    internal KeyboardAccessoryView? Bar { get; set; }

    /// <summary>The field the bar is currently serving. Null while nothing is focused.</summary>
    protected IKeyboardAccessoryHost? Owner => Bar?.CurrentOwner;

    /// <summary>Called whenever the bar changes which field it serves, so items can re-evaluate.</summary>
    protected internal virtual void OnOwnerChanged(IKeyboardAccessoryHost? owner)
    {
    }
}

/// <summary>
/// Moves focus to the previous or next field on the page. Disables itself at the ends of the run.
/// </summary>
public class KeyboardNavigationItem : KeyboardAccessoryItem
{
    public KeyboardNavigationItem()
    {
        ApplyDefaultText();
        Clicked += OnClicked;

        StyleGuard.MarkReady(this, typeof(KeyboardNavigationItem));
    }

    public static readonly BindableProperty DirectionProperty = BindableProperty.Create(
        nameof(Direction), typeof(KeyboardNavigationDirection), typeof(KeyboardNavigationItem), KeyboardNavigationDirection.Next,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(KeyboardNavigationItem), () =>
        {
            var item = (KeyboardNavigationItem)b;
            item.ApplyDefaultText();
            item.SyncEnabled();
        }));
    public KeyboardNavigationDirection Direction
    {
        get => (KeyboardNavigationDirection)GetValue(DirectionProperty);
        set => SetValue(DirectionProperty, value);
    }

    bool textSetByUser;

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == nameof(Text))
            textSetByUser = true;
    }

    void ApplyDefaultText()
    {
        if (textSetByUser && !string.IsNullOrEmpty(Text))
            return;

        Text = Direction == KeyboardNavigationDirection.Previous ? "‹" : "›";
        this.SetDynamicResource(FontSizeProperty, Themes.ShinyThemeKeys.Type.TitleLargeSize);
        textSetByUser = false;
    }

    protected internal override void OnOwnerChanged(IKeyboardAccessoryHost? owner) => SyncEnabled();

    void SyncEnabled()
    {
        var element = Owner?.NavigationElement;
        IsEnabled = element is not null && KeyboardFieldNavigator.CanMove(element, Direction);
        Opacity = IsEnabled ? 1 : 0.35;
    }

    void OnClicked(object? sender, EventArgs e)
    {
        if (Owner?.NavigationElement is VisualElement element)
            KeyboardFieldNavigator.Move(element, Direction);
    }
}

/// <summary>
/// Dismisses the keyboard. The reason the bar exists on numeric fields — the iOS number pad has no
/// return key, so without this there is no way to put it away.
/// </summary>
public class KeyboardDismissItem : KeyboardAccessoryItem
{
    public KeyboardDismissItem()
    {
        if (string.IsNullOrEmpty(Text))
            Text = "Done";

        this.SetDynamicResource(FontSizeProperty, Themes.ShinyThemeKeys.Type.BodyLargeSize);
        SetDynamicResource(ToolColorProperty, Themes.ShinyThemeKeys.Color.Primary);
        Clicked += OnClicked;

        StyleGuard.MarkReady(this, typeof(KeyboardDismissItem));
    }

    void OnClicked(object? sender, EventArgs e) => Owner?.DismissKeyboard();
}

/// <summary>
/// Flexible gap. Everything before it is pushed left, everything after it right.
/// </summary>
public class KeyboardAccessorySpacer : ContentView
{
    public KeyboardAccessorySpacer()
    {
        BackgroundColor = Colors.Transparent;
        InputTransparent = true;
        HorizontalOptions = LayoutOptions.Fill;
    }
}
