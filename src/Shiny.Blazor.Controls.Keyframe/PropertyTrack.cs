namespace Shiny.Blazor.Controls.Keyframe;

/// <summary>
/// <see cref="Track"/> under another name, for inside <c>@foreach</c> / <c>@if</c> blocks: there Razor
/// reads <c>&lt;Track&gt;</c> as the HTML <c>&lt;track&gt;</c> void element and will not let it have
/// Key children. In plain markup the two are interchangeable.
/// </summary>
public sealed class PropertyTrack : Track;
