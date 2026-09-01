# Document & Slide Viewers

[← All Shiny Controls](../../README.md)

> Same packages as the Spreadsheet: `Shiny.Maui.Controls.Office` / `Shiny.Blazor.Controls.Office`.

`DocumentView` renders `.docx` and `SlideView` renders `.pptx`. Both are **read-only** — editing those
two formats is a later phase.

```csharp
using var document = await WordDocument.OpenAsync("report.docx");
using var deck = await SlideDeck.OpenAsync("deck.pptx");
```

```xml
<office:DocumentView Document="{Binding Document}" Zoom="1.0" />
<office:SlideView Deck="{Binding Deck}" Mode="Single" />
```

```razor
<div style="height:520px"><DocumentView Document="document" /></div>
<div style="height:460px"><SlideView Deck="deck" @bind-SlideIndex="index" @bind-Mode="mode" /></div>
```

**Word reflows; it does not paginate.** Content is laid out as one continuous column at the control's
width, so there are no pages, headers or footers. That is deliberate: a viewer without a full
pagination engine puts page breaks in the wrong places, which reads as a bug rather than a gap. It
resolves the whole style chain — document defaults, the named style with its entire `basedOn`
ancestry, then direct formatting — along with list numbering from `numbering.xml`, tables with column
spans, vertical merges and shading, inline images, and an `Outline()` for navigation. List numbers are
derived from document order rather than frozen at read time, so editing inside a list item leaves its
number alone and adding or removing an item renumbers the rest of the list.

**PowerPoint scales; it does not reflow.** Slides are fixed-size artboards, so the view fits and
letterboxes them. Shapes arrive resolved through slide → layout → master, which matters because a
title placeholder typically carries text and nothing else. ~20 preset geometries, solid and gradient
fills, outlines, theme colours with their `lumMod`/`lumOff`/`shade`/`tint` modifiers applied, per-level
text styles, speaker notes, pictures, tables, a scrolling thumbnail-grid mode, and a full-screen
presenting mode.

## Presenting mode

`SlideView` goes full screen for a room:

```xml
<office:SlideView x:Name="Viewer" Deck="{Binding Deck}" IsPresenting="{Binding Presenting}"
                  PresentingChanged="OnPresentingChanged" />
```

```csharp
this.Viewer.StartPresenting();   // also StopPresenting() / TogglePresenting()
```

```razor
<SlideView @ref="viewer" Deck="deck" @bind-IsPresenting="presenting" />
@* await viewer.StartPresentingAsync() — see below for why the method beats the binding on the web *@
```

The slide is fitted edge to edge on black with no border, `Mode` is ignored for the length of the show
(a thumbnail wall is how you find a slide, not how you show one) and put back when it ends, and the
inline viewer is left on the slide the show ended on. A control bar — previous, a counter, next,
**Notes** when any slide in the deck has speaker notes, and Exit — fades out after a few seconds and
comes back on a touch or a pointer move; the notes panel it opens stays put, because notes are read
while you are talking rather than moving the mouse. Turn the bar off with `ShowPresenterControls`.

Tapping or clicking advances, except in the left quarter of the surface, which goes back. On MAUI the
show is a modal page (the platform back gesture leaves it) and the display is kept awake for its
duration — `KeepScreenOnWhilePresenting`. On Blazor `F5` starts a show and `Escape` leaves one, the
browser's Fullscreen API is requested on top of a full-window CSS surface rather than instead of it —
a refused request (an iframe without `allowfullscreen`, iOS Safari) still gives the room a full-window
deck — and the pointer hides with the bar. Call `StartPresentingAsync()` rather than setting the bound
parameter where you can: a browser only grants fullscreen inside the gesture that asked for it, and a
round trip through a parameter loses that gesture.

`IsPresenting` is two-way and `PresentingChanged` fires however the show ended, including Escape, the
Android back button, and a fullscreen exit the browser made on its own.

**Fonts are bundled on Blazor.** `Shiny.Blazor.Controls.Office` ships Carlito and Caladea (SIL OFL
1.1, ~1 MB compressed), metric-compatible with Calibri and Cambria, loaded automatically on first
render. SkiaSharp on WebAssembly has no access to system fonts and returns a wrong-but-non-null
fallback for every request, so without them every document renders in a single monospace face. MAUI
uses the platform's own fonts and needs no bundle.

Both preserve the package exactly — opening and saving is byte-identical — and both report what they
could not draw:

```csharp
var collector = new UnsupportedFeatureCollector();
using var document = await WordDocument.OpenAsync(path, collector);
// charts, SmartArt, footnotes, comments, headers/footers, custom geometry...
```

## Dark mode

`Theme` is nullable and **unset means follow the host** — the app's light/dark appearance on MAUI,
the page's `color-scheme` on Blazor — and it keeps up live when that flips. Pass `DocumentTheme.Light`
or `DocumentTheme.Dark` only to pin one regardless of the app around it. See
[Styling & theming](styling.md#dark-mode).
