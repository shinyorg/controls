using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shiny;

namespace Sample.Features.Home;

[ShellMap<HomePage>(registerRoute: false)]
public partial class HomeViewModel : ObservableObject
{
    public CatalogSection[] Sections => Catalog.Sections;

    public int TotalControls => Catalog.TotalControls;

    public int TotalSections => Catalog.Sections.Length;

    /// <summary>
    /// What is in the search box.
    /// </summary>
    /// <remarks>
    /// Re-runs the search on every keystroke rather than on a Search button, because at this many demos
    /// the point is to narrow while typing — a search you have to commit to is one you use once and
    /// then go back to scrolling.
    /// </remarks>
    [ObservableProperty]
    public partial string Query { get; set; } = String.Empty;

    /// <summary>One card per demo, every one of them, created on the first search and then reused.</summary>
    public ObservableCollection<CatalogSearchCard> Results { get; } = new();

    int matchCount;

    /// <summary>Whether the page is showing results rather than the sectioned browse.</summary>
    public bool IsSearching => !String.IsNullOrWhiteSpace(this.Query);

    /// <summary>The inverse, so the sectioned browse can bind without a converter.</summary>
    public bool IsBrowsing => !this.IsSearching;

    public bool HasNoResults => this.IsSearching && this.matchCount == 0;

    public string ResultCount => this.matchCount.ToString();

    partial void OnQueryChanged(string value)
    {
        // The cards are built once and then only shown, hidden and reordered. Clearing and re-adding
        // them recreated every native card on every keystroke, and a one-letter query matches nearly
        // all of them — that is what made typing in the search box crawl on a phone.
        if (this.Results.Count == 0 && !String.IsNullOrWhiteSpace(value))
        {
            foreach (var section in Catalog.Sections)
                foreach (var item in section.Items)
                    this.Results.Add(new CatalogSearchCard(new CatalogHit(
                        item.Route, item.Label, item.Icon, item.Blurb, section.AccentColor, section.Title)));
        }

        var ranked = Catalog.Search(value);
        var order = new Dictionary<string, int>(ranked.Count);
        for (var i = 0; i < ranked.Count; i++)
            order[ranked[i].Route] = i;

        foreach (var card in this.Results)
        {
            if (order.TryGetValue(card.Hit.Route, out var position))
            {
                card.Order = position;
                card.IsMatch = true;
            }
            else
            {
                card.IsMatch = false;
            }
        }
        this.matchCount = ranked.Count;

        this.OnPropertyChanged(nameof(this.IsSearching));
        this.OnPropertyChanged(nameof(this.IsBrowsing));
        this.OnPropertyChanged(nameof(this.HasNoResults));
        this.OnPropertyChanged(nameof(this.ResultCount));
    }

    [RelayCommand]
    void ClearSearch() => this.Query = String.Empty;

    // Absolute ("//") so a card jumps straight to the flyout item rather than pushing the page onto the
    // home page's own stack — tapping Home afterwards would otherwise land back on the demo.
    [RelayCommand]
    async Task Navigate(string route)
        => await Shell.Current.GoToAsync($"//{route}");
}
