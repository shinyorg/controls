# Flyout (MAUI only)

A side panel that slides in from either edge, can rest as a narrow icon rail instead of a full panel,
and either **pushes** the content aside or **floats** over it. It replaces MAUI's `FlyoutPage` for
apps that want more than a drawer, and it works **inside Shell** — which `FlyoutPage` cannot.

Everything is in `Shiny.Maui.Controls` (namespace `Shiny.Maui.Controls.Flyout`, reachable from XAML
through the usual `xmlns:shiny="http://shiny.net/maui/controls"`). No extra package, no registration
beyond `UseShinyControls()`.

> **Blazor equivalent**: `AppLayoutPanel` inside `AppLayout` — see [layout.md](./layout.md). Never emit
> `FlyoutView`/`FlyoutPanel` for Blazor, and never emit `AppLayout` for MAUI.

## The three pieces

| Type | What it is |
|---|---|
| `FlyoutPanel` | One panel. Owns its content, its widths, its state and how it presents. |
| `FlyoutView` | The layout that hosts a `Content` view plus a `Start` and/or `End` panel. Drop it in any page. |
| `ShinyFlyoutPage` | A page built around a `FlyoutView`. The `FlyoutPage` replacement. |
| `ShinyFlyout` | Attached properties that install a flyout over **every** page a Shell or NavigationPage shows. |
| `IFlyoutService` | Drives whichever flyout is on the page currently showing, from a view model. |

## Pushing: shift, don't crush

`FlyoutView.PushMode` (also on `ShinyFlyoutPage`) decides what a *pushing* panel does to the content:

| | |
|---|---|
| `Shift` *(default)* | The content keeps its **full width** and is translated aside; its far edge slides out of view and is clipped. Nothing inside re-lays out — text does not rewrap, columns do not collapse, a list re-measures no rows. |
| `Resize` | The content is genuinely narrowed and everything inside reflows to fit. |

`Shift` is the drawer feel (the whole screen slides over). `Resize` is the chrome feel (a sidebar the
editor makes room for) — use it for a responsive layout that *should* reflow beside an open panel.

Two things to know:

- **The mode governs every displacement the view applies, the rail included.** That keeps "Shift
  never resizes your content" true without exception — but it means a permanent rail in `Shift` mode
  pushes its width of content off the far edge for good. An app whose rail is permanent chrome wants
  `Resize`.
- **Two shifting panels cancel rather than crush**: a 280 start and a 200 end give a net 80 shift,
  because shifting cannot satisfy both sides at once and splitting the difference by narrowing is the
  exact thing the mode exists to avoid.

`Overlay` is unaffected — it never moves the content either way.


## Vocabulary

```
State:        Hidden | Collapsed | Expanded          (Collapsed = the rail)
Side:         Start | End                            (RTL-aware; Start is left in LTR)
Presentation: Overlay | Push | Auto
```

**The one rule worth remembering:** `Presentation` decides what an **Expanded** panel does. `Push`
insets the content and re-lays it out; `Overlay` floats over it with a scrim. A **Collapsed** rail
*always* insets the content on both presentations — it is chrome, not a drawer. `Hidden` insets
nothing. That is why expanding a rail in `Overlay` slides the panel over the content without the
content moving at all.

## As a page — the FlyoutPage replacement

```xml
<shiny:ShinyFlyoutPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                       xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                       xmlns:shiny="http://shiny.net/maui/controls"
                       x:Class="MyApp.MainPage"
                       Title="Workspace">

    <shiny:ShinyFlyoutPage.Start>
        <shiny:FlyoutPanel x:Name="Nav"
                           State="Collapsed"
                           CollapsedState="Collapsed"
                           Presentation="Auto"
                           CompactWidth="700"
                           ExpandedWidth="260"
                           CollapsedWidth="64"
                           IsResizable="True">
            <shiny:FlyoutPanel.HeaderContent>
                <Label Text="Explorer" FontAttributes="Bold" Padding="16,14" />
            </shiny:FlyoutPanel.HeaderContent>

            <shiny:FlyoutPanel.RailContent>
                <VerticalStackLayout Spacing="6" HorizontalOptions="Center" Padding="0,12">
                    <Button Text="&#x1F5C2;" Clicked="OnToggle" />
                    <Button Text="&#x1F50D;" Clicked="OnToggle" />
                </VerticalStackLayout>
            </shiny:FlyoutPanel.RailContent>

            <VerticalStackLayout Padding="8">
                <Label Text="Files" Padding="12,10" />
                <Label Text="Search" Padding="12,10" />
            </VerticalStackLayout>

            <shiny:FlyoutPanel.FooterContent>
                <Label Text="v1.0" Padding="16,12" FontSize="12" />
            </shiny:FlyoutPanel.FooterContent>
        </shiny:FlyoutPanel>
    </shiny:ShinyFlyoutPage.Start>

    <!-- implicit content = Detail -->
    <ScrollView>
        <VerticalStackLayout Padding="24" Spacing="16">
            <Label Text="Detail" FontSize="28" />
        </VerticalStackLayout>
    </ScrollView>
</shiny:ShinyFlyoutPage>
```

```csharp
public partial class MainPage : ShinyFlyoutPage
{
    public MainPage() => InitializeComponent();

    void OnToggle(object? sender, EventArgs e) => _ = this.Nav.ToggleAsync();
}
```

**`Detail` is a `View`, not a `Page`.** Only `Window`, `Shell`, `NavigationPage`, `TabbedPage` and
`FlyoutPage` can parent a `Page` in MAUI, so — unlike `FlyoutPage.Detail` — this cannot host a
`NavigationPage`. Navigate with Shell (see below), put the page in a `NavigationPage` and install the
flyout with `ShinyFlyout`, or swap `Detail` yourself. Never generate `<ShinyFlyoutPage.Detail><NavigationPage>`.

`ShinyFlyoutPage` derives from `ShinyContentPage`, so the overlay host, `FloatingPanel`s and the
built-in loading overlay all still work, with the flyout underneath them.

## Inside a page — FlyoutView

For one screen that needs a panel (an inspector, a filter pane), use the view directly:

```xml
<ContentPage xmlns:shiny="http://shiny.net/maui/controls">
    <shiny:FlyoutView x:Name="Flyout">
        <shiny:FlyoutView.End>
            <shiny:FlyoutPanel State="Hidden" CollapsedState="Hidden"
                               Presentation="Overlay" ExpandedWidth="320" />
        </shiny:FlyoutView.End>

        <Grid>…page body…</Grid>
    </shiny:FlyoutView>
</ContentPage>
```

The panels span the **full height of the `FlyoutView`**. To keep one below an app bar, put the
`FlyoutView` below the app bar rather than around it.

## Across every page — ShinyFlyout (Shell and NavigationPage)

Declare it once on the Shell (or `NavigationPage`, or a single page) and every page it shows gets one:

```xml
<Shell xmlns:shiny="http://shiny.net/maui/controls"
       FlyoutBehavior="Disabled">

    <shiny:ShinyFlyout.StartTemplate>
        <DataTemplate>
            <shiny:FlyoutPanel State="Hidden" CollapsedState="Hidden"
                               Presentation="Overlay" ExpandedWidth="280">
                <VerticalStackLayout>…nav…</VerticalStackLayout>
            </shiny:FlyoutPanel>
        </DataTemplate>
    </shiny:ShinyFlyout.StartTemplate>

    …ShellContent…
</Shell>
```

- **It is a `DataTemplate`, not an instance.** Each page builds its own panel. Sharing one instance
  would mean re-parenting it on every navigation, which rebuilds its native views and throws away
  scroll position and focus. What carries across pages is the **state** — a drawer left open is still
  open on the page you land on.
- Set `Shell.FlyoutBehavior="Disabled"` so Shell's own drawer stays out of the way.
- Safe to combine with `ShinyTabBarBehavior` and a docked `ProgressLine` on the same Shell — the
  flyout always nests inside their page overlay, whichever installs first. No ordering workarounds needed.
- The flyout wraps the **page's content**, so with Shell a panel sits below the nav bar and above the
  tab bar, and `Push` pushes the page content, not Shell's chrome. Add `Shell.NavBarIsVisible="False"`
  if the panel should run the full height of the window.
- A page that declares its own `ShinyFlyoutPage` is skipped — it already has one.
- **View options go on the host**, as attached properties next to the templates:
  `ShinyFlyout.PushMode`, `ShinyFlyout.ScrimColor`, `ShinyFlyout.ScrimOpacity`,
  `ShinyFlyout.IsAnimationEnabled`. They are copied onto every installed `FlyoutView`, and a change
  later reaches the pages already showing. Never generate code that calls
  `ShinyFlyout.GetFlyoutView(page).PushMode = …` after each navigation. Panel options
  (`IsSwipeEnabled`, `Presentation`, widths) belong on the `FlyoutPanel` inside the template.

```xml
<Shell xmlns:shiny="http://shiny.net/maui/controls"
       FlyoutBehavior="Disabled"
       shiny:ShinyFlyout.PushMode="Resize"
       shiny:ShinyFlyout.ScrimOpacity="0.5">
    <shiny:ShinyFlyout.StartTemplate>…</shiny:ShinyFlyout.StartTemplate>
</Shell>
```

## Driving it from code

```csharp
// from a view model, with no reference to the page
public class ShellViewModel(IFlyoutService flyouts)
{
    public Task ToggleNav() => flyouts.ToggleAsync();                       // Start by default
    public Task ShowInspector() => flyouts.SetStateAsync(FlyoutSide.End, FlyoutPanelState.Expanded);
}
```

`IFlyoutService` is registered by `UseShinyControls()` and resolves the flyout on the page currently
showing. It also exposes `GetPanel`, `GetState` and a `StateChanged` event.

Directly on a panel: `ToggleAsync()`, `ExpandAsync()`, `CollapseAsync()`, `HideAsync()`,
`SetStateAsync(state)` — each completes when the transition finishes. On the view:
`ToggleAsync(side)`, `SetStateAsync(side, state)`, `GetState(side)`, `GetPanel(side)`,
`GetEffectivePresentation(side)`, `GetCurrentWidth(side)`, `GetContentInset(side)`.

## FlyoutPanel reference

| Property | Type | Default | Notes |
|---|---|---|---|
| `PanelContent` | `View?` | — | Content property; the expanded body |
| `RailContent` | `View?` | `null` | What `Collapsed` shows instead. Unset = the collapsed panel keeps showing the leading edge of `PanelContent` |
| `HeaderContent` / `FooterContent` | `View?` | `null` | Pinned above/below the body; they do not scroll |
| `Side` | `FlyoutSide` | `Start` | Set for you by `FlyoutView.Start`/`.End` |
| `State` | `FlyoutPanelState` | `Expanded` | Two-way |
| `CollapsedState` | `FlyoutPanelState` | `Collapsed` | Where a dismiss lands. `Hidden` for a plain drawer |
| `ExpandedWidth` | `double` | `280` | Two-way — a resize drag writes back here |
| `CollapsedWidth` | `double` | `64` | The rail width |
| `MinExpandedWidth` / `MaxExpandedWidth` | `double` | `160` / `480` | Resize clamps |
| `IsResizable` | `bool` | `false` | Drag handle on the inner edge. Push + Expanded only |
| `Presentation` | `FlyoutPresentation` | `Auto` | |
| `CompactWidth` | `double` | `800` | Host width at/above which `Auto` pushes |
| `CollapseBelow` | `double` | `0` (off) | Host width under which an expanded panel drops to `CollapsedState`, restoring when it grows back |
| `HasScrim` / `CloseOnScrimTap` | `bool` | `true` | Floating panels only |
| `IsSwipeEnabled` / `EdgeSwipeWidth` | `bool` / `double` | `true` / `20` | Edge swipe to open, drag the scrim to close |
| `AnimationDuration` | `double` | `250` | ms; 0 snaps |
| `IsContentScrollEnabled` | `bool` | `true` | Wraps `PanelContent` in a `ScrollView` |
| `ShowHeaderWhenCollapsed` / `ShowFooterWhenCollapsed` | `bool` | `true` | |
| `PanelBackgroundColor` / `DividerColor` | `Color?` | theme | Unset follows the theme (`SurfaceContainerLow` / `OutlineVariant`) |
| `DividerWidth` / `HasDivider` / `HasShadow` / `CornerRadius` | | `1` / `true` / `true` / `0` | A floating panel drops the divider for a shadow |
| `UseFeedback` | `bool` | `true` | Routes state changes through `IFeedbackService` |

Read-only: `EffectivePresentation`, `CurrentWidth`, `Host`. Event: `StateChanged`.

`FlyoutView` adds `Content`, `Start`, `End`, `ScrimColor`, `ScrimOpacity` (0.4) and
`IsAnimationEnabled`.

## Behaviours worth knowing

- **Compacting** (`CollapseBelow`) is measured against the **`FlyoutView`**, not the window, so a
  flyout nested in a pane reacts to the pane. It is a response to the viewport, not a preference, so
  it is never persisted — and a **deliberate** state change outranks it: once the user has closed the
  panel themselves, growing the host back does not re-open it.
- **A slide is not a resize.** Hidden ⇄ anything translates the panel at its final size (no layout
  pass, so the drawer stays smooth on a phone). Collapsed ⇄ Expanded really resizes, and the content
  beside a pushing panel is re-laid out each frame. That is deliberate, and it is why the control is a
  custom layout rather than a `Grid` with animated column widths.
- **A panel is never wider than the flyout.** `ExpandedWidth="400"` on a 320-wide phone gives 320.
- **The edge-swipe strip sits over the content**, so taps landing in that `EdgeSwipeWidth` band go to
  it. Set `EdgeSwipeWidth="0"` for content that needs the very edge.
- **`Start`/`End` mirror under `FlowDirection="RightToLeft"`** — a `Start` panel comes in from the
  right, and the content is inset from the right.
- **Gestures**: swipe in from the edge to open (floating panels), tap or drag the scrim to close, drag
  the inner-edge handle to resize (pushing panels with `IsResizable`). There is deliberately no
  drag-from-inside-the-panel gesture — it fights the scrolling content the panel usually holds.
- **macOS AppKit (`net11.0-macos`)**: `ShinyFlyout`'s declare-once install re-parents a page's content
  at runtime, which does not re-render on that head (the same pre-existing limitation as Toast,
  Dialogs and the in-app Quick Entry). Use `ShinyFlyoutPage` or `FlyoutView` in the page's own markup
  there, which never re-parents anything.

## Don't

- Don't use `IsVisible` to show/hide a panel — that is `VisualElement`'s, and the host drives it.
  Bind `State`.
- Don't put a `FlyoutView` inside a `ScrollView`. It fills its host and its panels scroll their own
  bodies.
- Don't set `ShinyFlyoutPage.Content` / `PageContent` — the flyout lives there. Use `Detail`.
- Don't declare both `ShinyFlyoutPage` and `ShinyFlyout.StartTemplate` for the same page.

### Automation ids (MAUI)

Set `AutomationId` on a `FlyoutView` and its scrim is named after it. The panels are yours, so give them ids directly. On a `ShinyFlyoutPage`, or a page that `ShinyFlyout` installs on, the view is named after the page.

| Part | Id |
|---|---|
| Scrim (closes an overlaid panel) | `<id>_Scrim` |
| The FlyoutView on a page with an id | `<page id>_Flyout` (scrim `<page id>_Flyout_Scrim`) |

## Foldables

`FlyoutPanel.FoldBehavior` (`SnapToFold` default / `None`). With a fold posture source registered, an **expanded** panel snaps to a book fold. It pushes, ignores Min/MaxExpandedWidth, hides the grip, and never writes `ExpandedWidth`. Do not compute fold widths yourself. See [foldables.md](./foldables.md).
