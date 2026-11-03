using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Shiny.Maui.Controls.Cells;
using Shouldly;
using Xunit;
using TvTableSection = Shiny.Maui.Controls.Sections.TableSection;
using SwitchCell = Shiny.Maui.Controls.Cells.SwitchCell;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// A <c>SwitchCell</c> whose value shows or hides a section used to hang the app when switched on.
/// The re-render ran inside the switch's own <c>IsToggled</c> change and detached every cell; a detached
/// cell lost its inherited BindingContext, its <c>On</c> fell to false and was written into the switch still
/// mid-set. MAUI queued that write and replayed it — off, re-render, another off/on pair — forever.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class TableViewDeferredRenderTests
{
    [Fact]
    public void SwitchingOnASectionFromASwitchSettles()
    {
        var dispatcher = new QueueingDispatcher();
        DispatcherProvider.SetCurrent(new QueueingProvider(dispatcher));
        try
        {
            new Application();
            var model = new Model();
            var table = BuildTable(model);
            var toggle = table.Root.Sections[0].Cells[0].GetVisualTreeDescendants().OfType<Switch>().Single();

            toggle.IsToggled = true;
            dispatcher.RunAll();

            model.Writes.ShouldBe(1, "one tap is one write — anything more is the switch fighting the re-render");
            model.Feature.ShouldBeTrue();
            ((VerticalStackLayout)table.ScrollContent).Children.Count(x => x is not BoxView)
                .ShouldBe(2, "the section the switch turned on is rendered");
        }
        finally
        {
            TestDispatcherProvider.Install();
        }
    }

    /// <summary>Several sections changing from one write re-render once, not once each.</summary>
    [Fact]
    public void SectionChangesTogetherRenderOnce()
    {
        var dispatcher = new QueueingDispatcher();
        DispatcherProvider.SetCurrent(new QueueingProvider(dispatcher));
        try
        {
            new Application();
            var model = new Model();
            var table = BuildTable(model, dependentSections: 2);
            dispatcher.RunAll();
            var renders = 0;
            table.ModelChanged += (_, _) => renders++;

            model.Feature = true;
            dispatcher.RunAll();

            renders.ShouldBe(1);
        }
        finally
        {
            TestDispatcherProvider.Install();
        }
    }

    /// <summary>
    /// A cell hidden by a binding stays hidden, and out of the new section, through a re-render.
    /// </summary>
    /// <remarks>
    /// It used to be skipped by the detach, left inside the outgoing section layout, and orphaned with
    /// it - which cleared its inherited BindingContext, so its <c>IsVisible</c> fell to the default
    /// <c>true</c> and the same render added it to the new section. On Android its view was still a
    /// child of the old one: "The specified child already has a parent", the moment a settings page
    /// with a hidden row re-rendered after it was on screen.
    /// </remarks>
    [Fact]
    public void ABoundHiddenCellStaysOutOfTheRender()
    {
        var dispatcher = new QueueingDispatcher();
        DispatcherProvider.SetCurrent(new QueueingProvider(dispatcher));
        try
        {
            new Application();
            var model = new Model();
            var table = BuildTable(model);

            var hidden = new LabelCell { Title = "Busy" };
            hidden.SetBinding(VisualElement.IsVisibleProperty, nameof(Model.Busy));
            table.Root.Sections[0].Cells.Add(hidden);
            dispatcher.RunAll();

            model.Feature = true;
            dispatcher.RunAll();

            hidden.IsVisible.ShouldBeFalse("its binding still says hidden");
            hidden.Parent.ShouldBeNull("a cell the render left out must not be left inside an old section");
            table.ScrollContent.GetVisualTreeDescendants().ShouldNotContain(hidden);
        }
        finally
        {
            TestDispatcherProvider.Install();
        }
    }

    /// <summary>
    /// A cell hidden when the table first renders appears once its IsVisible turns true, and goes again
    /// when it turns false - in one re-render each time.
    /// </summary>
    /// <remarks>
    /// Only visible cells are rendered, and nothing re-rendered when one cell's visibility changed, so a
    /// row bound to state that starts false (GitHub's "Signed in as", "Sign out", a sign-in code) never
    /// showed until something unrelated re-rendered the table.
    /// </remarks>
    [Fact]
    public void ACellHiddenAtFirstRenderAppearsWhenShown()
    {
        var dispatcher = new QueueingDispatcher();
        DispatcherProvider.SetCurrent(new QueueingProvider(dispatcher));
        try
        {
            new Application();
            var model = new Model();
            var table = BuildTable(model);
            var row = new LabelCell { Title = "Signed in as" };
            row.SetBinding(VisualElement.IsVisibleProperty, nameof(Model.SignedIn));
            table.Root.Sections[0].Cells.Add(row);
            dispatcher.RunAll();
            table.ScrollContent.GetVisualTreeDescendants().ShouldNotContain(row);

            var renders = 0;
            table.ModelChanged += (_, _) => renders++;

            model.SignedIn = true;
            dispatcher.RunAll();
            table.ScrollContent.GetVisualTreeDescendants().ShouldContain(row);
            renders.ShouldBe(1);

            // Rendered now, so it hides and shows itself: no re-render, which would re-attach every
            // cell and take focus (and the keyboard) from an EntryCell being typed in.
            var entry = new Shiny.Maui.Controls.Cells.EntryCell { Title = "Typing here" };
            table.Root.Sections[0].Cells.Add(entry);
            dispatcher.RunAll();
            renders = 0;

            model.SignedIn = false;
            dispatcher.RunAll();
            row.IsVisible.ShouldBeFalse();
            model.SignedIn = true;
            dispatcher.RunAll();
            row.IsVisible.ShouldBeTrue();
            table.ScrollContent.GetVisualTreeDescendants().ShouldContain(row);
            renders.ShouldBe(0, "a rendered cell's show/hide must not rebuild the table");
        }
        finally
        {
            TestDispatcherProvider.Install();
        }
    }

    /// <summary>The same for a cell generated from a section's ItemsSource.</summary>
    [Fact]
    public void AGeneratedCellHiddenAtFirstRenderAppearsWhenShown()
    {
        var dispatcher = new QueueingDispatcher();
        DispatcherProvider.SetCurrent(new QueueingProvider(dispatcher));
        try
        {
            new Application();
            var item = new Model();
            var section = new TvTableSection("Items")
            {
                ItemTemplate = new DataTemplate(() =>
                {
                    var cell = new LabelCell { Title = "Item" };
                    cell.SetBinding(VisualElement.IsVisibleProperty, nameof(Model.SignedIn));
                    return cell;
                }),
                ItemsSource = new[] { item }
            };
            var table = new TableView();
            table.Root.Sections.Add(section);
            dispatcher.RunAll();
            var generated = section.GetAllCells().Single();
            table.ScrollContent.GetVisualTreeDescendants().ShouldNotContain(generated);

            item.SignedIn = true;
            dispatcher.RunAll();
            table.ScrollContent.GetVisualTreeDescendants().ShouldContain(generated);
        }
        finally
        {
            TestDispatcherProvider.Install();
        }
    }

    /// <summary>
    /// Filling a section's collection one item at a time builds each cell once - it used to rebuild every
    /// cell per add, n(n+1)/2 cells for n items.
    /// </summary>
    [Fact]
    public void FillingASectionItemsSourceBuildsEachCellOnce()
    {
        var dispatcher = new QueueingDispatcher();
        DispatcherProvider.SetCurrent(new QueueingProvider(dispatcher));
        try
        {
            new Application();
            var created = 0;
            var items = new System.Collections.ObjectModel.ObservableCollection<string>();
            var section = new TvTableSection("Items")
            {
                ItemTemplate = new DataTemplate(() =>
                {
                    created++;
                    return new LabelCell();
                }),
                ItemsSource = items
            };
            var table = new TableView();
            table.Root.Sections.Add(section);
            dispatcher.RunAll();

            for (var i = 0; i < 50; i++)
                items.Add($"Item {i}");
            dispatcher.RunAll();

            created.ShouldBe(50);
            section.GetAllCells().Count().ShouldBe(50);
        }
        finally
        {
            TestDispatcherProvider.Install();
        }
    }

    /// <summary>The same for whole sections generated from the table's own collection.</summary>
    [Fact]
    public void FillingATableItemsSourceBuildsEachSectionOnce()
    {
        var dispatcher = new QueueingDispatcher();
        DispatcherProvider.SetCurrent(new QueueingProvider(dispatcher));
        try
        {
            new Application();
            var created = 0;
            var items = new System.Collections.ObjectModel.ObservableCollection<string>();
            var table = new TableView
            {
                ItemTemplate = new DataTemplate(() =>
                {
                    created++;
                    return new TvTableSection();
                }),
                ItemsSource = items
            };
            dispatcher.RunAll();

            for (var i = 0; i < 20; i++)
                items.Add($"Group {i}");
            dispatcher.RunAll();

            created.ShouldBe(20);
            ((VerticalStackLayout)table.ScrollContent).Children.Count(x => x is not BoxView).ShouldBe(20);
        }
        finally
        {
            TestDispatcherProvider.Install();
        }
    }

    static TableView BuildTable(Model model, int dependentSections = 1)
    {
        var table = new TableView();

        var features = new TvTableSection("Features");
        var cell = new SwitchCell { Title = "Feature" };
        cell.SetBinding(SwitchCell.OnProperty, nameof(Model.Feature));
        features.Cells.Add(cell);
        table.Root.Sections.Add(features);

        for (var i = 0; i < dependentSections; i++)
        {
            var dependent = new TvTableSection("Dependent");
            dependent.SetBinding(TvTableSection.IsVisibleProperty, nameof(Model.Feature));
            dependent.Cells.Add(new LabelCell { Title = "Row" });
            table.Root.Sections.Add(dependent);
        }

        // Last, the way a page's ViewModel arrives after its XAML is built
        table.BindingContext = model;
        return table;
    }

    sealed class Model : INotifyPropertyChanged
    {
        public int Writes { get; private set; }

        public bool Feature
        {
            get;
            set
            {
                if (field == value)
                    return;

                // The loop never ends on its own; stop it here so a regression fails rather than hangs
                if (++this.Writes > 20)
                    throw new InvalidOperationException("The switch and the re-render are fighting");

                field = value;
                this.OnPropertyChanged();
            }
        }

        public bool Busy => false;

        public bool SignedIn
        {
            get;
            set
            {
                if (field == value)
                    return;
                field = value;
                this.OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        void OnPropertyChanged([CallerMemberName] string? name = null)
            => this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Holds dispatched work until the test runs it, the way the UI thread does after the current event.</summary>
    sealed class QueueingDispatcher : IDispatcher
    {
        readonly Queue<Action> queue = new();

        public bool IsDispatchRequired => false;

        public bool Dispatch(Action action)
        {
            this.queue.Enqueue(action);
            return true;
        }

        public bool DispatchDelayed(TimeSpan delay, Action action) => this.Dispatch(action);

        public IDispatcherTimer CreateTimer() => new TestDispatcher.ControllableTimer();

        public void RunAll()
        {
            while (this.queue.TryDequeue(out var action))
                action();
        }
    }

    sealed class QueueingProvider(QueueingDispatcher dispatcher) : IDispatcherProvider
    {
        public IDispatcher? GetForCurrentThread() => dispatcher;
    }
}
