using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.Cells;

public class CommandCell : LabelCell
{
    Label arrowLabel = default!;

    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command), typeof(ICommand), typeof(CommandCell), null);

    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(
        nameof(CommandParameter), typeof(object), typeof(CommandCell), null);

    public static readonly BindableProperty ShowArrowProperty = BindableProperty.Create(
        nameof(ShowArrow), typeof(bool), typeof(CommandCell), true,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CommandCell), () =>
            {
                ((CommandCell)b).arrowLabel.IsVisible = (bool)n;
            }));

    public static readonly BindableProperty KeepSelectedUntilBackProperty = BindableProperty.Create(
        nameof(KeepSelectedUntilBack), typeof(bool), typeof(CommandCell), false);

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

    public CommandCell()
    {
        arrowLabel = new Label
        {
            Text = "\u203A",
            FontSize = 20,
            VerticalOptions = LayoutOptions.Center,
            Opacity = 0.5,
            Margin = new Thickness(4, 0, 0, 0)
        };

        var accessoryLayout = new HorizontalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Spacing = 4
        };

        if (AccessoryView is Label valueLabel)
        {
            var parent = valueLabel.Parent as Layout;
            parent?.Children.Remove(valueLabel);
            accessoryLayout.Children.Add(valueLabel);
        }
        accessoryLayout.Children.Add(arrowLabel);

        Grid.SetColumn(accessoryLayout, 2);
        Grid.SetRowSpan(accessoryLayout, 2);

        var grid = RootGrid;
        if (AccessoryView != null)
            grid.Children.Remove(AccessoryView);
        grid.Children.Add(accessoryLayout);

        if (OperatingSystem.IsLinux())
        {
            // GTK/Linux: TapGestureRecognizer on ContentView is unreliable,
            // so overlay a transparent Button (native GtkButton) to capture taps.
            var tapOverlay = new Button
            {
                BackgroundColor = Colors.Transparent,
                BorderColor = Colors.Transparent,
                BorderWidth = 0,
                Padding = 0,
                CornerRadius = 0,
                Text = string.Empty
            }.Neutralize();
            tapOverlay.Clicked += (_, _) => OnCellTapped(this, new TappedEventArgs(null));
            Grid.SetColumn(tapOverlay, 0);
            Grid.SetColumnSpan(tapOverlay, 3);
            Grid.SetRow(tapOverlay, 0);
            Grid.SetRowSpan(tapOverlay, 2);
            grid.Children.Add(tapOverlay);
        }

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(CommandCell));
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
}