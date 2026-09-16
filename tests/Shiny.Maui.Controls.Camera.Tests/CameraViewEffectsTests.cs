using Shiny.Controls.Camera;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Camera.Tests;

/// <summary>
/// Pins the wiring between <see cref="CameraView.Effects"/> and the <see cref="CameraView.EffectChain"/>
/// snapshot the platform handlers actually render from. The effects themselves are covered elsewhere; what
/// matters here is that mutating the collection is *noticed* — a chain that never rebuilds looks exactly like
/// an effect that does nothing, with no error anywhere to say otherwise.
/// </summary>
public class CameraViewEffectsTests
{
    [Fact]
    public void Adding_to_the_default_collection_rebuilds_the_chain()
    {
        // Regression: Effects is created by a BindableProperty defaultValueCreator, and MAUI does NOT raise
        // propertyChanged for a lazily-created default. The CollectionChanged subscription therefore never
        // happened, so every spatial effect (Comic/Sketch/Posterize/Pixelate/Blur) silently did nothing while
        // the colour filters — which route through Filter, a real property change — worked fine.
        var camera = new CameraView();

        camera.Effects.Add(CameraEffects.Comic);

        camera.EffectChain.Effects.ShouldContain(CameraEffects.Comic);
    }

    [Fact]
    public void Removing_from_the_default_collection_rebuilds_the_chain()
    {
        var camera = new CameraView();
        camera.Effects.Add(CameraEffects.Comic);
        camera.Effects.Remove(CameraEffects.Comic);

        camera.EffectChain.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Replacing_the_collection_wholesale_rebuilds_and_keeps_tracking()
    {
        var camera = new CameraView();
        camera.Effects.Add(CameraEffects.Comic);

        camera.Effects = new System.Collections.ObjectModel.ObservableCollection<ICameraEffect>();
        camera.EffectChain.IsEmpty.ShouldBeTrue("the old collection's contents must not linger");

        // the replacement has to be tracked too, not just the original
        camera.Effects.Add(CameraEffects.Sketch);
        camera.EffectChain.Effects.ShouldContain(CameraEffects.Sketch);
    }

    [Fact]
    public void The_old_collection_stops_being_tracked_after_a_replacement()
    {
        var camera = new CameraView();
        var original = camera.Effects;

        camera.Effects = new System.Collections.ObjectModel.ObservableCollection<ICameraEffect>();
        original.Add(CameraEffects.Comic);

        camera.EffectChain.IsEmpty.ShouldBeTrue("a detached collection must no longer drive the chain");
    }

    [Fact]
    public void Filter_and_effects_compose_with_the_filter_first()
    {
        var camera = new CameraView { Filter = CameraFilter.Sepia };
        camera.Effects.Add(CameraEffects.Comic);

        camera.EffectChain.Effects.Count.ShouldBe(2);
        camera.EffectChain.Effects[0].ShouldBe(CameraEffects.Sepia);
        camera.EffectChain.Effects[1].ShouldBe(CameraEffects.Comic);
    }

    [Fact]
    public void Changing_the_filter_alone_rebuilds_the_chain()
    {
        var camera = new CameraView();
        camera.EffectChain.IsEmpty.ShouldBeTrue();

        camera.Filter = CameraFilter.Noir;

        camera.EffectChain.Effects.ShouldContain(CameraEffects.Noir);
    }

    [Fact]
    public void Clearing_the_filter_removes_it_from_the_chain()
    {
        var camera = new CameraView { Filter = CameraFilter.Noir };
        camera.Filter = CameraFilter.None;

        camera.EffectChain.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void A_bound_collection_that_outlives_the_view_does_not_keep_it_alive()
    {
        // Effects is commonly bound to a view model's collection, which outlives the page. A direct
        // CollectionChanged handler made that collection root the view (and its handler and page) for as
        // long as the view model lived; MAUI never calls anything that could unsubscribe it.
        var effects = new System.Collections.ObjectModel.ObservableCollection<ICameraEffect>();
        var reference = AttachAndAbandon(effects);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        reference.TryGetTarget(out _).ShouldBeFalse();
        Should.NotThrow(() => effects.Add(CameraEffects.Comic));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static WeakReference<CameraView> AttachAndAbandon(System.Collections.ObjectModel.ObservableCollection<ICameraEffect> effects)
    {
        var camera = new CameraView { Effects = effects };
        effects.Add(CameraEffects.Mono);
        camera.EffectChain.Effects.ShouldContain(CameraEffects.Mono);
        return new WeakReference<CameraView>(camera);
    }
}
