# FloatingPanel + OverlayHost (MAUI)

A floating panel overlay system for .NET MAUI. Panels slide in from the bottom or top edge of the page with configurable detents, header peek when closed, backdrop dimming, drag-handle gestures, keyboard handling, and feedback. Multiple panels can coexist on the same page without blocking touches on content underneath.

**Architecture:**
- **OverlayHost**: A transparent `Grid` layer with `InputTransparent=true, CascadeInputTransparent=false` — touches pass through to content underneath, but panels and backdrop still receive input on their visible areas. Manages a shared backdrop for all overlay clients (`FloatingPanel`, `Overlay`, `LoadingOverlay`).
- **FloatingPanel**: A `ContentView` that lives inside an `OverlayHost`. Animates height (not translation) so the panel only occupies the space it needs. Pan gesture is restricted to the drag handle only — no scroll conflicts with content.
- **Overlay / LoadingOverlay**: Full-screen overlay controls that also live inside an `OverlayHost`. See the overlay skill for details.
- **ShinyContentPage**: A `ContentPage` with a built-in `OverlayHost`. Set page content via `PageContent` and add panels/overlays via `Panels`.

> **Blazor note:** Blazor does not use FloatingPanel/OverlayHost. It retains `SheetView` which uses CSS `position: fixed`, `z-index`, and `pointer-events: none` for overlay behavior natively.

### Blazor `SheetView` layout

The sheet container is always viewport-tall and is translated down to the detent, so on its own the part below the detent line would be laid out off-screen. `sheet.js` publishes the on-screen height as `--sheet-visible-height` on every transform (drag included) and `.shiny-sheet-viewport` sizes itself off it, which keeps header, handle and content inside the band the user can actually see and touch. Consequences when writing sheet content:

- Content that should fill the sheet takes `height: 100%` — it resolves against the visible band, not the viewport.
- Content taller than the band scrolls inside `.shiny-sheet-scroll` instead of disappearing below the fold. A trailing action row or a `ChatView` input bar therefore stays reachable at a Quarter or Half detent.
- The container clips to its corner radius, so a full-bleed `HeaderTemplate` keeps the sheet's rounded top.
- Don't give sheet content a fixed pixel height chosen for one detent; it will overflow the smaller ones.

## Basic Usage with ShinyContentPage

```xml
<shiny:ShinyContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                         xmlns:shiny="http://shiny.net/maui/controls"
                         x:Class="MyApp.MainPage">
    <shiny:ShinyContentPage.PageContent>
        <ScrollView>
            <VerticalStackLayout Padding="20" Spacing="10">
                <Button Text="Open Panel" Command="{Binding OpenCommand}" />
            </VerticalStackLayout>
        </ScrollView>
    </shiny:ShinyContentPage.PageContent>
    <shiny:ShinyContentPage.Panels>
        <shiny:FloatingPanel IsOpen="{Binding IsOpen, Mode=TwoWay}">
            <VerticalStackLayout Padding="20" Spacing="10">
                <Label Text="Panel Content" FontSize="18" FontAttributes="Bold" />
                <Button Text="Close" Command="{Binding CloseCommand}" />
            </VerticalStackLayout>
        </shiny:FloatingPanel>
    </shiny:ShinyContentPage.Panels>
</shiny:ShinyContentPage>
```

## Basic Usage with OverlayHost (manual)

```xml
<ContentPage>
    <Grid>
        <ScrollView>
            <!-- page content -->
        </ScrollView>

        <shiny:OverlayHost>
            <shiny:FloatingPanel IsOpen="{Binding IsOpen, Mode=TwoWay}">
                <!-- panel content -->
            </shiny:FloatingPanel>
        </shiny:OverlayHost>
    </Grid>
</ContentPage>
```

## FloatingPanel Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `IsOpen` | `bool` | `false` | Opens/closes the panel (two-way bindable) |
| `Position` | `FloatingPanelPosition` | `Bottom` | Where the panel slides from (`Bottom`, `BottomTabs`, or `Top`). Use `BottomTabs` when inside a Shell TabBar to clip the panel above the tab bar |
| `PanelContent` | `View?` | `null` | The content displayed inside the panel (`[ContentProperty]`) |
| `HeaderTemplate` | `View?` | `null` | Optional header view at the screen edge; shown as a peek bar when closed |
| `ShowHeaderWhenClosed` | `bool` | `false` | When true, the header peeks from the edge when the panel is closed |
| `Detents` | `ObservableCollection<DetentValue>` | Quarter, Half, Full | Snap points as ratios of available height |
| `PanelBackgroundColor` | `Color` | `White` | Background color of the panel |
| `HandleColor` | `Color` | `Grey` | Color of the drag handle indicator |
| `ShowHandle` | `bool` | `true` | Show/hide the drag handle bar |
| `PanelCornerRadius` | `double` | `16` | Corner radius of the panel |
| `HasBackdrop` | `bool` | `true` | Shows a dimming backdrop behind the panel |
| `CloseOnBackdropTap` | `bool` | `true` | Tapping the backdrop closes the panel |
| `AnimationDuration` | `double` | `250` | Animation duration in milliseconds |
| `ExpandOnInputFocus` | `bool` | `true` | Auto-expands to highest detent when an input is focused |
| `IsLocked` | `bool` | `false` | Prevents all user dismissal (drag, header tap close, backdrop tap); panel can only be closed via code. Header tap still opens the panel |
| `FitContent` | `bool` | `false` | Measures content and auto-computes a single detent to fit it (ignores Detents when true) |
| `IsContentScrollEnabled` | `bool` | `true` | Wraps the content in a `ScrollView`. Set **`False`** when the content already scrolls itself (a `TableView`, `CollectionView`, etc.) — nesting scroll-views collapses the inner one to near-zero height, so its rows render blank. Safe with `ShowHeaderWhenClosed` |
| `UseFeedback` | `bool` | `true` | Feedback on open, close, and detent snap |

## OverlayHost Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `BackdropColor` | `Color` | `Black` | Backdrop color |
| `BackdropMaxOpacity` | `double` | `0.5` | Maximum backdrop opacity |

The backdrop is shared across all clients (`FloatingPanel`, `Overlay`, `LoadingOverlay`). It stays visible as long as any client is active, and tapping it dismisses all active overlay clients.

## ShinyContentPage Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `PageContent` | `View?` | `null` | Main page content (`[ContentProperty]`) |
| `Panels` | `IList<IView>` | — | Collection of `FloatingPanel`, `Overlay`, and `LoadingOverlay` instances |
| `OverlayHost` | `OverlayHost` | — | The internal OverlayHost (read-only) |
| `BackdropColor` | `Color` | `Black` | Forwarded to internal OverlayHost |
| `BackdropMaxOpacity` | `double` | `0.5` | Forwarded to internal OverlayHost |

## DetentValue

Predefined snap points (or create custom ones):

| Static Property | Ratio | Description |
|---|---|---|
| `DetentValue.Quarter` | `0.25` | 25% of available height |
| `DetentValue.Half` | `0.50` | 50% of available height |
| `DetentValue.ThreeQuarters` | `0.75` | 75% of available height |
| `DetentValue.Full` | `1.0` | Full available height |

Custom detent: `new DetentValue(0.33)` for 33% height.

## FloatingPanel Events

| Event | Args | Description |
|---|---|---|
| `Opened` | `EventArgs` | Panel finished opening animation |
| `Closed` | `EventArgs` | Panel finished closing animation |
| `DetentChanged` | `DetentValue` | Panel snapped to a different detent |

## Public Methods

| Method | Description |
|---|---|
| `AnimateToDetentAsync(DetentValue)` | Programmatically animate to a specific detent |

## Gotchas — input controls (Entry / Editor / SearchBar) inside a panel

This section captures patterns to watch for when a panel contains a text-input control. If a user reports "can't tap / can't type / entry only works when the panel is fully expanded," check these in order:

1. **Do NOT re-introduce `scrollView.IsEnabled = (currentHeight ≈ highestHeight)` gating.** A prior version disabled the inner ScrollView whenever the panel was below the topmost detent. On iOS/Android, `IsEnabled=false` on a parent disables hit-testing for the entire subtree — the Entry could not receive focus or keystrokes until the panel was pulled to the top detent. The drag-vs-scroll gesture concern that gate tried to solve is moot because the pan recognizer is already restricted to `dragHandleContainer` only. Leave `scrollView.IsEnabled` alone.
2. **Don't fight platform keyboard avoidance from `OnInputFocused`.** When the Entry is already at (or above) the highest detent, do NOT trigger another `AnimateToDetentAsync(...)` on focus — the simultaneous `HeightRequest` animation, MAUI's `KeyboardAutoManagerScroll`, and any chat-level padding adjustment will all run together and drop keystrokes on iOS. Current code short-circuits when `currentDetentIndex >= sortedDetents.Count - 1`; keep that guard.
3. **Pan gesture must stay scoped to the drag handle.** It is currently attached only to `dragHandleContainer` (FloatingPanel.cs, ctor). Do not move it to the panel root, the Border, the inner Grid, or "the whole header." That would intercept taps meant for an Entry sitting elsewhere in the panel.
4. **Leave `AdjustForKeyboard` on for a ChatView inside a panel.** ChatView's iOS keyboard handler adds bottom padding equal to the keyboard overlap, and once `ExpandOnInputFocus` has taken the panel to its top detent that padding is the *only* thing lifting the composer clear of the keyboard — turn it off and the input bar sits underneath. The original worry (this fighting the panel's height animation and dropping keystrokes) is already handled by gotcha #2's guard, which short-circuits the competing animation at the top detent. Turn it off only when something else owns the overlap. See the chatview skill.
5. **Watch the negative bottom `Margin` for safe-area extension.** `ApplyBottomSafeAreaExtension` sets `Margin = new Thickness(0, 0, 0, -bottomInset)` so the panel paints into the iOS home-indicator zone. UIKit hit-testing clips to parent bounds by default, so anything you place in that extended strip will not receive touches. Keep interactive controls above the safe-area inset.
6. **`HookInputViews` walks Layout/ContentView/ScrollView/Border children.** Custom controls that are none of these will not have their inner Entry hooked, so `ExpandOnInputFocus` will not fire for them. If you add a new control that wraps inputs differently (e.g., a `View`-derived custom shell), extend the traversal.
7. **Don't set `HeightRequest` on the panel's content larger than the panel viewport.** If a child control has a fixed height taller than the resolved detent, controls near the bottom of that content (e.g., an input bar) end up below the visible area. Either drop the explicit `HeightRequest` and let the layout fill, or raise the detent / use `FitContent="True"`.

## Gotchas — a closed panel must not cover the page

If a user reports "the panel covers the page when closed" or "I can't scroll the page behind the panel":

1. **Show/hide the body with `SetContentVisible`, never `scrollView.IsVisible`.** With `IsContentScrollEnabled=false` the content row holds `contentHost` directly and `scrollView` is not in the tree — hiding only the ScrollView left the whole body on screen under a peeking header. `SetContentVisible` sets both.
2. **Open/Close never early-return on `isAnimating`.** They call `StopAnimations()` and start from the current height; the `finished` callbacks bail out if `IsOpen` has flipped since. An early return dropped the change and left the panel open with `IsOpen=false`.
3. **`OverlayHost.HideBackdrop` sets `InputTransparent=true` before the fade.** Animations stall on a page that is not being drawn, so waiting for the fade could leave an invisible full-page scrim eating touches.

## FloatingPanel Features

- **Drag handle gesture**: Pan gesture restricted to the drag handle only — content scrolls normally with no gesture conflicts
- **Height animation**: Panel animates `HeightRequest` instead of `TranslationY`, so it only occupies the space it needs — no phantom touch areas
- **Keyboard handling**: Automatically expands when an Entry/Editor is focused (unless already at the highest detent — see Gotchas above), restores when keyboard dismissed
- **Backdrop**: Shared backdrop managed by OverlayHost; dims proportionally to panel position
- **Multiple panels**: Multiple FloatingPanels can coexist in the same OverlayHost without blocking each other or content underneath
- **Locked mode**: When `IsLocked="True"`, all user-initiated dismissal is blocked (drag, header tap to close, backdrop tap to close) — the panel can only be closed via code. Header tap still opens the panel
- **Fit content**: When `FitContent="True"`, the panel measures its content and auto-computes a single detent to fit it
- **Position**: Slides from bottom (`Position="Bottom"`, default), top (`Position="Top"`), or bottom with tabs (`Position="BottomTabs"` — clips above the tab bar)
- **Header peek**: Set `ShowHeaderWhenClosed="True"` with a `HeaderTemplate` to show a persistent header bar when the panel is closed — tapping it opens the panel
- **Feedback**: Subtle haptic on open, close, and detent snap; disable with `UseFeedback="False"`

## FloatingPanel Locked Example

```xml
<shiny:ShinyContentPage>
    <shiny:ShinyContentPage.PageContent>
        <!-- page content -->
    </shiny:ShinyContentPage.PageContent>
    <shiny:ShinyContentPage.Panels>
        <!-- Signature capture: locked + auto-sized -->
        <shiny:FloatingPanel IsOpen="{Binding IsSignatureOpen}"
                             IsLocked="True"
                             FitContent="True"
                             HasBackdrop="True"
                             PanelCornerRadius="20">
            <VerticalStackLayout Padding="20" Spacing="15">
                <Label Text="Draw your signature" FontSize="18" FontAttributes="Bold" />
                <Button Text="Done" Command="{Binding DoneCommand}" />
            </VerticalStackLayout>
        </shiny:FloatingPanel>

        <!-- Selector: locked with explicit detent -->
        <shiny:FloatingPanel IsOpen="{Binding IsSelectorOpen}"
                             IsLocked="True"
                             HasBackdrop="True"
                             PanelCornerRadius="20">
            <shiny:FloatingPanel.Detents>
                <shiny:DetentValue Ratio="0.5" />
            </shiny:FloatingPanel.Detents>
            <CollectionView ItemsSource="{Binding Items}" />
        </shiny:FloatingPanel>
    </shiny:ShinyContentPage.Panels>
</shiny:ShinyContentPage>
```

## FloatingPanel with Custom Detents

```xml
<shiny:FloatingPanel IsOpen="{Binding IsOpen, Mode=TwoWay}"
                     PanelBackgroundColor="#1E1E1E"
                     HandleColor="#888888"
                     PanelCornerRadius="24"
                     HasBackdrop="True"
                     CloseOnBackdropTap="True"
                     AnimationDuration="300">
    <shiny:FloatingPanel.Detents>
        <shiny:DetentValue Ratio="0.33" />
        <shiny:DetentValue Ratio="0.66" />
        <shiny:DetentValue Ratio="1.0" />
    </shiny:FloatingPanel.Detents>
    <VerticalStackLayout Padding="20">
        <Label Text="Custom Panel" TextColor="White" />
    </VerticalStackLayout>
</shiny:FloatingPanel>
```

The panel **opens at the smallest detent** and drags up to larger ones. To open **full-screen by default**, give
it a single `Full` detent (`DetentValue.Full`, ratio `1.0`):

```xml
<shiny:FloatingPanel.Detents>
    <shiny:DetentValue Ratio="1.0" />
</shiny:FloatingPanel.Detents>
```

```csharp
// or from code
panel.Detents = new ObservableCollection<DetentValue> { DetentValue.Full };
```
