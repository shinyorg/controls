namespace Shiny.Maui.Controls.Keyframe.Tests;

/// <summary>
/// Visual-state and implicit transitions start from the value that was on screen and land on the
/// value the state or setter owns, through the animation layer.
/// </summary>
public class TransitionTests
{
    public TransitionTests() => TestDispatcherProvider.Install();

    static Label WithStates(params VisualTransition[] transitions)
    {
        var label = StubHandler.Attach(new Label());

        var group = new VisualStateGroup { Name = "Size" };
        group.States.Add(new VisualState { Name = "Small", Setters = { new Setter { Property = VisualElement.ScaleProperty, Value = 1d } } });
        group.States.Add(new VisualState { Name = "Big", Setters = { new Setter { Property = VisualElement.ScaleProperty, Value = 2d } } });
        VisualStateManager.SetVisualStateGroups(label, [group]);

        foreach (var transition in transitions)
            StateTransitions.GetTransitions(label).Add(transition);

        VisualStateManager.GoToState(label, "Small");
        return label;
    }

    [Fact]
    public void AStateChangeStartsFromTheOldValueAndTheStateOwnsTheNewOne()
    {
        var label = WithStates(new VisualTransition { Duration = TimeSpan.FromSeconds(1) });

        VisualStateManager.GoToState(label, "Big");

        Assert.Equal(1d, label.Scale, 6);
        Assert.True(AnimationLayer.IsAnimated(label, VisualElement.ScaleProperty));
        Assert.Equal(2d, AnimationLayer.GetBaseValue(label, VisualElement.ScaleProperty));
    }

    [Fact]
    public void AStateChangeWithNoMatchingTransitionSnaps()
    {
        var label = WithStates(new VisualTransition { To = "Elsewhere", Duration = TimeSpan.FromSeconds(1) });

        VisualStateManager.GoToState(label, "Big");

        Assert.Equal(2d, label.Scale, 6);
    }

    [Fact]
    public void UseTransitionsFalseSnapsOneChange()
    {
        var label = WithStates(new VisualTransition { Duration = TimeSpan.FromSeconds(1) });

        StateTransitions.GoToState(label, "Big", useTransitions: false);

        Assert.Equal(2d, label.Scale, 6);
    }

    [Fact]
    public void ReducedMotionInstantLandsOnTheStateValue()
    {
        var label = WithStates(new VisualTransition { Duration = TimeSpan.FromSeconds(1), ReducedMotion = ReducedMotionBehavior.Instant });

        VisualStateManager.GoToState(label, "Big");

        Assert.Equal(2d, label.Scale, 6);
        Assert.False(AnimationLayer.IsAnimated(label, VisualElement.ScaleProperty));
    }

    [Fact]
    public void InterruptingContinuesFromTheValueOnScreen()
    {
        var label = WithStates(new VisualTransition { Duration = TimeSpan.FromSeconds(1) });
        VisualStateManager.GoToState(label, "Big");
        AnimationLayer.Write(label, VisualElement.ScaleProperty, 1.5d); // as if the transition were halfway

        VisualStateManager.GoToState(label, "Small");

        Assert.Equal(1.5d, label.Scale, 6);
        Assert.Equal(1d, AnimationLayer.GetBaseValue(label, VisualElement.ScaleProperty));
    }

    [Fact]
    public void AnImplicitTransitionStartsFromTheOldValue()
    {
        var box = StubHandler.Attach(new BoxView());
        PropertyTransitions.GetTransitions(box).Add(new PropertyTransition { Property = "Opacity", Duration = TimeSpan.FromSeconds(1) });

        box.Opacity = 0.2d;

        Assert.Equal(1d, box.Opacity, 6);
        Assert.Equal(0.2d, AnimationLayer.GetBaseValue(box, VisualElement.OpacityProperty));
    }

    [Fact]
    public void ImplicitTransitionsIgnoreChangesBeforeTheHandlerExists()
    {
        var box = new BoxView();
        PropertyTransitions.GetTransitions(box).Add(new PropertyTransition { Property = "Opacity", Duration = TimeSpan.FromSeconds(1) });

        box.Opacity = 0.2d;

        Assert.Equal(0.2d, box.Opacity, 6);
    }

    [Fact]
    public void AllCoversRegisteredProperties()
    {
        var box = StubHandler.Attach(new BoxView());
        PropertyTransitions.GetTransitions(box).Add(new PropertyTransition { Property = "All", Duration = TimeSpan.FromSeconds(1) });

        box.TranslationX = 50d;

        Assert.Equal(0d, box.TranslationX, 6);
        Assert.Equal(50d, AnimationLayer.GetBaseValue(box, VisualElement.TranslationXProperty));
    }

    [Fact]
    public void ANullColourFadesFromTransparent()
    {
        var box = StubHandler.Attach(new BoxView());
        PropertyTransitions.GetTransitions(box).Add(new PropertyTransition { Property = "BackgroundColor", Duration = TimeSpan.FromSeconds(1) });

        box.BackgroundColor = Colors.Red;

        Assert.Equal(0f, box.BackgroundColor.Alpha, 3);
    }

    [Fact]
    public void BeginAndStopActionsReachTheSameRun()
    {
        var box = StubHandler.Attach(new BoxView());
        var pulse = new Keyframes { Duration = TimeSpan.FromSeconds(1), Iterations = double.PositiveInfinity, Fill = FillMode.Both };
        pulse.Tracks.Add(new Track { Property = "Scale", Keys = { new Key { Offset = 0, Value = "1.5" }, new Key { Offset = 1, Value = "2" } } });

        box.Play(pulse);
        Assert.Equal(1.5d, box.Scale, 6);
        Assert.NotNull(box.GetPlayer(pulse));

        box.StopAnimations();
        Assert.Equal(1d, box.Scale, 6);
        Assert.Null(box.GetPlayer(pulse));
    }
}
