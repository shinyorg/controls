using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Shiny.Maui.Controls.Keyframe.Tests;

/// <summary>
/// The animation layer's promise: an animated value shows without taking the property over, so the
/// binding, style or visual state underneath keeps working — the thing FadeToAsync gets wrong.
/// </summary>
public class AnimationLayerTests
{
    public AnimationLayerTests() => TestDispatcherProvider.Install();

    sealed class ViewModel : INotifyPropertyChanged
    {
        double opacity = 1d;

        public double Opacity
        {
            get => opacity;
            set
            {
                opacity = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Opacity)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    static (Label Label, ViewModel Model) Bound()
    {
        var model = new ViewModel();
        var label = new Label { BindingContext = model };
        label.SetBinding(VisualElement.OpacityProperty, new Binding(nameof(ViewModel.Opacity)));
        return (label, model);
    }

    [Fact]
    public void AWriteShowsTheAnimatedValue()
    {
        var (label, _) = Bound();

        AnimationLayer.Write(label, VisualElement.OpacityProperty, 0.25d);

        Assert.Equal(0.25d, label.Opacity);
        Assert.True(AnimationLayer.IsAnimated(label, VisualElement.OpacityProperty));
    }

    [Fact]
    public void ABindingUpdateStillWinsAfterAnAnimatedWrite()
    {
        var (label, model) = Bound();
        AnimationLayer.Write(label, VisualElement.OpacityProperty, 0.25d);

        model.Opacity = 0.6d;

        Assert.Equal(0.6d, label.Opacity);
    }

    [Fact]
    public void APlainSetValueKillsTheBindingWhichIsTheBugThisAvoids()
    {
        var (label, model) = Bound();
        label.Opacity = 0.25d;

        model.Opacity = 0.6d;

        Assert.Equal(0.25d, label.Opacity);
    }

    [Fact]
    public void BaseValueFollowsTheBindingWhileAnimating()
    {
        var (label, model) = Bound();
        AnimationLayer.Write(label, VisualElement.OpacityProperty, 0.25d);

        model.Opacity = 0.6d;
        AnimationLayer.Write(label, VisualElement.OpacityProperty, 0.3d);

        Assert.Equal(0.6d, AnimationLayer.GetBaseValue(label, VisualElement.OpacityProperty));
        Assert.Equal(0.3d, label.Opacity);
    }

    [Fact]
    public void ReleaseLandsOnTheLiveBindingValue()
    {
        var (label, model) = Bound();
        AnimationLayer.Write(label, VisualElement.OpacityProperty, 0.25d);
        model.Opacity = 0.6d;
        AnimationLayer.Write(label, VisualElement.OpacityProperty, 0.3d);

        AnimationLayer.Release(label, VisualElement.OpacityProperty);

        Assert.Equal(0.6d, label.Opacity);
        Assert.False(AnimationLayer.IsAnimated(label, VisualElement.OpacityProperty));

        model.Opacity = 0.9d;
        Assert.Equal(0.9d, label.Opacity);
    }

    [Fact]
    public void AStyleValueSurvivesAndReturnsOnRelease()
    {
        var label = new Label
        {
            Style = new Style(typeof(Label)) { Setters = { new Setter { Property = VisualElement.OpacityProperty, Value = 0.5d } } }
        };

        AnimationLayer.Write(label, VisualElement.OpacityProperty, 0.1d);
        Assert.Equal(0.1d, label.Opacity);

        AnimationLayer.Release(label, VisualElement.OpacityProperty);
        Assert.Equal(0.5d, label.Opacity);
    }

    [Fact]
    public void ALocalValueSetDuringTheAnimationWins()
    {
        var label = new Label();
        AnimationLayer.Write(label, VisualElement.ScaleProperty, 2d);

        label.Scale = 3d;

        Assert.Equal(3d, label.Scale);
        AnimationLayer.Release(label, VisualElement.ScaleProperty);
        Assert.Equal(3d, label.Scale);
    }

    [Fact]
    public void ADynamicResourceStyleStaysLive()
    {
        var resources = new ResourceDictionary();
        var page = new ContentPage { Resources = resources };
        var label = new Label();
        label.SetDynamicResource(VisualElement.StyleProperty, "LabelStyle");
        resources["LabelStyle"] = new Style(typeof(Label))
        {
            Setters = { new Setter { Property = VisualElement.OpacityProperty, Value = 0.4d } }
        };
        page.Content = label;

        AnimationLayer.Write(label, VisualElement.OpacityProperty, 0.9d);
        AnimationLayer.Release(label, VisualElement.OpacityProperty);
        resources["LabelStyle"] = new Style(typeof(Label))
        {
            Setters = { new Setter { Property = VisualElement.OpacityProperty, Value = 0.7d } }
        };

        Assert.Equal(0.7d, label.Opacity);
    }

    [Fact]
    public void KeyframesTracksWriteThroughTheLayer()
    {
        var (label, model) = Bound();
        var keyframes = new Keyframes { Duration = TimeSpan.FromSeconds(1) };
        keyframes.Tracks.Add(new Track
        {
            Property = "Opacity",
            Keys = { new Key { Offset = 0, Value = "0" }, new Key { Offset = 1, Value = "1" } }
        });

        var timeline = keyframes.Build(label);
        timeline.CaptureBaselines();
        timeline.Evaluate(TimeSpan.FromMilliseconds(500));

        Assert.Equal(0.5d, label.Opacity, 6);

        // The binding is still live underneath, and the end of a fill-less run hands back to it.
        model.Opacity = 0.8d;
        timeline.Evaluate(TimeSpan.FromMilliseconds(600));
        Assert.Equal(0.6d, label.Opacity, 6);

        timeline.Evaluate(TimeSpan.FromSeconds(2));
        Assert.Equal(0.8d, label.Opacity, 6);
    }
}
