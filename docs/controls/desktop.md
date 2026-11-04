# Desktop (Tray Icon + Docking + Desktop Quick Entry) & the On-Screen Keyboard

[← All Shiny Controls](../../README.md)

`Shiny.Maui.Controls.Desktop` is a single desktop-only add-on that combines a cross-platform **system tray / status-bar icon** (Windows, macOS AppKit, MacCatalyst, Linux ayatana-appindicator), Visual-Studio-style **window docking** (dockable tool windows, tabbed groups, splitters, auto-hide rails, tear-off floating windows), and the **desktop presentation of Quick Entry** — the borderless always-on-top prompt window that opens over any application from a global hotkey, in the style of Claude Desktop's quick entry or the Copilot key. Quick Entry itself ships in the core packages. A touch / kiosk **on-screen keyboard** is planned for it but not built. On the Blazor side there is no equivalent add-on — docking *and* the on-screen keyboard both ship in the main `Shiny.Blazor.Controls` package.

```bash
dotnet add package Shiny.Maui.Controls.Desktop
```

Register in `MauiProgram.cs` with one call, `UseShinyDesktop()`:

```csharp
using Shiny;

builder
    .UseMauiApp<App>()
    .UseShinyControls()
    .UseShinyDesktop(cfg => cfg
        .AddDockPanel<SolutionExplorerPanel>("solution-explorer", displayName: "Explorer", icon: "📁")
        .AddDockPanel<OutputPanel>("output")
        .UseQuickEntryWindow()                              // opt-in: native-window quick entry
        .UseFileDrop(o => o.AllowedExtensions.Add(".pdf"))  // opt-in: the window accepts dropped files
    );
```

`UseShinyDesktop()` always registers the parts that do nothing until you use them: the tray icon factory (`ITrayIconFactory`), `IGlobalHotKeyService`, the in-app [keyboard shortcut](keyboard-shortcuts.md) key sources for macOS, Mac Catalyst and Linux, the docking registry, and the notch window service (`INotchWindowService`, macOS AppKit, Windows and Linux only). Two parts change how the app behaves just by being registered, so they are opt-in:

- `cfg.UseQuickEntryWindow()` presents Quick Entry in a native always-on-top window with the whole-screen glow, and claims `QuickEntryOptions.HotKey` at startup.
- `cfg.UseFileDrop(...)` / `cfg.UseFileDrop<TDelegate>(...)` makes the window accept dropped files. See [File Drop](file-drop.md).

`cfg.ConfigureNotch(o => …)` sets the notch window options. It exists only in the macOS AppKit, Windows and Linux builds, so it is the one call that needs `#if !ANDROID && !IOS && !MACCATALYST`. Everything else compiles on every target, so shared code needs no `#if`. `UseShinyDesktop()` is safe to call more than once.

> The six earlier extension methods (`UseTrayIcon()`, `UseDesktopQuickEntry()`, `UseDesktopNotch()`, `UseDesktopKeyboardShortcuts()`, `UseShinyDocking()` with `builder.AddDockPanel<T>()`, and `UseFileDrop()`) have been removed. Each one was easy to forget, and forgetting one failed silently. Use `UseShinyDesktop()` instead.

> Namespaces: `using Shiny.Maui.Controls.Desktop.TrayIcon;` for the tray API, `using Shiny.Maui.Controls.Desktop.Docking;` for docking, and `using Shiny.Maui.Controls.Desktop.QuickEntry;` for global hotkeys (the popup's own API is in `Shiny.Maui.Controls.QuickEntry`). `UseShinyDesktop` itself lives in the `Shiny` namespace. There is no `UseOnScreenKeyboard` — see below.

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

### Pills and progress rows

A menu can also carry **pills** (a short coloured tag after an item's label, such as "New" or "Pro") and **progress rows** (a labelled bar for usage against a limit, a sync or a download). This is the usage-meter style of menu you see in apps like Claude Desktop.

```csharp
var session = new TrayProgressItem("Session usage", 0.18, caption: "Resets in 4 hr 12 min");
var sync    = new TrayProgressItem("Syncing library") { Tone = TrayTone.Success, Detail = "0 of 40" };

icon.SetMenu(TrayMenu.Build(b => b
    .Progress(session)
    .Progress(sync)
    .Separator()
    .Item(new TrayMenuItem("Upgrade plan", OpenBilling) { Pill = new TrayPill("Pro", TrayTone.Accent) })
    .Item(new TrayMenuItem("What's new", OpenChangelog) { Pill = new TrayPill("New", TrayTone.Success) })));

// later, from any thread (a timer is fine): the row repaints in place, even while the menu is open
session.Value = 0.82;
session.Tone = TrayTone.Warning;
session.Caption = "Resets in 2 hr 3 min";
```

| Member | Description |
|---|---|
| `TrayMenuItem.Pill` | `TrayPill(Text, Tone)` drawn after the label. A solid pill with white text, so it stays readable on light, dark and highlighted rows. Changing it rebuilds the menu |
| `TrayProgressItem.Label` | Text on the left of the label row |
| `TrayProgressItem.Value` | Fraction complete, `0`–`1`. Values outside that range are clamped, and `NaN` counts as `0` |
| `TrayProgressItem.Detail` | Text on the right of the label row. `null` (the default) shows the percentage, e.g. `62%`; `""` shows nothing |
| `TrayProgressItem.Caption` | Optional smaller line under the bar |
| `TrayProgressItem.Tone` | Bar colour: `Neutral`, `Accent` (default), `Success`, `Warning`, `Danger` |
| `Progress(label, value, caption)` / `Progress(item)` | Builder methods on both `TrayMenuBuilder` and `TraySubmenuBuilder`. Keep the item's reference if you will update it |

Progress rows are informational: they never highlight and can't be clicked. Changing `Value`, `Label`, `Detail`, `Tone` or the caption's text repaints only that row, without rebuilding the menu, so a timer can drive it and an open menu keeps moving. Adding or removing the caption changes the row's height, so that one does rebuild the menu. Pills are supported on `TrayMenuItem` only (not on check items or submenu headers).

| Platform | Pills | Progress rows |
|---|---|---|
| macOS (AppKit) | Image attachment in the item's attributed title; the label still turns to the highlight colour when selected | A custom `NSView` drawn with CoreGraphics; semantic colours follow light/dark mode |
| MacCatalyst | Same, via `objc_msgSend` | Same, through an `NSView` subclass registered at runtime |
| Windows | Owner-drawn row (`MF_OWNERDRAW`) that keeps the visual-style background and hover highlight, the menu font, icon and accelerator | Owner-drawn. An open menu is repainted with `InvalidateRect` |
| Linux | Text: `Upgrade plan  [Pro]` | Text: `Session usage   ▰▰▰▰▰▰▱▱▱▱  62%`, with the caption as a greyed row beneath it. The desktop shell draws app-indicator menus from DBusMenu and keeps only labels, so nothing richer can get through |

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
    .UseShinyDesktop(cfg => cfg.UseQuickEntryWindow());
```

`cfg.UseQuickEntryWindow()` adds two things on top of what `UseShinyDesktop()` always registers:

- a **native-window presentation** — borderless, always-on-top, opening over *other applications*. The core service picks it whenever `QuickEntryOptions.Presentation` allows, which `Auto` (the default) does on Windows, macOS AppKit and Linux
- the **screen glow across the whole display** rather than just your page

It also claims `QuickEntryOptions.HotKey` at startup. **`IGlobalHotKeyService`** (system-wide shortcuts, useful on its own) is registered by every `UseShinyDesktop()` call, with or without the quick entry window.

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

> **In-app shortcuts are a different thing.** Global hotkeys fire while your app is in the background. To react to keys while your own window has focus (`Primary+S`, `?`, `Ctrl+K, Ctrl+C`), declare [Keyboard Shortcuts](keyboard-shortcuts.md) on the page instead. `UseShinyDesktop()` supplies their key sources on AppKit, GTK4 and Mac Catalyst; Windows needs nothing beyond core.

> **Wayland caveats.** A Wayland client cannot position its own toplevel or raise itself above other windows, so under Wayland the desktop popup is undecorated but the compositor decides where it appears and it is an ordinary window in the stack; the whole-display glow is unavailable and the in-app one is used instead. Under X11 everything behaves as it does on Windows and macOS.

## Notch window

Puts a [`NotchView`](notch.md) at the **top-centre of the screen**, in a borderless window above every other app, and morphs the window to fit whatever content you give it. Works on **macOS (AppKit), Windows and Linux (GTK4)**.

It lives on the **primary display** (the one chosen as main in Displays settings), moving when displays are plugged in or removed. On a **MacBook** whose built-in display is primary, it lives in the **camera notch**, above the menu bar. The empty notch is exactly the size of the hardware cut-out (read from `NSScreen.AuxiliaryTopLeftArea`/`AuxiliaryTopRightArea`), so it disappears into it. Give it content and it grows down out of the notch, with the content padded below the camera housing. **Everywhere else** — a Mac display without a notch (or `UseHardwareNotch = false`), every Windows and Linux screen — it floats as an island centred at the top of the screen, `TopMargin` below the menu bar, a top-docked taskbar or a top panel. Set `TopMargin = 0` and `Shape = NotchShape.Notch` for a notch that hangs from the top edge instead — on a Mac add `CoverMenuBar = true` so that edge is the screen's own top, over the menu bar, rather than the bottom of the menu bar.

```csharp
#if !ANDROID && !IOS && !MACCATALYST
builder
    .UseShinyControls()
    .UseShinyDesktop(cfg => cfg.ConfigureNotch(o =>
    {
        o.MaxWidth = 520;
        o.Configure = notch => notch.SurfaceColor = Colors.Black;
    }));
#endif
```

```csharp
#if !ANDROID && !IOS && !MACCATALYST
// INotchWindowService, injected
await notch.ShowAsync(new Label { Text = "Recording 0:42", TextColor = Colors.White });
await notch.SetContentAsync(new NowPlayingView());   // grows to fit
await notch.SetContentAsync(null);                   // back to the empty notch
notch.Hide();

// a "live activity" beside the camera rather than below it (macOS with a notch)
var cutout = notch.HardwareNotchSize;               // Size.Zero without a notch, always on Windows/Linux
await notch.SetContentAsync(new PeekView(cutout.Width), n =>
{
    n.ContentPadding = new Thickness(10, 0);
    n.MinNotchHeight = cutout.Height;
});

// expand on hover
notch.PointerEntered += (_, _) => ShowPanel();
notch.PointerExited += (_, _) => ShowPeek();
#endif
```

`SetContentAsync`/`ShowAsync` take an optional `Action<NotchView>` that styles the notch for that content only, after `NotchWindowOptions.Configure`. `PointerEntered`/`PointerExited` (and `IsPointerOver`) report the mouse over the notch window whether or not your app is active, with no accessibility permission on any platform.

| `NotchWindowOptions` | Default | |
|---|---|---|
| `UseHardwareNotch` | true | Hug the camera notch when the screen has one (macOS) |
| `TopMargin` | 8 | Gap between the top of the usable screen area and the floating island |
| `CoverMenuBar` | false | macOS without a notch: measure `TopMargin` from the screen's top edge, so the island sits over the menu bar |
| `MaxWidth` / `MaxHeight` | 640 / 480 | Caps, in points |
| `MorphDuration` | 300 | Window reshape time, ms. `0` snaps |
| `JoinAllSpaces` | true | Every Space and over full-screen apps (macOS); every workspace (Linux X11); ignored on Windows |
| `Configure` | null | Styles each `NotchView` the service creates; runs last, so it wins |

**How it behaves per platform**

| | macOS | Windows | Linux X11 | Linux Wayland |
|---|---|---|---|---|
| Position | Camera notch, or under the menu bar | Top-centre of the primary monitor's work area | Top-centre of the primary monitor's work area | Wherever the compositor puts it |
| Above other apps | Yes (status level) | Yes (topmost) | Yes (`_NET_WM_STATE_ABOVE`) | No, ordinary window |
| In taskbar / app switcher | No | No (tool window) | No (skip taskbar + pager) | Compositor's choice |
| Morph | Native window-frame animation | Window resized every frame | Window resized every frame | Window resized every frame |
| Outline | Transparent window, antialiased | Window region (aliased edges) | Transparent window (needs a compositor) | Transparent window |
| Takes focus | Never | Never (`WS_EX_NOACTIVATE`) | Not presented, so not on show | Not presented, so not on show |

- **macOS, Windows and Linux only.** `INotchWindowService`, `NotchWindowOptions` and `cfg.ConfigureNotch` exist in the `net10.0-macos`, Windows and `net10.0` (Linux GTK4) builds and not on Android, iOS or Mac Catalyst, so guard them with `#if !ANDROID && !IOS && !MACCATALYST`. `UseShinyDesktop()` itself needs no guard. It registers the notch service wherever it exists. There, use an in-app `NotchView`.
- **Shape on Windows.** A WinUI window can't be see-through, so the window is clipped to the notch outline with a window region, re-cut on every frame, and the page behind the notch paints the surface colour. Region edges are not antialiased, and a `Stroke` is clipped away with the corners.
- **Wayland.** A client can't place or raise its own toplevel, so the notch still morphs and reports the pointer but isn't pinned to the top. Doing better needs `wlr-layer-shell`, which GNOME doesn't implement and this package doesn't depend on — the same limit as the quick entry popup.
- **Windows title bar.** MAUI reserves a title-bar strip across the top of every WinUI window's content, taller than the empty notch. The notch window collapses it through MAUI's internal `WindowRootView.UpdateAppTitleBar` (an `UnsafeAccessor`, so AOT-safe). If a future MAUI renames it, the content just sits lower until this is updated.
- The **window** does the morph and the surface fills it. On macOS that is a native frame animation, because the AppKit head runs no layout when a realized view changes size. Windows and GTK have no animated resize, so the window is stepped every 16ms with the notch's own ease.
- `SetContentAsync` builds a fresh page for the new content, because AppKit never creates a native view for a child added to an already-realized layout. The other hosts do the same so they all behave alike.
- The window never takes keyboard focus, so it suits status, media controls, timers and progress, not typing.

## Docking

Visual-Studio-style docking host for MAUI desktop apps — schema, contracts, the in-window `DockHostView`, drag-drop, splitters, auto-hide rails, and tear-off floating windows.

```csharp
using Shiny;
using Sample.Features.Docking;  // SolutionExplorerPanel, OutputPanel

builder
    .UseMauiApp<App>()
    .UseShinyDesktop(cfg => cfg
        .AddDockPanel<SolutionExplorerPanel>("solution-explorer", displayName: "Explorer", icon: "📁")
        .AddDockPanel<OutputPanel>("output"));
```

`AddDockPanel` is on the `UseShinyDesktop` configuration, not on `MauiAppBuilder`. It takes optional `displayName` (tab title, defaults to the panel ID), `icon` (emoji / unicode glyph) and `canClose` arguments. A panel view can also implement `IDockableContent` to control its own per-instance `Title`, `Icon`, `CanClose` / `CanFloat`, and receive `OnActivated` / `OnDeactivated` callbacks.

Pass `canClose: false` for a panel the surface cannot function without — a file explorer's folder tree, an editor's document area. Closing one of those leaves a layout with nothing on screen that would bring it back, unless the app has built its own reopen affordance. The flag hides the tab's close button **and** makes `HidePanelAsync` refuse the panel, so it is a rule rather than a missing button:

```csharp
builder.UseShinyDesktop(cfg => cfg
    .AddDockPanel<FolderTreePanel>("explorer-tree", displayName: "Folders", icon: "📁", canClose: false)
    .AddDockPanel<OutputPanel>("output"));   // closable, as most panels should be
```

Blazor's `services.AddShinyDocking().AddDockPanel<T>(…)` takes the same `canClose` argument.

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
| `IDockableContentFactory` | `Task<View> CreateAsync(string instanceId, ...)` + `DisplayName` / `Icon` / `CanClose` — registered via `cfg.AddDockPanel<T>` |
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

The component itself implements `IDockHost` — grab it with `@ref` to call `ShowPanelAsync` / `ResetLayoutAsync` / `Snapshot` and subscribe to `Events`. CSS custom properties (e.g. `--shiny-dock-host-bg`) provide theming hooks without recompiling. The left and right rails are each capped at 30% of the host's width, so on a phone two side rails never overrun the document area or lie on top of each other's tabs and resize strips.

### Foldables

`DockSplitter.FoldBehavior` (default `SnapToFold`): with a fold posture source registered, a splitter whose split runs the same way as the fold (side by side for a book, stacked for a tabletop) lines up with the fold. It is display-only: `Ratio` and the saved layout keep the user's ratio, and dragging pauses while snapped. Locked splitters do not snap. See [Foldables](foldables.md).

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
