# NotchView

[← All Shiny Controls](../../README.md)

A Dynamic Island / camera-notch style surface that holds **anything** and morphs its width, height and corner radius to fit it. Available on both MAUI and Blazor, in the core packages.

There are no compact/expanded states to manage. Put a view inside it and the notch sizes itself to that view. Swap the content, or let the content change its own size (text grows, a row appears), and the notch springs to the new size. During the morph the content stays laid out at its final size and the surface clips it, so text never re-wraps frame by frame. The content is pinned to the top of the surface, which grows and shrinks from its top edge, so anything along the top of the content (a tab strip, a header) stays still while the bottom is revealed or clipped. Content with a `WidthRequest` or `MaximumWidthRequest` is measured at that width, so text inside it wraps to the same number of lines it is laid out with and the notch is tall enough for all of them.

```xml
<Grid>
    <ScrollView>
        <!-- page content -->
    </ScrollView>

    <!-- Same Grid cell: the notch floats over the top of the page, top-centre by default -->
    <shiny:NotchView x:Name="Notch" Margin="0,12,0,0" Tapped="OnNotchTapped">
        <HorizontalStackLayout Spacing="8">
            <Ellipse Fill="Red" WidthRequest="10" HeightRequest="10" VerticalOptions="Center" />
            <Label Text="0:42" TextColor="White" FontAttributes="Bold" />
        </HorizontalStackLayout>
    </shiny:NotchView>
</Grid>
```

```csharp
// swap to bigger content - the notch grows to fit, the new content fades in
Notch.Content = new NowPlayingView();
Notch.Content = null; // back to the minimum size
```

```razor
<!-- Overlays the top-centre of the screen by default, flush with the top edge -->
<NotchView ContentKey="@state" OnClick="Expand" OnNotchSizeChanged="s => size = s">
    @if (state == "recording")
    {
        <span>● 0:42</span>
    }
    else
    {
        <NowPlaying />
    }
</NotchView>
```

| Property | Type (MAUI / Blazor) | Default | Description |
|---|---|---|---|
| Content / ChildContent | View / RenderFragment | null | Anything. The notch sizes itself to it |
| ContentKey | — / object | null | Blazor only. Change it when you swap content and the new content fades in (MAUI fades on any `Content` swap) |
| Inline | — / bool | false | Blazor only. `false` overlays the top of the screen in the browser's top layer; `true` lays it out in the page flow |
| Shape | NotchShape | Island (MAUI) / Notch (Blazor) | `Island` rounds every corner; `Notch` squares the top corners so it hangs from the top edge |
| SurfaceColor | Color / string | Black | The fill. Black on every theme (it imitates a hole in the screen), so give the content light text. Blazor: `--shiny-notch-surface` |
| Stroke / StrokeThickness | Color? / string, double | none | Optional outline |
| ContentPadding | Thickness / string | 14,8 / "8px 14px" | Space between the edge and the content |
| MinNotchWidth / MinNotchHeight | double | 120 / 36 | The smallest size, and the size with no content |
| MaxNotchWidth / MaxNotchHeight | double / double? | unbounded | Caps. The parent's width, less the notch's own `Margin`, also caps the notch; wider content wraps, taller content is clipped |
| CornerRadius | double | -1 | `-1` is automatic: a full pill while short, capped at 28 as it grows tall. Always clamped to half the height |
| MorphDuration | uint / int | 420 | Milliseconds per resize. `0` snaps |
| MorphEasing | Easing? / — | soft spring | MAUI only. Blazor uses a matching `cubic-bezier` |
| HasShadow | bool | true | Soft drop shadow |
| Command / CommandParameter | ICommand / — | null | MAUI: run on a tap of the surface |
| NotchSize / Size | Size / NotchSize | — | Read-only: the size it is at, or morphing towards |

| Event | MAUI | Blazor |
|---|---|---|
| Surface tapped | `Tapped` | `OnClick` (clicks inside the content bubble here too; use `@onclick:stopPropagation` on inner buttons) |
| Morph started, with the target size | `NotchSizeChanged` | `OnNotchSizeChanged` |
| Morph finished | `MorphCompleted` | — |

MAUI also has `Refresh()`, for a size change that does not invalidate measure (a font loaded late).

**Placement.** On Blazor the notch overlays the **very top of the screen** wherever the component sits in the tree: it opens as a manual popover, so it lives in the browser's top layer, flush with the top edge of the viewport and centred, above the page, its scroll, every `z-index` and every parent's `overflow`/`transform`. Its width cap is the viewport. An `Island` floats 8px below the top edge (plus `env(safe-area-inset-top)`); `--shiny-notch-offset` moves either shape down. `Inline="true"` puts it back in the page flow, centred in its parent and capped by the parent's width, for a notch inside a card or a device mock-up. Browsers without popover support fall back to `position: fixed` with a very high `z-index`.

On MAUI it positions like any other view: `HorizontalOptions="Center"` and `VerticalOptions="Start"` by default. It sizes to its content and never scrolls, so put it next to the page's scroller (same Grid cell), not inside a fixed-height box.

**Accessibility.** Blazor turns the morph and the fade off under `prefers-reduced-motion`.

## Over every app (desktop window)

`Shiny.Maui.Controls.Desktop` can put the same `NotchView` in a borderless window at the **top-centre of the screen, above every other app**, on **macOS, Windows and Linux**. On a MacBook it goes into the **hardware camera notch**: the empty notch is exactly the size of the cut-out, so it disappears into it, and content grows down out of it. Everywhere else (a Mac display without a notch, Windows, Linux) it floats as an island at the top of the screen, like the [quick entry](quick-entry.md) popup. See [Desktop → Notch window](desktop.md#notch-window).

```csharp
#if !ANDROID && !IOS && !MACCATALYST
builder.UseShinyDesktop(cfg => cfg.ConfigureNotch(o => o.MaxWidth = 520));

await notchWindow.ShowAsync(new Label { Text = "Recording 0:42", TextColor = Colors.White });
await notchWindow.SetContentAsync(new NowPlayingView());
#endif
```

**Platform notes**

- macOS (AppKit), Windows and Linux (GTK4) only. `UseShinyDesktop()` registers `INotchWindowService` wherever it exists, with no `#if`. The service, `NotchWindowOptions` and `cfg.ConfigureNotch` don't exist on Android, iOS or Mac Catalyst, so guard the code that uses them with `#if !ANDROID && !IOS && !MACCATALYST`.
- Only macOS has a camera notch to find. Windows and Linux always get the floating island.
- On the AppKit head the window, not the surface, animates the resize (a native frame animation). Windows and GTK have no animated window resize, so the window is resized once per frame instead.
- Every `SetContentAsync` animates as an open or close: the new content fades in while the window morphs to its size. On macOS the old content goes with the notch too, frozen into an image that is clipped as the window collapses (or rides along as it grows) and fades out, so closing to a smaller content (or none) collapses into it rather than leaving an empty black shape to shrink. Windows and Linux get the fade-in. The morph starts once the new content has been laid out, so heavy content doesn't eat into it. `MorphDuration = 0` swaps instantly.
- On Windows the window is clipped to the notch outline with a window region, because a WinUI window can't be transparent. The edges are not antialiased and a `Stroke` is clipped off.
- Under a Linux Wayland session the compositor decides where the window goes and it is not kept above other windows. X11 behaves like Windows and macOS.
- The window never takes keyboard focus. It is for glancing and clicking, not typing.

<!-- TODO: capture screenshots for notch -->

## Automation ids (MAUI)

Set `AutomationId` on the notch and its surface is named after it, for UI tests:

| Part | Id |
|---|---|
| Surface (the morphing card, tap target) | `<id>_Surface` |
