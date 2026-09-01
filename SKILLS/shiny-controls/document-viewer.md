# Document & Slide Viewers (docx / pptx)

`DocumentView` renders `.docx`; `SlideView` renders `.pptx`. Both are **read-only** — the editors are a
later phase. They ship in the same packages as the spreadsheet:

| Package | Host |
|---|---|
| `Shiny.Controls.Office.Shared` | readers, models, layout, controllers — no UI dependency |
| `Shiny.Controls.Office.Skia` | the shared SkiaSharp painters |
| `Shiny.Maui.Controls.Office` | `DocumentView`, `SlideView` for MAUI |
| `Shiny.Blazor.Controls.Office` | `<DocumentView>`, `<SlideView>` for Blazor **WebAssembly** |

Same two constraints as the spreadsheet: **MAUI needs `UseShinyOffice()`** (it registers SkiaSharp, plus the AppKit canvas on `net10.0-macos`), and **Blazor is WASM-only**.
On Blazor the container needs an **explicit height** or the canvas collapses to zero.

## Fonts

`Shiny.Blazor.Controls.Office` bundles **Carlito** (metric-compatible with Calibri, Aptos and Segoe UI)
and **Caladea** (metric-compatible with Cambria), both SIL OFL 1.1, eight faces, ~1 MB compressed.
They load automatically on the first render of any Office view, once per session, and are HTTP-cached.

This is not a nicety. **SkiaSharp on WebAssembly has no access to system fonts at all**, and
`SKTypeface.FromFamilyName` returns a wrong-but-non-null fallback rather than failing — so without the
bundle every document renders in one monospace face and glyphs outside its coverage become tofu boxes.

Metric-compatible means glyph advances match the original, so a document laid out against Calibri
breaks its lines in the same places against Carlito.

To add a face the bundle does not cover:

```csharp
OfficeFonts.Register(await http.GetByteArrayAsync("fonts/MyFont.ttf"));
```

On **MAUI** no bundle is needed — the platform's own fonts are used, with the same substitution table
applied on top.

## Word — `DocumentView`

```csharp
using var document = await WordDocument.OpenAsync("/path/report.docx");
```

### MAUI

```xml
<office:DocumentView x:Name="Viewer" Document="{Binding Document}" Zoom="1.0" />
```

### Blazor

```razor
<div style="height:520px">
    <DocumentView Document="document" Zoom="1.0" Theme="DocumentTheme.Dark" />
</div>
```

### It reflows; it does not paginate

Content is laid out as one continuous column at the control's width. There are no page breaks, no
headers and no footers — a viewer without a full pagination engine would put page boundaries in the
wrong places, which reads as a rendering bug rather than a missing feature.

**Do not generate code that expects pages, page numbers or `PageCount`.** `WordDocument.Page` exists,
but it only reports the *authored* page size and margins so the view can pick a sensible measure.

### What it renders

Paragraphs with the full style chain resolved (document defaults → named style with its whole
`basedOn` ancestry → direct formatting), bold/italic/underline/strike/colour/highlight/super/subscript,
alignment and justification, indents and hanging indents, line and paragraph spacing, numbered and
bulleted lists with running counters resolved from `numbering.xml`, tables with column spans, vertical
merges and cell shading, inline images, and hyperlink colouring.

### Outline and navigation

```csharp
foreach (var (level, text) in document.Outline())
    Console.WriteLine(new string(' ', level * 2) + text);

view.Controller?.ScrollToHeading(index);   // index into Outline()
view.Controller?.ScrollTo(0);
view.Controller!.Zoom = 1.25;
document.PlainText;                         // whole document as text
```

## PowerPoint — `SlideView`

```csharp
using var deck = await SlideDeck.OpenAsync("/path/deck.pptx");
```

### MAUI

```xml
<office:SlideView x:Name="Viewer" Deck="{Binding Deck}" Mode="Single" SlideChanged="OnSlideChanged" />
```

### Blazor

```razor
<div style="height:460px">
    <SlideView Deck="deck" @bind-SlideIndex="index" @bind-Mode="mode" />
</div>
```

### It scales; it does not reflow

A slide is a fixed-size artboard, so the view fits and letterboxes it rather than re-laying it out.
Text keeps exactly the proportions it was authored at, at any size.

```csharp
deck.SlideWidth;    // 1280 for a 16:9 deck at 96dpi
deck.AspectRatio;
deck.Slides[0].Title;
deck.Slides[0].Notes;
```

`SlideViewMode.Single` fits one slide; `SlideViewMode.Grid` is a scrolling thumbnail wall where
clicking a thumbnail opens that slide.

### Presenting mode

The deck full screen for a room. Both hosts: the slide fitted edge to edge on black with no border, an
auto-hiding control bar, tap/click to advance (the left quarter goes back), and speaker notes on
demand.

MAUI — a modal page, so the platform back gesture leaves the show:

```xml
<office:SlideView x:Name="Viewer" Deck="{Binding Deck}"
                  IsPresenting="{Binding Presenting}"
                  PresentingChanged="OnPresentingChanged" />
```

```csharp
this.Viewer.StartPresenting();     // StopPresenting(), TogglePresenting()
```

Blazor — call the method rather than setting the bound parameter where you can. A browser only grants
fullscreen inside the gesture that asked for it, and a round trip through a parameter loses that
gesture; the binding still presents, it just falls back to the full-window CSS surface.

```razor
<SlideView @ref="viewer" Deck="deck" @bind-IsPresenting="presenting" />

@code {
    SlideView? viewer;
    bool presenting;

    Task PresentAsync() => this.viewer!.StartPresentingAsync();   // StopPresentingAsync(), TogglePresentingAsync()
}
```

| Member | Both hosts |
| --- | --- |
| `IsPresenting` | Two-way. Writes back when the show ends however it ended. |
| `ShowPresenterControls` | The auto-hiding bar. Default `true`; off for a kiosk or second screen. |
| `KeepScreenOnWhilePresenting` | MAUI only, default `true`. Ignored where the platform has no such notion. |
| `PresentingChanged` / `IsPresentingChanged` | Fires on Escape, the Android back button, and a fullscreen exit the browser made on its own. |

Rules worth knowing:

- `Mode` is **ignored while presenting** — the controller forces `Single` — and is put back when the
  show ends. Do not try to present a thumbnail grid.
- The inline viewer is left on the slide the show ended on.
- The **Notes** button appears only when some slide in the deck has speaker notes. The panel it opens
  does *not* fade with the bar.
- On Blazor `F5` starts a show and `Escape` leaves one. Fullscreen is requested on top of a full-window
  CSS surface, never instead of it, so a refused request (an iframe without `allowfullscreen`, iOS
  Safari) still fills the window.

### Placeholder inheritance is done for you

Shapes come back already resolved through **slide → layout → master**. A title placeholder on a slide
routinely carries text and nothing else — its position, size and text style all live on the layout.
Decorative (non-placeholder) shapes from the layout and master are included; their *placeholders* are
not, because those are templates rather than content.

### What it renders

Preset geometry for ~20 common shapes (rect, rounded rect, ellipse, triangle, diamond, line, the four
arrows, pentagon, hexagon, star, chevron, parallelogram, trapezoid, plus, can, cloud), solid and linear
gradient fills, outlines including dashes, theme colours **with their `lumMod`/`lumOff`/`shade`/`tint`
modifiers applied**, text bodies with per-level formatting from the layout's list style, bullets,
alignment, vertical anchoring, PowerPoint's recorded autofit shrink, pictures, and tables.

## Dark mode

`DocumentTheme.Dark` darkens the page and **adapts the document's own colours for contrast**, keeping
their hue: black body text lifts to light grey, blue headings stay blue, red stays red. A colour that
already contrasts is left exactly as authored, so the adaptation is invisible on the light theme.
Set `OverrideDocumentColors = true` to force every colour to `Text` instead — legible, but it flattens
headings and warnings to one grey.

`SlideTheme.Dark` darkens **only the surround**. A slide is a fixed artboard with authored colours,
like a photograph — inverting it would show the deck's own dark text on a dark background and
misrepresent what the author made. PowerPoint's own dark mode behaves the same way.

## Reporting what was skipped

Both viewers preserve the whole package and report what they could not draw:

```csharp
var collector = new UnsupportedFeatureCollector();
using var document = await WordDocument.OpenAsync(path, collector);

foreach (var feature in collector.Features)
    Console.WriteLine($"{feature.Part}: {feature.Feature} ({feature.Severity})");
```

A viewer never reports `Lossy` — it does not write. Opening and saving is byte-identical.

## Not implemented

Do not generate code that assumes these work:

- **Editing either format.** Both are read-only. `SaveAsAsync` writes back what was opened, unchanged.
- **Pagination**, headers, footers, footnotes, endnotes, comments and tracked-change marks in Word.
  Tracked *insertions* render as normal text; deletions are hidden.
- Text wrapping around floating images; tab stops (a tab becomes four spaces).
- Charts, SmartArt, embedded OLE and animations in PowerPoint — reported, not drawn.
- Custom/freeform geometry, drawn as the bounding rectangle.
- Group shapes are flattened, which is wrong if the group has been scaled relative to its children.

### Dark mode

`Theme` is nullable; **leave it unset** to follow the host's light/dark scheme. Pass
`DocumentTheme.Light` / `.Dark` only to pin one.
