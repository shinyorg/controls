# CLAUDE.md

Guidance for working in the **Shiny Controls** repo (`Shiny.Maui.Controls` + `Shiny.Blazor.Controls` and their add-on packages).

## Repo layout

- `src/` — one project per package per host. Core: `Shiny.Maui.Controls`, `Shiny.Blazor.Controls`. Add-ons on **both** hosts: `*.Barcodes`, `*.Camera`, `*.Camera.Ai`, `*.Diagram`, `*.FloorPlan`, `*.Gamepad`, `*.Markdown`, `*.MediaElement`, `*.MermaidDiagrams`, `*.Office`, `*.SpeechAddins`, themes `*.Themes.Aurora/Material/Ocean/Terminal`. **MAUI-only**: `Shiny.Maui.Controls.Desktop`, `Shiny.Maui.Controls.Foldables` (the built-in fold posture providers — Jetpack WindowManager on Android, `UIHinge` + reserved regions on iOS 27.1, which is why it has an extra `net10.0-ios27.1` TFM — feeding the `IFoldPostureSource` seam in core), `Shiny.Maui.Controls.Keyframe` (+ `.Keyframe.Export`), `Shiny.Maui.Controls.MediaElement.Linux`, camera analyzers `Camera.Barcode/Documents/Face/Motion/Ocr`. Cross-host shared libraries (referenced by the hosts, also published): `Shiny.Controls.Camera.Shared`, `.Diagram.Shared` (the diagramming engine — the graph model, tidy tree / layered / mindmap / radial / force-directed layouts, connector routing and shape-outline geometry; dependency-free), `.Gamepad.Shared` (the on-screen gamepad engine — layouts, multi-touch hit testing, stick/d-pad maths, and the `VirtualGamepad`/`VirtualGamepadManager` that make it a Shiny.Gamepad `IGamepad`), `.FloorPlan.Shared` (the floor plan engine — document model, Skia renderers, hit-testing, camera, tools; the reason FloorPlan is an add-on and not core is that it drags SkiaSharp with it), `.Gantt.Shared` (the Gantt scheduling engine — calendars, dependency cascades, critical path, time-to-pixel geometry), `.RangePickers.Shared` (the date/time range picker engine — DateRange/TimeRange/DateTimeRange, tap-to-select rules with min/max dates and spans and blocked days, month grids, presets, time slots with duration and overnight rules, compact culture-aware range formatting), `.Gauge.Shared` (the gauge engine — compass-angle scale mapping, 1-2-5 tick generation, fit-to-box dial layout, and the spring/ease needle physics with momentum, overshoot, stop pegs and the ignition sweep; the Blazor `gauge.js` mirrors the physics), `.Keyboard.Shared` (the keyboard shortcut engine — gesture parsing/display, Primary ⌘/Ctrl, layout-aware matching, scopes with modal blocking, chords, repeat/release, native key-code tables; the Blazor JS matcher in `keyboard-shortcuts.js` mirrors its rules), `.Keyframe.Shared`, `.MediaElement.Shared`, `.MotionIcons.Shared`, `.Office.Shared`, `.Office.Skia`. There is **no** `*.Kiosk` package — the Blazor on-screen keyboard and docking ship in `Shiny.Blazor.Controls`.
- `samples/Sample/` — the MAUI + Blazor demo app. Each control has a feature page under `samples/Sample/Features/<Area>/`, wired into `AppShell.xaml` and `MauiProgram.cs`.
- `tests/` — unit tests.
- `SKILLS/shiny-controls/` — the **local skill** (`SKILL.md` + one markdown file per control) that teaches code generation for these controls.
- `README.md` — the front door: summary paragraph, NuGet badges, Getting Started, and a grouped index linking to `docs/controls/`.
- `docs/controls/` — **one markdown file per control** (`datagrid.md`, `document-editor.md`, …), named to match `SKILLS/shiny-controls/` where a skill file exists, plus `styling.md` for the cross-cutting styling/theming note. Images are referenced as `../../assets/…`.
- `themes/` — M3 theme pack seeds.

## Documentation site

The public docs live in a **separate repo**: `~/Desktop/dev/documentation` (Astro / Starlight).

- Controls docs: `src/content/docs/controls/<control>/`
- Controls release notes: `src/content/docs/controls/release-notes.mdx`
- Main menu (sidebar): `src/sidebar-topics.mjs` (the `Controls` topic, ~line 289). The homepage menu (`HomepageNav.astro`) is **auto-generated from this file** — no separate edit needed.
- Homepage: `src/content/docs/index.mdx` — the **"UI Controls"** `<Card>` (~line 149) lists every control grouped by category (Flagship / Layout & Overlays / Input / Display & Media / Status & Feedback / Desktop).

## Required updates for EVERY fix & feature

With each fix and each new feature, update all of the following so they stay in sync:

1. **`docs/controls/<control>.md`** — reflect new/changed behavior in that control's own page (add the page and a row in the README's grouped index if the control is new). Touch `README.md` itself only for the summary paragraph, a NuGet badge, Getting Started, or the index.
2. **Local skill** (`SKILLS/shiny-controls/`) — update the relevant control's `.md` (or add a new one and reference it in `SKILL.md`) so generated code matches.
3. **Shiny docs** (`~/Desktop/dev/documentation`):
   - **Release notes** — add an entry to `src/content/docs/controls/release-notes.mdx`.
   - **Menu** — for a **new feature**, add the new menu node(s) under the `Controls` topic in `src/sidebar-topics.mjs`. (The homepage menu updates automatically from this.)

### Additionally, if the control itself is NEW

4. Add its docs folder under `src/content/docs/controls/<control>/`.
5. Add it to the **homepage section** — the "UI Controls" `<Card>` in `src/content/docs/index.mdx` (place it in the appropriate category group).
6. Add its top-level node to the **main menu** (`src/sidebar-topics.mjs`) under the `Controls` topic — which also surfaces it in the homepage menu.
7. **Leave a screenshot TODO** — do **not** capture screenshots as part of the feature/release work. Instead, after wiring up a new component, note a TODO (e.g. `TODO: capture screenshots for <control>`) so they can be done later on request. See **Screenshots** below.
8. **Pass the Jon Dick Nitpick checklist** (below) on both hosts before calling the control done.

### Additionally, if the PACKAGE is new (or removed / renamed)

9. Add it to the solution (`Shiny.Controls.slnx`) and to `Build.slnf`.
10. Add a NuGet badge to `README.md` — the badge block sits directly under the summary paragraph — and mention the package in the summary paragraph itself.
11. Add it to the **`Package` dropdown** in `.github/ISSUE_TEMPLATE/bug_report.yml` and to the **`Target Package`** dropdown in `.github/ISSUE_TEMPLATE/feature_request.yml`. Both dropdowns mirror `src/` exactly, so a removed or renamed package must come out of them too.
12. Update the **Repo layout** bullet at the top of this file.

## Jon Dick Nitpick checklist (new controls)

Things that get spotted as bugs straight away, so check every new control (and any reworked layout) for them on **both hosts** before it ships. Each one is a symptom of the control fighting the container it was put in rather than fitting it.

1. **No scroll region inside a scroll region (double scrollbars).** A control that scrolls must not end up inside another scroller on the same axis: two scrollbars side by side, or a wheel or finger that scrolls the inner region until it hits the end and then jerks the outer one. Pick one owner per axis. If the control fills its parent, the control scrolls and the parent doesn't (give it a real height: `height:100%` / `flex:1` with `min-height:0` on Blazor, a `Grid` row of `*` on MAUI, never a `ScrollView` around it). If it sizes to its content, it doesn't scroll and the page does. Watch for the usual causes: a scroller in the control's template plus one in the sample page, a Blazor `overflow:auto` on both the root and an inner pane, a MAUI `CollectionView`/`ScrollView` placed inside a `ScrollView`, and a content region that is a few pixels too tall because of a border or padding, which adds a scrollbar that only moves 2px.
2. **No double borders.** When a control with its own border fills a parent that also draws one (a card, a `demo-section`, a `Border`, another Shiny control), the two edges sit together and read as one double-thick line. Only one of them may draw the edge. A control that is designed to fill its host should have a way to drop its own border or stroke (a `Border`/`ShowBorder` parameter, or a CSS custom property / style token), and when one Shiny control hosts another, the inner one is turned off. The same goes for stacked `border-radius` (a rounded inner edge inside a rounded outer edge with a gap of background between them) and for doubled dividers where two adjacent panes each draw the shared edge.

*More to come — add new items here as they are found.*

## Supported platforms

These are the platforms the issue templates offer and that features should be reasoned about against:

- **MAUI** — iOS, Android, Mac Catalyst, macOS (AppKit, `net10.0-macos`), Windows (WinUI), Linux (GTK4).
- **Blazor** — WebAssembly, Server, and Hybrid (MAUI `BlazorWebView`).

Not every package covers every one; MAUI add-ons multi-target `net10.0` plus some subset of `-ios/-android/-maccatalyst/-macos`, and the Windows TFM is only added on Windows or with `-p:ForceWindowsTfm=true`.

## Screenshots (on request)

Capturing control screenshots is a **separate, on-request task** — never run it automatically as part of a fix, feature, or release. When a new control ships, just leave a TODO (see step 7 above). Only capture when the user explicitly asks.

When asked to capture screenshots for a control:

1. **MAUI** — drive the demo app (`samples/Sample/`) to the control's feature page (`samples/Sample/Features/<Area>/`) and capture the screenshot with **mauidevflow**.
2. **Blazor** — drive the Blazor sample to the matching page and capture the screenshot with **Playwright**.
3. **Add them to the docs repo** (`~/Desktop/dev/documentation`):
   - Save images under `public/images/<control>/`, named `s1.png`, `s2.png`, … (one folder per control, sequential names).
   - Reference them from the control's `.mdx` (e.g. `src/content/docs/controls/<control>/index.mdx`) with `<img src="/images/<control>/s1.png" width="220" alt="<Control>" />`.

## Blog posts (only when explicitly requested)

Do **not** write blog posts automatically as part of a fix/feature. Write them **only when the user asks**. When asked to blog a feature, produce **two** posts — first the docs-site version, then adapt it for the personal blog.

### 1. Docs site — `~/Desktop/dev/documentation`

- File: `src/content/docs/blog/YYYY/MM/<slug>.mdx` (current year/month folders; create the month folder if needed).
- Frontmatter:
  ```yaml
  ---
  title: '...'
  description: '...'
  date: YYYY-MM-DD
  authors:
    - allanritchie
  tags:
    - Release        # or Feature, AI, etc.
  ---
  ```
- Body is MDX. Reuse components where relevant, e.g. `import NugetBadge from '/src/components/NugetBadge.astro';` then `<NugetBadge name="Shiny.Maui.Controls.Xxx" />`.
- Voice: product/release-note tone — what shipped, breaking changes, code samples, how to use it. **No hero image** on this site.

### 2. Personal blog — `~/Desktop/dev/blog` (adapt the docs post)

- File: `src/content/blog/YYYY/MM/<slug>.mdx` (note: `content/blog`, not `content/docs/blog`).
- Frontmatter (different schema — see `src/content.config.ts`):
  ```yaml
  ---
  title: '...'
  description: '...'
  pubDate: 'Mon DD YYYY'                          # e.g. 'Jun 15 2026'
  heroImage: '../../../../assets/<slug>-hero.svg'
  tags: ['Shiny', '.NET MAUI', 'Controls']
  ---
  ```
- Voice: rework the docs post into a personal, first-person narrative ("Here's something that shouldn't be hard but is…", "So I built…") — story/motivation up front, not a dry changelog.
- **Hero image is required.** Create `src/assets/<slug>-hero.svg`:
  - SVG, `viewBox="0 0 1200 630"`, `width="1200" height="630"`.
  - Match the house style: dark navy/indigo gradient background (`#0f172a` → `#1e1b4b`), cyan/green/violet accent gradients, subtle glow filters, the feature name as the headline. Crib an existing one (e.g. `datasync-hero.svg`, `documentdb-orleans-hero.svg`) as a starting template.

## Conventions

- Keep MAUI and Blazor at feature parity where the platform allows; note platform-only features explicitly (Desktop and Keyframe are MAUI-only; SheetView, Layout/AppLayout, SplashScreen and the on-screen keyboard are Blazor-only, etc.).
- Add/update a demo page in `samples/Sample/Features/` for any new control or notable feature.
- **Android: a `Java.Lang.Object` that implements a Java *library* interface must implement every default method too.** The generated Java wrapper forwards all of them to .NET, and with minSdk < 24 D8 strips the default bodies out of the interface, so any you leave to the binding throw `AbstractMethodError` the moment Java calls one (black CameraView preview #59, MediaElement crash on play). Return what the Java default returns. Framework (`android.*`) interfaces are not desugared and are safe. To check, look at `samples/Sample/obj/*/net10.0-android/android/src/crc*/<Class>.java`: any `n_…` entry whose handler type is the interface rather than its `…Invoker` is a default method that needs a body.
- Build with `dotnet build Build.slnf`.
