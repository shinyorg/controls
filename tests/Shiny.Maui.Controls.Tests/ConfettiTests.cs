using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>The physics, the presets, burst completion, and how the attached trigger hooks a view.</summary>
[Collection(ApplicationResourcesCollection.Name)]
public class ConfettiTests
{
    public ConfettiTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
    }


    [Fact]
    public void ABurstLaunchesEveryParticleFromTheOrigin()
    {
        var sim = new ConfettiSimulation(new Random(1));
        sim.Emit(new ConfettiOptions { ParticleCount = 30 }, 100, 200);

        sim.Particles.Count.ShouldBe(30);
        sim.Particles.ShouldAllBe(p => p.X == 100 && p.Y == 200);
    }


    /// <summary>Angle 90 is up: with no spread every particle has to rise on its first frame.</summary>
    [Fact]
    public void NinetyDegreesLaunchesUpwards()
    {
        var sim = new ConfettiSimulation(new Random(2));
        sim.Emit(new ConfettiOptions { ParticleCount = 20, Spread = 0, Gravity = 0 }, 0, 500);

        sim.Step();

        sim.Particles.ShouldAllBe(p => p.Y < 500 && Math.Abs(p.X) < 0.0001);
    }


    [Fact]
    public void ParticlesDieAfterTheirTicks()
    {
        var sim = new ConfettiSimulation(new Random(3));
        sim.Emit(new ConfettiOptions { ParticleCount = 10, Ticks = 5 }, 0, 0);

        for (var i = 0; i < 4; i++)
            sim.Step();
        sim.IsEmpty.ShouldBeFalse();

        sim.Step();
        sim.IsEmpty.ShouldBeTrue();
    }


    [Fact]
    public void EmojiReplaceShapes()
    {
        var sim = new ConfettiSimulation(new Random(4));
        sim.Emit(new ConfettiOptions { ParticleCount = 10, Emoji = ["🦄"] }, 0, 0);

        sim.Particles.ShouldAllBe(p => p.Text == "🦄");
    }


    [Fact]
    public async Task ARunCompletesOnceEveryShotHasLaunchedAndFaded()
    {
        var canvas = new ConfettiCanvas(new Random(5));
        canvas.Layout(new Rect(0, 0, 400, 800));

        var task = canvas.Fire(
        [
            new ConfettiShot(TimeSpan.Zero, new ConfettiOptions { ParticleCount = 5, Ticks = 3 }),
            new ConfettiShot(TimeSpan.FromMilliseconds(100), new ConfettiOptions { ParticleCount = 5, Ticks = 3 })
        ]);

        canvas.Simulation.Particles.Count.ShouldBe(5);

        // The first shot's particles fade before the second shot is due - the run must not end there.
        for (var i = 0; i < 4; i++)
            canvas.Advance(TimeSpan.FromMilliseconds(17));
        task.IsCompleted.ShouldBeFalse();

        for (var i = 0; i < 10; i++)
            canvas.Advance(TimeSpan.FromMilliseconds(17));

        await task.WaitAsync(TimeSpan.FromSeconds(1));
        canvas.Simulation.IsEmpty.ShouldBeTrue();
    }


    [Fact]
    public void OriginsAreFractionsOfTheCanvas()
    {
        var canvas = new ConfettiCanvas(new Random(6));
        canvas.Layout(new Rect(0, 0, 400, 800));

        _ = canvas.Fire([new ConfettiShot(TimeSpan.Zero, new ConfettiOptions { ParticleCount = 1, OriginX = 0.25, OriginY = 0.75 })]);

        canvas.Simulation.Particles[0].X.ShouldBe(100);
        canvas.Simulation.Particles[0].Y.ShouldBe(600);
    }


    [Fact]
    public async Task ClearEndsTheRun()
    {
        var canvas = new ConfettiCanvas(new Random(7));
        canvas.Layout(new Rect(0, 0, 400, 800));

        var task = canvas.Fire(ConfettiPresets.Build(ConfettiPreset.Fireworks, null, new Random(7)));
        canvas.Clear();

        await task.WaitAsync(TimeSpan.FromSeconds(1));
        canvas.Simulation.IsEmpty.ShouldBeTrue();
    }


    [Theory]
    [InlineData(ConfettiPreset.Burst, 1)]
    [InlineData(ConfettiPreset.Random, 1)]
    [InlineData(ConfettiPreset.Stars, 6)]
    public void PresetsLaunchFromTheOrigin(ConfettiPreset preset, int shots)
    {
        var built = ConfettiPresets.Build(preset, new Point(0.2, 0.3), new Random(8));

        built.Count.ShouldBe(shots);
        built.ShouldAllBe(s => s.Options.OriginX == 0.2 && s.Options.OriginY == 0.3);
    }


    [Fact]
    public void FireworksTaperAndIgnoreTheOrigin()
    {
        var built = ConfettiPresets.Build(ConfettiPreset.Fireworks, new Point(0.5, 0.5), new Random(9));

        built.First().Options.ParticleCount.ShouldBeGreaterThan(built.Last().Options.ParticleCount);
        built.ShouldAllBe(s => s.Options.OriginX < 0.3 || s.Options.OriginX > 0.7);
    }


    [Fact]
    public void ACustomRecipeIsCopiedNotMutated()
    {
        var options = new ConfettiOptions { OriginX = 0.1 };

        var shots = ConfettiHost.Shots(ConfettiPreset.Burst, options, new Point(0.9, 0.9));

        shots.Single().Options.OriginX.ShouldBe(0.9);
        options.OriginX.ShouldBe(0.1);
    }


    /// <summary>Buttons ignore gesture recognizers, so they must be hooked through Clicked instead.</summary>
    [Fact]
    public void AButtonIsHookedThroughClickedNotAGesture()
    {
        var button = new Button();
        Confetti.SetTrigger(button, ConfettiTrigger.Tap);

        Confetti.GetHook(button).ShouldNotBeNull();
        Confetti.GetHook(button)!.Recognizer.ShouldBeNull();
        button.GestureRecognizers.ShouldBeEmpty();
    }


    [Fact]
    public void AnyOtherViewGetsATapRecognizerThatComesOffAgain()
    {
        var view = new BoxView();
        Confetti.SetTrigger(view, ConfettiTrigger.DoubleTap);

        var tap = view.GestureRecognizers.OfType<TapGestureRecognizer>().Single();
        tap.NumberOfTapsRequired.ShouldBe(2);

        Confetti.SetTrigger(view, ConfettiTrigger.None);

        view.GestureRecognizers.ShouldBeEmpty();
        Confetti.GetHook(view).ShouldBeNull();
    }


    [Fact]
    public void OptionsCloneCopiesTheLists()
    {
        var options = new ConfettiOptions();
        var clone = options.Clone();
        clone.Colors.Clear();
        clone.Shapes.Add(ConfettiShape.Star);

        options.Colors.Count.ShouldBe(ConfettiOptions.DefaultColors.Count);
        options.Shapes.ShouldNotContain(ConfettiShape.Star);
    }
}
