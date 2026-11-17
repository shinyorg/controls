# File Drop

Window-level file drop: files dragged from Finder / Explorer / Files onto the **application window**, anywhere in it — including on top of a `BlazorWebView` or any other hosted web content.

| Host | Package | Platforms | Payload |
| --- | --- | --- | --- |
| MAUI | `Shiny.Maui.Controls.Desktop` | Windows (WinUI), macOS AppKit (`net11.0-macos`), Mac Catalyst, Linux (GTK4) | Real file paths |
| Blazor | `Shiny.Blazor.Controls` (core) | WebAssembly, Server, Hybrid | Bytes only — the browser gives no path |

On iOS, Android, and anywhere else the MAUI package's `net11.0` asset lands, `IFileDropService` still resolves, `IsSupported` is `false`, `AttachTo` is a no-op and no event ever fires. **Never wrap consuming code in `#if`** — that is exactly the case this design removes.

## Not `DropGestureRecognizer`

Do not suggest MAUI's `DropGestureRecognizer` for this job. It is per-view, is unimplemented on the AppKit and GTK4 heads, is [broken on Mac Catalyst](https://github.com/dotnet/maui/issues/23627), and — the reason this service exists — sits **behind** hosted web content, so an app whose UI is a `BlazorWebView` never sees the drop at all. Use `DropGestureRecognizer` only when the target is genuinely one view in a MAUI-native page on iOS / Android / Windows.

## MAUI setup

```csharp
using Shiny;

builder
    .UseMauiApp<App>()
    .UseShinyControls()
    .UseShinyDesktop(cfg => cfg.UseFileDrop(o =>
    {
        o.AllowedExtensions.Add(".pdf");   // empty (default) = accept everything
        o.MaxFileSize = 50 * 1024 * 1024;  // 0 = no limit
        o.MaxFiles = 10;                   // 0 = no limit
        o.AllowDirectories = false;
        o.SuppressWebViewDrop = true;      // the switch that makes this work over a web view
        o.AutoAttachWindows = true;
    }));
```

Namespace for the API: `using Shiny.Maui.Controls.Desktop.FileDrop;`. `UseShinyDesktop` is in `Shiny`. File drop is **opt-in** on the configuration (`cfg.UseFileDrop(...)` / `cfg.UseFileDrop<TDelegate>(...)`) because registering it makes the window accept drops. **Never emit `builder.UseFileDrop(...)`**: that extension was removed.

Windows are attached automatically as they open. With `AutoAttachWindows = false`, call `IFileDropService.AttachTo(window)` and dispose the result to detach.

## Blazor setup

```csharp
services.AddShinyFileDrop(o => o.AllowedExtensions.Add(".png"));
```

`AddShinyControls()` already registers it; `cfg.ConfigureFileDrop(o => …)` is the umbrella's equivalent of the delegate above. Then place **one** host in the root layout:

```razor
@using Shiny.Blazor.Controls.FileDrop

<FileDropHost />
```

`<FileDropHost />` exists because the service imports a JS module, which prerendering cannot do — it calls `StartAsync()` after the first render. An app that would rather do that itself can skip the component and call `StartAsync()` from its own `OnAfterRenderAsync(firstRender: true)`.

The listeners go on `window` in the **capture** phase, so a drop is caught wherever it lands and before any component can consume it, and the browser's default action for a dropped file — navigating away to it, which unloads the app — never happens.

## Consuming

```csharp
public MyViewModel(IFileDropService drop)
{
    drop.DragEnter += (_, e) => this.IsDragging = e.HasAcceptableFiles;
    drop.DragOver  += (_, e) => this.Position = $"{e.Position.X:0},{e.Position.Y:0}";  // MAUI: Position; Blazor: e.X / e.Y
    drop.DragLeave += (_, e) => this.IsDragging = false;
    drop.Dropped   += (_, e) =>
    {
        this.IsDragging = false;
        foreach (var file in e.Files)
            this.Import(file);
    };
}
```

`IFileDropService`: `IsSupported`, `IsEnabled` (toggles reporting without detaching), `Options`, the four events, and `AttachTo(Window)` (MAUI) / `StartAsync` + `StopAsync` + `ReleaseAsync` (Blazor).

`DroppedFile`:

- **MAUI** — `FileName`, `FullPath` (set on every desktop platform), `Length`, `Extension`, `ContentType`, `IsDirectory`, `OpenReadAsync()`, `ReadAllBytesAsync()`.
- **Blazor** — `FileName`, `Length`, `ContentType`, `LastModified`, `Extension`, `IsMetadataKnown`, `OpenReadAsync(maxAllowedSize = 32MB)`, `ReadAllBytesAsync(...)`. There is **no** `FullPath`; do not generate code that reads one.

## App-wide handling: `IFileDropDelegate`

Use the events when the handling belongs to a page or component. Use a delegate when it belongs to the *app* — an import that must work whatever is on screen, with constructor-injected services.

```csharp
builder.UseShinyDesktop(cfg => cfg.UseFileDrop<ImportFileDropDelegate>());  // MAUI  — singleton
services.AddShinyFileDrop<ImportFileDropDelegate>();  // Blazor — scoped

public class ImportFileDropDelegate(IImportService imports) : IFileDropDelegate
{
    public async Task OnFilesDropped(FileDropContext context)
    {
        await imports.QueueAsync(context.Files);
        context.Handled = true;   // suppresses the Dropped event for this drop
    }
}
```

The delegate runs **before** `Dropped` and can consume the drop. A delegate that throws is logged and swallowed — native drop handlers do not catch managed exceptions.

## Rules that trip people up

- **A wholly refused drop raises `DragLeave`, not `Dropped`.** No platform sends a leave after a drop, so an overlay bound to the drag state would otherwise stay up for good. `RejectedCount` on those args says how many were filtered out.
- **A drag in progress knows less than the drop does.** Browsers hide names and sizes until the drop lands (`IsMetadataKnown` is false; only `ContentType` is set), and Mac Catalyst has only a suggested name. Filters therefore accept an unnameable hover and re-check on drop. Bind an overlay to `Files.Count` / `HasAcceptableFiles`, not to a file name.
- **`SuppressWebViewDrop` is the switch that makes this work over web content** and the first one to turn off if hosted web content misbehaves. It sets `AllowDrop = false` plus revokes the OLE registration on WebView2, unregisters `WKWebView`'s dragged types on AppKit, strips the drop interactions inside `WKWebView` on Catalyst, and puts the GTK drop target in the capture phase.
- **Blazor releases a drop's files once the handler returns.** They sit in JS memory until then. Set `ReleaseFilesAfterHandling = false` to read one later, and call `ReleaseAsync(files)` when done.
- **Do not draw the drop affordance for the user.** The service reports the drag; the overlay is the app's. Make it `InputTransparent` (MAUI) / `pointer-events: none` (Blazor) — the window-level target does the catching, and an overlay that swallows the pointer only gets in the way.
- **On Mac Catalyst a drop is staged into the temp directory** before `FullPath` is set, because UIKit hands over `NSItemProvider`s rather than paths. Code written against `OpenReadAsync()` works on every platform; code written against `FullPath` is desktop-only, which is usually fine.

## Platform notes

- **Windows** — XAML drag/drop on the window's root element, not OLE `RegisterDragDrop` on the HWND. Compile-verified only; not yet exercised on a Windows machine.
- **macOS AppKit** — the drop view becomes the window's `contentView` and MAUI's content becomes its subview, because AppKit finds a drop's destination by hit-testing and then walking **up** the superview chain. A transparent overlay on top cannot work: one that returns `nil` from `hitTest:` is never found, and one that does not swallows every click.
- **Mac Catalyst** — a `UIDropInteraction` on the `UIWindow`. The weakest of the four: UIKit gives the drop to the deepest view that wants it, and there is no supported opt-out inside `WKWebView` the way WebView2 has one.
- **Linux/GTK4** — a `GtkDropTarget` on the toplevel in the capture phase, with preloading on so the file list is readable during `motion` rather than only on `drop`. Compile-verified only; GTK cannot be exercised on macOS.
