# Styling & theming

[← All Shiny Controls](../../README.md)

How implicit styles, theme packs and the *unset* appearance defaults apply to every control in this folder.

**Styling (MAUI)** — every control can be targeted by an implicit or explicit `Style`, so
app-wide theming is set once rather than repeated at each usage site:

```xml
<!-- App.xaml -->
<Style TargetType="shiny:PillView">
    <Setter Property="CornerRadius" Value="10" />
    <Setter Property="FontSize" Value="12" />
</Style>
```

Leave a colour property unset to inherit the active Shiny theme; setting one explicitly
overrides the theme default for that instance — permanently. A `ChatView` with
`MyBubbleColor="#DCF8C6"` keeps that green through every `ShinyThemeManager.SetTheme` call, so omit
the property unless you mean to pin it.

**Theming (Blazor)** — the tokens are two layers, and only the small one is meant to be edited:

```css
/* your own stylesheet, linked after the Shiny theme */
:root {
    --shiny-primary: #5B21B6;   /* the container tint, surface tint and focus ring follow */
    --shiny-radius: 4px;        /* every corner in the ramp follows */
    --shiny-density: 0.9;       /* control heights and the spacing scale follow */
}
.shiny-theme-dark { --shiny-primary: #A78BFA; }
```

The ~35 authoring tokens (`background`, `foreground`, `card`, `popover`, `muted`, the nine semantic
families with their foregrounds, `border`, `input`, `ring`, and the `radius`/`density`/`text-scale`/
`border-width`/font knobs) are what a theme states. The 153 `--shiny-color-*`, `--shiny-shape-*`,
`--shiny-type-*` … names every control consumes are *derived* from them with `var()` and
`color-mix()`, so an override ripples instead of having to be repeated per role. See
[`themes/README.md`](../../themes/README.md).

**What visibly changes when you swap theme packs.** A theme is not just a palette. Alongside the
colour seeds it can define its own **shape** (corner geometry), **typography** (family, scale,
weight, tracking), **elevation** (`shadow` / `flat` / `outline` / `glow`, with separate intensity
and softness), **density** (spacing ramp and control metrics) and **border widths** — so a pack
restyles the geometry and type of every control, not only its hue. Terminal is square, monospace
and dense with hairline rings instead of shadows; Ocean is soft, roomy and barely-shadowed; Aurora
glows instead of casting shadows. Colour alone would not carry that: the neutral ramp is nearly
identical between packs because a tone-98 near-white has no room to hold a hue, which is why a
palette-only theme used to leave most controls looking the same.

Numeric appearance properties that a theme owns (`CornerRadius`, `FontSize`, stroke widths)
default to `-1`, meaning *unset — let the theme decide*. A literal default would have been written
to the control at construction and beaten the theme permanently rather than merely by default.
Setting one to a real value still pins it, as before.

**Watch out for implicit `BoxView` styles.** The .NET MAUI project template ships
`<Style TargetType="BoxView">` with a setter for **`BackgroundColor`** — which paints an opaque
rectangle *behind* the shape rather than setting the `BoxView`'s own `Color`. Because it is
implicit it applies app-wide, including to `BoxView`s inside controls, and it turns
`<BoxView Color="Transparent" />` spacers into solid dark bars, puts dark corners behind rounded
shapes, and hides gradient `Background`s. Prefer an empty `<Grid HeightRequest="..." />` for
spacers, and if you want a default separator colour set `Color`, not `BackgroundColor`.

## Dark mode

Every colour a control paints by default comes from the theme, so the whole control set follows the
app's light/dark scheme with nothing wired up per control. Three mechanisms carry that, one per kind
of surface.

**CSS surfaces (Blazor)** read `var(--shiny-color-*)`. Where a colour is a `[Parameter]` — a
toolbar's `BackgroundColor`, a sheet's `SheetBackgroundColor`, a calendar's `CalendarCellColor` — its
default is now a `var()` reference rather than a literal, because those land as *inline styles* and
an inline literal cannot be corrected by any stylesheet the app adds later. Passing your own value
still pins it.

> **Pin the ink with the fill.** If you set a literal background on a control, set its text colour
> too. The theme's `on-surface` goes light in dark mode, so a pinned pale background left with the
> default ink ends up light-on-light.

**Native widgets (Blazor)** — `<select>`, checkboxes, date inputs, scrollbars, the popover backdrop —
are painted by the browser and ignore your tokens entirely. The generated theme declares
`color-scheme` alongside the colour tokens, on the same scope, so those follow too. Because
`color-scheme` inherits, this works whether the theme class sits on `<html>` or on a container div.
The matching `.shiny-theme-light` class is also emitted, so a deliberately-light region inside a dark
app resolves correctly rather than inheriting the dark tokens.

**Drawn surfaces (both hosts)** — the Skia-backed `SpreadsheetView`, `DocumentView`,
`DocumentEditor`, `SlideView` and `SlideEditor`, plus `MarkdownView` — cannot inherit a CSS colour,
so the scheme has to reach them as a value. Their `Theme` property is nullable and **unset means
follow the host**:

| `Theme`  | Result                                                             |
| -------- | ------------------------------------------------------------------ |
| unset    | Follows the app (MAUI) or the page's `color-scheme` (Blazor), live. |
| `.Light` | Pinned light — a document preview that must stay paper-white.       |
| `.Dark`  | Pinned dark.                                                        |

On Blazor the scheme is read from the element's computed `color-scheme` rather than from
`matchMedia`, so an app that flips its theme with a class on a container — which is the common case,
since a Blazor app rarely owns `<html>` — is tracked correctly. `MarkdownTheme` gains a third value,
`MarkdownTheme.Themed`, which is the new unset default: every colour in it is a token, so markdown
follows the theme pack as well as the scheme.

`SlideTheme.Dark` deliberately darkens only the surround. A slide is a fixed artboard with authored
colours, like a photograph; inverting it would misrepresent the deck.

**MAUI: the host's implicit `Button` style no longer reaches inside a control.** Controls are built
from primitives, and the .NET MAUI project template's `<Style TargetType="Button">` applies to every
one of them — including the flat glyph buttons inside a `DataGrid` pager or a sheet tab strip. Its
`Disabled` visual state sets `BackgroundColor` to `Gray600` in dark mode, so the buttons you *cannot*
press were the only ones with a background, and its base setter painted internal chrome in the app's
brand colour. Internal parts now carry their own `CommonStates` group (a locally-set attached
property beats one arriving through a style) and express disabled as opacity. Implicit styles
targeting the Shiny control types themselves — `<Style TargetType="shiny:PillView">` — are
unaffected; this only stops the app's `Button`/`Entry` styles leaking into parts you never declared.
