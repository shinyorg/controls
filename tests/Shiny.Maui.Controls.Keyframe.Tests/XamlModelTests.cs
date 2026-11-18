namespace Shiny.Maui.Controls.Keyframe.Tests;

public class XamlModelTests
{
    [Theory]
    [InlineData("60%", 0.6d)]
    [InlineData("0.25", 0.25d)]
    [InlineData(" 100% ", 1d)]
    public void CueReadsPercentagesAndFractions(string text, double expected)
        => Assert.Equal(expected, CueTypeConverter.Parse(text), 6);

    [Fact]
    public void KeyFrameEasingArrivesAtItsFrame()
    {
        var label = new Label();
        var keyframes = new Keyframes { Duration = TimeSpan.FromSeconds(1) };
        keyframes.Frames.Add(new KeyFrame { Cue = 0, Setters = { new Setter { Property = VisualElement.ScaleProperty, Value = 0d } } });
        keyframes.Frames.Add(new KeyFrame { Cue = 1, Easing = Easings.Hold, Setters = { new Setter { Property = VisualElement.ScaleProperty, Value = 1d } } });

        var timeline = keyframes.Build(label);
        timeline.CaptureBaselines();
        timeline.Evaluate(TimeSpan.FromMilliseconds(900));

        // Hold on the arriving frame keeps the segment on its starting value until it ends.
        Assert.Equal(0d, label.Scale, 6);
    }

    [Fact]
    public void APropertyFirstSetAfterZeroStartsFromItsLiveValue()
    {
        var label = new Label { Opacity = 0.2d };
        var keyframes = new Keyframes { Duration = TimeSpan.FromSeconds(1) };
        keyframes.Frames.Add(new KeyFrame { Cue = 0.5, Setters = { new Setter { Property = VisualElement.OpacityProperty, Value = 1d } } });

        var timeline = keyframes.Build(label);
        timeline.CaptureBaselines();
        timeline.Evaluate(TimeSpan.FromMilliseconds(250));

        Assert.Equal(0.6d, label.Opacity, 6);
    }

    [Fact]
    public void KeyFramesReachUnregisteredProperties()
    {
        var label = new Label { TextColor = Colors.Black };
        var keyframes = new Keyframes { Duration = TimeSpan.FromSeconds(1) };
        keyframes.Frames.Add(new KeyFrame { Cue = 0, Setters = { new Setter { Property = Label.FontSizeProperty, Value = "10" } } });
        keyframes.Frames.Add(new KeyFrame { Cue = 1, Setters = { new Setter { Property = Label.FontSizeProperty, Value = "20" } } });

        var timeline = keyframes.Build(label);
        timeline.CaptureBaselines();
        timeline.Evaluate(TimeSpan.FromMilliseconds(500));

        Assert.Equal(15d, label.FontSize, 6);
    }

    [Fact]
    public void ContentRoutesTracksAndFrames()
    {
        var keyframes = new Keyframes();
        keyframes.Content.Add(new Track());
        keyframes.Content.Add(new KeyFrame());

        Assert.Single(keyframes.Tracks);
        Assert.Single(keyframes.Frames);
        Assert.Throws<InvalidOperationException>(() => keyframes.Content.Add(new Label()));
    }

    [Fact]
    public void IterationAndEndDelaysReachTheTiming()
    {
        var keyframes = new Keyframes
        {
            Duration = TimeSpan.FromSeconds(1),
            Iterations = 2,
            IterationDelay = TimeSpan.FromMilliseconds(500),
            EndDelay = TimeSpan.FromMilliseconds(250)
        };
        keyframes.Tracks.Add(new Track { Property = "Opacity", Keys = { new Key { Offset = 0, Value = "0" }, new Key { Offset = 1, Value = "1" } } });

        var timeline = keyframes.Build(new Label());

        Assert.Equal(TimeSpan.FromMilliseconds(2750), timeline.TotalDuration);
    }

    static Keyframes Fade(double seconds = 1)
    {
        var keyframes = new Keyframes { Duration = TimeSpan.FromSeconds(seconds), Fill = FillMode.Both };
        keyframes.Tracks.Add(new Track { Property = "Opacity", Keys = { new Key { Offset = 0, Value = "0" }, new Key { Offset = 1, Value = "1" } } });
        return keyframes;
    }

    [Fact]
    public void ChoreographyStaggersChildrenOfTheTarget()
    {
        var stack = new VerticalStackLayout { new Label(), new Label(), new Label() };
        var choreography = new Choreography { ApplyTo = ChoreographyTarget.Children, Stagger = TimeSpan.FromMilliseconds(100) };
        choreography.Children.Add(Fade());

        var node = choreography.Build(stack);
        node.CaptureBaselines();
        node.Evaluate(TimeSpan.FromMilliseconds(500));

        var labels = stack.Children.Cast<Label>().ToArray();
        Assert.Equal(0.5d, labels[0].Opacity, 6);
        Assert.Equal(0.4d, labels[1].Opacity, 6);
        Assert.Equal(0.3d, labels[2].Opacity, 6);
        Assert.Equal(TimeSpan.FromMilliseconds(1200), node.TotalDuration);
    }

    [Fact]
    public void ChoreographySequenceRunsChildrenBackToBack()
    {
        var choreography = new Choreography { Mode = ChoreographyMode.Sequence, Stagger = TimeSpan.FromMilliseconds(250) };
        choreography.Children.Add(Fade());
        choreography.Children.Add(Fade(0.5));

        var node = choreography.Build(new Label());

        Assert.Equal(TimeSpan.FromMilliseconds(1750), node.TotalDuration);
    }

    [Fact]
    public void ValueBlendingTreatsTrueAsVisibleThroughout()
    {
        Assert.Equal(true, ValueBlending.Blend(typeof(bool), true, false, 0.9d));
        Assert.Equal(false, ValueBlending.Blend(typeof(bool), true, false, 1d));
    }

    [Fact]
    public void ValueBlendingBlendsThicknessAndCornerRadius()
    {
        Assert.Equal(new Thickness(5), ValueBlending.Blend(typeof(Thickness), new Thickness(0), new Thickness(10), 0.5d));
        Assert.Equal(new CornerRadius(4), ValueBlending.Blend(typeof(CornerRadius), new CornerRadius(0), new CornerRadius(8), 0.5d));
    }

    [Fact]
    public void ValueBlendingSwitchesDiscreteValuesHalfway()
    {
        Assert.Equal(LayoutOptions.Start, ValueBlending.Blend(typeof(LayoutOptions), LayoutOptions.Start, LayoutOptions.End, 0.4d));
        Assert.Equal(LayoutOptions.End, ValueBlending.Blend(typeof(LayoutOptions), LayoutOptions.Start, LayoutOptions.End, 0.6d));
    }

    [Fact]
    public void VisualTransitionSpecificityPrefersTheNarrowestMatch()
    {
        var any = new VisualTransition();
        var toPressed = new VisualTransition { To = "Pressed" };
        var exact = new VisualTransition { From = "Normal", To = "Pressed" };
        var otherGroup = new VisualTransition { Group = "Focus", To = "Pressed" };

        Assert.True(exact.Score("CommonStates", "Normal", "Pressed") > toPressed.Score("CommonStates", "Normal", "Pressed"));
        Assert.True(toPressed.Score("CommonStates", "Normal", "Pressed") > any.Score("CommonStates", "Normal", "Pressed"));
        Assert.Equal(-1, otherGroup.Score("CommonStates", "Normal", "Pressed"));
    }
}
