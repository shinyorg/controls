using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.Maui.Controls.Infrastructure;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

[Collection(ApplicationResourcesCollection.Name)]
public class NotchViewTests
{
    public NotchViewTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
    }


    [Fact]
    public void EmptyNotchSitsAtItsMinimumSize()
    {
        var notch = new NotchView();

        notch.NotchSize.ShouldBe(new Size(120, 36));
        notch.SurfaceSize.ShouldBe(new Size(120, 36));
        StyleGuard.HasPending(notch).ShouldBeFalse();
    }


    [Fact]
    public void SizesToContentPlusPadding()
    {
        var notch = new NotchView { ContentPadding = new Thickness(10, 5) };

        notch.Content = new BoxView { WidthRequest = 300, HeightRequest = 80 };
        notch.Refresh();

        notch.NotchSize.ShouldBe(new Size(320, 90));
        notch.SurfaceSize.ShouldBe(new Size(320, 90));
    }


    [Fact]
    public void ResizesWhenTheContentIsSwapped()
    {
        var notch = new NotchView { ContentPadding = new Thickness(0) };
        var sizes = new List<Size>();
        notch.NotchSizeChanged += (_, e) => sizes.Add(e.NewSize);

        notch.Content = new BoxView { WidthRequest = 400, HeightRequest = 200 };
        notch.Refresh();
        notch.Content = new BoxView { WidthRequest = 150, HeightRequest = 40 };
        notch.Refresh();

        sizes.ShouldBe([new Size(400, 200), new Size(150, 40)]);
        notch.SurfaceSize.ShouldBe(new Size(150, 40));
    }


    [Fact]
    public void ResizesWhenTheContentChangesItsOwnSize()
    {
        var box = new BoxView { WidthRequest = 200, HeightRequest = 50 };
        var notch = new NotchView { ContentPadding = new Thickness(0), Content = box };
        notch.Refresh();

        box.HeightRequest = 120;
        notch.Refresh();

        notch.NotchSize.ShouldBe(new Size(200, 120));
    }


    [Fact]
    public void ClearingTheContentShrinksBackToTheMinimum()
    {
        var notch = new NotchView { Content = new BoxView { WidthRequest = 300, HeightRequest = 100 } };
        notch.Refresh();

        notch.Content = null;
        notch.Refresh();

        notch.NotchSize.ShouldBe(new Size(120, 36));
    }


    [Fact]
    public void NeverGrowsPastTheMaximum()
    {
        var notch = new NotchView
        {
            ContentPadding = new Thickness(0),
            MaxNotchWidth = 250,
            MaxNotchHeight = 60,
            Content = new BoxView { WidthRequest = 500, HeightRequest = 300 }
        };
        notch.Refresh();

        notch.NotchSize.ShouldBe(new Size(250, 60));
    }


    [Fact]
    public void DoesNotRaiseSizeChangedWhenNothingMoved()
    {
        var notch = new NotchView { Content = new BoxView { WidthRequest = 200, HeightRequest = 50 } };
        notch.Refresh();

        var raised = 0;
        notch.NotchSizeChanged += (_, _) => raised++;
        notch.Refresh();
        notch.Refresh();

        raised.ShouldBe(0);
    }


    [Theory]
    [InlineData(300, 600, 300)]   // max wins
    [InlineData(800, 400, 400)]   // offered width wins
    [InlineData(double.PositiveInfinity, double.PositiveInfinity, 1000)] // unbounded
    public void WidthIsCappedByTheSmallerOfMaxAndOffered(double max, double available, double expected)
    {
        var size = NotchView.ComputeNotchSize(new Size(1000, 40), new Thickness(0), 100, 30, max, double.PositiveInfinity, available);
        size.Width.ShouldBe(expected);
    }


    [Fact]
    public void MinimumWinsOverACapSmallerThanIt()
    {
        var size = NotchView.ComputeNotchSize(new Size(50, 10), new Thickness(0), 200, 40, 150, 30, double.PositiveInfinity);
        size.ShouldBe(new Size(200, 40));
    }


    [Theory]
    [InlineData(-1, 300, 36, 18)]    // auto, short: a full pill
    [InlineData(-1, 300, 200, 28)]   // auto, tall: capped
    [InlineData(10, 300, 200, 10)]   // explicit
    [InlineData(500, 300, 40, 20)]   // explicit, clamped to half the height
    [InlineData(500, 30, 200, 15)]   // explicit, clamped to half the width
    public void CornerRadiusResolves(double requested, double width, double height, double expected)
        => NotchView.ResolveCornerRadius(requested, width, height).ShouldBe(expected);


    [Fact]
    public void NotchShapeSquaresTheTopCorners()
    {
        var notch = new NotchView { Shape = NotchShape.Notch };

        notch.SurfaceCorners.TopLeft.ShouldBe(0);
        notch.SurfaceCorners.TopRight.ShouldBe(0);
        notch.SurfaceCorners.BottomLeft.ShouldBe(18);
        notch.SurfaceCorners.BottomRight.ShouldBe(18);
    }


    [Fact]
    public void IslandShapeRoundsEveryCorner()
    {
        var notch = new NotchView();

        notch.SurfaceCorners.ShouldBe(new CornerRadius(18));
    }


    [Fact]
    public void TappingTheSurfaceRaisesTappedAndRunsTheCommand()
    {
        object? received = null;
        var tapped = 0;
        var notch = new NotchView
        {
            Command = new Command(p => received = p),
            CommandParameter = "expand"
        };
        notch.Tapped += (_, _) => tapped++;

        notch.SurfaceTapCommand!.Execute(null);

        tapped.ShouldBe(1);
        received.ShouldBe("expand");
    }


    [Fact]
    public void StyledPropertiesApply()
    {
        var style = new Style(typeof(NotchView));
        style.Setters.Add(new Setter { Property = NotchView.MinNotchWidthProperty, Value = 200d });
        style.Setters.Add(new Setter { Property = NotchView.ShapeProperty, Value = NotchShape.Notch });

        var notch = new NotchView { Style = style };

        notch.NotchSize.Width.ShouldBe(200);
        notch.SurfaceCorners.TopLeft.ShouldBe(0);
        StyleGuard.HasPending(notch).ShouldBeFalse();
    }


    [Fact]
    public void FillingAHostReportsTheSizeWithoutSizingTheSurface()
    {
        var notch = new NotchView
        {
            FillsHost = true,
            FollowsOfferedWidth = false,
            ContentPadding = new Thickness(0),
            Content = new BoxView { WidthRequest = 500, HeightRequest = 120 }
        };
        var completed = 0;
        notch.MorphCompleted += (_, _) => completed++;

        notch.Content = new BoxView { WidthRequest = 300, HeightRequest = 80 };
        notch.Refresh();

        notch.NotchSize.ShouldBe(new Size(300, 80));
        notch.SurfaceSize.ShouldBe(new Size(-1, -1));
        completed.ShouldBe(1);
    }


    [Fact]
    public void FirstContentIsShownAtOnceByDefault()
    {
        var content = new BoxView { WidthRequest = 300, HeightRequest = 80 };
        var notch = new NotchView { FillsHost = true, FollowsOfferedWidth = false, Content = content };
        notch.Refresh();

        ((VisualElement)content.Parent!).Opacity.ShouldBe(1);
    }


    [Fact]
    public void FirstContentStartsHiddenToFadeInWhenAskedTo()
    {
        // The desktop notch window builds a fresh notch for every swap, so the content it swaps in is
        // always the notch's first - and still has to fade in as the window morphs to it.
        var content = new BoxView { WidthRequest = 300, HeightRequest = 80 };
        var notch = new NotchView { FillsHost = true, FollowsOfferedWidth = false, FadeInFirstContent = true, Content = content };
        notch.Refresh();

        // No native view yet: the fade waits for one rather than snapping to visible.
        ((VisualElement)content.Parent!).Opacity.ShouldBe(0);
    }


    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(520d)]
    public void ContentWithAFixedWidthIsMeasuredAtThatWidth(double maxWidth)
    {
        // A stack with WidthRequest hands its children the width IT was offered, not its own, so text
        // inside measured as one long line and then wrapped onto lines the notch had no room for.
        var notch = new NotchView
        {
            ContentPadding = new Thickness(0),
            MaxNotchWidth = maxWidth,
            Content = new WrappingProbe { WidthRequest = 280 }
        };
        notch.Refresh();

        notch.NotchSize.ShouldBe(new Size(280, WrappingProbe.HeightAt(280)));
    }


    [Fact]
    public void ContentMaximumWidthCapsTheMeasure()
    {
        var notch = new NotchView
        {
            ContentPadding = new Thickness(0),
            Content = new WrappingProbe { MaximumWidthRequest = 300 }
        };
        notch.Refresh();

        notch.NotchSize.ShouldBe(new Size(300, WrappingProbe.HeightAt(300)));
    }


    [Fact]
    public void OfferedWidthCapLeavesOutTheMargin()
    {
        // The width a parent offers includes the notch's own margin, and the surface has to fit inside it.
        var notch = new NotchView
        {
            Margin = new Thickness(16, 0),
            ContentPadding = new Thickness(0),
            Content = new WrappingProbe()
        };

        ((IView)notch).Measure(400, double.PositiveInfinity);

        notch.NotchSize.ShouldBe(new Size(368, WrappingProbe.HeightAt(368)));
    }


    /// <summary>
    /// Behaves like a stack with wrapping text in it: it sizes from the constraint it is offered and
    /// ignores its own width request, as a layout does when it measures its children. (A real Label and
    /// stack can't measure without a handler.)
    /// </summary>
    sealed class WrappingProbe : View
    {
        const double TextWidth = 900;
        const double LineHeight = 20;

        public static double HeightAt(double width) => Math.Ceiling(TextWidth / width) * LineHeight;

        protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
        {
            var width = Math.Min(TextWidth, widthConstraint);
            return new Size(width, HeightAt(width));
        }
    }
}
