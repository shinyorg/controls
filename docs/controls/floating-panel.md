# FloatingPanel + OverlayHost

[← All Shiny Controls](../../README.md)

A floating panel overlay system for MAUI. Panels slide in from the bottom or top of the screen with configurable snap positions (detents), optional header peek when closed, backdrop dimming, and feedback. Multiple panels can coexist on the same page without blocking touches on content underneath.

**OverlayHost** is a transparent Grid layer that manages backdrop and touch passthrough for overlay clients (`FloatingPanel`, `Overlay`, `LoadingOverlay`). **ShinyContentPage** is a convenience ContentPage with a built-in OverlayHost.

> **Blazor equivalent — `SheetView`.** Blazor sheets lay their content out inside the band the detent actually puts on screen, so a footer, an action row or a `ChatView` input bar stays reachable at every detent instead of being pushed below the fold. Give sheet content `height: 100%` to fill that band; anything taller scrolls inside the sheet. A full-bleed `HeaderTemplate` is clipped to the sheet's rounded corners.

| Closed | Open | Header (Closed) | Header (Open) | Top (Closed) | Top (Open) |
|:---:|:---:|:---:|:---:|:---:|:---:|
| ![Closed](../../assets/sheet1.png) | ![Open](../../assets/sheet2.png) | ![Header Closed](../../assets/sheet3.png) | ![Header Open](../../assets/sheet4.png) | ![Top Closed](../../assets/sheet5.png) | ![Top Open](../../assets/sheet6.png) |

```xml
<!-- Using ShinyContentPage (recommended) -->
<shiny:ShinyContentPage xmlns:shiny="http://shiny.net/maui/controls">
    <shiny:ShinyContentPage.PageContent>
        <!-- Your page content here -->
    </shiny:ShinyContentPage.PageContent>
    <shiny:ShinyContentPage.Panels>
        <shiny:FloatingPanel
            IsOpen="{Binding IsSheetOpen}"
            Position="Bottom"
            HasBackdrop="True"
            CloseOnBackdropTap="True"
            PanelCornerRadius="16">
            <shiny:FloatingPanel.Detents>
                <shiny:DetentValue Value="Quarter" />
                <shiny:DetentValue Value="Half" />
                <shiny:DetentValue Value="Full" />
            </shiny:FloatingPanel.Detents>
            <!-- Your panel content here -->
        </shiny:FloatingPanel>
    </shiny:ShinyContentPage.Panels>
</shiny:ShinyContentPage>
```

**FloatingPanel Properties:**

| Property | Type | Description |
|---|---|---|
| IsOpen | bool | Show/hide the panel (TwoWay) |
| Position | FloatingPanelPosition | `Bottom`, `BottomTabs`, or `Top` — which edge the panel slides from. Use `BottomTabs` when inside a Shell TabBar to clip above the tab bar |
| Detents | ObservableCollection\<DetentValue\> | Snap positions (Quarter, Half, Full) |
| PanelContent | View | Content displayed in the panel (`[ContentProperty]`) |
| HeaderTemplate | View | Optional header view at the screen edge; shown as a peek bar when closed |
| ShowHeaderWhenClosed | bool | When true, the header peeks from the edge when the panel is closed |
| HasBackdrop | bool | Fade backdrop behind panel |
| CloseOnBackdropTap | bool | Close when backdrop tapped |
| PanelCornerRadius | double | Corner radius |
| HandleColor | Color | Drag handle color |
| ShowHandle | bool | Show/hide the drag handle bar |
| PanelBackgroundColor | Color | Panel background color |
| AnimationDuration | double | Animation speed (ms) |
| ExpandOnInputFocus | bool | Auto-expand when input focused |
| IsLocked | bool | Prevents drag dismiss; code-only control |
| FitContent | bool | Auto-computes detent from content size |
| IsContentScrollEnabled | bool | Wraps content in a ScrollView (default true). Set **false** when content scrolls itself (a `TableView`/`CollectionView`) — nested scroll-views collapse the inner one to near-zero height. Works with `ShowHeaderWhenClosed`: closed, only the header is on screen either way |
| UseFeedback | bool | Feedback on open, close, and detent snap (default: true) |

**Touches when closed:** a closed panel is either gone (`IsVisible=false`) or only its peeking header — the body is hidden whichever `IsContentScrollEnabled` is, so a `ScrollView`/`CollectionView` on the page underneath scrolls normally. The shared backdrop stops taking touches the moment it starts fading out, not when the fade ends, and flipping `IsOpen` mid-animation reverses the panel rather than being ignored.

**OverlayHost Properties:**

| Property | Type | Description |
|---|---|---|
| BackdropColor | Color | Backdrop color (default: Black) |
| BackdropMaxOpacity | double | Maximum backdrop opacity (default: 0.5) |

**ShinyContentPage Properties:**

| Property | Type | Description |
|---|---|---|
| PageContent | View | Main page content |
| Panels | IList\<IView\> | Collection of FloatingPanel, Overlay, and LoadingOverlay instances |
| BackdropColor | Color | Forwarded to internal OverlayHost |
| BackdropMaxOpacity | double | Forwarded to internal OverlayHost |

Every `ShinyContentPage` also has a **built-in `LoadingOverlay`** — no need to add one to `Panels`. Just bind `IsLoading`; it's brought to the front when shown and never dismisses on a backdrop tap. Customize it with the `Loading*` passthroughs (including a `LoadingContentTemplate` to fully replace the spinner content):

```xml
<shiny:ShinyContentPage IsLoading="{Binding IsBusy}"
                        LoadingMessage="Working on it…"
                        LoadingBlurRadius="8">
    ...
    <!-- optional: fully custom loading content -->
    <shiny:ShinyContentPage.LoadingContentTemplate>
        <DataTemplate>
            <Label Text="Please wait…" TextColor="White" />
        </DataTemplate>
    </shiny:ShinyContentPage.LoadingContentTemplate>
</shiny:ShinyContentPage>
```

| Built-in loading property | Type | Description |
|---|---|---|
| IsLoading | bool | Show/hide the built-in loading overlay (TwoWay) |
| LoadingMessage | string? | Message under the spinner/progress bar |
| LoadingIsIndeterminate | bool | Spinner (true, default) vs determinate progress bar |
| LoadingProgress | double | Progress 0–100 when determinate (TwoWay) |
| LoadingSpinnerColor | Color? | Accent color override |
| LoadingBlurRadius | double | Frosted-glass blur for the loading backdrop |
| LoadingContentTemplate | DataTemplate? | Replaces the default spinner/progress content |
| LoadingOverlay | LoadingOverlay | The underlying overlay instance for advanced use |

`Overlay` also gained `CloseOnBackdropTap` (default `true`) — set `false` to keep an overlay up until dismissed in code.
