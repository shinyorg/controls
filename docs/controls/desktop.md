# Desktop (Tray Icon + Docking + Desktop Quick Entry) & the On-Screen Keyboard

[← All Shiny Controls](../../README.md)

`Shiny.Maui.Controls.Desktop` is a single desktop-only add-on that combines a cross-platform **system tray / status-bar icon** (Windows, macOS AppKit, MacCatalyst, Linux ayatana-appindicator), Visual-Studio-style **window docking** (dockable tool windows, tabbed groups, splitters, auto-hide rails, tear-off floating windows), and the **desktop presentation of Quick Entry** — the borderless always-on-top prompt window that opens over any application from a global hotkey, in the style of Claude Desktop's quick entry or the Copilot key. Quick Entry itself ships in the core packages. A touch / kiosk **on-screen keyboard** is planned for it but not built. On the Blazor side there is no equivalent add-on — docking *and* the on-screen keyboard both ship in the main `Shiny.Blazor.Controls` package.

```bash
dotnet add package Shiny.Maui.Controls.Desktop
```

Register in `MauiProgram.cs` — call one or both extensions depending on what you need:

```csharp
using Shiny;

builder
    .UseMauiApp<App>()
    .UseShinyControls()
    .UseTrayIcon()         // tray / status-bar icon
    .UseShinyDocking()     // docking host
    .AddDockPanel<SolutionExplorerPanel>("solution-explorer", displayName: "Explorer", icon: "📁")
    .AddDockPanel<OutputPanel>("output")
    .UseDesktopQuickEntry();   // native-window quick entry + global hotkeys
```

> Namespaces: `using Shiny.Maui.Controls.Desktop.TrayIcon;` for the tray API, `using Shiny.Maui.Controls.Desktop.Docking;` for docking, and `using Shiny.Maui.Controls.Desktop.QuickEntry;` for global hotkeys (the popup's own API is in `Shiny.Maui.Controls.QuickEntry`). The extension methods themselves live in the `Shiny` namespace. There is no `UseOnScreenKeyboard` — see below.

## Tray Icon

Resolve `ITrayIconFactory` from DI to create as many tray icons as you need. Build menus declaratively, set the icon from any `Stream`, and dispose to remove the icon cleanly. The same PNG asset works on every platform — Windows wraps it as an ICO internally.

```csharp
public class MyTrayHost
{
    readonly ITrayIcon icon;

    public MyTrayHost(ITrayIconFactory factory)
    {
        this.icon = factory.Create();
        this.icon.Tooltip = "My App";
        this.icon.IsTemplateImage = true; // macOS dark/light auto-tint
        this.icon.SetIcon(() => FileSystem.OpenAppPackageFileAsync("trayicon.png").Result);

        this.icon.SetMenu(TrayMenu.Build(b => b
            .Item(new TrayMenuItem("Show window", ShowMainWindow) { Accelerator = "Ctrl+Shift+W", Icon = OpenIconStream })
            .Item(new TrayMenuItem("New item", NewItem) { Accelerator = "Ctrl+N" })
            .Check("Notifications", true, on => SetNotifications(on))
            .Separator()
            .Submenu("Status", s => s
                .Item("Available", () => SetStatus(Status.Available))
                .Item("Busy", () => SetStatus(Status.Busy))
                .Item("Away", () => SetStatus(Status.Away)))
            .Separator()
            .Item(new TrayMenuItem("Quit", () => Application.Current!.Quit()) { Accelerator = "Ctrl+Q" })));

        this.icon.PrimaryClick += (_, e) => ShowMainWindow();
        this.icon.DoubleClick  += (_, e) => OpenSettings();

        // Badge, balloon/toast, animated icon — see the API table below
        this.icon.Badge = "3";
        this.icon.ShowNotification("Connected", "Background sync is running.");
    }
}
```

| Member | Description |
|---|---|
| `SetIcon(Func<Stream>)` | Set the icon from a stream factory — the host re-reads it for DPI/theme changes. PNG or ICO bytes both work |
| `Tooltip` | Hover tooltip (Windows / macOS) or accessible description (Linux) |
| `Title` | Optional text label shown beside or instead of the icon on macOS and Linux (ignored on Windows) |
| `Badge` | String composited onto the icon as a red pill on Windows; rendered beside the icon on macOS / Linux. Set to `null` to clear |
| `IsVisible` | Show/hide without disposing |
| `IsTemplateImage` | When `true`, macOS treats the icon as a template image and auto-tints for the light/dark menu bar |
| `SetMenu(TrayMenu)` | Assign the context menu — mutate items at any time and the menu rebuilds |
| `ShowMenu()` | Programmatically open the menu (useful from a left-click handler on Windows) |
| `ShowNotification(title, message)` | Best-effort balloon / toast via the native subsystem (Windows `NIF_INFO`, macOS / Catalyst `NSUserNotificationCenter`, Linux libnotify). For richer in-app toasts inside your MAUI UI use `Shiny.Maui.Controls.Toast` |
| `StartAnimation(frames, interval)` / `StopAnimation()` / `IsAnimating` | Cycle a list of `Func<Stream>` frames on a shared timer; reverts to the last static icon on stop |
| `PrimaryClick` / `SecondaryClick` / `DoubleClick` | Click events with screen coordinates (`TrayClickEventArgs`) |
| `Dispose()` | Removes the tray icon and frees native resources |

`TrayMenu.Build(b => …)` supports `Item`, `Check`, `Separator`, and `Submenu`. `TrayMenuItem` exposes `IsEnabled`, `IsVisible`, `Label`, optional `Icon` (`Func<Stream>` — rendered next to the label), and `Accelerator` (e.g. `"Ctrl+S"`, `"Cmd+Q"`, `"F1"`). The accelerator string is both the visual hint *and* the dispatch trigger — see the table below for per-platform behaviour. Use the shared `TrayAccelerator.Parse(string)` helper if you need the parsed `Modifiers` + `Key` yourself.

**Platform notes:**
- **Linux:** depends on `libayatana-appindicator3` and `libgtk-3` — install via your distro's package manager (`apt install libayatana-appindicator3-1 libgtk-3-0` on Debian/Ubuntu). `ShowNotification` additionally needs `libnotify` (usually pre-installed); if missing it silently no-ops
- **MacCatalyst:** bridges to AppKit via the Objective-C runtime — your app needs permission to `dlopen` AppKit at runtime (granted by default in normal Catalyst apps)
- **Windows:** uses `Shell_NotifyIcon` directly. Windows 11 hides new tray icons by default — users have to promote yours from the overflow flyout. Badge composition uses `System.Drawing.Common` (pulled in only for the Windows TFM)
- **macOS template images:** set `IsTemplateImage = true` and supply a flat black-on-transparent PNG for the menu bar to auto-tint with the user's appearance

**Accelerator dispatch matrix:**

| Platform | Mechanism | Scope |
|---|---|---|
| Windows | `RegisterHotKey` on the tray host window | Global system hotkey while your process is running |
| macOS (AppKit) | `NSMenuItem.KeyEquivalent` + modifier mask | App-wide while your app is foreground |
| MacCatalyst | Same as AppKit via `objc_msgSend` | App-wide while your app is foreground |
| Linux | `gtk_widget_add_accelerator` on a `GtkAccelGroup` | Best-effort — fires while the indicator menu is open or focused |

## Quick Entry (desktop window + global hotkeys)

The quick entry popup itself lives in the core package — see [Quick Entry](quick-entry.md) — and already works on every platform as an in-app overlay. What this add-on adds is the *desktop* half:

```csharp
using Shiny;
using Shiny.Maui.Controls.QuickEntry;

builder
    .UseShinyControls(cfg => cfg.ConfigureQuickEntry(o =>
    {
        o.HotKey = OperatingSystem.IsMacOS() ? "Cmd+Opt+Space" : "Ctrl+Alt+Space";
        o.ScreenGlow = ScreenGlowTrigger.WhileBusy;
    }))
    .UseDesktopQuickEntry();
```

`UseDesktopQuickEntry()` registers three things:

- a **native-window presentation** — borderless, always-on-top, opening over *other applications*. The core service picks it whenever `QuickEntryOptions.Presentation` allows, which `Auto` (the default) does on Windows, macOS AppKit and Linux
- the **screen glow across the whole display** rather than just your page
- **`IGlobalHotKeyService`** — system-wide shortcuts, useful on its own

It is safe to call unconditionally: on MacCatalyst (and anywhere else that isn't a desktop) the presenters report themselves unsupported and the core service quietly stays with the overlay.

##### Global hotkeys

```csharp
var registration = hotKeys.Register("Ctrl+Shift+K", () => DoSomething());
// null means the combination could not be claimed — that is a normal outcome, not an exception
```

| Platform | Mechanism | Notes |
|---|---|---|
| Windows | `RegisterHotKey` on a message-only window | Reliable; fails if another process already owns the combination |
| macOS (AppKit) | Carbon `RegisterEventHotKey` | No Accessibility permission prompt, unlike an `NSEvent` global monitor |
| Linux / X11 | `XGrabKey` on the root window | Full support, including window placement and always-on-top |
| Linux / Wayland | `org.freedesktop.portal.GlobalShortcuts` | GNOME 45+ / KDE Plasma 6+. Binding shows the user a confirmation prompt, so the hotkey starts working asynchronously, and the compositor may bind a different trigger than the one you asked for |
| MacCatalyst | — | Not supported |

> **Wayland caveats.** A Wayland client cannot position its own toplevel or raise itself above other windows, so under Wayland the desktop popup is undecorated but the compositor decides where it appears and it is an ordinary window in the stack; the whole-display glow is unavailable and the in-app one is used instead. Under X11 everything behaves as it does on Windows and macOS.

## Docking

Visual-Studio-style docking host for MAUI desktop apps — schema, contracts, the in-window `DockHostView`, drag-drop, splitters, auto-hide rails, and tear-off floating windows.

```csharp
using Shiny;
using Sample.Features.Docking;  // SolutionExplorerPanel, OutputPanel

builder
    .UseMauiApp<App>()
    .UseShinyDocking()
    .AddDockPanel<SolutionExplorerPanel>("solution-explorer", displayName: "Explorer", icon: "📁")
    .AddDockPanel<OutputPanel>("output");
```

`AddDockPanel` takes optional `displayName` (tab title, defaults to the panel ID), `icon` (emoji / unicode glyph) and `canClose` arguments. A panel view can also implement `IDockableContent` to control its own per-instance `Title`, `Icon`, `CanClose` / `CanFloat`, and receive `OnActivated` / `OnDeactivated` callbacks.

Pass `canClose: false` for a panel the surface cannot function without — a file explorer's folder tree, an editor's document area. Closing one of those leaves a layout with nothing on screen that would bring it back, unless the app has built its own reopen affordance. The flag hides the tab's close button **and** makes `HidePanelAsync` refuse the panel, so it is a rule rather than a missing button:

```csharp
services
    .AddShinyDocking()
    .AddDockPanel<FolderTreePanel>("explorer-tree", displayName: "Folders", icon: "📁", canClose: false)
    .AddDockPanel<OutputPanel>("output");   // closable, as most panels should be
```

`DockHostView` attaches to any existing `ContentPage` — it does not subclass `ContentPage`, so your Shell / page architecture stays unchanged:

```xml
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:docking="clr-namespace:Shiny.Maui.Controls.Desktop.Docking;assembly=Shiny.Maui.Controls.Desktop">
    <docking:DockHostView InitialLayout="{Binding StartupLayout}"
                          LayoutStore="{Binding LayoutStore}"
                          IsLocked="{Binding IsLayoutLocked}" />
</ContentPage>
```

| Building block | Purpose |
|---|---|
| `DockHostView` | Root dock surface (attaches inside any page); bindable `InitialLayout`, `LayoutStore`, `IsLocked` |
| `DockGroupView` | Tabbed group of panels |
| `DockTabStrip` | Tab strip with overflow + drag-to-reorder |
| `DockSplitter` | Draggable splitter between adjacent dock children |
| `IDockHost` | Per-window controller: `LoadAsync`, `Snapshot`, `ShowPanelAsync` / `HidePanelAsync` / `ActivatePanelAsync`, `ResetLayoutAsync`, `FloatPanelAsync`, `SetRailCollapsedAsync`, `IsLocked` |
| `IDockableContent` | Optional interface on panel views — per-instance title/icon, close/float gating, activation callbacks, pointer-down claim for embedded editors |
| `IDockableContentFactory` | `Task<View> CreateAsync(string instanceId, ...)` + `DisplayName` / `Icon` / `CanClose` — registered via `AddDockPanel<T>` |
| `IDockLayoutStore` | Bring-your-own persistence contract — load/save the layout tree as JSON; saves are debounced via `SaveDebounceMs` |
| `IDockEvents` | `LayoutChanged`, `PanelActivated`, `DragStarted/Completed/Cancelled` |
| `IDockCommandScope` | Scopes Ctrl+W / Ctrl+Tab / Ctrl+Alt+PgUp/Dn to the dock surface |

Everything is interactive end-to-end, and drags work the way Visual Studio's do. Pull a tab out of its strip and it becomes a translucent floating window that follows the pointer; a docking compass appears over the pane underneath (centre = add as a tab, edges = split) and four guides sit on the host's outer edges (dock to that side of the whole layout). Hovering a guide shows a preview of exactly where the panel will land; letting go anywhere else leaves it floating right there. Floating windows have real chrome — title bar, Dock and Close buttons, resize from every edge and corner — and dragging a window by its title bar shows the same guides, so you can drop the whole window back into the layout. Double-click a tab to float it, double-click a floating title bar to send it home to the group it came from (`FloatPanelAsync` does the former from code). Drag splitters to resize; collapse individual panels (or whole rails via `SetRailCollapsedAsync`) to slim edge bars that restore on click. The full state — splits, ratios, collapsed panels, floating-window bounds — round-trips through `Snapshot()` / `LoadAsync()` and auto-saves through the attached `IDockLayoutStore`. `IsLocked = true` freezes the layout (tab switching still works) for kiosk / demo scenarios.

The layout schema (`DockRoot`, `DockWindowState`, `DockSplit`, `DockGroup`, `DockTab`) is a pure POCO tree with a source-generated `System.Text.Json` context — round-trip your dock layout to disk with `DockSerialization.Serialize` / `Deserialize`. Schema versioning (`SchemaVersion` + `MinReadableVersion`) and an `IDockLayoutMigrator` hook are wired in from day one so saved layouts survive future schema changes.

##### Blazor

Same shape, same contracts — different host. No extra package: docking is part of `Shiny.Blazor.Controls`.

```csharp
using Shiny.Blazor.Controls.Docking;

builder.Services
    .AddShinyDocking()
    .AddDockPanel<SolutionExplorerPanel>("solution-explorer", displayName: "Explorer", icon: "📁")
    .AddDockPanel<OutputPanel>("output");
```

```razor
@using Shiny.Blazor.Controls.Docking

<DockHost @ref="host"
          InitialLayout="@layout"
          LayoutStore="@layoutStore"
          IsLocked="@locked" />
```

The component itself implements `IDockHost` — grab it with `@ref` to call `ShowPanelAsync` / `ResetLayoutAsync` / `Snapshot` and subscribe to `Events`. CSS custom properties (e.g. `--shiny-dock-host-bg`) provide theming hooks without recompiling.

## On-Screen Keyboard

> [!IMPORTANT]
> **Blazor only.** The keyboard ships in `Shiny.Blazor.Controls`. The MAUI half —
> `UseOnScreenKeyboard` / `IOnScreenKeyboard` / `OnScreenKeyboardView` in
> `Shiny.Maui.Controls.Desktop` — is still a design and will not compile.

Touch / kiosk soft keyboard. US-QWERTY with a symbols layer, bottom-docked, auto-shows when an `<input>` / `<textarea>` gains focus, and — critically — does **not** take the caret off it when keys are tapped.

```csharp
using Shiny.Blazor.Controls.OnScreenKeyboard;

builder.Services.AddShinyOnScreenKeyboard(opts =>
{
    opts.AutoShowOnFocus = true;
    opts.AutoHideOnBlur  = true;
    opts.HeightPx        = 280;
    opts.PushContent     = true;     // pad the body out from under the keys (false = overlay)
    opts.Theme           = OnScreenKeyboardTheme.Auto;   // follows the app's theme tokens
});
```

```razor
@using Shiny.Blazor.Controls.OnScreenKeyboard

@* Place once in MainLayout.razor — the host watches focus for the whole document *@
<OnScreenKeyboardHost />
```

Drive visibility from code via DI:

```csharp
@inject IOnScreenKeyboardService Keyboard

<button @onclick="() => Keyboard.Show()">Kiosk mode</button>
```

`IOnScreenKeyboardService` is `Show` / `Hide` / `Toggle` / `IsVisible` / `VisibilityChanged`. Both it and `OnScreenKeyboardOptions` are registered **scoped** — the options object is live, so change it at runtime and the host picks it up on the next render, and being per-scope means one user's settings are not everyone's under Blazor Server. `AddShinyControls()` covers this too; `ConfigureKeyboard` is the umbrella's equivalent of the `opts` delegate above.

`⇧` is momentary, `⇪` is sticky and only raises the letters (the number row keeps its digits), and holding a character, `⌫`, space or an arrow auto-repeats. Arrows are caret-aware: `▲` / `▼` walk to the same column on the adjacent line of a `<textarea>`. Enter dispatches real key events and submits the form on a single-line input; set `EnterInsertsNewLine` to type a newline in a `<textarea>` instead. Theming is entirely `--shiny-osk-*` custom properties.

Limitations: DOM inputs only — no injection into another window, process or cross-origin frame. No Shadow DOM (`focusin` does not pierce shadow roots). No IME / dead-key composition, English US-QWERTY only. No Ctrl / Alt chords — and no inert keys on the board pretending otherwise. Keys are `tabindex="-1"` by design, since taking focus is the one thing the control exists to avoid; the ARIA tree is there so the board is describable, not tab-navigable.
