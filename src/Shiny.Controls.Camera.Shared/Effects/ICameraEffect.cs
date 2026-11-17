using Microsoft.Maui.Graphics;

namespace Shiny.Controls.Camera;

/// <summary>
/// A pluggable camera effect. Effects are held in order on <c>CameraView.Effects</c> and applied to the
/// preview, to captured stills and to recorded video, so what the user sees is what gets saved.
/// </summary>
/// <remarks>
/// <para>
/// Do not implement this interface directly — implement one (or more) of <see cref="IColorEffect"/>,
/// <see cref="INativeEffect"/>, <see cref="IDrawEffect"/> or <see cref="ICaptureEffect"/>. An effect that
/// implements none of them is inert.
/// </para>
/// <para>
/// There are three kinds because there are genuinely three mechanisms, and no single method signature
/// honours all of them without lying about what a platform can do: a colour matrix is uniform everywhere, a
/// GPU program is per-backend, and compositing is a canvas operation. An effect may implement several — the
/// built-in looks carry both a native program (for fidelity where the platform has one) and a colour matrix
/// (so the effect still does something everywhere else).
/// </para>
/// </remarks>
public interface ICameraEffect
{
    /// <summary>Stable identifier, used to key the effect in the chain and in diagnostics.</summary>
    string Id { get; }

    /// <summary>When <c>false</c> the effect is skipped without being removed from the chain.</summary>
    bool IsEnabled { get; }
}


/// <summary>
/// An effect expressed as a colour matrix — the cheapest kind, and the only kind honoured on <i>every</i>
/// platform and surface including Windows and the bare <c>net11.0</c> head.
/// </summary>
public interface IColorEffect : ICameraEffect
{
    /// <summary>The matrix applied to each pixel. Return <see cref="ColorMatrix4x5.Identity"/> for a no-op.</summary>
    ColorMatrix4x5 ColorMatrix { get; }
}


/// <summary>
/// An effect backed by a native GPU program — a Core Image filter, an AGSL shader, an SVG filter — described
/// as data so no platform type leaks into the public API. This is what spatial looks (comic, sketch, blur,
/// distortion) need, because a colour matrix cannot see a pixel's neighbours.
/// </summary>
public interface INativeEffect : ICameraEffect
{
    /// <summary>The per-backend programs. See <see cref="NativeEffectDescriptor"/> for the fallback order.</summary>
    NativeEffectDescriptor Descriptor { get; }
}


/// <summary>
/// An effect that draws <i>over</i> the frame — masks, stickers, watermarks, telemetry. This is the mechanism
/// behind face effects: the analyzer tracks, the draw effect paints.
/// </summary>
/// <remarks>
/// <para><b>Threading:</b> <see cref="Draw"/> is invoked once per frame on a capture/encoder/render thread —
/// <b>never</b> the UI thread. Read mutable state through a <c>volatile</c> field or an immutable snapshot,
/// and never touch UI objects from inside it.</para>
/// <para><b>Coordinate space:</b> draw in the frame's pixel space, origin top-left, extent
/// <c>(0,0)..(context.Width, context.Height)</c>. Frames arrive upright and, for the front camera, already
/// un-mirrored. Analyzer geometry in <see cref="CameraEffectContext"/> is normalized, so multiply by
/// <c>context.Width</c>/<c>context.Height</c> to place it.</para>
/// </remarks>
public interface IDrawEffect : ICameraEffect
{
    /// <summary>Paint this effect for one frame.</summary>
    /// <param name="canvas">Canvas over the frame; draw in pixel space.</param>
    /// <param name="frame">The frame bounds in pixels (<c>0,0,Width,Height</c>).</param>
    /// <param name="context">Per-frame metadata and the latest analyzer results.</param>
    void Draw(ICanvas canvas, RectF frame, CameraEffectContext context);
}


/// <summary>
/// An effect applied once, asynchronously, to an <b>encoded still</b> after capture — the place for work far
/// too slow for a frame budget, such as sending the photo to an image-generation model.
/// </summary>
/// <remarks>
/// Deliberately outside the live chain: a round-trip to a hosted model is seconds of latency and a per-image
/// cost, so it must never sit on a frame loop. Capture effects run in <c>CameraView.Effects</c> order after
/// every live effect has already been baked into the JPEG.
/// </remarks>
public interface ICaptureEffect : ICameraEffect
{
    /// <summary>
    /// Transform an encoded still. Return the input unchanged to pass through — implementations should do
    /// exactly that on failure rather than throwing away the user's photo.
    /// </summary>
    /// <param name="jpeg">The encoded JPEG produced by capture (with live effects already applied).</param>
    /// <param name="ct">Cancels the transform.</param>
    ValueTask<byte[]> ApplyAsync(byte[] jpeg, CancellationToken ct);
}


/// <summary>How much of an effect the current platform will actually honour.</summary>
/// <remarks>
/// Ask <c>CameraView.GetEffectSupport(effect)</c> before offering an effect in a UI — it is better to grey out
/// a Comic button on Windows than to ship one that silently does nothing.
/// </remarks>
public enum EffectSupport
{
    /// <summary>The platform cannot apply this effect anywhere. It will be skipped.</summary>
    Unsupported,

    /// <summary>Applied to captured stills only; the live preview is unaffected (e.g. Windows).</summary>
    StillOnly,

    /// <summary>
    /// Applied everywhere, but degraded to the effect's colour matrix because this platform has no program
    /// for its native descriptor (e.g. a spatial look on Android below the shader API level).
    /// </summary>
    ColorOnly,

    /// <summary>Applied as authored, on every surface the platform supports.</summary>
    Full
}
