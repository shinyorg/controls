# Foldables (fold-aware controls)

Android foldables (Pixel Fold etc.) and the iPhone Duo (iOS 27.1). When a fold **divides the window**,
the controls line up with it. The logic is in core `Shiny.Maui.Controls`, behind a posture seam. The
add-on **`Shiny.Maui.Controls.Foldables`** supplies the built-in providers. Blazor does it in CSS in core
`Shiny.Blazor.Controls`, and there is no Blazor add-on.

**Rule: with no posture source registered, nothing changes.** Never add fold code paths "just in case".
Registering the source is the opt-in.

## Setup (MAUI)

```csharp
builder
    .UseShinyControls()
    .UseShinyFoldables();   // Shiny.Maui.Controls.Foldables; either order
```

- Android: Jetpack WindowManager (`Xamarin.AndroidX.Window.WindowJava`), following the resumed Activity.
  Live updates need MAUI's default `MainActivity` ConfigChanges (ScreenSize/SmallestScreenSize/ScreenLayout/Orientation).
- iOS: `UIHingeInteraction` plus the `division` reserved region. In the package's `net10.0-ios` build (compiled
  against the iOS 27.2 bindings), so a plain `net10.0-ios` app gets it; active on iOS 27.1+ only. Never on Mac Catalyst.
- Other platforms: `FoldPostureProvider.IsSupported == false`, posture is always `FoldInfo.None`.

## API (namespace `Shiny.Maui.Controls`)

```csharp
public enum FoldPostureState { None, Flat, HalfOpen }
public enum FoldPostureOrientation { None, Vertical, Horizontal }      // Vertical = book
public sealed record FoldInfo(FoldPostureState State, FoldPostureOrientation Orientation,
    Rect? Fold /* window dp; null unless it divides the window */, Thickness HingeMargin = default)
{ static None; bool IsSeparating, IsBook, IsTabletop; Rect? Avoid; }
public interface IFoldPostureSource { FoldInfo Current { get; } event EventHandler? Changed; }
public enum FoldBehavior { None, SnapToFold }                         // default SnapToFold everywhere
cfg.UseFoldPosture(IFoldPostureSource? source);                        // ShinyControlConfiguration
Shiny.Maui.Controls.Foldables.FoldPostureProvider.Instance / .IsSupported
```

- Do **not** name things `FoldState`/`FoldOrientation` in generated code. Those are Shiny.Core's
  (`Shiny.FoldState`), and the controls' enums are `FoldPosture…` to avoid ambiguity.
- An explicit `UseFoldPosture` source **always wins** over the package's provider, in either order.
- To bridge Shiny.Core's `IScreenPosture` (6.4 beta), write a small `IFoldPostureSource` adapter that
  maps State/Orientation (same member order) and copies `Fold`. Do not reference Shiny.Core from the controls.
- To simulate a fold, wrap the provider: `cfg.UseFoldPosture(myDebugSource.Wrap(FoldPostureProvider.Instance))`,
  where `Current => simulated ?? inner.Current`. Fold rects are in **window** coordinates.

## Per-control behaviour (MAUI)

| Control | Book (vertical fold) | Tabletop |
|---|---|---|
| `FlyoutPanel.FoldBehavior` | An expanded panel pushes, its inner edge sits on the fold, and the content starts after the hinge. Min/MaxExpandedWidth are ignored, the grip hides, End mirrors, and only the panel nearer the fold snaps. `ExpandedWidth` is never written. | none |
| Dialogs | larger segment | top; action sheet bottom |
| Overlay / LoadingOverlay / in-app QuickEntry | larger segment | top (bottom if `ContentAlignment=End`) |
| Toast | larger segment | Bottom toast below the fold, Top toast above it |
| `FloatingPanel.FoldDetent` (bool, true) | none | implicit detent on the fold, opened to; nearest declared detent when it goes |
| `GanttView.FoldBehavior` | splitter centred on the fold; `TaskPaneWidth` untouched | none |
| `DataGrid.FoldBehavior` | frozen runs keep only the columns that fit their side (needs HorizontalScroll) | none |
| `DockSplitter.FoldBehavior` (Desktop pkg) | side-by-side split on the fold, display-only | stacked split on the fold |
| CarouselGallery / CoverFlowView / ImageViewer | centred in the larger segment | ImageViewer: top |
| Walkthrough | callout in the target's segment | same |
| ShinyTabBar | tabs dealt to both segments, gap column over the fold, centre button pinned | none |

Zero-width folds get no gutter. The platform's hinge margin, if any, is the gutter.

## Blazor (CSS, Chromium on Android only)

- `AppLayoutPanel FoldBehavior="FoldBehavior.SnapToFold"` (default): an expanded docked panel is sized
  to its segment. It assumes the AppLayout spans the viewport.
- `DialogHost`, `ModalView`, `Overlay`, `ToastHost`, `ImageViewer` go into the first segment for a book
  and the top segment for a tabletop (action sheet and `place-bottom` modal go to the bottom).
- `SheetView FoldDetent="true"` (default): a tabletop detent from `window.viewport.segments`.
- Simulation on any browser: put class `shiny-fold-sim-book` (with `--shiny-fold-sim-left/-right`) or
  `shiny-fold-sim-tabletop` (with `--shiny-fold-sim-top/-bottom`) on `<html>`, then dispatch
  `window` event `shiny-foldchange`.
