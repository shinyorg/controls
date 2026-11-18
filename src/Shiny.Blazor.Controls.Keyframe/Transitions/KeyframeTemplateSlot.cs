namespace Shiny.Blazor.Controls.Keyframe;

/// <summary>
/// Collects the Keyframes written inside a template fragment (a TransitionGroup's AddKeyframes, a
/// CustomPageTransition's Enter) so their tracks can be applied to other elements. Keyframes inside
/// render nothing and never play on their own.
/// </summary>
internal sealed class KeyframeTemplateSlot : IKeyframeContainer
{
    readonly List<IKeyframeNode> nodes = [];

    public bool AsTemplates => true;

    public void Register(IKeyframeNode node) => this.nodes.Add(node);

    public void Unregister(IKeyframeNode node) => this.nodes.Remove(node);

    /// <summary>The first Keyframes in the slot, if any.</summary>
    public Keyframes? Template => this.nodes.OfType<Keyframes>().FirstOrDefault();
}
