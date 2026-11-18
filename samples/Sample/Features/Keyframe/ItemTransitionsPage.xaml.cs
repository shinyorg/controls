using System.Collections.ObjectModel;
using Shiny.Maui.Controls.Keyframe;

namespace Sample.Features.Keyframe;

public partial class ItemTransitionsPage : ContentPage
{
    int next = 1;

    public ItemTransitionsPage()
    {
        InitializeComponent();

        for (; next <= 30; next++)
            Items.Add($"Item {next}");

        BindingContext = this;
    }

    public ObservableCollection<string> Items { get; } = [];

    void OnAdd(object? sender, EventArgs e) => Items.Insert(0, $"Item {next++}");

    void OnShuffle(object? sender, EventArgs e)
    {
        var random = new Random();
        var from = random.Next(Items.Count);
        var to = random.Next(Items.Count);
        Items.Move(from, to);
    }

    async void OnRemove(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: string item })
            await ItemTransitions.RemoveAsync(List, item, () => Items.Remove(item));
    }

    void OnNativeToggled(object? sender, ToggledEventArgs e)
        => ItemTransitions.SetMode(List, e.Value ? ItemTransitionMode.None : ItemTransitionMode.Platform);
}
