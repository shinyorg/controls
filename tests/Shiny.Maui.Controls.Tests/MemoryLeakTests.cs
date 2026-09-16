using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;
using Shiny.Controls.Gantt;
using Shiny.Maui.Controls.Gantt;
using Grid2 = Shiny.Maui.Controls.DataGrid.DataGrid;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Kanban;
using Shiny.Maui.Controls.Tree;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// Retention regressions: a publisher that outlives a control (a view model's collection, a model
/// object, a singleton service) must not keep that control - or the page it was on - alive.
/// </summary>
/// <remarks>
/// Each control is built inside a non-inlined helper so no stack slot keeps it reachable, the shared
/// test dispatcher's timer list (which holds every timer, and through its Tick every control) is
/// cleared, and only then is the collector asked whether the control survived.
/// </remarks>
[Collection(ApplicationResourcesCollection.Name)]
public class MemoryLeakTests
{
    public MemoryLeakTests()
    {
        TestDispatcherProvider.Install();
        TestDispatcherProvider.Instance.Timers.Clear();
        _ = new Application();
    }


    static void Collect()
    {
        TestDispatcherProvider.Instance.Timers.Clear();
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        GC.Collect();
    }


    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference Attach<T>(Func<T> create) where T : class
        => new(create());


    // ---------------------------------------------------------------------------------------------
    // WeakEventSubscription itself
    // ---------------------------------------------------------------------------------------------

    sealed class Listener
    {
        public int Calls;
        public IDisposable? Subscription;
        public void OnChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => this.Calls++;
    }


    [Fact]
    public void WeakSubscriptionDeliversWhileTheSubscriberLives()
    {
        var source = new ObservableCollection<int>();
        var listener = new Listener();
        listener.Subscription = WeakEventSubscription.CollectionChanged(source, listener.OnChanged);

        Collect();
        source.Add(1);

        listener.Calls.ShouldBe(1);
        GC.KeepAlive(listener);
    }


    [Fact]
    public void DisposingTheWeakSubscriptionDetachesImmediately()
    {
        var source = new ObservableCollection<int>();
        var listener = new Listener();
        listener.Subscription = WeakEventSubscription.CollectionChanged(source, listener.OnChanged);

        listener.Subscription!.Dispose();
        source.Add(1);

        listener.Calls.ShouldBe(0);
    }


    [Fact]
    public void WeakSubscriptionDoesNotRootTheSubscriber()
    {
        var source = new ObservableCollection<int>();
        var weak = Attach(() =>
        {
            var listener = new Listener();
            listener.Subscription = WeakEventSubscription.CollectionChanged(source, listener.OnChanged);
            return listener;
        });

        Collect();

        weak.IsAlive.ShouldBeFalse();

        // The orphaned proxy removes itself on the next change rather than throwing.
        Should.NotThrow(() => source.Add(1));
        GC.KeepAlive(source);
    }


    // ---------------------------------------------------------------------------------------------
    // Controls bound to a long-lived collection
    // ---------------------------------------------------------------------------------------------

    public static TheoryData<string> BoundControls => new()
    {
        "DataGrid",
        nameof(ChipGroup),
        nameof(TreeView),
        nameof(Accordion),
        nameof(TimelineView),
        nameof(TableView),
        nameof(FabMenu),
    };


    static object Build(string name, ObservableCollection<string> source) => name switch
    {
        "DataGrid" => new Grid2 { ItemsSource = source },
        nameof(ChipGroup) => new ChipGroup { ItemsSource = source },
        nameof(TreeView) => new TreeView { ItemsSource = source },
        nameof(Accordion) => new Accordion { ItemsSource = source },
        nameof(TimelineView) => new TimelineView { ItemsSource = source },
        nameof(TableView) => new TableView { ItemsSource = source },
        nameof(FabMenu) => new FabMenu { Items = new ObservableCollection<FabMenuItem>() },
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };


    [Theory]
    [MemberData(nameof(BoundControls))]
    public void AViewModelCollectionDoesNotKeepTheControlAlive(string control)
    {
        // Stands in for a singleton view model's collection, which outlives every page it is shown on.
        var source = new ObservableCollection<string> { "a", "b" };

        var weak = Attach(() => Build(control, source));
        Collect();

        weak.IsAlive.ShouldBeFalse($"{control} is still rooted by the bound collection");
        GC.KeepAlive(source);
    }


    [Fact]
    public void KanbanCardsDoNotKeepTheBoardAlive()
    {
        var cards = new ObservableCollection<KanbanCard> { new() { Title = "one" } };

        var weak = Attach(() => new KanbanView { Cards = cards });
        Collect();

        weak.IsAlive.ShouldBeFalse();
        GC.KeepAlive(cards);
    }


    [Fact]
    public void GanttTasksDoNotKeepTheChartAlive()
    {
        var tasks = new ObservableCollection<GanttTask> { new() { Id = "t1", Name = "Task", Start = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero), End = new DateTimeOffset(2026, 1, 9, 0, 0, 0, TimeSpan.Zero) } };

        var weak = Attach(() => new GanttView { Tasks = tasks });
        Collect();

        weak.IsAlive.ShouldBeFalse();
        GC.KeepAlive(tasks);
    }


    [Fact]
    public void ABoundCollectionStillDrivesTheControlAfterACollection()
    {
        var source = new ObservableCollection<string> { "a" };
        var group = new ChipGroup { ItemsSource = source };

        Collect();
        source.Add("b");

        group.Chips.Count.ShouldBe(2);
    }


    // ---------------------------------------------------------------------------------------------
    // ProgressLine (the singleton service's line)
    // ---------------------------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference ShowOnPageThenDetach(ProgressLine line)
    {
        var host = new Grid();
        var page = new ContentPage { Content = host };
        host.Children.Add(line);
        host.Children.Remove(line);
        return new WeakReference(page);
    }


    [Fact]
    public void ANonDockingLineReleasesThePageItWasShownOn()
    {
        // The service's line is held by a singleton; it must not keep the last page it drew on.
        var line = new ProgressLine { Dock = false };

        var page = ShowOnPageThenDetach(line);
        Collect();

        page.IsAlive.ShouldBeFalse();
        GC.KeepAlive(line);
    }


    // ---------------------------------------------------------------------------------------------
    // Loops and timers that must stop when the view leaves the tree
    // ---------------------------------------------------------------------------------------------

    // Loaded/Unloaded never fire headlessly (there is no window), so they are driven through MAUI's
    // own internal senders. Unloaded is ignored unless Loaded fired first.
    static void Send(VisualElement element, string method)
        => typeof(VisualElement)
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes)!
            .Invoke(element, null);

    static void SendUnloaded(VisualElement element)
    {
        Send(element, "SendLoaded");
        Send(element, "SendUnloaded");
    }


    [Fact]
    public void UnloadingAProgressBarStopsItsPulseTimer()
    {
        var bar = new ProgressBar { PulseInterval = TimeSpan.FromSeconds(1), PulseEnabled = true };
        var timer = TestDispatcherProvider.Instance.Timers.Last();
        timer.IsRunning.ShouldBeTrue();

        SendUnloaded(bar);

        timer.IsRunning.ShouldBeFalse();
    }
}
