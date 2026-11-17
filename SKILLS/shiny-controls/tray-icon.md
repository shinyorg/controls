# Tray Icon (Desktop)

The tray icon API ships in the **`Shiny.Maui.Controls.Desktop`** add-on package — a desktop-only library that also contains the [Docking](./docking.md) host. It's MAUI-only; tray icons are a desktop OS concept and there is no Blazor equivalent.

Supported platforms:

- **Windows** — `Shell_NotifyIcon`
- **macOS AppKit** — `NSStatusBar.SystemStatusBar` (native macOS app, `net11.0-macos`)
- **MacCatalyst** — bridges to AppKit at runtime via the Objective-C runtime
- **Linux** — `libayatana-appindicator3` + GTK 3 (native dependency must be installed: `libayatana-appindicator3-1`, `libgtk-3-0`)

## Setup

```bash
dotnet add package Shiny.Maui.Controls.Desktop
```

In `MauiProgram.cs`:

```csharp
using Shiny;
using Shiny.Maui.Controls.Desktop.TrayIcon;   // ITrayIcon, ITrayIconFactory, TrayMenu, TrayMenuItem, ...

var builder = MauiApp.CreateBuilder();
builder
    .UseMauiApp<App>()
    .UseShinyDesktop();
```

`UseShinyDesktop()` always registers `ITrayIconFactory` as a singleton (no `cfg` option needed) — the correct per-platform factory is picked automatically. On Android, iOS, or any other unsupported runtime, `Create()` throws `PlatformNotSupportedException`, so guard your code with platform checks if the same MAUI app runs on mobile too.

> **Note:** Prior to the Desktop package merger this lived in `Shiny.Maui.Controls.TrayIcon` under the `Shiny.Maui.Controls.TrayIcon` namespace. Update your `using` directive to `Shiny.Maui.Controls.Desktop.TrayIcon`.

> **Never emit `UseTrayIcon()`.** It was removed. `UseShinyDesktop()` (in the `Shiny` namespace) replaces it and includes the tray factory.

## Creating a tray icon

Resolve the factory from DI and create as many icons as you need. Icons are not owned by any MAUI `Window` — they live for the lifetime of your process until you call `Dispose()`.

```csharp
public class MyTrayHost(ITrayIconFactory factory)
{
    ITrayIcon? icon;

    public void Start()
    {
        this.icon = factory.Create();
        this.icon.Tooltip = "My App";
        this.icon.IsTemplateImage = true; // macOS auto-tint
        this.icon.SetIcon(() => FileSystem.OpenAppPackageFileAsync("trayicon.png").Result);
        this.icon.SetMenu(BuildMenu());

        this.icon.PrimaryClick   += (_, _) => ShowMainWindow();
        this.icon.SecondaryClick += (_, _) => { /* opens menu automatically */ };
        this.icon.DoubleClick    += (_, _) => OpenSettings();
    }

    public void Stop() => this.icon?.Dispose();
}
```

The same **PNG** asset works on every platform — on Windows the implementation auto-wraps PNG bytes in an ICO container so you don't need a separate `.ico` file.

## Building menus

`TrayMenu.Build(b => …)` is the idiomatic builder. Menus are an `ObservableCollection` underneath — mutate items at any time and the platform handler rebuilds automatically.

```csharp
var menu = TrayMenu.Build(b => b
    .Item(new TrayMenuItem("Show window", ShowMainWindow)
    {
        Accelerator = "Ctrl+Shift+W",
        Icon = () => FileSystem.OpenAppPackageFileAsync("show.png").Result
    })
    .Item(new TrayMenuItem("New item", NewItem) { Accelerator = "Ctrl+N" })
    .Check("Notifications", true, enabled => SetNotifications(enabled))
    .Separator()
    .Submenu("Status", s => s
        .Item("Available", () => SetStatus(Status.Available))
        .Item("Busy",      () => SetStatus(Status.Busy))
        .Item("Away",      () => SetStatus(Status.Away)))
    .Separator()
    .Item(new TrayMenuItem("Quit", () => Application.Current!.Quit())
    {
        Accelerator = "Ctrl+Q"
    }));

icon.SetMenu(menu);
```

Menu item types:

| Type | Builder method | Notes |
|---|---|---|
| `TrayMenuItem` | `.Item(label, action)` or `.Item(TrayMenuItem)` | Standard clickable item. Supports optional `Icon` (`Func<Stream>`) and `Accelerator` |
| `TrayCheckMenuItem` | `.Check(label, isChecked, toggled)` | Renders a checked state. `toggled(bool)` receives the new value |
| `TraySeparator` | `.Separator()` | Visual separator |
| `TraySubmenu` | `.Submenu(label, builder)` | Nested menu — same builder API |
| `TrayProgressItem` | `.Progress(label, value, caption)` or `.Progress(TrayProgressItem)` | Non-clickable labelled bar. `Value` 0–1 (clamped), `Detail` (null = percentage, `""` = none), `Caption`, `Tone` |

`TrayMenuItem.Pill = new TrayPill("New", TrayTone.Success)` draws a solid pill after the label. Tones: `Neutral`, `Accent`, `Success`, `Warning`, `Danger`. Pills exist on `TrayMenuItem` only — not on check items or submenu headers.

All items expose `IsEnabled`, `IsVisible`, and `Label`. Set them at any time:

```csharp
var pauseItem = new TrayMenuItem("Pause sync", () => Pause());
menu.Items.Add(pauseItem);
// later
pauseItem.Label = "Resume sync";
pauseItem.IsEnabled = !syncing;
```

The handler subscribes to the menu's internal `Changed` event and rebuilds the native menu when any item property changes or the collection is mutated.

### Pills and progress rows (usage meters)

```csharp
var session = new TrayProgressItem("Session usage", 0.18, caption: "Resets in 4 hr 12 min");

icon.SetMenu(TrayMenu.Build(b => b
    .Progress(session)
    .Separator()
    .Item(new TrayMenuItem("Upgrade plan", OpenBilling) { Pill = new TrayPill("Pro", TrayTone.Accent) })));

// any thread, e.g. a Timer callback: repaints just that row, even while the menu is open
session.Value = 0.82;
session.Tone = session.Value >= 0.75 ? TrayTone.Warning : TrayTone.Accent;
session.Caption = "Resets in 2 hr 3 min";
```

- Keep a reference to each `TrayProgressItem` you will update — don't rebuild the menu to move a bar.
- `Value`/`Label`/`Detail`/`Tone`/caption text changes repaint in place; adding or removing the caption (null ⇄ text) and any `Pill` change rebuild the menu.
- Native on macOS AppKit, MacCatalyst (custom `NSView` + attributed-title pill) and Windows (owner-drawn rows). **Linux renders text**: `Upgrade plan  [Pro]` and `Session usage   ▰▰▰▰▰▰▱▱▱▱  62%` plus a greyed caption row, because the shell draws app-indicator menus from DBusMenu labels only.

### Per-menu-item icons

Set `TrayMenuItem.Icon` to a stream factory returning a PNG. Same asset format across every platform. The factory is invoked each time the menu rebuilds, so it must produce a fresh stream on every call.

```csharp
new TrayMenuItem("Refresh", Refresh)
{
    Icon = () => FileSystem.OpenAppPackageFileAsync("refresh.png").Result
}
```

| Platform | Mechanism |
|---|---|
| Windows | `SetMenuItemInfoW` + 32bpp pre-multiplied alpha HBITMAP. Bitmap auto-sized to `SM_CXMENUCHECK` |
| macOS | `NSMenuItem.Image` (sized to 16×16 / template image as appropriate) |
| MacCatalyst | Same as macOS via `objc_msgSend setImage:` + `setSize:` |
| Linux | `gtk_image_menu_item_new_with_label` + `gtk_image_new_from_file` — deprecated in GTK 3.10 but still functional. Hosts that have disabled menu-item icons (notably some GNOME shells) ignore them |

### Hardware keyboard accelerator dispatch

`TrayMenuItem.Accelerator` is parsed by the shared `TrayAccelerator` record and used both as the visible hint *and* the dispatch wiring. Recognised modifier tokens (case-insensitive, `+` separated): `Ctrl`/`Control`, `Alt`/`Option`/`Opt`, `Shift`, `Cmd`/`Command`/`Meta`/`Win`/`Super`. The key token is a single letter, digit, `F1..F24`, or one of: `Esc`, `Enter`/`Return`, `Tab`, `Space`, `Backspace`, `Delete`, `Insert`, `Home`, `End`, `PageUp`, `PageDown`, `Left`, `Up`, `Right`, `Down`.

```csharp
var parsed = TrayAccelerator.Parse("Ctrl+Shift+P");
// parsed.Modifiers => Control | Shift, parsed.Key => "P"
```

| Platform | Mechanism | Scope |
|---|---|---|
| Windows | `RegisterHotKey` on the tray's hidden message-only window, dispatched via `WM_HOTKEY` | Global system hotkey while the process is running |
| macOS (AppKit) | `NSMenuItem.KeyEquivalent` + `KeyEquivalentModifierMask` | App-wide while your app is foreground |
| MacCatalyst | `setKeyEquivalent:` + `setKeyEquivalentModifierMask:` via `objc_msgSend` | App-wide while your app is foreground |
| Linux | `gtk_widget_add_accelerator` on a `GtkAccelGroup` attached to the menu | Best-effort — fires while the indicator menu is open or focused; truly global hotkeys require `libkeybinder` which the library does not wire |

When a hotkey can't be parsed or registered (unknown key name, modifier-only string, collision with another app's global hotkey on Windows), registration silently no-ops and the accelerator stays as a display hint only.

## ITrayIcon API

| Member | Description |
|---|---|
| `SetIcon(Func<Stream>)` | Set the icon from a stream factory. PNG or ICO bytes both work; macOS scales the image to the menu bar's own height, so a full-size app icon is a fine source. Use a factory (not a Stream directly) so the host can re-read for DPI/theme changes |
| `Tooltip` | Hover tooltip (Windows / macOS) or accessible description (Linux) |
| `Title` | Optional text label shown beside or instead of the icon. macOS / Linux only — ignored on Windows |
| `Badge` | Optional string shown as a red pill composited on the icon (Windows) or beside the icon (macOS / Linux). Set to `null` to clear |
| `IsVisible` | Show/hide without disposing — `true` by default |
| `IsTemplateImage` | macOS only: when `true`, the icon is treated as a template image and auto-tints for the light/dark menu bar. Supply a flat black-on-transparent PNG |
| `SetMenu(TrayMenu)` | Assign or replace the context menu |
| `ShowMenu()` | Programmatically open the menu — useful from a `PrimaryClick` handler on Windows where left-click doesn't open the menu by default |
| `ShowNotification(string title, string message)` | Best-effort balloon / toast via the OS subsystem (Windows `Shell_NotifyIcon NIF_INFO`, macOS / Catalyst `NSUserNotificationCenter`, Linux `libnotify`). No-ops on Linux if `libnotify` is missing |
| `StartAnimation(IReadOnlyList<Func<Stream>> frames, TimeSpan interval)` | Cycle `frames` on a shared `System.Threading.Timer`. Calling again replaces the running animation |
| `StopAnimation()` | Stop the active animation and restore the last static icon set via `SetIcon` |
| `IsAnimating` | `true` while an animation started via `StartAnimation` is running |
| `PrimaryClick` | Left-click. On macOS, primary click opens the menu instead — handler still fires for telemetry |
| `SecondaryClick` | Right-click / control-click |
| `DoubleClick` | Windows + macOS only — Linux has no double-click signal |
| `Dispose()` | Removes the tray icon and frees native resources. Always dispose when you're done — orphaned icons can persist in the Windows tray until reboot |

`TrayClickEventArgs` carries `X` / `Y` screen coordinates (best-effort across platforms).

## Badge / overlay numbers

```csharp
icon.Badge = unread.ToString();   // shows "3" overlay on Windows, "3" beside icon on macOS/Linux
icon.Badge = null;                // clear
```

- **Windows:** the current icon is re-rendered with a rounded red pill containing the badge text in the bottom-right corner. Long strings (>3 chars) are truncated to `"xx+"`. Compositing uses `System.Drawing.Common` pulled in only for the Windows TFM.
- **macOS / MacCatalyst:** the badge string is appended to the status button title (combined with `Title` if both are set).
- **Linux:** the badge is set on `app_indicator_set_label` alongside `Title`.

## Balloon / toast notifications

```csharp
icon.ShowNotification("Sync complete", "Uploaded 12 files.");
```

This is a *system-level* notification — Action Center on Windows, Notification Center on macOS, the desktop notifier daemon on Linux. For richer in-app toasts living inside your MAUI window, use `Shiny.Maui.Controls.Toast` instead.

## Animated icon ticking

```csharp
var frames = new Func<Stream>[]
{
    () => FileSystem.OpenAppPackageFileAsync("spin-0.png").Result,
    () => FileSystem.OpenAppPackageFileAsync("spin-1.png").Result,
    () => FileSystem.OpenAppPackageFileAsync("spin-2.png").Result,
    () => FileSystem.OpenAppPackageFileAsync("spin-3.png").Result
};

icon.StartAnimation(frames, TimeSpan.FromMilliseconds(150));
// later
icon.StopAnimation(); // restores the last static icon set via SetIcon
```

The timer is owned by `TrayIconBase` and is disposed automatically on `Dispose()`.

## Platform notes & gotchas

### Linux
- Hard dependency on `libayatana-appindicator3.so.1` and `libgtk-3.so.0`. Install via your distro:
  - Debian/Ubuntu: `apt install libayatana-appindicator3-1 libgtk-3-0`
  - Fedora: `dnf install libayatana-appindicator-gtk3 gtk3`
- GNOME 40+ needs the AppIndicator extension installed by the user; KDE works out of the box.
- The first tray icon initializes GTK (`gtk_init_check`). Subsequent icons reuse the existing GTK state.
- `ShowNotification` lazily initializes `libnotify` on first call. If the library is missing it gracefully no-ops.
- Icon PNG is written to a temp file (the app-indicator API takes a path, not a stream). Menu item icons follow the same pattern — each gets its own temp file that's cleaned on menu rebuild and dispose.

### MacCatalyst
- Catalyst is UIKit and has no `NSStatusItem`. The implementation `dlopen`s AppKit at runtime and goes through `objc_msgSend`.
- Menu callbacks ride a runtime-allocated `NSObject` subclass (`ShinyTrayCB`) with `[UnmanagedCallersOnly]` trampolines — fully AOT-compatible.
- Hardened sandboxes that disallow loading AppKit will reject the `dlopen`. Normal Catalyst apps work fine.
- `ShowNotification` uses `NSUserNotificationCenter` — modern apps should consider migrating to `UNUserNotificationCenter` but the older API still works.

### macOS AppKit
- Uses native MAUI bindings on `net11.0-macos`. Cleanest of the four implementations.
- Click events route via `NSStatusItem.Button.Activated`; left vs right is distinguished by inspecting `NSApplication.SharedApplication.CurrentEvent`.
- On macOS, **primary (left) click opens the menu** when one is assigned. To get a left-click event handler that doesn't open the menu, don't call `SetMenu` and use `ShowMenu()` from within your handler instead.
- `KeyEquivalent` + modifier mask on `NSMenuItem` is dispatched natively by AppKit when your app is in the foreground.

### Windows
- Uses Win32 `Shell_NotifyIcon` with a hidden message-only window for `WM_TRAYICON` callbacks.
- Right-click opens the menu via `TrackPopupMenuEx`.
- The implementation auto-wraps PNG bytes in a Vista-style ICO container — pass the same PNG you use everywhere else.
- **Windows 11** hides new tray icons in the overflow flyout by default. Users have to drag yours into the always-visible area. Document this for your users.
- Badge compositing requires `System.Drawing.Common` (referenced only on the Windows TFM via `PackageReference`).
- Global accelerator dispatch uses `RegisterHotKey` against the host message window — these are *process-global* and survive any window losing focus.
- Always call `Dispose()` on app shutdown or the icon may persist in the tray until reboot.

## When to use this skill

Invoke the tray icon skill when the user asks for:

- A system tray icon, status bar icon, menu bar icon
- A desktop app that runs in the background with a tray icon
- A tray icon with a right-click context menu, submenus, checkmark items, **per-item icons**, or **keyboard accelerators that actually fire**
- A **badge** / unread count / overlay number on the tray icon
- Click handlers (left, right, double) on a tray icon
- Showing tooltips on a tray icon
- A **balloon / toast** raised from the tray icon (or distinguishing it from `Shiny.Maui.Controls.Toast`)
- An **animated** / ticking tray icon (sync spinner, recording indicator)
- A "menu bar app" (macOS) — note this also requires `LSUIElement` in `Info.plist`
- Tray icons that auto-adapt to macOS dark/light menu bar (template images)
- Linux app indicators / KDE/GNOME tray icons
- Show/hide / disable / enable a tray icon at runtime
- Updating tray menu items dynamically (e.g. "Pause" ↔ "Resume", check states)
