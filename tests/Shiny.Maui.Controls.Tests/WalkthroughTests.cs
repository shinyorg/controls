using Microsoft.Maui.Controls;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// What a walkthrough decides without needing a screen: which steps are in the run, whether it has
/// already been shown to this user, and that nothing moves when it is not running. The drawing and the
/// spotlight travel need a real layout and belong in the device tests.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class WalkthroughTests
{
    public WalkthroughTests()
    {
        TestDispatcherProvider.Install();

        // A fresh Application per test, not `Application.Current ?? new` — Application.Current is
        // process-wide, so anything one test merges would leak into the rest of the collection.
        _ = new Application();

        // Never the Preferences-backed default: that is real device state, and a test that writes it
        // would change what the next run of the suite sees.
        Walkthrough.Store = new InMemoryWalkthroughStore();
    }


    static Walkthrough Build(params string[] names)
    {
        var walkthrough = new Walkthrough();
        foreach (var name in names)
            walkthrough.Steps.Add(new WalkthroughStep { Name = name, Text = name });

        return walkthrough;
    }


    [Fact]
    public void StepsAreCountedAsTheyAreAdded()
    {
        var walkthrough = Build("One", "Two", "Three");

        walkthrough.StepCount.ShouldBe(3);
        walkthrough.CurrentStepIndex.ShouldBe(-1);
        walkthrough.IsRunning.ShouldBeFalse();
    }


    [Fact]
    public void AHiddenStepDropsOutOfTheRun()
    {
        var walkthrough = Build("One", "Two", "Three");

        walkthrough.Steps[1].IsVisible = false;

        walkthrough.StepCount.ShouldBe(2);
    }


    [Fact]
    public void AStepBecomingVisibleAgainRejoinsTheRun()
    {
        var walkthrough = Build("One", "Two");
        walkthrough.Steps[0].IsVisible = false;
        walkthrough.StepCount.ShouldBe(1);

        walkthrough.Steps[0].IsVisible = true;

        walkthrough.StepCount.ShouldBe(2);
    }


    [Fact]
    public void RemovingAStepStopsItBeingCounted()
    {
        var walkthrough = Build("One", "Two");
        var step = walkthrough.Steps[1];

        walkthrough.Steps.Remove(step);

        walkthrough.StepCount.ShouldBe(1);

        // The walkthrough must have let go of the step's change notification too, or a detached step
        // would keep driving a control it is no longer part of.
        step.IsVisible = false;
        walkthrough.StepCount.ShouldBe(1);
    }


    [Fact]
    public void WithoutARememberKeyItHasNeverRun()
    {
        var walkthrough = Build("One");

        walkthrough.HasRun.ShouldBeFalse();

        // Reset on a keyless walkthrough is a no-op rather than an error.
        Should.NotThrow(walkthrough.Reset);
    }


    [Fact]
    public void TheRememberedFlagIsReadAndClearedByKey()
    {
        var walkthrough = Build("One");
        walkthrough.RememberRunKey = "tour-a";

        Walkthrough.Store.SetHasRun("tour-a", true);
        walkthrough.HasRun.ShouldBeTrue();

        walkthrough.Reset();
        walkthrough.HasRun.ShouldBeFalse();
    }


    [Fact]
    public void TheRememberedFlagIsScopedToItsKey()
    {
        var a = Build("One");
        a.RememberRunKey = "tour-a";

        var b = Build("One");
        b.RememberRunKey = "tour-b";

        Walkthrough.Store.SetHasRun("tour-a", true);

        a.HasRun.ShouldBeTrue();
        b.HasRun.ShouldBeFalse();
    }


    [Fact]
    public void ClearRunForgetsAKeyWithoutAnInstance()
    {
        Walkthrough.Store.SetHasRun("tour-c", true);

        Walkthrough.ClearRun("tour-c");

        Walkthrough.Store.HasRun("tour-c").ShouldBeFalse();
    }


    [Fact]
    public void MovingAWalkthroughThatIsNotRunningDoesNothing()
    {
        var walkthrough = Build("One", "Two");

        // Every one of these has to be inert. A signal recorded while nothing is running would be
        // consumed by the *next* run's first step, which skips it for no visible reason.
        Should.NotThrow(() =>
        {
            walkthrough.Next();
            walkthrough.Back();
            walkthrough.Skip();
            walkthrough.Stop();
        });

        walkthrough.IsRunning.ShouldBeFalse();
        walkthrough.CurrentStepIndex.ShouldBe(-1);
    }


    [Fact]
    public void StartingWithNoStepsIsANoOp()
    {
        var walkthrough = new Walkthrough();

        Should.NotThrow(() => walkthrough.Start());

        walkthrough.IsRunning.ShouldBeFalse();
    }


    [Fact]
    public void StartingOffAPageIsANoOpRatherThanACrash()
    {
        // Not parented to anything, so there is no page to wrap and nothing to measure against.
        var walkthrough = Build("One", "Two");

        Should.NotThrow(() => walkthrough.Start());

        walkthrough.IsRunning.ShouldBeFalse();
    }


    [Fact]
    public void StepsInheritTheBindingContext()
    {
        var walkthrough = Build("One");
        var model = new object();

        walkthrough.BindingContext = model;

        // Steps are BindableObjects, not elements, so nothing propagates this for them — without it a
        // step could not bind Text or IsVisible to the page's view-model at all.
        walkthrough.Steps[0].BindingContext.ShouldBeSameAs(model);
    }


    [Fact]
    public void AStepAddedAfterTheBindingContextStillInheritsIt()
    {
        var walkthrough = new Walkthrough();
        var model = new object();
        walkthrough.BindingContext = model;

        var step = new WalkthroughStep { Name = "Late" };
        walkthrough.Steps.Add(step);

        step.BindingContext.ShouldBeSameAs(model);
    }


    [Fact]
    public void AStepFallsBackToTheWalkthroughsHighlightSettings()
    {
        var walkthrough = Build("One");
        walkthrough.Highlight = WalkthroughHighlight.Circle;
        walkthrough.HighlightPadding = 12;

        var step = walkthrough.Steps[0];

        // Null means inherit, which is what lets one step differ without every step restating the rest.
        step.Highlight.ShouldBeNull();
        step.HighlightPadding.ShouldBeNull();

        (step.Highlight ?? walkthrough.Highlight).ShouldBe(WalkthroughHighlight.Circle);
        (step.HighlightPadding ?? walkthrough.HighlightPadding).ShouldBe(12);
    }


    static (Walkthrough Tour, ContentPage Page) OnAPage(params string[] names)
    {
        var walkthrough = Build(names);
        var page = new ContentPage { Content = new Grid { Children = { walkthrough } } };
        return (walkthrough, page);
    }

    static async Task<WalkthroughEndReason> NextEnd(Walkthrough walkthrough)
    {
        var ended = new TaskCompletionSource<WalkthroughEndReason>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnEnded(object? sender, WalkthroughEndedEventArgs e) => ended.TrySetResult(e.Reason);
        walkthrough.Ended += OnEnded;
        try
        {
            (await Task.WhenAny(ended.Task, Task.Delay(10_000))).ShouldBe(ended.Task, "the run never ended");
            return await ended.Task;
        }
        finally
        {
            walkthrough.Ended -= OnEnded;
        }
    }


    /// <summary>
    /// A Stop that lands while the run is still starting (waiting for layout, before the first step) is a
    /// Stop. The loop used to fall straight out as Completed, which also recorded the tour as run, so it
    /// would never auto-start again.
    /// </summary>
    [Fact]
    public async Task StoppingDuringStartUpEndsAsStoppedAndRecordsNothing()
    {
        var (walkthrough, _) = OnAPage("One", "Two");
        walkthrough.RememberRunKey = "tour-startup-stop";

        var end = NextEnd(walkthrough);
        walkthrough.Start();
        walkthrough.IsRunning.ShouldBeTrue();
        walkthrough.CurrentStepIndex.ShouldBe(-1);

        walkthrough.Stop();

        (await end).ShouldBe(WalkthroughEndReason.Stopped);
        Walkthrough.Store.HasRun("tour-startup-stop").ShouldBeFalse();
        walkthrough.IsRunning.ShouldBeFalse();
    }


    /// <summary>
    /// A Start straight after a Stop, while the old run is still unwinding, starts a fresh run once the
    /// old one is gone instead of being ignored.
    /// </summary>
    [Fact]
    public async Task StartingWhileTheOldRunUnwindsStartsAgain()
    {
        var (walkthrough, _) = OnAPage("One", "Two");

        var first = NextEnd(walkthrough);
        walkthrough.Start();
        walkthrough.Stop();
        walkthrough.Start();

        (await first).ShouldBe(WalkthroughEndReason.Stopped);

        // the queued Start has begun a new run
        walkthrough.IsRunning.ShouldBeTrue();

        var second = NextEnd(walkthrough);
        walkthrough.Stop();
        (await second).ShouldBe(WalkthroughEndReason.Stopped);
    }


    /// <summary>
    /// A Start that lands while the old run is tearing itself down (here: held mid scrim fade-out) waits
    /// for the teardown. It used to start at once, and the old run's teardown then dismantled the new
    /// one - IsRunning false, the run never ending, and every later Start ignored.
    /// </summary>
    [Fact]
    public async Task StartingDuringTheTeardownStartsAFreshWorkingRun()
    {
        var host = new FrozenAnimationHost();
        var (walkthrough, page) = OnAPage("One", "Two");
        host.Attach(page);

        var first = NextEnd(walkthrough);
        walkthrough.Start();
        walkthrough.Stop();

        // past the start-up layout wait: the stopped run is now held in its scrim fade-out
        await Task.Delay(1_500);
        first.IsCompleted.ShouldBeFalse();

        walkthrough.Start();
        await host.Finish();
        (await first).ShouldBe(WalkthroughEndReason.Stopped);

        // the queued run is alive and still answers to Stop
        walkthrough.IsRunning.ShouldBeTrue();
        var second = NextEnd(walkthrough);
        walkthrough.Stop();
        await Task.Delay(1_500);
        await host.Finish();
        (await second).ShouldBe(WalkthroughEndReason.Stopped);
        walkthrough.IsRunning.ShouldBeFalse();
    }

    /// <summary>
    /// The spotlight is placed in root coordinates. A layer that also inset by the safe area (a
    /// layout's default) moved every spotlight down by the status bar on Android.
    /// </summary>
    [Fact]
    public void TheWalkthroughLayerDoesNotApplyTheSafeAreaAgain()
        => new Infrastructure.PageOverlay.WalkthroughLayer().SafeAreaEdges.ShouldBe(SafeAreaEdges.None);
}
