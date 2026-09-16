using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.Cells;

[ContentProperty(nameof(CustomContent))]
public class CustomCell : CellBase
{
    public static readonly BindableProperty CustomContentProperty = BindableProperty.Create(
        nameof(CustomContent), typeof(View), typeof(CustomCell), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CustomCell), () => OnCustomContentChanged(b, o, n)));

    public static readonly BindableProperty UseFullSizeProperty = BindableProperty.Create(
        nameof(UseFullSize), typeof(bool), typeof(CustomCell), false,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CustomCell), () =>
            {
                ((CustomCell)b).UpdateLayout();
            }));

    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command), typeof(ICommand), typeof(CustomCell), null);

    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(
        nameof(CommandParameter), typeof(object), typeof(CustomCell), null);

    public static readonly BindableProperty LongCommandProperty = BindableProperty.Create(
        nameof(LongCommand), typeof(ICommand), typeof(CustomCell), null);

    public static readonly BindableProperty LongCommandParameterProperty = BindableProperty.Create(
        nameof(LongCommandParameter), typeof(object), typeof(CustomCell), null);

    public static readonly BindableProperty ShowArrowProperty = BindableProperty.Create(
        nameof(ShowArrow), typeof(bool), typeof(CustomCell), false,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CustomCell), () =>
            {
                ((CustomCell)b).UpdateArrowVisibility();
            }));

    public static readonly BindableProperty KeepSelectedUntilBackProperty = BindableProperty.Create(
        nameof(KeepSelectedUntilBack), typeof(bool), typeof(CustomCell), false);

    Label? arrowLabel;

    public View? CustomContent
    {
        get => (View?)GetValue(CustomContentProperty);
        set => SetValue(CustomContentProperty, value);
    }

    public bool UseFullSize
    {
        get => (bool)GetValue(UseFullSizeProperty);
        set => SetValue(UseFullSizeProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public ICommand? LongCommand
    {
        get => (ICommand?)GetValue(LongCommandProperty);
        set => SetValue(LongCommandProperty, value);
    }

    public object? LongCommandParameter
    {
        get => GetValue(LongCommandParameterProperty);
        set => SetValue(LongCommandParameterProperty, value);
    }

    public bool ShowArrow
    {
        get => (bool)GetValue(ShowArrowProperty);
        set => SetValue(ShowArrowProperty, value);
    }

    public bool KeepSelectedUntilBack
    {
        get => (bool)GetValue(KeepSelectedUntilBackProperty);
        set => SetValue(KeepSelectedUntilBackProperty, value);
    }

    public CustomCell()
    {
        // Double-tap for long command (MAUI has no built-in long-press gesture)
        var longPress = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        longPress.Tapped += OnLongPressed;
        GestureRecognizers.Add(longPress);

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(CustomCell));
    }

    static void OnCustomContentChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((CustomCell)bindable).UpdateLayout();
    }

    void UpdateLayout()
    {
        if (UseFullSize && CustomContent != null)
        {
            Content = CustomContent;
        }
        else
        {
            UpdateAccessoryContent();
        }
    }

    void UpdateAccessoryContent()
    {
        if (CustomContent == null) return;

        var grid = RootGrid;
        if (grid == null) return;

        var layout = new HorizontalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Spacing = 4
        };
        layout.Children.Add(CustomContent);

        if (ShowArrow)
        {
            EnsureArrowLabel();
            layout.Children.Add(arrowLabel!);
        }

        Grid.SetColumn(layout, 2);
        Grid.SetRowSpan(layout, 2);

        // Remove old accessory if present
        if (AccessoryView != null && grid.Children.Contains(AccessoryView))
            grid.Children.Remove(AccessoryView);

        grid.Children.Add(layout);
    }

    void EnsureArrowLabel()
    {
        arrowLabel ??= new Label
        {
            Text = "\u203A",
            FontSize = 20,
            VerticalOptions = LayoutOptions.Center,
            Opacity = 0.5
        };
    }

    void UpdateArrowVisibility()
    {
        // Rebuild layout to include/exclude arrow
        if (!UseFullSize)
            UpdateAccessoryContent();
    }

    protected override bool ShouldKeepSelection() => KeepSelectedUntilBack;

    protected override void OnTapped()
    {
        if (KeepSelectedUntilBack)
        {
            var page = GetParentPage();
            if (page != null)
                ClearSelectionOnAppearing(page);
        }

        if (Command?.CanExecute(CommandParameter) == true)
            Command.Execute(CommandParameter);
    }

    void OnLongPressed(object? sender, TappedEventArgs e)
    {
        if (LongCommand?.CanExecute(LongCommandParameter) == true)
            LongCommand.Execute(LongCommandParameter);
    }
}