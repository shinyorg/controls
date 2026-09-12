# Shiny Controls — Theme Token Reference & Migration Guide

This is the canonical contract that **controls consume**. It is not the thing a theme author writes —
see the authoring layer in [`README.md`](README.md), which is ~35 values these are derived from. Read
this page when you are writing a control and need to know which role to reach for.

**Blazor** consumes CSS custom properties; **MAUI** consumes `ResourceDictionary` keys (via
`ShinyThemeKeys`) bound with `SetDynamicResource`.

> **Where a value comes from.** Every role below is derived from an authoring token: most are a
> straight alias (`--shiny-color-primary` *is* `--shiny-primary`), and the containers and the upper
> surface ramp are a mix — the accent laid over the page, or the page tinted toward its own ink. That
> is why overriding `--shiny-primary` moves the container with it, and why you should reach for the
> role rather than the authoring token when writing a control: the role is the one that carries the
> meaning.

## Golden rules

1. **Always keep a fallback.** Blazor: `var(--shiny-color-x, #originalHex)` — keep the *exact*
   original hex as the fallback so the default look is unchanged when no theme stylesheet is linked.
   *(This one is on its way out — see the note at the end.)*
2. **Don't tokenize content colors.** Never replace colors that represent user data or previews:
   the ColorPicker spectrum/swatch values, ImageEditor pixel/brush sample colors, or any inline
   `style` that paints a user-chosen color. Chrome (borders, backgrounds, toolbars) is fine.
3. **Leave `transparent` / `Colors.Transparent` alone.** They are not theme colors.
4. **Map by role, not by hue.** A gray border → `outline-variant`, not "some gray". See table below.

## Color tokens

Blazor CSS var (`--shiny-color-…`) ↔ MAUI key (`ShinyThemeKeys.Color.…`). Same logical role:

| CSS var | MAUI key | Use for |
|---|---|---|
| `--shiny-color-primary` | `Primary` | Brand/accent fills (FAB, primary buttons, active bar) |
| `--shiny-color-on-primary` | `OnPrimary` | Text/icons on `primary` |
| `--shiny-color-primary-container` | `PrimaryContainer` | Softer accent fills |
| `--shiny-color-on-primary-container` | `OnPrimaryContainer` | Text on `primary-container` |
| `--shiny-color-secondary` / `-container`, `on-…` | `Secondary…` | Secondary accents |
| `--shiny-color-tertiary` / `-container`, `on-…` | `Tertiary…` | Tertiary accents |
| `--shiny-color-error` / `-container`, `on-…` | `Error…` | Validation/error (form fields) |
| `--shiny-color-background` | `Background` | Page background |
| `--shiny-color-on-background` | `OnBackground` | Text on background |
| `--shiny-color-surface` | `Surface` | Component/card/sheet background |
| `--shiny-color-on-surface` | `OnSurface` | Primary text/icons |
| `--shiny-color-surface-variant` | `SurfaceVariant` | Muted surfaces, slider tracks |
| `--shiny-color-on-surface-variant` | `OnSurfaceVariant` | Secondary text, placeholders, muted icons |
| `--shiny-color-surface-container-lowest/-low/-/-high/-highest` | `SurfaceContainerLowest…Highest` | Elevated surfaces, hover rows, skeleton base |
| `--shiny-color-surface-tint` | `SurfaceTint` | Tonal elevation tint |
| `--shiny-color-outline` | `Outline` | Borders, dividers (prominent) |
| `--shiny-color-outline-variant` | `OutlineVariant` | Subtle borders, separators, gridlines |
| `--shiny-color-shadow` | `Shadow` | Shadow color (usually black) |
| `--shiny-color-scrim` | `Scrim` | Modal/overlay backdrops (usually black) |
| `--shiny-color-inverse-surface` | `InverseSurface` | Toast/snackbar background |
| `--shiny-color-inverse-on-surface` | `InverseOnSurface` | Text on inverse surface |
| `--shiny-color-inverse-primary` | `InversePrimary` | Accent on inverse surface |
| `--shiny-color-success` / `-container`, `on-…` | `Success…` | Positive status |
| `--shiny-color-info` / `-container`, `on-…` | `Info…` | Informational status |
| `--shiny-color-warning` / `-container`, `on-…` | `Warning…` | Warning status |
| `--shiny-color-caution` / `-container`, `on-…` | `Caution…` | Caution status |
| `--shiny-color-critical` / `-container`, `on-…` | `Critical…` | Critical/danger status |

Status pattern (Pill/Toast/Badge): **container** = soft fill background, **on-container** = text on
that fill, **role** (e.g. `success`) = the vivid border/accent.

## Shape / border / elevation / state / density / type / spacing

These are the axes a theme uses to have a *personality* rather than just a palette. They vary
per-pack, so a hardcoded literal here is as much a theming bug as a hardcoded colour.

| CSS var | MAUI key | Notes |
|---|---|---|
| `--shiny-shape-corner-{none,extra-small,small,medium,large,extra-large,full}` | `Shape.Corner…` and `Shape.Corner…Radius` | The plain key is a `double`; the `…Radius` twin is a `CornerRadius` struct, because a dynamic resource is assigned with no conversion and `RoundRectangle.CornerRadius` will silently drop a double |
| `--shiny-border-{thin,medium,thick}` | `Border.{Thin,Medium,Thick}` | Stroke widths, px / double |
| `--shiny-elevation-{0..5}` | `Elevation.Level0..5` (MAUI `Shadow`) | Blazor = `box-shadow` string. Tinted/outline/glow styles reference colour vars, so they follow dark mode |
| `--shiny-state-{hover,focus,pressed,dragged}-opacity` | `State.…Opacity` | 0..1 |
| `--shiny-density-scale` | `Density.Scale` | Bare multiplier for `calc()` on off-ramp values |
| `--shiny-density-{control-height,control-height-small,row-height,touch-target}` | `Density.…` | px / double. `touch-target` is deliberately never scaled — shrinking the hit area below the platform minimum is an accessibility bug, not a design choice |
| `--shiny-type-font-family{,-display,-mono}` | `Type.FontFamily{,Display,Mono}` | Blazor: a CSS stack (`inherit` when the pack does not set one). MAUI: a font alias the host app registered with `ConfigureFonts`; unregistered names fall back to the system font |
| `--shiny-type-scale` | `Type.Scale` | Bare multiplier for `calc()` on off-scale sizes |
| `--shiny-type-{role}-{size,line-height,weight,tracking}` | `Type.{Role}Size`, `Type.{Role}Attributes` | role = display/headline/title/body/label-large/medium/small. MAUI has no numeric weight on `Label`, so the scale's weight becomes `FontAttributes.Bold` at 600+ |
| `--shiny-spacing-{0..8}` | `Spacing.Space0..8` | 0,4,8,12,16,24,32,48,64 px at density 1 |

### Blazor: on-scale versus off-scale values

Use the role token when the literal is exactly a scale value; wrap anything else in `calc()` against
the matching multiplier, so it still tracks the theme without shifting under the default:

```css
font-size: var(--shiny-type-body-medium-size, 14px);      /* 14 is on the scale */
font-size: calc(13px * var(--shiny-type-scale, 1));       /* 13 is not */
padding: var(--shiny-spacing-2, 8px) calc(10px * var(--shiny-density-scale, 1));
```

**Never wrap an `em` size in that calc.** `em` already resolves against an ancestor the theme has
scaled, so multiplying again applies the scale twice. Apply it once at the control's root instead and
let the `em` children inherit — this is how Wizard's type scales:

```css
.shiny-wizard { font-size: calc(1em * var(--shiny-type-scale, 1)); }   /* identity at scale 1 */
.shiny-wizard__step-label { font-size: 0.8125em; }                     /* left alone */
```

`rem` is the opposite case: it resolves against the document root, which no token touches, so it
does need the calc.

Corner radii snap to the nearest bucket rather than using `calc()` — a 2px radius shift is
imperceptible where a 2px type shift is not. Leave `border-radius: 50%` alone: a circular avatar or
spinner is intrinsic geometry, not themeable chrome. Pill shapes (`999px`) do map to `corner-full`,
so a square theme squares them off.

### MAUI: initializers and unset defaults

An object initializer cannot call `SetDynamicResource`, so chain the helper instead — a literal left
in the initializer beats the theme permanently:

```csharp
this.label = new Label { … }.WithFontSize(ShinyThemeKeys.Type.BodySmallSize);
border.StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerMediumRadius);
border.SetDynamicResource(VisualElement.ShadowProperty, ShinyThemeKeys.Elevation.Level3);
```

Numeric appearance `BindableProperty` defaults use the `ThemeTokens.Unset` sentinel (`-1`) — the same
idea as `null` for colours. `propertyChanged` then routes through `SetTokenOrValue` /
`SetCornerTokenOrValue`, which applies an explicit value or re-binds the token when cleared. Font
sizes only convert on an *exact* match with a role in the type scale; snapping 13px to the nearest
role would visibly re-typeset the default theme.

Never hand a control the theme's own `Shadow` instance if it mutates it — one dictionary entry is
shared by every control that resolves it. `ShinyButton` and `TextEntry` own theirs for that reason
(and are listed in `ThemeGeometryCoverageTests.ShadowIsOwned`).

`ThemeGeometryCoverageTests` fails the build on a hardcoded on-scale font size, a literal
`RoundRectangle` radius, or an ad-hoc `new Shadow`, and asserts end-to-end that swapping a theme
re-renders a live control.

## Common hardcoded value → token cheatsheet

| Original | Token (role) |
|---|---|
| `#FFFFFF`/`white` as a **surface/card bg** | `surface` (or `surface-container-lowest`) |
| `#FFFFFF`/`white` as **text on a colored fill** | the matching `on-*` |
| `#000000`/`black` **text** | `on-surface` |
| `#000000` **overlay/backdrop** | `scrim` |
| `#2196F3` `#3B82F6` `#007AFF` (brand blue) | `primary` |
| `#DC2626` `#EF4444` (red badge/error) | `error` (or `critical`) |
| `#E5E7EB` `#E1E1E1` `#E0E0E0` (light divider/track) | `outline-variant` (border) or `surface-container-highest` (fill) |
| `#D1D5DB` `#CCC` (border) | `outline-variant` |
| `#9CA3AF` `#6B7280` `#888` (muted text/icon) | `on-surface-variant` |
| `#374151` `#1F2937` `#212121` (strong text/track) | `on-surface` |
| `#111827` (near-black text) | `on-surface` |
| `#F3F4F6` `#F9FAFB` (subtle fill/hover) | `surface-container-high` / `surface-container` |
| `#E3F2FD` `#DBEAFE` `#EFF6FF` (selected/highlight) | `secondary-container` or `primary-container` |
| Status sets (success green / info blue / warn yellow / danger red) | matching `*-container` + `on-*-container` + role |
| `rgba(0,0,0,0.x)` hover veil | keep, or `rgba(0,0,0,var(--shiny-state-hover-opacity))` |

## MAUI migration pattern (from `Fab.cs` / `PillView.cs`)

`Color` properties (BackgroundColor, TextColor, label TextColor):
```csharp
view.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Primary);
label.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnPrimary);
```

`Brush` properties (Border.Stroke, Background) — drive a `SolidColorBrush`'s Color so theme swaps propagate:
```csharp
var brush = new SolidColorBrush();
brush.SetDynamicResource(SolidColorBrush.ColorProperty, ShinyThemeKeys.Color.Outline);
border.Stroke = brush;
```

Color `BindableProperty` defaults: change the default to `null` and in `propertyChanged`, apply the
explicit `Color` when set, otherwise re-apply the `SetDynamicResource` (so clearing returns to theme).
Add `using Shiny.Maui.Controls.Themes;`. Don't tokenize `Colors.Transparent`.

After editing, the keys live in `src/Shiny.Maui.Controls/Themes/Generated/ShinyThemeKeys.cs`.

---

## A note on rule 1

Keeping the original hex as a `var()` fallback was the right call while the token system was being
retrofitted onto controls that already had colours. It has since produced 668 hex literals in the
component CSS, and `--shiny-color-primary` alone appears with **15 different fallback values** — so a
mistyped token name does not fail loudly, it renders in a 2019-era blue, and the component CSS can no
longer be read as one design system.

The intent is to drop the fallbacks and let a missing token be visible, once the inline-style
parameter defaults are moved into the stylesheets (they are the reason a theme cannot always reach a
control today). Until then: keep following rule 1 for consistency, and do not invent a *new* fallback
value — copy the one the theme actually ships.
