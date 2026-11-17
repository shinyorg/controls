# File Drop

[← All Shiny Controls](../../README.md)

Files dragged from Finder / Explorer / Files onto the **application window** — anywhere in it, including on top of a `BlazorWebView` or any other hosted web content.

This is deliberately not `DropGestureRecognizer`. That one is per-view, is unimplemented on the AppKit and GTK4 heads and [broken on Mac Catalyst](https://github.com/dotnet/maui/issues/23627), and — the reason this exists — it sits *behind* hosted web content, so an app whose UI is a `BlazorWebView` never sees the drop at all. `IFileDropService` attaches to the **native window** and, by default, stops the web view claiming the drag first, so a drop anywhere in the window reaches your code.

| Host | Platforms | Payload |
| --- | --- | --- |
| MAUI (`Shiny.Maui.Controls.Desktop`) | Windows (WinUI), macOS (AppKit + Mac Catalyst), Linux (GTK4) | Real file paths |
| Blazor (`Shiny.Blazor.Controls`) | WebAssembly, Server, Hybrid | Bytes — the browser gives no path |

On iOS, Android, and anywhere else the `net11.0` asset lands, the service still resolves, `IsSupported` is `false` and nothing fires — so shared code needs no `#if`.

## MAUI

```csharp
using Shiny;

builder
    .UseMauiApp<App>()
    .UseShinyControls()
    .UseShinyDesktop(cfg => cfg.UseFileDrop(o =>
    {
        o.AllowedExtensions.Add(".pdf");   // empty = accept everything
        o.MaxFileSize = 50 * 1024 * 1024;
        o.MaxFiles = 10;
    }));
```

File drop is opt-in on `UseShinyDesktop()` because a registered service starts accepting drops on the window straight away. The old `builder.UseFileDrop()` extension has been removed.

```csharp
using Shiny.Maui.Controls.Desktop.FileDrop;

public MyViewModel(IFileDropService drop)
{
    drop.DragEnter += (_, e) => this.IsDragging = e.HasAcceptableFiles;
    drop.DragLeave += (_, e) => this.IsDragging = false;
    drop.Dropped   += (_, e) =>
    {
        this.IsDragging = false;
        foreach (var file in e.Files)
            this.Import(file.FullPath!);       // or await file.OpenReadAsync()
    };
}
```

Windows are attached automatically as they open. Turn that off with `AutoAttachWindows = false` and call `AttachTo(window)` yourself.

## Blazor

`AddShinyControls()` covers the registration; `ConfigureFileDrop` is the umbrella's equivalent of the options delegate. Place one `<FileDropHost />` in your root layout — it starts the listeners at the one moment JS interop is legal, after the first render.

```csharp
builder.Services.AddShinyFileDrop(o => o.AllowedExtensions.Add(".png"));
```

```razor
@using Shiny.Blazor.Controls.FileDrop
@inject IFileDropService FileDrop

@* once, in MainLayout.razor *@
<FileDropHost />
```

```csharp
this.FileDrop.Dropped += async (_, e) =>
{
    foreach (var file in e.Files)
    {
        await using var stream = await file.OpenReadAsync();
        await this.Upload(file.FileName, stream);
    }
};
```

The listeners go on `window` in the **capture** phase — the browser equivalent of "over top of any web view". A drop is caught wherever it lands, before any component can consume it, and the browser never gets to do its default thing with a dropped file, which is to navigate away to it and unload the app.

## App-wide handling: `IFileDropDelegate`

The events belong to a page or component. When a drop should be handled the same way whatever is on screen — with constructor-injected services rather than whatever the current page captured — register a delegate instead. It runs first, and can consume the drop.

```csharp
builder.UseShinyDesktop(cfg => cfg.UseFileDrop<ImportFileDropDelegate>());  // MAUI
services.AddShinyFileDrop<ImportFileDropDelegate>(); // Blazor

public class ImportFileDropDelegate(IImportService imports) : IFileDropDelegate
{
    public async Task OnFilesDropped(FileDropContext context)
    {
        await imports.QueueAsync(context.Files);
        context.Handled = true;   // suppresses the Dropped event for this drop
    }
}
```

## Things worth knowing

- **A wholly refused drop raises `DragLeave`, not `Dropped`.** No platform sends a "leave" after a drop, so an overlay bound to the drag state would otherwise stay up for good. `RejectedCount` tells you how many were filtered out.
- **A drag in progress reveals less than the drop does.** Browsers hide file names and sizes until the drop lands, and Mac Catalyst has only a suggested name, so `DragEnter` / `DragOver` may report placeholder entries. The count is always real; on MAUI desktop everything else is too.
- **`SuppressWebViewDrop` is what makes this work over web content**, and it is the first switch to turn off if hosted web content starts behaving oddly. It sets `AllowDrop = false` and revokes the OLE registration on WebView2, unregisters `WKWebView`'s dragged types on AppKit, strips the drop interactions inside `WKWebView` on Catalyst, and puts the GTK drop target in the capture phase.
- **Blazor releases a drop's files once your handler returns.** They live in JS memory until then. Set `ReleaseFilesAfterHandling = false` if you need to read one later, and call `ReleaseAsync` yourself when you are done.
