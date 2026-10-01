# Docking (Desktop)

A Visual-Studio-style window-docking system for desktop apps — dockable tool windows, tabbed groups, splitters, auto-hide rails, and tear-off floating windows.

| Package | Use when |
|---|---|
| **`Shiny.Maui.Controls.Desktop`** (`Shiny.Maui.Controls.Desktop.Docking` namespace) | .NET MAUI desktop apps. Combined with the [Tray Icon](./tray-icon.md) feature in the same package since both share the desktop TFM matrix (`net10.0;-windows;-maccatalyst;-macos` + Linux GTK4 path) |
| **`Shiny.Blazor.Controls`** (`Shiny.Blazor.Controls.Docking` namespace) | Blazor apps — **no add-on package**, docking is part of the main Blazor package. WASM-supported in v1; Blazor Server / `BlazorWebView` will be supported as the contracts stabilize |

Mobile (iOS / Android) is **out of scope by design**. Tear-off floating windows on macOS Catalyst are limited to in-app floating — native AppKit (`net10.0-macos`) and Windows get full tear-off via `NSPanel` / `AppWindow`; native GTK4 windows on Linux.

## Setup (.NET MAUI)

```bash
dotnet add package Shiny.Maui.Controls.Desktop
```

`MauiProgram.cs`:

```csharp
using Shiny;
using MyApp.Panels;   // SolutionExplorerPanel, OutputPanel — your dockable views

var builder = MauiApp.CreateBuilder();
builder
    .UseMauiApp<App>()
    .UseShinyDocking()
    .AddDockPanel<SolutionExplorerPanel>("solution-explorer", displayName: "Explorer", icon: "📁")
    .AddDockPanel<OutputPanel>("output");
```

Each call to `AddDockPanel<TView>("id", displayName: …, icon: …, canClose: …)`:

1. Registers `TView` as a transient service in DI.
2. Registers an `IDockableContentFactory` keyed to the stable string ID you supply.
3. Optionally sets the tab `displayName` (defaults to the panel ID) and an `icon` (emoji / unicode glyph) for the tab and collapsed edge bars. A panel view that implements `IDockableContent` overrides both per-instance.
4. Optionally sets `canClose: false` for a panel the surface cannot function without — a file explorer's folder tree, an editor's document area. That hides the tab's close button **and** makes `HidePanelAsync` refuse the panel, so a stray call cannot strand a layout the user has no way to rebuild. Defaults to `true`.

Persisted dock layouts reference panels by that ID — so you can rename the C# class without breaking saved layouts, and unknown IDs in a stored layout get parked in a "missing panels" tray rather than silently dropped.

Attach the host inside any existing `ContentPage` — `DockHostView` is a `ContentView`, **not** a `ContentPage` subclass. You keep full control of your Shell / page architecture:

```xml
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:docking="clr-namespace:Shiny.Maui.Controls.Desktop.Docking;assembly=Shiny.Maui.Controls.Desktop"
             x:Class="MyApp.DockingShellPage"
             Title="IDE">
    <docking:DockHostView x:Name="Dock"
                          InitialLayout="{Binding StartupLayout}"
                          LayoutStore="{Binding LayoutStore}"
                          IsLocked="{Binding IsLayoutLocked}" />
</ContentPage>
```

`DockHostView` implements `IDockHost` directly — call `Dock.ShowPanelAsync(...)`, `Dock.ResetLayoutAsync()`, subscribe to `Dock.Events`, etc. from code-behind. Bindable properties: `InitialLayout` (`DockRoot?` — the layout to build when no persisted layout loads), `LayoutStore` (`IDockLayoutStore?` — when set, the host loads from it at startup and auto-saves layout changes, debounced by `SaveDebounceMs`), and `IsLocked` (`bool`).

A dockable panel is just a regular `View`:

```csharp
public sealed class SolutionExplorerPanel : ContentView
{
    public SolutionExplorerPanel(/* inject anything you need */)
    {
        Content = new VerticalStackLayout
        {
            Padding = 12,
            Children = { new Label { Text = "Solution Explorer" } }
        };
    }
}
```

## Setup (Blazor)

No extra package — docking ships in `Shiny.Blazor.Controls`.

`Program.cs`:

```csharp
using Shiny.Blazor.Controls.Docking;

builder.Services
    .AddShinyDocking()
    .AddDockPanel<SolutionExplorerPanel>("solution-explorer", displayName: "Explorer", icon: "📁")
    .AddDockPanel<OutputPanel>("output");
```

`_Imports.razor`:

```razor
@using Shiny.Blazor.Controls.Docking
```

Any razor page:

```razor
<DockHost @ref="host"
          InitialLayout="@layout"
          LayoutStore="@layoutStore"
          IsLocked="@locked" />
```

Component parameters: `InitialLayout` (`DockRoot?`), `IsLocked` (`bool`), `LayoutStore` (`IDockLayoutStore?` — loaded at startup, auto-saved with debounce on every layout change), and `BackgroundColor` (CSS color override). The component implements `IDockHost` — capture it with `@ref` to call `ShowPanelAsync` / `ResetLayoutAsync` / `Snapshot` and subscribe to `Events` (e.g. in `OnAfterRender`).

Each panel is a regular Razor component (`ComponentBase` subclass). Blazor docking supports in-app floating; popping panels out into separate browser windows is not supported (Blazor runtime instances cannot share component instances across windows).

### Panels the user must not close

```csharp
builder.Services
    .AddShinyDocking()
    // the tree is how you navigate; without it the explorer is a listing of nowhere
    .AddDockPanel<FolderTreePanel>("explorer-tree", displayName: "Folders", icon: "📁", canClose: false)
    .AddDockPanel<FileListPanel>("explorer-files", displayName: "Files", icon: "📄", canClose: false)
    // an output pane is a convenience - closing it costs nothing
    .AddDockPanel<OutputPanel>("output");
```

Prefer this to hiding the close button with CSS: the flag is enforced by the host, so
`HidePanelAsync` and any future close path honour it too.

### Defining an initial layout

```csharp
readonly DockRoot layout = new()
{
    MainWindow = new DockWindowState
    {
        LeftRail = new DockGroup { Tabs = { new DockTab { PanelTypeId = "solution-explorer" } } },
        DocumentArea = new DockSplit
        {
            Orientation = DockOrientation.Vertical,
            Ratio = 0.7,
            First = new DockGroup
            {
                Tabs =
                {
                    new DockTab { PanelTypeId = "editor" },
                    new DockTab { PanelTypeId = "readme" }
                }
            },
            Second = new DockGroup { Tabs = { new DockTab { PanelTypeId = "output" } } }
        }
    }
};
```

## Public Surface

### Schema (POCO tree — round-trips to JSON)

```
DockRoot
├── int SchemaVersion + int MinReadableVersion
├── DockWindowState MainWindow
└── List<DockWindowState> FloatingWindows         (stacking order is session state, not persisted)

DockWindowState
├── DockRect? Bounds                              (screen-coord rectangle — desktop only)
├── bool IsMaximized + bool IsFullScreen
├── DockNode DocumentArea                         (structurally distinct document well)
├── DockNode? LeftRail + TopRail + RightRail + BottomRail
├── double? LeftRailSize + TopRailSize + RightRailSize + BottomRailSize
├── List<DockArea> CollapsedRails                 (legacy whole-rail collapse — converted to per-panel on load)
├── List<DockCollapsedPanel> CollapsedTabs        (per-panel edge-bar collapse: which panel, which edge)
├── string? ActivePanelId                         (for focus restoration on load)
└── string? RestoreGroupId                        (floating windows only: the group it was torn from — docking back returns it there)

DockNode = DockSplit | DockGroup | DockEmpty       (System.Text.Json polymorphic, $kind discriminator)
DockSplit  { Orientation, Ratio (0..1), First, Second }
DockGroup  { GroupId, List<DockTab> Tabs, int ActiveTabIndex, List<int> FocusHistory (MRU), bool IsCollapsed }
DockEmpty  { }

DockTab            { PanelTypeId, PanelInstanceId, bool IsPinned }
DockCollapsedPanel { DockArea Area, DockTab Tab }
```

`DockSerialization.Serialize(DockRoot)` and `DockSerialization.Deserialize(string)` use a source-generated `JsonSerializerContext` (AOT-safe). Schema versioning is built in from day one: bump `SchemaVersion`, write an `IDockLayoutMigrator` for each step, and stored layouts migrate forward as users upgrade.

### Contracts

```csharp
public interface IDockHost
{
    bool IsLocked { get; set; }                    // layout read-only mode
    IDockEvents Events { get; }
    IDockCommandScope CommandScope { get; }

    Task LoadAsync(DockRoot root, CancellationToken ct = default);
    DockRoot Snapshot();

    Task ShowPanelAsync(string panelTypeId, DockArea preferredArea = DockArea.Left, CancellationToken ct = default);
    Task HidePanelAsync(string panelInstanceId, CancellationToken ct = default);
    Task ActivatePanelAsync(string panelInstanceId, CancellationToken ct = default);
    Task ResetLayoutAsync(CancellationToken ct = default);
    Task FloatPanelAsync(string panelInstanceId, CancellationToken ct = default);   // tear a panel off into its own floating window
    Task SetRailCollapsedAsync(DockArea area, bool collapsed, CancellationToken ct = default);
}

public interface IDockableContent
{
    string Title { get; }
    object? Icon { get; }
    bool CanClose { get; }
    bool CanFloat { get; }
    void OnActivated();
    void OnDeactivated();

    // Embedded editors return true to claim pointer-down — prevents
    // the dock system from starting a tab drag during caret edits.
    bool WantsPointerDown(double x, double y);
}

public interface IDockableContentFactory
{
    string PanelTypeId { get; }
    string DisplayName => PanelTypeId;     // tab title (default interface member)
    string? Icon => null;                  // optional emoji / unicode glyph
    Task<View> CreateAsync(string instanceId, CancellationToken ct = default);   // MAUI
    // (Blazor variant returns Task<RenderFragment>)
}

public interface IDockLayoutStore                  // bring-your-own — no default ships
{
    Task<DockRoot?> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(DockRoot root, CancellationToken ct = default);
    int SaveDebounceMs { get; }
}

public interface IDockLayoutMigrator               // forward-only migrations
{
    int FromVersion { get; }
    int ToVersion { get; }
    DockRoot Migrate(DockRoot input);
}

public interface IDockEvents
{
    event EventHandler<LayoutChangedEventArgs>? LayoutChanged;
    event EventHandler<PanelActivatedEventArgs>? PanelActivated;
    event EventHandler<DockDragEventArgs>? DragStarted;
    event EventHandler<DockDragEventArgs>? DragCompleted;
    event EventHandler<DockDragEventArgs>? DragCancelled;
}

public interface IDockCommandScope                 // for routing Ctrl+W / Ctrl+Tab / Ctrl+Alt+PgUp/Dn
{
    bool IsInScope { get; }                        // is keyboard focus inside the dock surface
    string? ActiveGroupId { get; }
    string? ActivePanelInstanceId { get; }
}
```

### Controls

| Type | Purpose |
|---|---|
| `DockHostView` (MAUI) / `<DockHost />` (Blazor) | Root dock surface. Attaches to an existing page; exposes `IDockHost` |
| `DockGroupView` | Tabbed group of panels |
| `DockTabStrip` | Tab strip with overflow (scroll + chevron), drag-to-reorder/tear-off, double-click to float |
| `DockSplitter` | Draggable splitter between two adjacent dock children; reports position as a 0..1 ratio so layouts survive resize |

## Interactions (what ships, end-to-end)

- **Tab drag (Visual Studio model)** — once a tab leaves its strip it becomes a **translucent floating window** that follows the pointer (on Blazor it carries a live snapshot of the panel). A **docking compass** (five guides: centre = tab into the group, left/right/top/bottom = split it) appears over the pane under the pointer, and **four outer guides** sit on the host edges (dock to that side of the whole layout). Only hovering a guide makes it a target — the accent **preview rectangle** shows exactly where the panel will land. Releasing anywhere else **floats** the panel right where it was let go, at the drag window's size. Over a tab strip the drop inserts at the caret. Escape cancels on Blazor (MAUI has no key hook). MAUI's drag window shows the panel title rather than a snapshot, and its resize grips do not change the cursor.
- **Floating windows** — real window chrome: title bar (icon + title), **Dock** and **Close** buttons (close is hidden when any panel in it is `CanClose: false`), accent title bar on the window holding the active panel, tab strip hidden for a single panel, **resize from all four edges and corners**, click to bring to front. **Drag the title bar** and the window itself travels translucent with the same guides — drop on a guide to dock the whole window (edge drops keep its internal splits; centre / tab-strip drops merge its tabs), anywhere else to just move it. Bounds persist in `DockRoot.FloatingWindows`.
- **Double-click** a tab to float it; double-click a floating title bar (or press Dock) to send it **home** — back into the group it was torn from (`RestoreGroupId`), or the left rail if that group is gone. `IDockHost.FloatPanelAsync(instanceId)` floats programmatically.
- **Splitters** — drag to resize; `DockSplit.Ratio` persists and is clamped to 0.08–0.92 so neither side can vanish.
- **Per-panel collapse** — collapse a tab to a slim edge bar (icon + rotated title); click to restore. `SetRailCollapsedAsync(area, collapsed)` collapses/restores a whole rail at once. Collapsed state persists via `CollapsedTabs`.
- **Locked mode** — `IsLocked = true` disables drag, resize, collapse, and close; switching between existing tabs still works.
- **Events** — wire `host.Events.LayoutChanged / PanelActivated / DragStarted / DragCompleted / DragCancelled` for telemetry, autosave hooks, or undo stacks.

## Panel lifecycle

Every tab in the layout has a unique `PanelInstanceId` (GUID). The host creates content once per instance via the factory and keeps the same view/component instance alive when it's moved between groups, rails, or floating windows.

## Disposal contract

When a panel is closed, the host disposes the content if it implements `IDisposable`. Tearing off to a floating window or moving between groups in the same window **does not** dispose — the same view instance is moved. This is intentional and documented because consumers will rely on whichever behavior ships first; retrofitting "recreated" → "preserved" would break every panel implementation.

## Persistence — bring-your-own store

There is no default `IDockLayoutStore` in v1 — wire one up against whatever you already use, then hand it to the host (`LayoutStore` bindable property on `DockHostView`, `LayoutStore` parameter on `<DockHost>`). The host loads from it at startup (falling back to `InitialLayout` when it returns `null`) and auto-saves every layout change, debounced by `SaveDebounceMs`. Two minimal patterns:

```csharp
// File-backed
public sealed class FileDockLayoutStore : IDockLayoutStore
{
    readonly string path;
    public FileDockLayoutStore(string path) => this.path = path;
    public int SaveDebounceMs => 500;

    public async Task<DockRoot?> LoadAsync(CancellationToken ct = default)
        => File.Exists(path) ? DockSerialization.Deserialize(await File.ReadAllTextAsync(path, ct)) : null;

    public Task SaveAsync(DockRoot root, CancellationToken ct = default)
        => File.WriteAllTextAsync(path, DockSerialization.Serialize(root), ct);
}
```

```csharp
// Shiny.Stores-backed (works on every host, syncs across launches)
public sealed class ShinyStoreDockLayoutStore(IKeyValueStore store) : IDockLayoutStore
{
    public int SaveDebounceMs => 500;

    public Task<DockRoot?> LoadAsync(CancellationToken ct = default)
        => Task.FromResult(store.Get<string?>("docking.layout") is { } json
            ? DockSerialization.Deserialize(json) : null);

    public Task SaveAsync(DockRoot root, CancellationToken ct = default)
    {
        store.Set("docking.layout", DockSerialization.Serialize(root));
        return Task.CompletedTask;
    }
}
```

## Theming

Chrome follows the theme tokens (`--shiny-color-primary` is the accent for the active-tab bar, focused-group border, active floating title bar, guides and preview). Blazor overrides: `--shiny-dock-accent`, `--shiny-dock-host-bg`, `--shiny-dock-group-bg`, `--shiny-dock-border`, `--shiny-dock-tabs-bg`, `--shiny-dock-tab-*`, `--shiny-dock-focus-border`, `--shiny-dock-float-header-bg/-fg`, `--shiny-dock-float-active-header-bg/-fg`, `--shiny-dock-float-shadow`, `--shiny-dock-splitter-hover`.

CSS custom properties (Blazor) and `ResourceDictionary` keys (MAUI) mirror each other so the same token list themes both hosts. Tokens are locked before styling lands so you don't end up with consumer-side `!important` hacks. Reduced-motion respect and `prefers-reduced-motion` honoring are first-class — animation duration is a theme token, not a constant.

## When to use this skill

Invoke the docking skill when the user asks for any of:

- A **Visual Studio-style** layout with dockable tool windows, tabbed groups, splitters
- A **dock host** / `DockHostView` / `<DockHost />` attached to a page
- A way to **register panels** by ID so layouts can be saved and reloaded
- Saving and restoring dock layouts to/from JSON, with schema versioning and migrations
- Tab tear-off / merge / reorder / drag-drop between groups
- Auto-hide rails on the left/right/top/bottom edges
- Tear-off floating windows on Windows, macOS AppKit, or Linux GTK4
- Layout locking, kiosk mode, read-only layout
- Workspace perspectives (Debugging / Design / Review preset layouts)
- Blazor docking (in-app floating)
- Avoiding global mouse hooks on macOS for cross-window drag (the architecture uses implicit mouse-down capture instead — no Accessibility entitlement required, App Store compatible)
- Adding a docking control to a `Shiny.Maui.Controls`-based app without subclassing `ContentPage`

## Related
- [Tray Icon](./tray-icon.md) — ships in the same `Shiny.Maui.Controls.Desktop` package
- [FloatingPanel](./floating-panel.md) — a mobile-shaped bottom/top sheet, distinct from the desktop docking auto-hide rails
