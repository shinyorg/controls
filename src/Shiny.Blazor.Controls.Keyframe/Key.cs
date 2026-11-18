using Microsoft.AspNetCore.Components;
using Shiny.Blazor.Controls.Keyframe.Compilation;

namespace Shiny.Blazor.Controls.Keyframe;

/// <summary>
/// One keyframe of a <see cref="Track"/>. As in CSS, <see cref="Easing"/> shapes the segment that
/// <i>starts</i> here, so the curve on the last key is never used.
/// </summary>
public sealed class Key : ComponentBase, IDisposable
{
    [CascadingParameter] Track? Track { get; set; }

    /// <summary>0..1 along the iteration.</summary>
    [Parameter] public double Offset { get; set; }

    /// <summary>
    /// The value: a number (px for lengths and translations, degrees for rotations), a colour, a
    /// MAUI Thickness, or any CSS value - text, as in XAML, so <c>Value="#2563EB"</c> and
    /// <c>Value="-60%"</c> work as written. Leave it out and the key is the element's live value when
    /// playback starts.
    /// </summary>
    [Parameter] public string? Value { get; set; }

    /// <summary>The curve for the segment starting at this key: a named curve, cubic-bezier(), steps(), spring() or bouncy().</summary>
    [Parameter] public string? Easing { get; set; }

    internal KeySpec ToSpec() => new(this.Offset, this.Value, this.Easing);

    protected override void OnInitialized() => this.Track?.AddKey(this);

    public void Dispose() => this.Track?.RemoveKey(this);
}
