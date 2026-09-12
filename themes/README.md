# Shiny Controls — Theming

Shiny Controls ship a central, Material-3-style theming system shared by **MAUI** and **Blazor**.
A theme is a set of design tokens (color roles, shape, elevation, type scale, state, spacing).
The core packages define the token contract and a built-in **Basic** theme; additional themes
(**Ocean**, **Material**, **Terminal**, **Aurora**) install as separate NuGet packs.

## A theme is more than a palette

A pack defines its own **personality**, not just its hue. Every block below is optional; omit one
and the shared default applies, so a theme only states the axes it wants to differ on.

| Block | What it controls | Example |
|---|---|---|
| `shape` | Corner geometry — `scale`, or absolute `corners` overrides | Terminal `scale: 0` (square), Ocean `1.75` (pillowy) |
| `typography` | `fontFamily` / `displayFamily` / `monoFamily`, `scale`, `weightOffset`, `trackingOffset`, `lineHeightScale` | Terminal is monospace throughout; Aurora is `weightOffset: 200` |
| `elevation` | `style` (`shadow` / `flat` / `outline` / `glow`), `intensity` (how dark), `softness` (how large and diffuse), `tint` | Terminal draws a hairline ring; Aurora glows in `primary` |
| `density` | `scale` on the spacing ramp and control metrics, plus absolute `controlHeight` / `controlHeightSmall` / `rowHeight` | Terminal `0.8` (compact), Ocean `1.2` (roomy) |
| `border` | The `thin` / `medium` / `thick` stroke ramp | A hand-drawn theme thickens `thin` to 2 |
| `state` | `hover` / `focus` / `pressed` / `dragged` layer opacities | Terminal presses harder |

`intensity` and `softness` are deliberately separate: "big soft halo at low opacity" and "tight dark
shadow" are different looks that one knob collapses into a single dim, shrunken shadow.

The built-in packs are picked to be visibly unalike: **Basic** (the neutral default, shipped in the
core packages), **Material** (M3 purple, Roboto, generous corners, tonal shadows), **Ocean** (soft,
airy, teal, shadows you have to look for), **Terminal** (square, dense, monospace, phosphor green,
rings instead of shadows) and **Aurora** (violet/cyan, rounded, bold, glowing).

Controls consume these tokens, so a pack restyles geometry and type across the whole set — not only
colour. That matters because the neutral colour ramp barely differs between packs (a tone-98
near-white has no room to carry a hue), which is why a palette-only theme leaves most controls
looking identical.

## Two layers: what you edit, and what controls consume

This is the important part, and it is what changed most recently.

**The authoring layer** is the ~35 values a human actually writes. It is deliberately close to what
every other modern design system asks for, so it needs no study:

| | |
|---|---|
| Surfaces | `background` `foreground` `card` `popover` `muted` `muted-foreground` |
| Semantic | `primary` `secondary` `accent` `destructive` `success` `info` `warning` `caution` `critical` — each with a `-foreground` |
| Lines | `border` `input` `ring` `shadow-color` |
| Knobs | `radius` `density` `text-scale` `line-height-scale` `font-weight-offset` `letter-spacing-offset` `border-width` `font-sans` `font-display` `font-mono` |

**The derived layer** is the 55 Material-3 colour roles plus the shape, type, spacing, density and
elevation ramps — the contract every control consumes, and which nobody should have to hand-maintain.
It is *expressed over* the authoring layer rather than duplicated:

```css
--shiny-color-primary:           var(--shiny-primary);
--shiny-color-primary-container: color-mix(in oklab, var(--shiny-primary) 22%, var(--shiny-background));
--shiny-shape-corner-large:      calc(var(--shiny-radius) * 1.3333);
--shiny-type-body-large-size:    calc(16px * var(--shiny-text-scale));
```

Three things fall out of that, and they are the reason for the split:

1. **One edit ripples.** Override `--shiny-primary` in your own stylesheet and the container tint, the
   surface tint and the focus ring all move with it. The old flat dictionary could not do that — every
   role was an independent literal, so changing a brand colour meant changing four.
2. **A dark scope restates 28 values, not 55.** The derived block is written once, in terms of tokens
   that are themselves per-scheme.
3. **A theme pack is a delta.** Packs used to be a complete 364-line re-declaration each; they now
   state their palette, their knobs, and only the handful of values that cannot be expressed over them
   (an outline-style elevation, a squared-off `corner-full`).

### Where the seeds fit now

`seeds` is still there and still the fastest way to get a coherent palette out of one brand colour:
the Material tonal machinery builds a full scheme, and the authoring layer is lifted out of it. What
changed is that it is no longer the only way in. A theme can state authoring values outright:

```json
{
    "name": "Team", "slug": "team",
    "seeds": { "primary": "#2563EB", "...": "..." },
    "tokens": {
        "light": { "background": "#FFFFFF", "primary": "#5B21B6", "radius": "…" },
        "dark":  { "background": "#0B0B0F", "primary": "#A78BFA" }
    }
}
```

Anything in `tokens` wins; anything omitted keeps the value the seeds produced. A theme that states
every token never consults its seeds at all.

### What the generator emits

`tools/ShinyThemeGen` expands a theme into the platform assets:

- **MAUI** → C# `ResourceDictionary` classes (`{Name}LightTheme` / `{Name}DarkTheme` / `{Name}Theme`).
  MAUI has no `color-mix` at paint time, so the derivations are computed here, in oklab — the same
  space the CSS mixes in, so the two hosts land on the same colour.
- **Blazor** → a CSS file: the authoring layer, then the derived contract expressed over it.

Controls consume the derived layer on both hosts: MAUI binds with
`SetDynamicResource(…, ShinyThemeKeys.Color.X)`, Blazor reads `var(--shiny-color-x)`.

Regenerate after editing any `/themes/*.json`:

```bash
dotnet run --project tools/ShinyThemeGen
```

See `token-reference.md` for the full token list and the hardcoded-color → token cheatsheet.

## Using a theme — MAUI

Basic is applied automatically by `UseShinyControls()`. To use a pack, install it and select it:

```csharp
// dotnet add package Shiny.Maui.Controls.Themes.Ocean
builder.UseShinyControls(cfg => cfg.UseOceanTheme());
// or .UseMaterialTheme() / .UseTerminalTheme() / .UseAuroraTheme() / .UseBasicTheme()
```

A pack's `typography.fontFamily` is a CSS font stack on Blazor. On MAUI it is a font *alias* the host
app must register with `ConfigureFonts`; an unregistered name falls back to the system font, so the
rest of the theme still applies.

Switch at runtime (light/dark follows the OS automatically and hot-swaps):

```csharp
ShinyThemeManager.SetTheme(new OceanTheme());
Application.Current.UserAppTheme = AppTheme.Dark;       // flips to the dark scheme live
```

Explicitly set control colors (e.g. `Fab.FabBackgroundColor`) still override the theme.

## Using a theme — Blazor

The core Basic stylesheet ships in `Shiny.Blazor.Controls`. Link it in `index.html`:

```html
<link href="_content/Shiny.Blazor.Controls/css/shiny-theme.css" rel="stylesheet" />
```

To use a pack, install it and link its stylesheet **after** the core one (it overrides `:root`):

```html
<!-- dotnet add package Shiny.Blazor.Controls.Themes.Ocean -->
<link href="_content/Shiny.Blazor.Controls.Themes.Ocean/css/shiny-theme-ocean.css" rel="stylesheet" />
```

Dark mode follows the OS by default. Force it by adding `shiny-theme-dark` (or `shiny-theme-light`)
to `<html>` or any container; the tokens cascade to that subtree.

## Overriding a theme without forking one

On **Blazor**, this is now the ordinary thing a web developer would try, and it works — the authoring
tokens are plain custom properties, and a later stylesheet wins:

```css
/* app.css, linked after the Shiny theme */
:root {
    --shiny-primary: #5B21B6;
    --shiny-radius: 4px;
    --shiny-density: 0.9;
}
.shiny-theme-dark {
    --shiny-primary: #A78BFA;
}
```

Everything derived from those moves with them. You do not need the generator, this repo, or a NuGet
package to restyle the control set.

## Authoring a new theme

1. Copy `basic.json` to `myteam.json`, set `name`/`slug`, and tweak the 11 seed colors.
2. Add whichever personality blocks you want to differ (see the table above). Everything you omit
   keeps the shared default, so a colour-only pack is still a one-block file.
3. Run the generator. New slugs emit to `src/Shiny.{Maui,Blazor}.Controls.Themes.{Name}/`.
4. Add the two project files (model them on the Ocean pack) and register in `Shiny.Controls.slnx` / `Build.slnf`.

`shiny-theme.schema.json` documents every field with its default, and editors will complete it from
the `$schema` reference at the top of each theme file.

### Which axis to reach for

Corner geometry is the single biggest lever — square versus pillowy reads as a different framework
before any colour registers. Elevation style is next (a flat or outline pack stops everything
floating), then typography (a monospace or heavier family changes the whole voice), then density.
Colour alone moves the least, which is the trap the original palette-only format fell into.
