using Shiny.Maui.Controls.Cells;
using Shiny.Maui.Controls.Images;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Pages;
using Shiny.Maui.Controls.Themes;
using Shiny.Maui.Controls.Toast;
using Shouldly;
using Xunit;
using Editor = Shiny.Maui.Controls.ImageEditor.ImageEditor;
using EditorIcon = Shiny.Maui.Controls.ImageEditor.ImageEditorIconDrawable;
using EditorMode = Shiny.Maui.Controls.ImageEditor.ImageEditorToolMode;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// The second sweep of colours that were copied out of <c>Application.Current.Resources</c> once - or,
/// for the loading ring, on every draw but only ever from the application. Each now follows a
/// light/dark flip on views that are already built, honours a palette scoped over the control, and
/// still lets an explicit colour win.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class ThemeFlipSnapshotTests
{
    readonly Application app;
    readonly BasicTheme theme = new();

    public ThemeFlipSnapshotTests()
    {
        TestDispatcherProvider.Install();
        this.app = new Application { UserAppTheme = AppTheme.Light };
        this.app.Resources.MergedDictionaries.Add(this.theme.Light);
    }

    Color Light(string key) => (Color)this.theme.Light[key];

    Color Dark(string key) => (Color)this.theme.Dark[key];

    void FlipToDark()
    {
        this.app.Resources.MergedDictionaries.Remove(this.theme.Light);
        this.app.Resources.MergedDictionaries.Add(this.theme.Dark);
    }

    ContentPage Host(View view)
    {
        var page = new ContentPage { Content = view };
        page.Parent = this.app;
        return page;
    }

    /// <summary>Hosts under a container whose own resources pin the dark palette while the app stays light.</summary>
    /// <remarks>
    /// The scope is attached to the app before the view goes into it. The other order - a subtree built
    /// bottom-up, scope and all, then parented - loses the scoped palette to the app's for every
    /// control, not just these: MAUI pushes the app's resources down on attach without skipping keys
    /// the scope only holds in a merged dictionary.
    /// </remarks>
    ContentPage HostScopedDark(View view)
    {
        var scope = new ContentView();
        scope.Resources.MergedDictionaries.Add(this.theme.Dark);
        var page = this.Host(scope);
        scope.Content = view;
        return page;
    }

    [Fact]
    public void ThePaletteActuallyDiffers()
    {
        foreach (var key in new[]
        {
            ShinyThemeKeys.Color.Primary, ShinyThemeKeys.Color.OnPrimary, ShinyThemeKeys.Color.InverseSurface,
            ShinyThemeKeys.Color.InverseOnSurface, ShinyThemeKeys.Color.SurfaceContainerHigh,
            ShinyThemeKeys.Color.SurfaceContainerHighest, ShinyThemeKeys.Color.OnSurface, ShinyThemeKeys.Color.Success
        })
            Light(key).ShouldNotBe(Dark(key), key);
    }


    // ---------------------------------------------------------------------------------------------
    // ImageEditor accent
    // ---------------------------------------------------------------------------------------------

    static Button ApplyButton(Editor editor)
        => ((IVisualTreeElement)editor).GetVisualTreeDescendants().OfType<Button>().Single(b => b.Text == editor.CropApplyText);

    [Fact]
    public void ImageEditorCropApplyFollowsAFlipAndAScopedPalette()
    {
        var editor = new Editor();
        this.Host(editor);
        editor.CurrentToolMode = EditorMode.Crop;
        var apply = ApplyButton(editor);
        apply.BackgroundColor.ShouldBe(Light(ShinyThemeKeys.Color.Primary));
        apply.TextColor.ShouldBe(Light(ShinyThemeKeys.Color.OnPrimary));

        this.FlipToDark();
        apply.BackgroundColor.ShouldBe(Dark(ShinyThemeKeys.Color.Primary), "unfixed, the accent was copied when the crop bar was built");
        apply.TextColor.ShouldBe(Dark(ShinyThemeKeys.Color.OnPrimary));

        var scoped = new Editor();
        this.HostScopedDark(scoped);
        this.app.Resources.MergedDictionaries.Remove(this.theme.Dark);
        this.app.Resources.MergedDictionaries.Add(this.theme.Light);
        scoped.CurrentToolMode = EditorMode.Crop;
        ApplyButton(scoped).BackgroundColor.ShouldBe(Dark(ShinyThemeKeys.Color.Primary));
    }

    [Fact]
    public void ImageEditorSelectedStrokeWidthFollowsAFlip()
    {
        var editor = new Editor { DrawStrokeWidth = 4 };
        var row = new HorizontalStackLayout();
        this.Host(new VerticalStackLayout { Children = { editor, row } });

        var selected = (Border)editor.CreateStrokeWidthButton(4);
        var unselected = (Border)editor.CreateStrokeWidthButton(8);
        row.Children.Add(selected);
        row.Children.Add(unselected);

        selected.BackgroundColor.ShouldBe(Light(ShinyThemeKeys.Color.Primary));
        ((Border)selected.Content!).BackgroundColor.ShouldBe(Light(ShinyThemeKeys.Color.OnPrimary));
        unselected.BackgroundColor.ShouldBe(Colors.Transparent);

        this.FlipToDark();

        selected.BackgroundColor.ShouldBe(Dark(ShinyThemeKeys.Color.Primary));
        ((Border)selected.Content!).BackgroundColor.ShouldBe(Dark(ShinyThemeKeys.Color.OnPrimary));
        unselected.BackgroundColor.ShouldBe(Colors.Transparent);
    }

    [Fact]
    public void ImageEditorShapeFillToggleFollowsAFlipAndComesBackToTheAccent()
    {
        var editor = new Editor();
        var row = new HorizontalStackLayout();
        this.Host(new VerticalStackLayout { Children = { editor, row } });

        var toggle = editor.CreateShapeFillToggle();
        row.Children.Add(toggle);
        var icon = (ThemedIconView)toggle.Content!;
        var drawable = (EditorIcon)icon.Drawable;

        editor.ShapeFillColor = Colors.Red;
        toggle.BackgroundColor.ShouldBe(Light(ShinyThemeKeys.Color.Primary));
        drawable.Color.ShouldBe(Light(ShinyThemeKeys.Color.OnPrimary));

        // Off -> transparent local value; back on must clear it or the accent never returns.
        editor.ShapeFillColor = null;
        toggle.BackgroundColor.ShouldBe(Colors.Transparent);
        drawable.Color.ShouldNotBe(Light(ShinyThemeKeys.Color.OnPrimary));

        editor.ShapeFillColor = Colors.Red;
        this.FlipToDark();
        toggle.BackgroundColor.ShouldBe(Dark(ShinyThemeKeys.Color.Primary));
        drawable.Color.ShouldBe(Dark(ShinyThemeKeys.Color.OnPrimary), "the drawable is what paints");
    }


    // ---------------------------------------------------------------------------------------------
    // SkeletonView shimmer
    // ---------------------------------------------------------------------------------------------

    static Color ExpectedSheen(Color baseColor)
        => baseColor.GetLuminosity() >= 0.98f ? baseColor.AddLuminosity(-0.06f) : baseColor.AddLuminosity(0.1f);

    // The sheen brush is built and re-tinted whether or not the sweep animates; turning the animation
    // off keeps the headless test from needing an animation manager.
    static SkeletonView BusySkeleton()
        => new() { ShimmerEnabled = false, IsBusy = true, ItemCount = 2 };

    [Fact]
    public void SkeletonShimmerFollowsAFlipWhileBusy()
    {
        var skeleton = BusySkeleton();
        this.Host(skeleton);
        skeleton.Layout(new Rect(0, 0, 300, 200));

        skeleton.CurrentSheenHighlight.ShouldBe(ExpectedSheen(Light(ShinyThemeKeys.Color.SurfaceContainerHigh)));

        this.FlipToDark();

        skeleton.CurrentSheenHighlight.ShouldBe(ExpectedSheen(Dark(ShinyThemeKeys.Color.SurfaceContainerHigh)),
            "unfixed, the sweep kept the light palette's highlight over the now-dark bars");
    }

    [Fact]
    public void SkeletonShimmerHonoursAScopedPaletteAndAnExplicitColour()
    {
        var scoped = BusySkeleton();
        this.HostScopedDark(scoped);
        scoped.Layout(new Rect(0, 0, 300, 200));
        scoped.CurrentSheenHighlight.ShouldBe(ExpectedSheen(Dark(ShinyThemeKeys.Color.SurfaceContainerHigh)));

        var explicitBase = BusySkeleton();
        explicitBase.BaseColor = Colors.Teal;
        this.Host(explicitBase);
        explicitBase.Layout(new Rect(0, 0, 300, 200));
        this.FlipToDark();
        explicitBase.CurrentSheenHighlight.ShouldBe(ExpectedSheen(Colors.Teal));

        var explicitSheen = BusySkeleton();
        explicitSheen.ShimmerColor = Colors.Orange;
        this.Host(explicitSheen);
        explicitSheen.Layout(new Rect(0, 0, 300, 200));
        this.app.Resources.MergedDictionaries.Remove(this.theme.Dark);
        this.app.Resources.MergedDictionaries.Add(this.theme.Light);
        explicitSheen.CurrentSheenHighlight.ShouldBe(Colors.Orange);
    }


    // ---------------------------------------------------------------------------------------------
    // Toast
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void UntypedToastFollowsAFlip()
    {
        var toast = new ToastView(new ToastConfig { Text = "hi", Spinner = ToastSpinnerPosition.Left });
        this.Host(toast);

        toast.Chrome.BackgroundColor.ShouldBe(Light(ShinyThemeKeys.Color.InverseSurface));
        toast.TextLabel.TextColor.ShouldBe(Light(ShinyThemeKeys.Color.InverseOnSurface));
        toast.Spinner!.Color.ShouldBe(Light(ShinyThemeKeys.Color.InverseOnSurface));

        this.FlipToDark();

        toast.Chrome.BackgroundColor.ShouldBe(Dark(ShinyThemeKeys.Color.InverseSurface), "unfixed, the colours were copied when the toast was built");
        toast.TextLabel.TextColor.ShouldBe(Dark(ShinyThemeKeys.Color.InverseOnSurface));
        toast.Spinner!.Color.ShouldBe(Dark(ShinyThemeKeys.Color.InverseOnSurface));
    }

    [Fact]
    public void TypedToastFollowsAFlip()
    {
        // Unfixed this never followed the token at all: the hex fallback was written as a local value
        // first, and a local value outranks the dynamic resource set on top of it.
        var toast = new ToastView(new ToastConfig { Text = "ok", Type = ToastType.Success, BorderThickness = 1 });
        this.Host(toast);

        toast.Chrome.BackgroundColor.ShouldBe(Light(ShinyThemeKeys.Color.Success));
        toast.TextLabel.TextColor.ShouldBe(Light(ShinyThemeKeys.Color.OnSuccess));
        ((SolidColorBrush)toast.Chrome.Stroke).Color.ShouldBe(Light(ShinyThemeKeys.Color.SuccessContainer));

        this.FlipToDark();

        toast.Chrome.BackgroundColor.ShouldBe(Dark(ShinyThemeKeys.Color.Success));
        toast.TextLabel.TextColor.ShouldBe(Dark(ShinyThemeKeys.Color.OnSuccess));
        ((SolidColorBrush)toast.Chrome.Stroke).Color.ShouldBe(Dark(ShinyThemeKeys.Color.SuccessContainer));
    }

    [Fact]
    public void ToastHonoursAScopedPaletteAndExplicitColours()
    {
        var scoped = new ToastView(new ToastConfig { Text = "hi" });
        this.HostScopedDark(scoped);
        scoped.Chrome.BackgroundColor.ShouldBe(Dark(ShinyThemeKeys.Color.InverseSurface));

        var explicitColours = new ToastView(new ToastConfig { Text = "hi", BackgroundColor = Colors.Purple, TextColor = Colors.Yellow });
        this.Host(explicitColours);
        this.FlipToDark();
        explicitColours.Chrome.BackgroundColor.ShouldBe(Colors.Purple);
        explicitColours.TextLabel.TextColor.ShouldBe(Colors.Yellow);
    }

    [Fact]
    public void AToastOutsideAnyThemeKeepsItsFallbacks()
    {
        this.app.Resources.MergedDictionaries.Remove(this.theme.Light);
        var toast = new ToastView(new ToastConfig { Text = "hi" });
        toast.Chrome.BackgroundColor.ShouldBe(Color.FromArgb("#323232"));
        toast.TextLabel.TextColor.ShouldBe(Colors.White);
    }


    // ---------------------------------------------------------------------------------------------
    // PickerPage accent
    // ---------------------------------------------------------------------------------------------

    static Label CheckMark(PickerPage page)
    {
        var collection = (CollectionView)page.Content;
        var row = (Grid)collection.ItemTemplate.CreateContent();
        return row.Children.OfType<Label>().Single(l => l.Text == "✓");
    }

    [Fact]
    public void PickerPageCheckmarkFollowsAFlip()
    {
        var cell = new PickerCell { ItemsSource = new[] { "a", "b" } };
        this.Host(cell);
        var check = CheckMark(new PickerPage(cell));

        check.TextColor.ShouldBe(Light(ShinyThemeKeys.Color.Primary));

        this.FlipToDark();

        check.TextColor.ShouldBe(Dark(ShinyThemeKeys.Color.Primary), "unfixed, the accent was copied when the page was built");
    }

    [Fact]
    public void PickerPageCheckmarkHonoursAPaletteScopedOverTheCellAndAnExplicitAccent()
    {
        var cell = new PickerCell { ItemsSource = new[] { "a" } };
        this.HostScopedDark(cell);
        CheckMark(new PickerPage(cell)).TextColor.ShouldBe(Dark(ShinyThemeKeys.Color.Primary));

        var accented = new PickerCell { ItemsSource = new[] { "a" }, AccentColor = Colors.Green };
        this.Host(accented);
        var check = CheckMark(new PickerPage(accented));
        this.FlipToDark();
        check.TextColor.ShouldBe(Colors.Green);
    }


    // ---------------------------------------------------------------------------------------------
    // LoadingRing
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void LoadingRingFollowsAFlip()
    {
        var ring = new LoadingRing { Percent = 0.5 };
        this.Host(ring);

        ring.EffectiveRingColor.ShouldBe(Light(ShinyThemeKeys.Color.Primary));
        ring.EffectiveTrackColor.ShouldBe(Light(ShinyThemeKeys.Color.SurfaceContainerHighest));
        ring.EffectiveTextColor.ShouldBe(Light(ShinyThemeKeys.Color.OnSurface));

        this.FlipToDark();

        ring.EffectiveRingColor.ShouldBe(Dark(ShinyThemeKeys.Color.Primary));
        ring.EffectiveTrackColor.ShouldBe(Dark(ShinyThemeKeys.Color.SurfaceContainerHighest));
        ring.EffectiveTextColor.ShouldBe(Dark(ShinyThemeKeys.Color.OnSurface));
    }

    [Fact]
    public void LoadingRingHonoursAScopedPaletteAndExplicitColours()
    {
        var scoped = new LoadingRing();
        this.HostScopedDark(scoped);

        // Unfixed, the ring read the application's (light) palette on every draw.
        scoped.EffectiveRingColor.ShouldBe(Dark(ShinyThemeKeys.Color.Primary));
        scoped.EffectiveTrackColor.ShouldBe(Dark(ShinyThemeKeys.Color.SurfaceContainerHighest));

        var explicitRing = new LoadingRing { RingColor = Colors.Red, TrackColor = Colors.Blue, TextColor = Colors.Green };
        this.HostScopedDark(explicitRing);
        explicitRing.EffectiveRingColor.ShouldBe(Colors.Red);
        explicitRing.EffectiveTrackColor.ShouldBe(Colors.Blue);
        explicitRing.EffectiveTextColor.ShouldBe(Colors.Green);
    }
}
