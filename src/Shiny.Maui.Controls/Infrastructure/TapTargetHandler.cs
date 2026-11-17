#if ANDROID || IOS || WINDOWS
using Microsoft.Maui.Handlers;

namespace Shiny.Maui.Controls.Infrastructure;

/// <summary>
/// The handler for every <see cref="ITapTarget"/>: MAUI's own content-view handler, plus the platform's
/// button semantics. See <see cref="ITapTarget"/>.
/// </summary>
/// <remarks>
/// <para>
/// Applied after MAUI maps <see cref="IView.Semantics"/>, because that mapping sets the same native
/// accessibility state (the traits on iOS, the accessibility delegate on Android) and would undo it.
/// </para>
/// <para>
/// Only the three platform heads get it. The net11.0 build serves Mac Catalyst, AppKit and GTK4, whose
/// heads bring their own content-view handlers; registering MAUI's stub one over theirs would leave the
/// control with no native view at all.
/// </para>
/// </remarks>
public partial class TapTargetHandler : ContentViewHandler
{
    /// <summary>MAUI's content-view mapper, with the button semantics appended to the semantics mapping.</summary>
    public static readonly IPropertyMapper<IContentView, IContentViewHandler> TapTargetMapper =
        new PropertyMapper<IContentView, IContentViewHandler>(ContentViewHandler.Mapper);

    static TapTargetHandler()
    {
        // Wrapped rather than appended: on Android MAUI's own mapping looks for its delegate on top of
        // the view, and finding ours there it wraps ours again — one more pair of delegates on every
        // semantics change. Ours is taken off first, so MAUI updates its own in place.
        TapTargetMapper.ModifyMapping<IContentView, IContentViewHandler>(nameof(IView.Semantics), (handler, view, previous) =>
        {
            var tapTarget = handler as TapTargetHandler;
            tapTarget?.RemoveButtonSemantics();
            previous?.Invoke(handler, view);
            tapTarget?.ApplyButtonSemantics();
        });
    }

    public TapTargetHandler() : base(TapTargetMapper)
    {
    }

    internal ITapTarget? Target => this.VirtualView as ITapTarget;

    partial void ApplyButtonSemantics();

    /// <summary>Takes off what <see cref="ApplyButtonSemantics"/> put on, where it wraps something of MAUI's.</summary>
    partial void RemoveButtonSemantics();
}
#endif
