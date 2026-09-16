using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.Maui.Controls.Cells;

using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Pages;

public class PickerPage : ContentPage
{
    readonly PickerCell ownerCell;
    readonly ObservableCollection<PickerItemViewModel> items = new();
    readonly CollectionView collectionView;

    public PickerPage(PickerCell ownerCell)
    {
        this.ownerCell = ownerCell;
        Title = ownerCell.PageTitle;

        // No explicit accent anywhere in the chain => follow the theme's Primary, live, as resolved
        // on the owner cell (see PickerCell.ThemeAccentColorProperty).
        var accentColor = ownerCell.AccentColor
            ?? ownerCell.ParentTableView?.CellAccentColor;

        collectionView = new CollectionView
        {
            ItemsSource = items,
            SelectionMode = Microsoft.Maui.Controls.SelectionMode.None,
            ItemTemplate = new DataTemplate(() =>
            {
                var grid = new Grid
                {
                    Padding = new Thickness(16, 12),
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Auto)
                    },
                    RowDefinitions =
                    {
                        new RowDefinition(GridLength.Auto),
                        new RowDefinition(GridLength.Auto)
                    }
                };

                var displayLabel = new Label
                {
                    VerticalOptions = LayoutOptions.Center
                }.WithFontSize(ShinyThemeKeys.Type.BodyLargeSize);
                displayLabel.SetBinding(Label.TextProperty, nameof(PickerItemViewModel.DisplayText));
                Grid.SetColumn(displayLabel, 0);
                grid.Children.Add(displayLabel);

                var subLabel = new Label
                {
                    Opacity = 0.6,
                    VerticalOptions = LayoutOptions.Start
                }.WithFontSize(ShinyThemeKeys.Type.BodySmallSize);
                subLabel.SetBinding(Label.TextProperty, nameof(PickerItemViewModel.SubDisplayText));
                subLabel.SetBinding(Label.IsVisibleProperty, nameof(PickerItemViewModel.HasSubText));
                Grid.SetColumn(subLabel, 0);
                Grid.SetRow(subLabel, 1);
                grid.Children.Add(subLabel);

                var checkLabel = new Label
                {
                    Text = "\u2713",
                    FontSize = 20,
                    VerticalOptions = LayoutOptions.Center
                };
                if (accentColor is not null)
                    checkLabel.TextColor = accentColor;
                else
                    checkLabel.SetBinding(Label.TextColorProperty, static (PickerCell c) => c.ThemeAccentColor, source: ownerCell);
                checkLabel.SetBinding(Label.IsVisibleProperty, nameof(PickerItemViewModel.IsSelected));
                Grid.SetColumn(checkLabel, 1);
                Grid.SetRowSpan(checkLabel, 2);
                grid.Children.Add(checkLabel);

                var tapGesture = new TapGestureRecognizer();
                tapGesture.Tapped += OnItemTapped;
                grid.GestureRecognizers.Add(tapGesture);

                return grid;
            })
        };

        Content = collectionView;
        LoadItems();
    }

    void LoadItems()
    {
        if (ownerCell.ItemsSource == null) return;

        var selectedItems = ownerCell.SelectedItems;
        var selectedItem = ownerCell.SelectedItem;

        foreach (var item in ownerCell.ItemsSource)
        {
            var isSelected = false;
            if (ownerCell.SelectionMode == Cells.SelectionMode.Single)
                isSelected = Equals(item, selectedItem);
            else if (selectedItems != null)
                isSelected = selectedItems.Contains(item);

            items.Add(new PickerItemViewModel
            {
                Item = item,
                DisplayText = ownerCell.GetDisplayText(item),
                SubDisplayText = ownerCell.GetSubDisplayText(item),
                IsSelected = isSelected
            });
        }
    }

    async void OnItemTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not View view || view.BindingContext is not PickerItemViewModel vm)
            return;

        if (ownerCell.SelectionMode == Cells.SelectionMode.Single)
        {
            foreach (var item in items)
                item.IsSelected = false;
            vm.IsSelected = true;

            ownerCell.OnSelectionComplete(vm.Item, null);
            await Navigation.PopAsync();
        }
        else
        {
            if (vm.IsSelected)
            {
                vm.IsSelected = false;
            }
            else
            {
                var currentCount = items.Count(i => i.IsSelected);
                if (ownerCell.MaxSelectedNumber > 0 && currentCount >= ownerCell.MaxSelectedNumber)
                    return;

                vm.IsSelected = true;
            }

            var selected = new ObservableCollection<object>();
            foreach (var item in items.Where(i => i.IsSelected))
                selected.Add(item.Item);

            ownerCell.OnSelectionComplete(null, selected);

            if (ownerCell.UsePickToClose &&
                ownerCell.MaxSelectedNumber > 0 &&
                selected.Count >= ownerCell.MaxSelectedNumber)
            {
                await Navigation.PopAsync();
            }
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        if (ownerCell.SelectionMode == Cells.SelectionMode.Multiple)
        {
            var selected = new ObservableCollection<object>();
            foreach (var item in items.Where(i => i.IsSelected))
                selected.Add(item.Item);
            ownerCell.OnSelectionComplete(null, selected);
        }
    }
}

class PickerItemViewModel : BindableObject
{
    public object Item { get; set; } = default!;

    public static readonly BindableProperty DisplayTextProperty = BindableProperty.Create(
        nameof(DisplayText), typeof(string), typeof(PickerItemViewModel), string.Empty);

    public static readonly BindableProperty SubDisplayTextProperty = BindableProperty.Create(
        nameof(SubDisplayText), typeof(string), typeof(PickerItemViewModel), string.Empty);

    public static readonly BindableProperty IsSelectedProperty = BindableProperty.Create(
        nameof(IsSelected), typeof(bool), typeof(PickerItemViewModel), false);

    public static readonly BindableProperty HasSubTextProperty = BindableProperty.Create(
        nameof(HasSubText), typeof(bool), typeof(PickerItemViewModel), false);

    public string DisplayText
    {
        get => (string)GetValue(DisplayTextProperty);
        set
        {
            SetValue(DisplayTextProperty, value);
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    public string SubDisplayText
    {
        get => (string)GetValue(SubDisplayTextProperty);
        set
        {
            SetValue(SubDisplayTextProperty, value);
            HasSubText = !string.IsNullOrEmpty(value);
            OnPropertyChanged(nameof(SubDisplayText));
        }
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set
        {
            SetValue(IsSelectedProperty, value);
            OnPropertyChanged(nameof(IsSelected));
        }
    }

    public bool HasSubText
    {
        get => (bool)GetValue(HasSubTextProperty);
        set
        {
            SetValue(HasSubTextProperty, value);
            OnPropertyChanged(nameof(HasSubText));
        }
    }

}