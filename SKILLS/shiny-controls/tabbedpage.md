# ShinyTabbedPage & ShinyTabBar (MAUI only)

An improved `TabbedPage` for .NET MAUI, in the core `Shiny.Maui.Controls` package. Motion icons in the
tabs, per-tab badges, an animated transition between tabs, lazily built tab content, and a raised
**centre button** that presents the current page's actions. The same bar also drops onto a `Shell`.

There is **no Blazor equivalent yet** — Blazor has the simpler `ShinyTabBar` component described in
`toolbar-tabbar.md`, which is a different type with a different API. Never emit
`<ShinyTabbedPage>` in Razor.

Nothing here touches a platform SDK, so it renders on every MAUI head — including AppKit
(`net10.0-macos`) and GTK4, where MAUI's own `TabbedPage` does not go.

## Two hosts, one bar

| | Use | Navigation is owned by |
|---|---|---|
| `ShinyTabbedPage` | tabs are screens, no stack inside them | the tabbed page (one screen per tab) |
| `ShinyTabBarBehavior` on a `Shell` | tabs are Shell sections, with their own stacks, routes and deep links | Shell |

Pick the Shell form whenever the app already has a Shell. It keeps routing, `ShellContent`'s own
lazy loading and per-tab navigation stacks, and only replaces the chrome.

## ShinyTabbedPage

```xml
xmlns:shiny="http://shiny.net/maui/controls"

<shiny:ShinyTabbedPage x:Class="MyApp.MainTabs"
                       Transition="Slide"
                       TransitionDuration="220"
                       IndicatorStyle="Pill">

    <shiny:ShinyTabbedPage.CenterButton>
        <shiny:TabCenterButton Icon="plus" Mode="Menu" />
    </shiny:ShinyTabbedPage.CenterButton>

    <!-- Inline content: built with the markup -->
    <shiny:ShinyTabItem Title="Home" Icon="home" Route="home">
        <views:HomeView />
    </shiny:ShinyTabItem>

    <!-- Templated content: built the first time the tab is selected, then kept -->
    <shiny:ShinyTabItem Title="Chat" Icon="message" Route="chat" Badge="3">
        <shiny:ShinyTabItem.ContentTemplate>
            <DataTemplate><views:ChatView /></DataTemplate>
        </shiny:ShinyTabItem.ContentTemplate>
    </shiny:ShinyTabItem>

    <!-- A whole ContentPage in the template is adopted -->
    <shiny:ShinyTabItem Icon="mail" Route="inbox">
        <shiny:ShinyTabItem.ContentTemplate>
            <DataTemplate><pages:InboxPage /></DataTemplate>
        </shiny:ShinyTabItem.ContentTemplate>
    </shiny:ShinyTabItem>
</shiny:ShinyTabbedPage>
```

Code-behind derives from the control, exactly as a `ContentPage` would:

```csharp
public partial class MainTabs : ShinyTabbedPage
{
    public MainTabs() => this.InitializeComponent();
}
```

`Tabs` is the content property, so the `<shiny:ShinyTabItem>` elements need no wrapper.

### Lazy content

`Content` is built with the markup. `ContentTemplate` is built the first time the tab is selected and
then cached — four tabs behind templates cost one view tree on launch, not four. `CacheTabContent`
(default true) turns the caching off, which rebuilds and therefore resets the tab every time it is
entered.

### A ContentPage in a template

The template may inflate a `View` **or** a `ContentPage`. A page is adopted: its `Content` is hosted,
its `Title` fills in a tab that has none, its `BindingContext` is mirrored onto the hosted view, and
`ShinyTabs` attached values are read straight off it. `ShinyTabItem.AdoptedPage` hands it back.

**Give a templated tab its own `Title` anyway.** The page does not exist until the tab is first
selected, so a tab relying on the page's `Title` renders unlabelled next to its labelled siblings
until someone taps it. The same goes for `Badge` — put it on the tab or the `ShellContent` when it
has to show before the tab is ever opened.

Two things it does **not** get:

- **A place on a navigation stack.** The page object is not the page on screen — the
  `ShinyTabbedPage` is. `this.Navigation` in its code-behind resolves (the adopted page is parented
  to the tabbed page) but pushes onto the tabbed page's stack.
- **`OnAppearing`.** MAUI raises page lifecycle from the platform, for the page the platform actually
  presented; this one is never presented. `IPageController.SendAppearing()` on it does nothing at
  all. Use `ITabAware` — see below.

### Lifecycle: ITabAware

```csharp
public class InboxViewModel : ITabAware
{
    public void OnTabAppearing() => this.StartPolling();
    public void OnTabDisappearing() => this.StopPolling();
}
```

Called on the tab's content, on the adopted page, and on either one's `BindingContext` — each object
once, even when it is reachable both ways. `OnTabAppearing` runs as soon as the tab becomes selected
(for the first tab, while the page is still being built) and again when the page itself is returned
to; `OnTabDisappearing` runs when another tab is chosen and when the page leaves the screen. Never
twice in a row. `ShinyTabItem.Appearing`/`Disappearing` are the event form.

### Transitions

`Transition` takes the same `StateTransition` the `StateView` and `Wizard` use:
`None`, `Fade`, `Slide`, `SlideLeft`, `SlideRight`, `SlideUp`, `SlideDown`, `Scale`. `Slide` is the
default and is **direction-aware** — a tab later in `Tabs` enters from the right, an earlier one from
the left, which is the only cue that says which way you just moved. Plus `TransitionDuration` (ms,
zero swaps instantly) and `TransitionEasing`.

### Page properties

Selection: `SelectedIndex`, `SelectedItem` (both two-way and kept in step), `GoTo(int)`,
`GoTo(string route)`, `SelectionChanged`, `TabReselected` (the already-selected tab tapped again —
scroll to top, pop to root).

Content: `Tabs`, `Transition`, `TransitionDuration`, `TransitionEasing`, `CacheTabContent`,
`SyncTitleWithTab` (default true — the page's `Title` follows the selected tab), `ContentHost` (the
`StateView` underneath, for anything not passed through).

Bar: `TabBarIsVisible`, `ContentBehindTabBar` (full-bleed under a translucent bar — leave room at the
bottom of your own content), `CenterButton`, `IndicatorStyle`, `LabelMode`, `SelectedColor`,
`UnselectedColor`, `IndicatorColor`, `BarHeight`, `BarBackgroundColor`, `BarCornerRadius`,
`BarMargin`, `BarStyle`, `HasShadow`, `IconSize`, `AnimateIcons`, and `TabBar` itself for everything else.

## ShinyTabItem

| Property | Notes |
|---|---|
| `Title` | The label. Filled in from an adopted page's `Title` when unset. |
| `Icon` | A built-in motion icon name (`home`, `message`, `search`, …). See motion-icons.md. |
| `IconSource` / `IconPathData` | Explicit `MotionIconDefinition`, or a raw SVG path in a 24x24 box. |
| `IconImage` | A plain `ImageSource` for artwork that is not a motion icon. Does not animate; the selected state shows as opacity rather than colour. |
| `Motion` | Which `MotionPreset` the icon plays on selection. |
| `Badge` | Text. `""` draws a **dot**; `null` draws nothing. |
| `BadgeColor` | Unset follows the theme's error colour. |
| `IsEnabled` / `IsVisible` | A hidden tab leaves the bar and the rest re-space. |
| `Route` | A stable id for `GoTo(string)`. Not a Shell route. Also becomes the cell's `AutomationId` (`tab-<route>`). |
| `Content` / `ContentTemplate` | See above. `ContentTemplate` wins. |
| `Tag`, `AdoptedPage`, `PageContext` | |

## The centre button

```xml
<shiny:TabCenterButton Icon="plus"
                       Mode="Menu"
                       Size="60"
                       RotateOnOpen="45" />
```

It is **not a tab** — it never becomes the selection. `Mode="Action"` just runs `Command` and raises
`ShinyTabBar.CenterClicked`. `Mode="Menu"` (the default) presents the current page's actions above
it, and **falls back to `Action` when neither the page nor the button declares anything**, so a
button that is only ever a button behaves like one without being reconfigured.

`Overhang` is how far it rises above the bar; left at `-1` it is **a third** of `Size`. Half centres
the circle exactly on the bar's top edge, which is the textbook diagram but reads as floating away
from the tabs it belongs to. Also `IconSize`, `Text` (a caption under it), `BackgroundColor`,
`ForegroundColor`, `Command`/`CommandParameter`, `IsEnabled`, `IsVisible` (leaves the gap, so tabs do
not jump — remove `CenterButton` entirely to reclaim the space).

An odd number of tabs is handled: the bar pads the short side with an empty column so the button
stays truly centred.

### Templating the button

`TabCenterButton.ContentTemplate` replaces the circle, its background, its shadow and its glyph with
your own. The bar keeps the press, the overhang and the column, so `Size` and `Overhang` still mean
something — they are the space the template is handed. The template's binding context is the
`TabCenterButton`, so `{Binding Size}` resolves.

```xml
<shiny:TabCenterButton Size="64">
    <shiny:TabCenterButton.ContentTemplate>
        <DataTemplate>
            <Border StrokeShape="RoundRectangle 18" BackgroundColor="#111827">
                <Label Text="+" TextColor="White" FontSize="30"
                       HorizontalOptions="Center" VerticalOptions="Center" />
            </Border>
        </DataTemplate>
    </shiny:TabCenterButton.ContentTemplate>
</shiny:TabCenterButton>
```

### Templating the popup

`ShinyTabBar.MenuTemplate` replaces everything inside the menu card — rows, layout, chrome. The bar
keeps the backdrop, the anchoring above the button, and the open/close animation. It beats every
other menu source, including a page's `MenuContent`, because it is the bar-wide chrome decision. The
binding context handed to it is the current page's, so bindings written in it resolve there.

Both the button and the popup are entirely optional: leave `CenterButton` null for an ordinary bar,
and use `Mode="Action"` for a button that never presents anything.

## What the centre button presents — per page

This is the half owned by the page rather than the bar, deliberately shaped like `ToolbarItems`. Set
it on **whatever the tab is showing** — the adopted `ContentPage`, the content view, or the
`ShellContent`.

```xml
<ContentPage ... xmlns:shiny="http://shiny.net/maui/controls"
             shiny:ShinyTabs.Badge="{Binding UnreadText}">

    <shiny:ShinyTabs.Actions>
        <shiny:TabActionCollection>
            <shiny:TabAction Text="New message" Icon="edit" Command="{Binding ComposeCommand}" />
            <shiny:TabAction Text="Mark all read" Icon="check" Command="{Binding MarkReadCommand}" />
            <shiny:TabAction IsSeparator="True" />
            <shiny:TabAction Text="Empty inbox" Icon="trash" IsDestructive="True" Command="{Binding EmptyCommand}" />
        </shiny:TabActionCollection>
    </shiny:ShinyTabs.Actions>
</ContentPage>
```

**The `<shiny:TabActionCollection>` wrapper is required** — an attached property whose value is a
collection needs a type the markup can name, the same way `VisualStateManager.VisualStateGroups`
needs `VisualStateGroupList`.

`TabAction`: `Text`, `Icon`/`IconSource`/`IconPathData`/`IconImage`, `Command`/`CommandParameter`,
`Clicked`, `IsDestructive` (draws it in the theme's error colour — nothing is confirmed for you),
`IsEnabled`, `IsSeparator`, `Tag`.

For something that is not a list of rows, hand the bar a view instead — it wins over `Actions`:

```xml
<shiny:ShinyTabs.MenuContent>
    <VerticalStackLayout Padding="20" Spacing="10" WidthRequest="260">
        <Label Text="Saved searches" FontAttributes="Bold" />
        ...
    </VerticalStackLayout>
</shiny:ShinyTabs.MenuContent>
```

`ShinyTabs.MenuContentTemplate` is the same thing built fresh on every open, which is what shows
current data rather than whatever it captured first. Precedence, highest first: the page's
`MenuContentTemplate`, the page's `MenuContent`, the button's `MenuContentTemplate`, the button's
`MenuContent`, the page's `Actions`, the button's `Actions`.

## ShinyTabs attached properties

Set on a page, a content view, or a `ShellContent`/`Tab`.

| Property | Notes |
|---|---|
| `Badge` | Beats `ShinyTabItem.Badge` while that page is showing. |
| `BadgeColor` | |
| `Icon` | Only read by `ShinyTabBarBehavior`, where a `ShellContent` has nowhere else to say it. |
| `Title` | Overrides the tab's label. |
| `Actions` | The centre menu's rows. Needs the `TabActionCollection` wrapper in XAML. |
| `MenuContent` / `MenuContentTemplate` | Custom centre-menu content. |
| `IsTabBarVisible` | Hides the whole bar while that page is showing. |

**Where to put a badge.** On the `ShinyTabItem` or the `ShellContent` when the count must show on a
tab the user has never opened — there is no page to ask yet. On the page when the page computes it.
The page's value wins, but only for the tab that page is showing, so the two never fight.

## The indicator travelling between tabs

`IndicatorTransition` is how the indicator gets from the tab it was on to the tab it is going to:

| | |
|---|---|
| `Slide` *(default)* | One indicator travels horizontally across the bar. |
| `None` | The indicator is drawn inside each cell and simply appears on the new tab as it disappears from the old. |

This is a different thing from `ShinyTabbedPage.Transition`, which animates the **content** between
tabs. They compose — sliding content under a sliding indicator is the usual pairing.

`IndicatorEasing` (default `CubicInOut`) and `AnimationDuration` shape the journey.

**Sliding needs measured geometry**, which does not exist until the bar has been laid out. Until it
does, the bar falls back to `None` and draws inside the cell — so the first frame is correct rather
than blank or parked in the corner at zero width. Nothing to configure; it swaps over on its own
once the bar has a size.

## Selection animations

`SelectionAnimation` animates a tab as it becomes selected and as it stops being:

| | |
|---|---|
| `Scale` *(default)* | The icon grows slightly into the selection. |
| `Lift` | The icon rises a few points. |
| `Bounce` | The icon overshoots and springs back. |
| `Fade` | The label fades under a steady icon. |
| `Indicator` | The indicator grows in from nothing rather than appearing. |
| `None` | Colour and indicator change instantly. |

For anything else, implement `ITabAnimator` and set `ShinyTabBar.Animator` — it wins over
`SelectionAnimation`, so **clear it to `null` to get the built-ins back**.

```csharp
public class SpinAnimator : ITabAnimator
{
    public Task AnimateAsync(TabAnimationContext context)
        => context.Icon?.RotateToAsync(context.IsSelected ? 360 : 0, context.Duration) ?? Task.CompletedTask;
}
```

`TabAnimationContext` hands over the `Cell`, the `Icon`, the `Label`, the `Indicator` (whichever the
current `IndicatorStyle` draws), `IsSelected`, `Duration`, `Item` and `Bar` — separately, because
they usually want different treatment: lifting the whole cell moves the label with the icon, which
is rarely wanted.

It is called **once per tab whose selected state actually changed** — never on a restyle, a badge
update or a rebuild, so an animation is not replayed by something the user cannot see. Both ends of
a change are called: the tab losing the selection and the one taking it.

## Animations look dead on an emulator

`Transition`, the travelling indicator and the selection animations all run through MAUI's animation
ticker, and MAUI **skips animations entirely** when the system reports them disabled — it jumps
straight to the final frame. Android emulator images frequently ship with the animator scale off or
unset, which makes every animation in the app look broken while nothing is actually wrong:

```bash
adb shell settings get global animator_duration_scale   # null or 0 = animations off
adb shell settings put global animator_duration_scale 1.0
```

Confirmed both ways on an API 36 emulator: with the scale at 0 a deliberately slowed 4-second slide
completed instantly; at 1.0 the same transition was caught mid-flight. Check this before debugging an
animation on Android.

## Transparency

`BarBackgroundOpacity` (`1` by default) fades the **background only** — icons, labels, badges and the
indicator stay fully opaque. A bar whose tabs fade along with it is not translucent, it is unreadable.

```xml
<shiny:ShinyTabbedPage BarBackgroundOpacity="0.6" ContentBehindTabBar="True" />
```

The alpha goes on the colour, never on a view: `Opacity` on the surface would take everything inside
it down too, and opacity multiplies down the tree so a child cannot undo it. It multiplies into any
alpha the colour already carried, and follows a theme swap.

Pair it with `ContentBehindTabBar` (or `BarStyle="Floating"`, which implies it) — without content
running underneath, there is nothing behind the glass to see.

## Menus close when the tab changes

Both the centre menu and the overflow menu close on any change of selected tab — a tap, `GoTo`, or a
binding on `SelectedIndex`. A menu belongs to the tab it was opened over, and changing tabs swaps the
page underneath it. A **reselect** does not close it: the page is the same one it was opened over.

## Overflow — the "More" tab

The bar folds tabs away on its own when they stop fitting. Nothing to switch on.

`MaxVisibleTabs` is `0` by default, which means **work it out from the bar's width**: the cap is the
width divided by `MinTabWidth` (72), less the centre button's column. Anything past it folds behind a
synthesized **More** tab, and the split is recomputed when the width changes — a rotation, a window
resize, or a Shell bar moving to a page with different chrome. Set `MaxVisibleTabs` to a number to
pin it instead.

```xml
<shiny:ShinyTabbedPage MaxVisibleTabs="4"        <!-- 0 = auto, the default -->
                       MinTabWidth="72"          <!-- what "does not fit" means -->
                       OverflowTitle="More"
                       OverflowIcon="more" />
```

- **The cap counts the More tab.** `MaxVisibleTabs="4"` over six tabs draws three real tabs and a
  More — otherwise adding the More cell would put the bar straight back over the width that caused
  the overflow. A cap of `1` is treated as `2`; a bar that is nothing but an overflow button is not a
  tab bar.
- **The More tab shows as selected** whenever the tab on screen is one it folded away, so the bar
  never reads as having nothing selected.
- **Folded tabs keep their badges** — a row in the menu carries the count that is no longer visible
  in the bar, which is the whole reason to notice it.
- It reuses the centre button's menu card, backdrop and animation, anchored to the trailing edge
  instead of the middle. The centre button does **not** rotate into its close glyph for it: that
  affordance belongs to the menu the button itself opened.
- In code: `HasOverflow`, `OverflowItems`, `OpenOverflow()` and `SelectOverflowItem(item)` on
  `ShinyTabBar`. The More cell's `AutomationId` is `tab-more`.

## Bar style — docked or floating

`BarStyle` is `TabBarStyle.Docked` by default. `TabBarStyle.Floating` is the iOS-style capsule:

```xml
<shiny:ShinyTabbedPage BarStyle="Floating" ...>
```

or `page.TabBar.BarStyle = TabBarStyle.Floating;`

What changes with `Floating`:

- The bar is inset from the page edges — 16 either side and 8 below — unless `BarMargin` is set.
- Corners become a capsule (half the bar height) unless `BarCornerRadius` is set.
- `ContentBehindTabBar` is implied, so the page's content is laid out against the **full height** of
  the page and the bar is drawn over it. Pad the bottom of a scrollable by about `BarHeight` so its
  last row is not left under the capsule. That property is still worth setting on its own for a
  docked bar you have made translucent.
- The safe-area inset moves from the bar's background to the bar itself, so the capsule clears the
  home indicator instead of painting through it.
- The shadow steps up from Level2 to Level3, because a detached bar has nothing behind it to read
  against.

In Shell (`ShinyTabBarBehavior`) the bar is already an overlay over the page, so `Floating` needs
nothing extra there.

## Safe area

`RespectSafeArea` is **on by default**. Docked, the background paints all the way to the bottom of
the screen while the tabs inside sit above the home indicator. Floating, the whole capsule is inset
instead. Turn it off for a bar that is not docked to the bottom of a page, or one already inside a
safe-area-aware container.

How it gets there matters if you are changing this code:

- A **docked** bar declares no `SafeAreaEdges` anywhere in its chain. The inset is read off the window
  and padded into the surface, and the surface's `HeightRequest` becomes `BarHeight + inset` — extra
  height, not a squeeze. `SafeAreaEdges` does not reach a view laid out inside a chain of `Auto`
  rows; declared there it arrives as zero, which is indistinguishable from a device with no home
  indicator. And any layer that *does* declare `Container` double-counts it once the background
  genuinely reaches the bottom — leaving it on the surface crushed the tab row to 16pt and clipped
  every icon.
- A **floating** bar is the opposite: the bar itself declares `Container` and lifts the whole capsule,
  so the gap underneath it — the thing that makes it read as floating — is not painted over.
- `ShinyTabbedPage`'s root grid is `SafeAreaEdges="None"`. A `Grid` defaults to `Container` and insets
  the area its children are arranged into, so left alone it hands the bar a row that stops short of
  the screen and nothing the bar does can get past that.

## Shell

```xml
<Shell xmlns:shiny="http://shiny.net/maui/controls" ...>

    <Shell.Behaviors>
        <shiny:ShinyTabBarBehavior>
            <shiny:ShinyTabBar IndicatorStyle="Pill">
                <shiny:ShinyTabBar.CenterButton>
                    <shiny:TabCenterButton Icon="plus" />
                </shiny:ShinyTabBar.CenterButton>
            </shiny:ShinyTabBar>
        </shiny:ShinyTabBarBehavior>
    </Shell.Behaviors>

    <TabBar>
        <Tab Title="Home" shiny:ShinyTabs.Icon="home">
            <ShellContent ContentTemplate="{DataTemplate local:HomePage}" Route="home" />
        </Tab>
        <Tab Title="Inbox" shiny:ShinyTabs.Icon="mail" shiny:ShinyTabs.Badge="12">
            <ShellContent ContentTemplate="{DataTemplate local:InboxPage}" Route="inbox" />
        </Tab>
    </TabBar>
</Shell>
```

The behavior hides the platform bar (once, on the Shell — the attached property is inherited), mirrors
the Shell's tabs into the bar, docks the bar over whichever page is showing, and turns a tap back into
a `CurrentItem` change. Everything Shell is good at keeps working.

Rules worth knowing:

- The bar's `Items` are **managed by the behavior** — anything set on them by hand is replaced. Per-tab
  chrome goes on the Shell elements with `ShinyTabs`.
- `Bar` is the behavior's content property, so the bar needs no property element.
- `TabSource` — `Auto` (default: the current `ShellItem`'s sections when there is more than one,
  otherwise the Shell's top-level items — where MAUI's own bar takes them from), `Sections`, `Items`.
- `HideOnPush` (default true) hides the bar on pages pushed onto a tab's stack, as Shell does with its
  own. `ShinyTabs.IsTabBarVisible="False"` on a page hides it for that page.
- `Transition` runs on the **incoming** page only. Shell owns the page swap itself, so by the time
  `Navigated` reports the change there is no outgoing page left to animate.

## ShinyTabBar

The bar on its own, for hosting it somewhere neither host covers.

Chrome: `Items`, `SelectedIndex`/`SelectedItem`, `BarHeight`, `BarBackgroundColor`,
`BarCornerRadius`, `BarMargin`, `BarPadding`, `BarStyle`, `HasShadow`, `SelectedColor`, `UnselectedColor`,
`IndicatorColor`, `IndicatorStyle`, `IndicatorTransition`, `IndicatorEasing`, `LabelMode`,
`IconSize`, `FontSize`, `AnimateIcons`, `AnimationDuration`, `SelectionAnimation`, `Animator`,
`MenuTemplate`, `RespectSafeArea`, `CenterButton`, `PageContext`, `IsMenuOpen`,
`SelectionChangedCommand`.

Events: `SelectionChanged`, `TabReselected`, `CenterClicked` (cancellable — set `Cancel` to take the
press over entirely), `ActionInvoked`, `MenuOpened`, `MenuClosed`. Methods: `GoTo(int)`,
`GoTo(string)`, `OpenMenu()`, `CloseMenu()`.

`IndicatorStyle`: `None`, `Pill` (default, M3), `Line` (top edge), `Underline` (bottom edge), `Dot`.
`IndicatorTransition`: `Slide` (default), `None`. `LabelMode`: `Always` (default), `SelectedOnly`,
`Never`.

`PageContext` is the element the bar reads `ShinyTabs` values off. It is set for you by
`ShinyTabbedPage` and `ShinyTabBarBehavior`; set it yourself only when hosting the bar by hand.

## AutomationIds

Each tab's cell carries `tab-<route>` (falling back to `tab-<title>`, lowercased with spaces
hyphenated), and the centre button carries `tab-center`. A tab with neither a `Route` nor a `Title`
gets none rather than an index-based id that would point at a different tab the moment the order
changed.

```bash
maui devflow ui tap --automationId tab-chat
maui devflow ui tap --automationId tab-center
```

## Gotchas

- **Colours follow the theme when unset.** `SelectedColor` → primary, `UnselectedColor` →
  on-surface-variant, `IndicatorColor` → secondary-container, `BarBackgroundColor` →
  surface-container. Only set them to override.
- **An image icon is never recoloured.** Tinting someone's full-colour PNG to the on-surface token
  would flatten it to a silhouette, so `IconImage` tabs use opacity for the selected state instead.
  Use a motion icon if you want the tab to take the theme's colours.
- **`Badge=""` is a dot, `Badge=null` is nothing.** An empty string is not "no badge".
- The centre button's menu is not a `FloatingPanel` and takes no detents — it is a card anchored above
  the button. For a real sheet, use `FloatingPanel` and open it from `CenterClicked`.
