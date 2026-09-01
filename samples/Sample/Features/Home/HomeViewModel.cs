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

    public ObservableCollection<CatalogHit> Results { get; } = new();

    /// <summary>Whether the page is showing results rather than the sectioned browse.</summary>
    public bool IsSearching => !String.IsNullOrWhiteSpace(this.Query);

    /// <summary>The inverse, so the sectioned browse can bind without a converter.</summary>
    public bool IsBrowsing => !this.IsSearching;

    public bool HasNoResults => this.IsSearching && this.Results.Count == 0;

    public string ResultCount => this.Results.Count.ToString();

    partial void OnQueryChanged(string value)
    {
        // Rebuilt in place rather than swapped for a new collection: BindableLayout re-reads an
        // ObservableCollection's changes, and reassigning the property would rebuild every card on
        // every keystroke.
        this.Results.Clear();

        foreach (var hit in Catalog.Search(value))
            this.Results.Add(hit);

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
