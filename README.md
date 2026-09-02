# Shiny Controls

A rich, ready-to-use UI controls library for both **.NET MAUI** and **Blazor**. One package per host covers TableView, DataGrid (with detail/breakdown rows and a TreeDataGrid hierarchy mode), TreeView, Scheduler, FloatingPanel/OverlayHost, DurationPicker, FrostedGlassView, Toast, Dialogs (owned, animated alert/confirm/prompt/action-sheet), Fab/FabMenu, ShinyToolbar/ShinyTabBar (Blazor), SplashScreen (Blazor), PillView, BadgeView, SecurityPin, PasswordStrength (a password field with a live strength meter, a rule checklist and a pluggable scorer), Captcha (Blazor — one component over a built-in local challenge plus reCAPTCHA, hCaptcha and Turnstile), SignaturePad, ShinyImage, ImageViewer, ImageEditor, MediaPickerButton, ChatView, ColorPicker, FontPicker, Slider, ProgressBar (with an animated, bidirectional fill), ProgressLine (the thin page-edge loading line, docked clear of the nav and tab bars and drivable from code), Overlay/LoadingOverlay, SkeletonView, Expander/Accordion (a disclosure panel whose fade, slide and height reveals combine and are aimed at any of the four edges, and the accordion list that coordinates a stack of them), AutoCompleteEntry, CountryPicker, AddressEntry, TextEntry, CarouselGallery, ParallaxCollectionView, StaggeredGrid, VirtualizedGrid, and StateView/Wizard (named branches switched by one string, and a multi-step flow built on them with a pointed progress bar, per-step validity gates and conditional steps), plus a **TimelineView** — a vertical rail of markers with arbitrary, self-sizing content beside each one, an active position (or all-active) driving which part of the rail is filled, and optional content on the far side of the rail. Walkthrough and Tooltip round that out: a guided tour that dims the page and cuts an animated spotlight around one control at a time — steps declared together in order, advancing on a command, on a tap of the highlighted control, or on a timer, with a RememberRunKey so onboarding runs once — and the themed tooltip bubble underneath it, which wraps its target or points at one, auto-flips to whichever side has room, and is drawn above the page so nothing can clip it. A **Flyout** — a side panel that slides in from either edge, rests as a narrow icon rail instead of a full panel, and either pushes the content aside or floats over it — replaces MAUI's `FlyoutPage` and, unlike it, works inside Shell. On MAUI there is also a **`ShinyNavigationPage`** — a real `NavigationPage`, with everything that implies, whose drawn bar carries toolbar items on the **left** as well as the right, plus an overflow menu, badges and an iOS-style collapsing large title. Blazor additionally gets layout primitives — `VStack`/`HStack`, a responsive `Grid`/`Row`/`Column`, and an `AppLayout` application shell whose left/right panels collapse to hidden, a toolbar rail or fully shown, drag-resize between a min and max, keep their own scroll regions, and can persist and auto-collapse when the shell gets narrow. Motion Icons — 111 animated icons that run on a timer, on hover, on tap or on command — ship in the core packages on both hosts, as does an Office-style **Ribbon** (tabs over titled command groups, with contextual tabs, split and menu buttons, a quick access row, a collapsing body, groups that fold into buttons when the window narrows, and a Simplified one-row mode for phones — which is what the ImageEditor's toolbar is built from). Sliders come in single-value (Slider) and two-thumb range (RangeSlider) flavors. Markdown, Mermaid Diagrams, Barcodes (1D + 2D, QR codes), Keyframe animation (declarative XAML timelines with seekable, reversible playback), and a cross-platform CameraView (preview, photo/video capture, a pluggable effects pipeline for colour/comic/sketch/blur looks, face masks and AI stylization, plus a pluggable frame-analysis pipeline for barcode/face/motion/OCR/structured-documents — and, on MAUI, an injectable **IMediaService** that drives all of it from a modal camera page of Shiny's own: permissions, compression, gallery picking, and one-line `ScanBarcodeAsync`/`ScanCreditCardAsync`/`ScanTextAsync` verbs contributed by the analyzer add-ons) ship as separate add-on packages per host, Office viewers and editors — a Spreadsheet (`SpreadsheetView`) that opens, renders and edits real `.xlsx` workbooks, with an optional formatting toolbar carrying the usual bold/italic/colour controls plus the spreadsheet-only ones: cell fill, number formats, decimal places, whole-column formatting and AutoSum, a `.docx` editor in two controls — `DocumentEditor` (the lone surface) and `DocumentEditorView` (the same plus formatting chrome), with spell check drawn from the platform's own dictionary on MAUI and replaceable on both hosts — a `.pptx` editor in two more — `SlideEditor` and `SlideEditorView` — where a click selects a shape to move or resize, a double-click puts a caret in its text, and a Slide show button plays the deck full screen from the slide being edited, both editors sharing one gallery of twenty preset shapes plus tables, pictures and text highlighting, and both accepting an image dragged in from the desktop, a OneNote-style free-form **Notebook** in two more — `NotebookEditor` and `NotebookEditorView` — an unbounded scrolling page you can write anywhere on, draw over with a pressure-sensitive pen, highlighter, point-or-stroke eraser and lasso, and fill with the same shapes, pictures and rich text as the other editors, organised into sections and pages and saved to a `.shinynote` zip, plus read-only `DocumentView` (`.docx`, reflowed rather than paginated) and `SlideView` (`.pptx`, fitted artboards or a thumbnail grid, with placeholders resolved through layout and master, plus a full-screen presenting mode with speaker notes) — a virtualized SkiaSharp grid shared verbatim by both hosts, over a document kernel with a transactional undo stack and a formula engine, editing the OOXML package surgically so an untouched workbook saves byte-identical — and a cross-platform MediaElement (local + remote audio/video with a themed, per-element-toggleable transport bar, background audio with OS lock-screen controls, and Picture-in-Picture) ship as separate add-on packages per host. Quick Entry — an assistant-style prompt popup (`PromptView`) summoned over whatever the user is looking at, with an optional Siri-style screen-edge glow — ships in the **core** packages on both hosts and works everywhere as an in-app overlay; on desktop it can instead open as a borderless always-on-top OS window over *other applications*, which is what the `Shiny.Maui.Controls.Desktop` add-on adds. That add-on also carries the **desktop-only** system tray / status-bar icon, Visual-Studio-style docking, global hotkeys (Windows, macOS AppKit, MacCatalyst, and Linux), and **window-level file drop** — files dragged from Finder / Explorer onto anywhere in the app window, including on top of a `BlazorWebView`, which MAUI's own `DropGestureRecognizer` cannot see; Blazor has the same service over the whole browser window in its core package. On the web there is no separate add-on: Blazor docking and the touch / kiosk on-screen keyboard both ship in the main `Shiny.Blazor.Controls` package.

[![MAUI NuGet](https://img.shields.io/nuget/v/Shiny.Maui.Controls.svg?label=Shiny.Maui.Controls)](https://www.nuget.org/packages/Shiny.Maui.Controls)
[![Blazor NuGet](https://img.shields.io/nuget/v/Shiny.Blazor.Controls.svg?label=Shiny.Blazor.Controls)](https://www.nuget.org/packages/Shiny.Blazor.Controls)
[![MAUI Markdown NuGet](https://img.shields.io/nuget/v/Shiny.Maui.Controls.Markdown.svg?label=Shiny.Maui.Controls.Markdown)](https://www.nuget.org/packages/Shiny.Maui.Controls.Markdown)
[![Blazor Markdown NuGet](https://img.shields.io/nuget/v/Shiny.Blazor.Controls.Markdown.svg?label=Shiny.Blazor.Controls.Markdown)](https://www.nuget.org/packages/Shiny.Blazor.Controls.Markdown)
[![MAUI Mermaid NuGet](https://img.shields.io/nuget/v/Shiny.Maui.Controls.MermaidDiagrams.svg?label=Shiny.Maui.Controls.MermaidDiagrams)](https://www.nuget.org/packages/Shiny.Maui.Controls.MermaidDiagrams)
[![Blazor Mermaid NuGet](https://img.shields.io/nuget/v/Shiny.Blazor.Controls.MermaidDiagrams.svg?label=Shiny.Blazor.Controls.MermaidDiagrams)](https://www.nuget.org/packages/Shiny.Blazor.Controls.MermaidDiagrams)
[![MAUI Barcodes NuGet](https://img.shields.io/nuget/v/Shiny.Maui.Controls.Barcodes.svg?label=Shiny.Maui.Controls.Barcodes)](https://www.nuget.org/packages/Shiny.Maui.Controls.Barcodes)
[![Blazor Barcodes NuGet](https://img.shields.io/nuget/v/Shiny.Blazor.Controls.Barcodes.svg?label=Shiny.Blazor.Controls.Barcodes)](https://www.nuget.org/packages/Shiny.Blazor.Controls.Barcodes)
[![MAUI Camera NuGet](https://img.shields.io/nuget/v/Shiny.Maui.Controls.Camera.svg?label=Shiny.Maui.Controls.Camera)](https://www.nuget.org/packages/Shiny.Maui.Controls.Camera)
[![Blazor Camera NuGet](https://img.shields.io/nuget/v/Shiny.Blazor.Controls.Camera.svg?label=Shiny.Blazor.Controls.Camera)](https://www.nuget.org/packages/Shiny.Blazor.Controls.Camera)
[![MAUI Camera AI NuGet](https://img.shields.io/nuget/v/Shiny.Maui.Controls.Camera.Ai.svg?label=Shiny.Maui.Controls.Camera.Ai)](https://www.nuget.org/packages/Shiny.Maui.Controls.Camera.Ai)
[![Blazor Camera AI NuGet](https://img.shields.io/nuget/v/Shiny.Blazor.Controls.Camera.Ai.svg?label=Shiny.Blazor.Controls.Camera.Ai)](https://www.nuget.org/packages/Shiny.Blazor.Controls.Camera.Ai)
[![MAUI Camera Barcode NuGet](https://img.shields.io/nuget/v/Shiny.Maui.Controls.Camera.Barcode.svg?label=Shiny.Maui.Controls.Camera.Barcode)](https://www.nuget.org/packages/Shiny.Maui.Controls.Camera.Barcode)
[![MAUI Camera Documents NuGet](https://img.shields.io/nuget/v/Shiny.Maui.Controls.Camera.Documents.svg?label=Shiny.Maui.Controls.Camera.Documents)](https://www.nuget.org/packages/Shiny.Maui.Controls.Camera.Documents)
[![MAUI Camera Face NuGet](https://img.shields.io/nuget/v/Shiny.Maui.Controls.Camera.Face.svg?label=Shiny.Maui.Controls.Camera.Face)](https://www.nuget.org/packages/Shiny.Maui.Controls.Camera.Face)
[![MAUI Camera Motion NuGet](https://img.shields.io/nuget/v/Shiny.Maui.Controls.Camera.Motion.svg?label=Shiny.Maui.Controls.Camera.Motion)](https://www.nuget.org/packages/Shiny.Maui.Controls.Camera.Motion)
[![MAUI Camera Ocr NuGet](https://img.shields.io/nuget/v/Shiny.Maui.Controls.Camera.Ocr.svg?label=Shiny.Maui.Controls.Camera.Ocr)](https://www.nuget.org/packages/Shiny.Maui.Controls.Camera.Ocr)
[![MAUI Keyframe NuGet](https://img.shields.io/nuget/v/Shiny.Maui.Controls.Keyframe.svg?label=Shiny.Maui.Controls.Keyframe)](https://www.nuget.org/packages/Shiny.Maui.Controls.Keyframe)
[![MAUI MediaElement NuGet](https://img.shields.io/nuget/v/Shiny.Maui.Controls.MediaElement.svg?label=Shiny.Maui.Controls.MediaElement)](https://www.nuget.org/packages/Shiny.Maui.Controls.MediaElement)
[![MAUI MediaElement Linux NuGet](https://img.shields.io/nuget/v/Shiny.Maui.Controls.MediaElement.Linux.svg?label=Shiny.Maui.Controls.MediaElement.Linux)](https://www.nuget.org/packages/Shiny.Maui.Controls.MediaElement.Linux)
[![Blazor MediaElement NuGet](https://img.shields.io/nuget/v/Shiny.Blazor.Controls.MediaElement.svg?label=Shiny.Blazor.Controls.MediaElement)](https://www.nuget.org/packages/Shiny.Blazor.Controls.MediaElement)
[![MAUI Office NuGet](https://img.shields.io/nuget/v/Shiny.Maui.Controls.Office.svg?label=Shiny.Maui.Controls.Office)](https://www.nuget.org/packages/Shiny.Maui.Controls.Office)
[![Blazor Office NuGet](https://img.shields.io/nuget/v/Shiny.Blazor.Controls.Office.svg?label=Shiny.Blazor.Controls.Office)](https://www.nuget.org/packages/Shiny.Blazor.Controls.Office)
[![MAUI Desktop NuGet](https://img.shields.io/nuget/v/Shiny.Maui.Controls.Desktop.svg?label=Shiny.Maui.Controls.Desktop)](https://www.nuget.org/packages/Shiny.Maui.Controls.Desktop)
[![MAUI Speech Addins NuGet](https://img.shields.io/nuget/v/Shiny.Maui.Controls.SpeechAddins.svg?label=Shiny.Maui.Controls.SpeechAddins)](https://www.nuget.org/packages/Shiny.Maui.Controls.SpeechAddins)
[![Blazor Speech Addins NuGet](https://img.shields.io/nuget/v/Shiny.Blazor.Controls.SpeechAddins.svg?label=Shiny.Blazor.Controls.SpeechAddins)](https://www.nuget.org/packages/Shiny.Blazor.Controls.SpeechAddins)
[![Keyframe Export NuGet](https://img.shields.io/nuget/v/Shiny.Maui.Controls.Keyframe.Export.svg?label=Shiny.Maui.Controls.Keyframe.Export)](https://www.nuget.org/packages/Shiny.Maui.Controls.Keyframe.Export)
[![Motion Icons NuGet](https://img.shields.io/nuget/v/Shiny.Controls.MotionIcons.Shared.svg?label=Shiny.Controls.MotionIcons.Shared)](https://www.nuget.org/packages/Shiny.Controls.MotionIcons.Shared)
[![MAUI Terminal Theme NuGet](https://img.shields.io/nuget/v/Shiny.Maui.Controls.Themes.Terminal.svg?label=Shiny.Maui.Controls.Themes.Terminal)](https://www.nuget.org/packages/Shiny.Maui.Controls.Themes.Terminal)
[![Blazor Terminal Theme NuGet](https://img.shields.io/nuget/v/Shiny.Blazor.Controls.Themes.Terminal.svg?label=Shiny.Blazor.Controls.Themes.Terminal)](https://www.nuget.org/packages/Shiny.Blazor.Controls.Themes.Terminal)
[![MAUI Aurora Theme NuGet](https://img.shields.io/nuget/v/Shiny.Maui.Controls.Themes.Aurora.svg?label=Shiny.Maui.Controls.Themes.Aurora)](https://www.nuget.org/packages/Shiny.Maui.Controls.Themes.Aurora)
[![Blazor Aurora Theme NuGet](https://img.shields.io/nuget/v/Shiny.Blazor.Controls.Themes.Aurora.svg?label=Shiny.Blazor.Controls.Themes.Aurora)](https://www.nuget.org/packages/Shiny.Blazor.Controls.Themes.Aurora)
[![MAUI Material Theme NuGet](https://img.shields.io/nuget/v/Shiny.Maui.Controls.Themes.Material.svg?label=Shiny.Maui.Controls.Themes.Material)](https://www.nuget.org/packages/Shiny.Maui.Controls.Themes.Material)
[![Blazor Material Theme NuGet](https://img.shields.io/nuget/v/Shiny.Blazor.Controls.Themes.Material.svg?label=Shiny.Blazor.Controls.Themes.Material)](https://www.nuget.org/packages/Shiny.Blazor.Controls.Themes.Material)
[![MAUI Ocean Theme NuGet](https://img.shields.io/nuget/v/Shiny.Maui.Controls.Themes.Ocean.svg?label=Shiny.Maui.Controls.Themes.Ocean)](https://www.nuget.org/packages/Shiny.Maui.Controls.Themes.Ocean)
[![Blazor Ocean Theme NuGet](https://img.shields.io/nuget/v/Shiny.Blazor.Controls.Themes.Ocean.svg?label=Shiny.Blazor.Controls.Themes.Ocean)](https://www.nuget.org/packages/Shiny.Blazor.Controls.Themes.Ocean)

## Getting Started

### .NET MAUI

```bash
dotnet add package Shiny.Maui.Controls
```

Register in your `MauiProgram.cs`:

```csharp
var builder = MauiApp.CreateBuilder();
builder
    .UseMauiApp<App>()
    .UseShinyControls();
```

Add the XAML namespace:

```xml
xmlns:shiny="http://shiny.net/maui/controls"
```

For Markdown controls (separate package):

```bash
dotnet add package Shiny.Maui.Controls.Markdown
```

```xml
xmlns:md="http://shiny.net/maui/markdown"
```

For Mermaid Diagrams (separate package):

```bash
dotnet add package Shiny.Maui.Controls.MermaidDiagrams
```

```xml
xmlns:diagram="http://shiny.net/maui/diagrams"
```

For Barcodes & QR codes (separate package):

```bash
dotnet add package Shiny.Maui.Controls.Barcodes
```

```xml
xmlns:bc="http://shiny.net/maui/barcodes"
```

```xml
<bc:QRCodeView Value="https://shinylib.net" Size="300" />
<bc:BarcodeView Value="5901234123457" Format="Ean13" />
```

Supported formats: QR Code, Aztec, Data Matrix, PDF417, Code 128/39/93, Codabar, EAN-8/13, UPC-A/E, ITF. Output is rendered as PNG via a pure-managed encoder (no SkiaSharp / System.Drawing dependency). Need an SVG string? Call `BarcodeRenderer.RenderSvg(...)` directly.

For Keyframe animation (separate package):

```bash
dotnet add package Shiny.Maui.Controls.Keyframe
```

```xml
xmlns:kf="http://shiny.net/maui/keyframe"
```

For the CameraView (separate package — iOS, Android, Windows, macOS AppKit, Blazor):

```bash
dotnet add package Shiny.Maui.Controls.Camera
```

```csharp
builder
    .UseShinyControls()
    .UseShinyCamera();
```

```xml
xmlns:cam="http://shiny.net/maui/camera"
```

```xml
<cam:CameraView Facing="Back" Filter="None" />
```

The same package registers **`IMediaService`** — inject it for permissions, photo/video capture through a
modal `CameraView` page, gallery picking, and (with the analyzer add-ons) `ScanBarcodeAsync`,
`ScanCreditCardAsync`, `ScanTextAsync` and the rest. See [IMediaService](docs/controls/media-service.md).

```csharp
var code = await media.ScanBarcodeAsync();
var photo = await media.TakePhotoAsync(new PhotoCaptureOptions { CompressionQuality = 80 });
```

For the MediaElement (separate package — iOS, Android, Windows, macOS AppKit, Linux GTK4, Blazor):

```bash
dotnet add package Shiny.Maui.Controls.MediaElement
```

```csharp
builder
    .UseShinyControls()
    .UseShinyMediaElement();
```

```xml
xmlns:media="http://shiny.net/maui/media"
```

```xml
<media:MediaElement Source="https://example.com/clip.mp4" AutoPlay="True" />
```

The full feature set — effects, analyzers, recording, orientation, AI — is in [CameraView](docs/controls/camera.md).

### Blazor

```bash
dotnet add package Shiny.Blazor.Controls
dotnet add package Shiny.Blazor.Controls.Markdown       # optional
dotnet add package Shiny.Blazor.Controls.MermaidDiagrams # optional
dotnet add package Shiny.Blazor.Controls.Barcodes       # optional
```

Add the `@using` directives — typically in `_Imports.razor`:

```razor
@using Shiny.Blazor.Controls
@using Shiny.Blazor.Controls.Cells
@using Shiny.Blazor.Controls.Sections
@using Shiny.Blazor.Controls.Scheduler
@using Shiny.Blazor.Controls.Markdown
@using Shiny.Blazor.Controls.MermaidDiagrams
@using Shiny.Blazor.Controls.Barcodes
@using Shiny.Controls.Barcodes
```

Most controls need no DI registration at all — drop the component into any `.razor` page and its
scoped CSS and JS module come along with it. A handful are driven by a service (Toast, Dialogs, the
splash screen, the walkthrough store, Docking and the on-screen keyboard), and one call covers all
of them, mirroring MAUI's `UseShinyControls()`:

```csharp
using Shiny.Blazor.Controls;

builder.Services.AddShinyControls();
```

With optional configuration, again shaped like the MAUI side:

```csharp
builder.Services.AddShinyControls(cfg => cfg
    .ConfigureDialogs(o => o.DefaultAnimation = DialogAnimation.Zoom)
    .ConfigureKeyboard(o => o.HeightPx = 320)
    .UseHttpImageDownloader()                    // ShinyImage through your HttpClient (optional)
    .AddDockPanel<ExplorerPanel>("explorer", "Explorer", "📁")
);
```

Every individual `AddShinyToast()` / `AddShinyDialogs()` / `AddShinySplashScreen()` /
`AddShinyWalkthrough()` / `AddShinyDocking()` / `AddShinyOnScreenKeyboard()` call still exists, and
all registrations are `TryAdd`, so the two styles compose in either order. Register à la carte when
you want to keep the WASM payload tight; to replace an implementation, use a `SetCustom*` method or
register your own first — first registration wins.

> All of these services are **scoped**, not singleton: they hold per-user UI state. Under WebAssembly
> the two lifetimes are identical, but on Blazor Server a singleton would show one user's toast,
> dialog or keyboard to every connected user.

#### MAUI → Blazor quick reference

| MAUI (XAML) | Blazor (Razor) |
|---|---|
| `<shiny:TableView>` with `<shiny:TableRoot>` | `<TableView>` (no `TableRoot` wrapper) |
| `<shiny:TreeView>` — `ExpandedIcon`/`CollapsedIcon` are `ImageSource` | `<TreeView TItem="…">` — icons are `RenderFragment` slots; adds keyboard navigation |
| `<shiny:PillView>` | `<Pill>` |
| `<shiny:BadgeView Text="…">` (wraps `Content`) | `<BadgeView Text="…">` (wraps `ChildContent`) |
| `<shiny:FloatingPanel>` in `<shiny:OverlayHost>` | `<SheetView>` with `<SheetContent>` child (Blazor uses CSS overlay) |
| `Value="{Binding Pin}"` (TwoWay) | `@bind-Value="pin"` |
| `IsOpen="{Binding IsOpen, Mode=TwoWay}"` | `@bind-IsOpen="isOpen"` |
| `Command="{Binding DoCommand}"` | `OnClick="DoAsync"` / `Clicked="DoAsync"` |
| `Color` type (e.g. `Colors.Blue`) | CSS color string (e.g. `"#2196F3"`) |
| `Fab.Icon="add.png"` (ImageSource) | `<Fab Icon="+">` (inline text/SVG string) |
| `shiny:CarouselGallery` | `<CarouselGallery>` — `PeekAreaInsets` → `PeekAmount`; adds `ShowIndicators` |
| `shiny:ParallaxCollectionView` | `<ParallaxList>` — `HeaderTemplate` → `HeroTemplate`; Blazor uses a JS scroll listener for the transform |
| `shiny:StaggeredGrid` | `<StaggeredGrid>` — `ItemSelectedCommand` → `ItemSelected` EventCallback |
| `shiny:VirtualizedGrid` | `<VirtualizedGrid>` — `CellPadding` → individual padding props; adds `EnableVirtualization`, `GroupedItems` |
| `ItemTemplate` as `DataTemplate` | `ItemTemplate` as `RenderFragment<object>` |
| `IToaster.ShowAsync(text, cfg => {})` (DI) | `IToastService.ShowAsync(text, cfg => {})` (DI + `<ToastHost />`) |
| `IDialogService.Confirm(...)` (DI; auto-attaches) | `IDialogService.Confirm(...)` (DI + `<DialogHost />`) |
| `<shiny:DataGrid>` + `<shiny:DataGridColumn PropertyName="..."/>` (items as `object`) | `<DataGrid TItem="T">` + `<PropertyColumn Property="x => x..."/>` (generic, `RenderFragment` templates) |
| `<shiny:TextEntry>` | `<TextEntry>` |
| `<shiny:Overlay>` in `<shiny:ShinyContentPage.Panels>` | `<Overlay>` (wraps ChildContent; custom content in `<OverlayContent>` slot) |
| `<shiny:LoadingOverlay>` in `<shiny:ShinyContentPage.Panels>` | `<LoadingOverlay>` (wraps ChildContent) |
| `<shiny:ProgressBar>` | `<ProgressBar>` |
| `<shiny:ProgressLine>` | `<ProgressLine>` + `<ProgressLineHost>` |
| `<MauiSplashScreen>` (native, build-time) | `SplashScreen` — `index.html` markup + `splash.js`, driven by `ISplashScreen` / `<SplashScreenHost />` |

`ISchedulerEventProvider` is identical across both hosts.

## Controls

Every control has its own page under **[`docs/controls/`](docs/controls)**. Start with
**[Styling & theming](docs/controls/styling.md)** — implicit styles, what a theme pack changes beyond
colour, and why appearance properties default to `-1` rather than a real value.

### Office

| Control | |
|---|---|
| **[Spreadsheet](docs/controls/spreadsheet.md)** | Open, render and edit real `.xlsx` — virtualized SkiaSharp grid, formula engine, formatting toolbar |
| **[Document & Slide Viewers](docs/controls/document-viewer.md)** | Read-only `.docx` reflowed to the control's width, and `.pptx` as fitted artboards |
| **[Document Editor](docs/controls/document-editor.md)** | A `.docx` editor — typing, styles, inline objects, page margins, platform spell check |
| **[Slide Editor](docs/controls/slide-editor.md)** | A `.pptx` editor — click a shape to move or resize it, double-click for a caret in its text |
| **[Notebook](docs/controls/notebook.md)** | A OneNote-style free-form canvas — write anywhere, draw over it, shapes and pictures, sections and pages |

### Collections & grids

| Control | |
|---|---|
| **[DataGrid](docs/controls/datagrid.md)** | Sorting, filtering, grouping, aggregates, selection, inline editing, paging, virtualization |
| **[TreeDataGrid](docs/controls/tree-data-grid.md)** | The DataGrid in hierarchy mode — nested rows with lazy loading per level |
| **[TableView](docs/controls/tableview.md)** | Settings-style table with 14 cell types, sections, cascading styles and drag-sort |
| **[TreeView](docs/controls/treeview.md)** | Hierarchical tree with lazy loading, drag/drop reorder and selection predicates |
| **[VirtualizedGrid](docs/controls/virtualized-grid.md)** | Grouped grid with sticky headers, virtualization and load-more |
| **[StaggeredGrid](docs/controls/staggered-grid.md)** | Pinterest-style masonry layout with variable-height items |
| **[ParallaxCollectionView](docs/controls/parallax-collection-view.md)** | A list with a hero header that translates, collapses and fades as it scrolls |
| **[CarouselGallery](docs/controls/carousel-gallery.md)** | Netflix-style carousel with snap-to-center, scale transforms and peek insets |
| **[Carousel](docs/controls/carousel.md)** | Embla-style drag carousel for Blazor — looping, autoplay, thumbnails, scroll-linked effects |

### Layout & overlays

| Control | |
|---|---|
| **[Layout & AppLayout](docs/controls/layout.md)** | VStack/HStack, a responsive 12-column grid, and an app shell with collapsible panels (Blazor) |
| **[FloatingPanel + OverlayHost](docs/controls/floating-panel.md)** | Draggable bottom/top panel with detents, header peek and backdrop management (MAUI) |
| **[Flyout](docs/controls/flyout.md)** | Side panel that rests as an icon rail and pushes or floats over content (MAUI) |
| **[TabbedPage & TabBar](docs/controls/tabbedpage.md)** | Tabs with motion icons, badges, transitions and a raised centre button (MAUI) |
| **[NavigationPage](docs/controls/navigationpage.md)** | Toolbar items on the **left** as well as the right, overflow, badges, large title (MAUI) |
| **[ShinyToolbar & ShinyTabBar](docs/controls/toolbar-tabbar.md)** | Screen-docked action toolbar and mobile-style tab bar (Blazor) |
| **[Ribbon](docs/controls/ribbon.md)** | Office-style tabbed command bar — groups, contextual tabs, collapse and overflow (desktop + Blazor) |
| **[Modal](docs/controls/modal.md)** | A modal window with focus trap, scroll lock, drag, resize and maximize (Blazor) |
| **[Overlay & LoadingOverlay](docs/controls/overlay.md)** | Full-screen overlay with a custom template or a built-in loading mode |
| **[Expander & Accordion](docs/controls/expander.md)** | Disclosure panel whose fade/slide/height reveals combine, plus the accordion that stacks them |
| **[StateView & Wizard](docs/controls/wizard.md)** | Named branches switched by one string, and the multi-step flow built on them |
| **[Timeline](docs/controls/timeline.md)** | Vertical rail of markers with arbitrary content beside each one, and an active position |
| **[Quick Entry](docs/controls/quick-entry.md)** | Assistant-style prompt popup over whatever the user is looking at, with a screen-edge glow |
| **[Fab & FabMenu](docs/controls/fab.md)** | Floating action button and the expanding multi-action menu |
| **[FrostedGlassView](docs/controls/frosted-glass.md)** | Native blur — `UIVisualEffectView`, `RenderEffect`, `backdrop-filter` |

### Input

| Control | |
|---|---|
| **[ShinyButton](docs/controls/button.md)** | A real working state, success/error states, and leading/trailing icon slots |
| **[TextEntry](docs/controls/textentry.md)** | Floating placeholder, tool slots, validation hints, character count, keyboard accessory |
| **[AutoCompleteEntry](docs/controls/autocomplete.md)** | Debounced search with dropdown suggestions and custom item templates |
| **[CountryPicker](docs/controls/country-picker.md)** | Country search with flag emoji, name and dial code |
| **[AddressEntry](docs/controls/address-entry.md)** | Address search with geocoding and structured results |
| **[ColorPicker](docs/controls/colorpicker.md)** | Spectrum, hue bar, opacity slider, hex input and preview swatch |
| **[FontPicker](docs/controls/fontpicker.md)** | Family and size pickers, each font previewed in its own typeface |
| **[Slider](docs/controls/slider.md)** | Cold-to-hot gradient track, blended thumb and tooltip |
| **[RangeSlider](docs/controls/range-slider.md)** | Two thumbs, per-thumb tooltips and min/max gap constraints |
| **[SecurityPin](docs/controls/security-pin.md)** | PIN entry with individually rendered cells and optional masking |
| **[PasswordStrength](docs/controls/password-strength.md)** | Live strength meter, rule checklist and a pluggable async scorer |
| **[Captcha](docs/controls/captcha.md)** | A local challenge, reCAPTCHA, hCaptcha or Turnstile behind one component (Blazor) |
| **[SignaturePad](docs/controls/signature-pad.md)** | Signature capture on a canvas with PNG export |
| **[MediaPickerButton](docs/controls/media-picker-button.md)** | Add photos from gallery or camera, re-encoded and capped, shown in a carousel |
| **[DurationPicker](docs/controls/duration-picker.md)** | Duration picker in a floating panel, with min/max and interval constraints |

### Display & media

| Control | |
|---|---|
| **[ShinyImage](docs/controls/shiny-image.md)** | Remote image loading with placeholder, progress ring, caching, and SVG on MAUI |
| **[ImageViewer](docs/controls/image-viewer.md)** | Full-screen overlay with pinch, pan and double-tap zoom |
| **[ImageEditor](docs/controls/image-editor.md)** | Crop, rotate, draw, shapes, text annotations, undo/redo and export |
| **[CameraView](docs/controls/camera.md)** | Preview, photo/video capture, an effects pipeline, and pluggable frame analyzers |
| **[IMediaService](docs/controls/media-service.md)** | MAUI service over a modal CameraView — permissions, capture, gallery, and Scan… for barcodes/OCR/documents |
| **[MediaElement](docs/controls/mediaelement.md)** | Local and remote audio/video, background playback with lock-screen controls, and PiP |
| **[ChatView](docs/controls/chatview.md)** | Provider-driven chat UI — bubbles, typing, paging, reactions, attachments, composer |
| **[Scheduler](docs/controls/scheduler.md)** | Calendar grid, agenda timeline and event list over one provider interface |
| **[Markdown](docs/controls/markdown.md)** | Native markdown renderer and an editor with a formatting toolbar and live preview |
| **[Mermaid Diagrams](docs/controls/mermaid-diagrams.md)** | Native Mermaid flowcharts — no WebView, AOT-compatible on MAUI |
| **[Barcodes & QR Codes](docs/controls/barcodes.md)** | Pure-managed 1D/2D barcode and QR rendering across 13 symbologies |
| **[Keyframe Animation](docs/controls/keyframe.md)** | Declarative XAML timelines that seek, reverse and export deterministically (MAUI) |
| **[Motion Icons](docs/controls/motion-icons.md)** | 111 animated icons driven by a timer, hover, tap, appearance or a command |

### Status & feedback

| Control | |
|---|---|
| **[Toast](docs/controls/toast.md)** | Code-invoked toasts with a queue, auto-dismiss, spinner and progress bar |
| **[Dialogs](docs/controls/dialogs.md)** | Owned, animated alert / confirm / prompt / action sheet — never the native one |
| **[ProgressBar](docs/controls/progressbar.md)** | Gradient fill with a Vista-style shimmer, determinate and indeterminate |
| **[ProgressLine](docs/controls/progressline.md)** | The thin page-edge loading line, docked clear of the nav and tab bars |
| **[BadgeView](docs/controls/badge.md)** | Content-wrapping corner badge with text, dot, count overflow and pulse |
| **[PillView](docs/controls/pillview.md)** | Status pills and chips with preset themes and accessible contrast |
| **[SplashScreen](docs/controls/splash-screen.md)** | A boot splash that paints before Blazor starts (Blazor) |

### Guidance

| Control | |
|---|---|
| **[Walkthrough](docs/controls/walkthrough.md)** | Dim the page and cut an animated spotlight around one control at a time |
| **[Tooltip](docs/controls/tooltip.md)** | Themed bubble that wraps or points at a target and auto-flips to whichever side has room |

### Desktop

| Control | |
|---|---|
| **[Desktop add-on](docs/controls/desktop.md)** | Tray icon, docking, desktop Quick Entry and global hotkeys — plus the on-screen keyboard |
| **[File Drop](docs/controls/file-drop.md)** | Files dropped anywhere on the window, including over a `BlazorWebView` |
