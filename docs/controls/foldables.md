# Foldables

[← All Shiny Controls](../../README.md)

Fold-aware layouts for Android foldables (Pixel Fold and friends) and the **iPhone Duo**. When a fold divides the window, the controls that split, overlay or centre content line themselves up with it: a `FlyoutView` panel snaps its inner edge to the hinge, dialogs, overlays and toasts move off it, a `FloatingPanel` gains a detent that sits on a tabletop fold, and the Gantt, DataGrid, dock splitters, carousels, Walkthrough and tab bar keep clear of it.

The fold logic is built into the core `Shiny.Maui.Controls` package behind a small posture seam. The **`Shiny.Maui.Controls.Foldables`** add-on supplies the built-in providers that feed it. Blazor gets the same behaviour from CSS viewport-segment media queries in the core `Shiny.Blazor.Controls` package, with no add-on.

> **No source, no change.** Until a posture source is registered every control behaves exactly as it always has. The fold-aware code does not even subscribe. Registering a source (by adding the Foldables package, or calling `UseFoldPosture`) is the opt-in.

## Install (MAUI)

```bash
dotnet add package Shiny.Maui.Controls.Foldables
```

```csharp
builder
    .UseShinyControls()
    .UseShinyFoldables();   // either order
```

`UseShinyFoldables()` registers `FoldPostureProvider.Instance` and wires it to the app lifecycle:

| Platform | Source | Notes |
|---|---|---|
| Android | Jetpack WindowManager (`Xamarin.AndroidX.Window.WindowJava` 1.5.1.3): `WindowInfoTracker` → `FoldingFeature` | Follows the resumed Activity. Bounds are converted from px to dp. Posture streams live only while the Activity handles `ScreenSize \| SmallestScreenSize \| ScreenLayout \| Orientation` config changes (MAUI's default `MainActivity` does). Without them a fold recreates the Activity and the posture is read again when it comes back. |
| iOS 27.1+ (iPhone Duo) | `UIHingeInteraction` for the hinge status, plus the window's active `division` reserved region (`UIView.GetReservedRegions`) for the fold rectangle and its margins | Built into the package's `net10.0-ios` build (compiled against the iOS 27.2 bindings), so a plain `net10.0-ios` app gets it. Guarded at runtime with `OperatingSystem.IsIOSVersionAtLeast(27, 1)` and never active on Mac Catalyst. |
| Everything else | — | `FoldPostureProvider.IsSupported` is false and the posture is always `FoldInfo.None`. |

Posture is reported for the current Activity or key window only.

## The API

All in the core package, namespace `Shiny.Maui.Controls` (registration on `ShinyControlConfiguration`, namespace `Shiny`):

```csharp
public enum FoldPostureState { None, Flat, HalfOpen }
public enum FoldPostureOrientation { None, Vertical, Horizontal }   // Vertical = book, Horizontal = tabletop

public sealed record FoldInfo(
    FoldPostureState State,
    FoldPostureOrientation Orientation,
    Rect? Fold,                      // window coordinates, dp / pt; null unless the fold divides the window
    Thickness HingeMargin = default) // space the platform asks content to keep clear of the fold
{
    public static FoldInfo None { get; }
    public bool IsSeparating { get; }   // Fold != null
    public bool IsBook { get; }
    public bool IsTabletop { get; }
    public Rect? Avoid { get; }         // Fold grown by HingeMargin: what the controls keep out of
}

public interface IFoldPostureSource
{
    FoldInfo Current { get; }
    event EventHandler? Changed;        // any thread; the controls marshal to the UI thread
}

public enum FoldBehavior { None, SnapToFold }   // two-pane controls; SnapToFold is the default

// ShinyControlConfiguration
public ShinyControlConfiguration UseFoldPosture(IFoldPostureSource? source);

// Shiny.Maui.Controls.Foldables
public sealed class FoldPostureProvider : IFoldPostureSource
{
    public static FoldPostureProvider Instance { get; }
    public static bool IsSupported { get; }
}
```

The enums are called `FoldPosture…`, not `FoldState`, on purpose: Shiny.Core has its own `Shiny.FoldState`, and an app importing both namespaces would otherwise get an ambiguous reference.

**An explicit source always wins.** A source passed to `UseFoldPosture` replaces the Foldables package's provider whichever is registered first. Pass `null` to turn fold handling off.

### Fold geometry rules

- Only a fold that **divides the window** counts. A Pixel Fold laid flat, or an iPhone Duo fully open, reports a state but no rectangle, so nothing reacts, and the device behaves like a big phone or a tablet, which is what both platforms recommend.
- The fold is mapped into each control's own coordinates (through the platform view, so toolbars and navigation bars are accounted for) and only counts when its centre line falls **inside** the control. A Flyout in a pane that does not reach the fold does not react.
- **Zero-width folds** (most Android foldables) get no gutter. When the platform reports a hinge margin (the iPhone Duo's division region carries margins), the margin becomes the gutter on each side.
- Snapping is always an **arrange-time override**. No control writes your width or ratio property, so opening the device flat puts back exactly what the user had.

## What each control does

| Control | Book (vertical fold) | Tabletop (horizontal fold) |
|---|---|---|
| **FlyoutView / ShinyFlyoutPage** (`FlyoutPanel.FoldBehavior`, default `SnapToFold`) | An **expanded** panel pushes (whatever `Presentation`/`CompactWidth` say), its inner edge sits on the fold and the content starts on the far side, narrowed into that segment even in `Shift` mode. `MinExpandedWidth`/`MaxExpandedWidth` are **ignored** while snapped (a fold position is not something the user can fix), the resize grip hides, and there is no scrim. The End panel mirrors. With a panel on both sides only the one **nearer the fold** snaps, and the far one snaps only when the near side shows nothing. A collapsed rail never snaps. | No change. |
| **Dialogs** (`IDialogService`) | Centred in the larger segment (ties go to the leading side, which is the right-hand side in RTL). | Top segment. Action sheets go to the bottom segment, under the thumb. |
| **Overlay / LoadingOverlay** | Content in the larger segment. | Top segment, or the bottom one when `ContentAlignment` is `End`. The backdrop still covers the whole page. |
| **Quick Entry (in-app)** | Placed by its Overlay, and its width is clamped to the segment. | As the Overlay. |
| **Toast** | In the larger segment (pill and fill modes). | `Bottom` toasts below the fold, `Top` toasts above it. |
| **FloatingPanel** (`FoldDetent`, default `true`) | No change. A sheet across both halves is what the system does too. | An implicit detent whose edge sits exactly on the fold. The panel **opens to it**, so a bottom panel fills the lower half. When the posture leaves tabletop it returns to the nearest declared detent. |
| **GanttView** (`FoldBehavior`) | The splitter is centred on the fold (widened to the hinge if that is wider than 6) with the task list on one side and the chart on the other. `TaskPaneWidth` is untouched and the splitter cannot be dragged while snapped. | No change. |
| **DataGrid** (`FoldBehavior`) | Each pinned run (`FrozenColumns` / `FrozenEndColumns`) keeps only the columns that fit on its own side of the fold. The rest scroll with the body until the device is opened flat. Needs `HorizontalScroll`. | No change. |
| **DockSplitter** (Desktop package, `FoldBehavior`) | A side-by-side split lines its splitter up with the fold. Display-only: `Ratio` and the saved layout keep the user's ratio, and dragging pauses while snapped. Locked splitters do not snap. | A stacked split lines up with the fold. |
| **CarouselGallery** | The focused item snaps to the centre of the larger segment (Android and iOS handlers). | No change. |
| **CoverFlowView** | The centre cover and its caption stand in the larger segment. | No change. |
| **ImageViewer** | The full-screen image is fitted into the larger segment. | Top segment. |
| **Walkthrough** | The callout is placed inside the segment its target is in (a centred callout goes to the larger segment), so it never opens across the hinge. | Same, top/bottom. |
| **ShinyTabBar / ShinyTabbedPage** | Tabs are dealt to the two segments in proportion to their widths, with a gap column exactly over the fold. A raised centre button counts as one more slot and is pinned over its own column instead of the bar's middle. | No change. The bar is already in the bottom segment. |

The iPhone Duo moves system tab bars to the side of the inner display (`UITraitCollection.VerticalBarEdge`). `ShinyTabBar` is drawn by Shiny, so it does not get that behaviour, and a side "rail" placement is not part of this release. For side navigation on a large display, use a `FlyoutView` rail.

## Using Shiny.Core's `IScreenPosture`

If your app already uses Shiny.Core's `IScreenPosture`, you can feed it to the controls instead of the Foldables package with a small adapter. This is illustrative only: `IScreenPosture` ships in the unpublished Shiny.Core 6.4 beta, and its enums are `Shiny.FoldState` / `Shiny.FoldOrientation`.

```csharp
sealed class ScreenPostureFoldSource : IFoldPostureSource
{
    readonly IScreenPosture posture;
    public ScreenPostureFoldSource(IScreenPosture posture)
    {
        this.posture = posture;
        posture.Changed += (_, _) => this.Changed?.Invoke(this, EventArgs.Empty);
    }

    public FoldInfo Current => new(
        (FoldPostureState)(int)posture.Current.State,              // None, Flat, HalfOpen
        (FoldPostureOrientation)(int)posture.Current.Orientation,  // None, Vertical, Horizontal
        posture.Current.Fold is { } r ? new Rect(r.X, r.Y, r.Width, r.Height) : null);

    public event EventHandler? Changed;
}

// builder.UseShinyControls(cfg => cfg.UseFoldPosture(new ScreenPostureFoldSource(screenPosture)));
```

## Simulating a fold

A source is just an interface, so a debug "simulate a fold" switch is a wrapper. The sample (`samples/Sample/Features/Foldables`) wraps the package's provider:

```csharp
public sealed class SampleFoldSource : IFoldPostureSource
{
    IFoldPostureSource? inner;
    FoldInfo? simulated;

    public static SampleFoldSource Instance { get; } = new();

    public SampleFoldSource Wrap(IFoldPostureSource? platform)
    {
        inner = platform;
        if (platform is not null)
            platform.Changed += (_, _) => { if (simulated is null) Changed?.Invoke(this, EventArgs.Empty); };
        return this;
    }

    public FoldInfo? Simulated
    {
        get => simulated;
        set { simulated = value; Changed?.Invoke(this, EventArgs.Empty); }
    }

    public FoldInfo Current => simulated ?? inner?.Current ?? FoldInfo.None;
    public event EventHandler? Changed;
}

// MauiProgram
builder
    .UseShinyControls(cfg => cfg.UseFoldPosture(SampleFoldSource.Instance.Wrap(FoldPostureProvider.Instance)))
    .UseShinyFoldables();

// a book fold down the middle of the window, 24 wide
SampleFoldSource.Instance.Simulated = new FoldInfo(
    FoldPostureState.HalfOpen, FoldPostureOrientation.Vertical,
    new Rect((window.Width - 24) / 2, 0, 24, window.Height));
```

The Foldables sample page has Device / Book / Tabletop / Flat buttons and a hinge-width switch, and puts a Flyout, dialogs, a toast, a loading overlay and a FloatingPanel on one screen.

## Blazor

There is no add-on and no C# service. The components read the fold from CSS: the `horizontal-viewport-segments` / `vertical-viewport-segments` media features and the `env(viewport-segment-*)` values (the Viewport Segments API). Only **Chromium on Android foldables** reports more than one segment, so everywhere else none of the rules apply and nothing changes.

| Component | Book | Tabletop |
|---|---|---|
| `AppLayoutPanel` (`FoldBehavior`, default `SnapToFold`) | An expanded, docked (not overlay) panel is sized to its segment. The left panel takes segment 0. The right panel takes segment 1 only when no left panel is shown. The hinge becomes the panel's margin and the resize handle hides. **Assumes the `AppLayout` spans the viewport**, which is what an app shell does, because the env values are viewport coordinates. | — |
| `DialogHost` | Dialog confined to the first segment. The backdrop still dims everything. | Top segment, and action sheets go to the bottom one. |
| `ModalView` | First segment. | Top segment, and `place-bottom` modals go to the bottom one. |
| `Overlay` / `LoadingOverlay` | Content in the first segment. | Top segment. |
| `ToastHost` | First segment. | Already correct: bottom toasts sit in the bottom segment, top toasts in the top one. |
| `ImageViewer` | Image fitted into the first segment. | Top segment. |
| `SheetView` (`FoldDetent`, default `true`) | — | `sheet.js` reads `window.viewport.segments` and adds a detent whose edge sits on the fold. The sheet opens to it, and leaves it for the nearest declared detent when the posture changes. It also listens to `navigator.devicePosture` `change`. |

CSS cannot choose the *larger* segment, so Blazor overlays always use the first one.

### Simulating a fold in the browser

The same rules are also keyed on a class on `<html>`, so they can be tested on any browser:

```js
const html = document.documentElement;
// book: the fold's left and right edges, px from the viewport's left
html.style.setProperty('--shiny-fold-sim-left', '400px');
html.style.setProperty('--shiny-fold-sim-right', '424px');
html.classList.add('shiny-fold-sim-book');
// tabletop: --shiny-fold-sim-top / --shiny-fold-sim-bottom + 'shiny-fold-sim-tabletop'
window.dispatchEvent(new Event('shiny-foldchange'));   // tells an open SheetView to re-read the posture
```

Chrome DevTools' dual-screen device emulation drives the real media features too. The Blazor sample's `/foldables` page has simulate buttons and a full-screen `AppLayout` to try it on.

TODO: capture screenshots for foldables (on request only).
