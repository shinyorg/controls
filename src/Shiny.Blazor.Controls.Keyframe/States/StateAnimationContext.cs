namespace Shiny.Blazor.Controls.Keyframe;

/// <summary>What a Keyframes inside a <see cref="StateAnimation"/> plays on, and whether its state is active.</summary>
internal sealed record StateAnimationContext(VisualStates Owner, bool Playing);
