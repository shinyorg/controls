# ShinyNavigationPage & ShinyNavBar (MAUI only)

**MAUI only**, core `Shiny.Maui.Controls` package. There is **no Blazor equivalent** — never emit
`<ShinyNavigationPage>` in Razor. (Blazor's nearest shape is `ShinyToolbar` inside `AppLayout`; see
toolbar-tabbar.md and layout.md.)

`ShinyNavigationPage` **is** a `NavigationPage`. Everything the stock page does still works and works
the same way — `PushAsync`/`PopAsync`/`PopToRootAsync`/`InsertPageBefore`/`RemovePage`, the modal
stack, page lifecycle, Android's hardware back button, `Pushed`/`Popped`/`PoppedToRoot`. What it adds
is a bar with items on the **left** as well as the right.

```xaml
<shiny:ShinyNavigationPage xmlns:shiny="http://shiny.net/maui/controls">
    <x:Arguments>
        <local:InboxPage />
    </x:Arguments>
</shiny:ShinyNavigationPage>
```

```csharp
// or in code - App.xaml.cs, a modal flow, wherever a NavigationPage went before
var nav = new ShinyNavigationPage(new InboxPage());
```

## Why the bar is drawn rather than native

No platform's native bar has a left slot to give you: it belongs to the back button on iOS, Android
and WinUI alike, and AppKit and GTK4 have no bar at all. So the native bar is **hidden** and
`ShinyNavBar` draws its own — which is also what makes the overflow menu, the badges, the motion
icons and the collapsing large title work identically on every head.

## Items — the whole point

Set `ShinyNav.LeftItems` and/or `ShinyNav.RightItems` on the **page**, not on the navigation page.

```xaml
<ContentPage Title="Inbox"
             xmlns:shiny="http://shiny.net/maui/controls"
             shiny:ShinyNav.Subtitle="12 unread">

    <shiny:ShinyNav.LeftItems>
        <shiny:NavBarItem Icon="menu" Command="{Binding OpenDrawerCommand}" />
    </shiny:ShinyNav.LeftItems>

    <shiny:ShinyNav.RightItems>
        <shiny:NavBarItem Icon="search" Command="{Binding SearchCommand}" />
        <shiny:NavBarItem Icon="bell" Badge="3" Command="{Binding AlertsCommand}" />
        <shiny:NavBarItem Text="Mark all read" Icon="check" Order="Secondary" Command="{Binding MarkAllCommand}" />
        <shiny:NavBarItem IsSeparator="True" Order="Secondary" />
        <shiny:NavBarItem Text="Delete all" Icon="trash" Order="Secondary" IsDestructive="True" Command="{Binding DeleteCommand}" />
    </shiny:ShinyNav.RightItems>
    ...
</ContentPage>
```

`NavBarItem` **derives from `ToolbarItem`**, so `Text`, `IconImageSource`, `Command`,
`CommandParameter`, `IsEnabled`, `IsDestructive`, `Clicked`, `Order` and `Priority` mean exactly what
they already mean. It adds `Icon` (a motion icon name), `IconSource`, `IconPathData`, `Motion`,
`IconColor`, `Badge`, `BadgeColor`, `Display`, `IsVisible`, `IsSeparator` and `Tag`.

Because the collections are typed `ToolbarItem`, a plain `<ToolbarItem>` can be dropped into either
one unchanged — and a page's **own `Page.ToolbarItems` are drawn on the right automatically**, ahead
of anything in `RightItems`. Adopting this page never means rewriting a toolbar.

**Rules that matter:**

- `Order="Secondary"` folds an item into the overflow menu however much room there is.
- Anything past `MaxVisibleItems` (default **3**, per side) folds in behind it. `0` or less means no
  limit, and then only `Secondary` items overflow.
- `Priority` orders items within their side; equal priorities keep declaration order.
- `IsSeparator` draws a divider **in the menu only** — on the bar itself it is skipped.
- `IsVisible="False"` hides an item without removing it, which is what a binding needs.
- `Badge=""` is a **dot**, `Badge=null` is nothing — the same rule as `ShinyTabs.Badge`.
- A **plain `ToolbarItem`** has no `Badge` property of its own — use the attached
  `shiny:ShinyNav.Badge` (and `shiny:ShinyNav.BadgeColor`) on it instead. Both work on a
  `NavBarItem` too, where its own `Badge` wins while it is non-null.
- A badged item that overflows keeps its badge on its menu row, and the overflow button draws a
  **dot** while anything behind it is badged. Nothing to configure.

```xml
<ContentPage.ToolbarItems>
    <ToolbarItem Text="Drafts" shiny:ShinyNav.Badge="{Binding DraftCount}" />
</ContentPage.ToolbarItems>
```

## What MAUI already gives you, honoured as-is

Do **not** invent new properties for these. The bar reads them straight off the page:

| MAUI API | Effect |
| --- | --- |
| `Page.Title` | the bar's title |
| `Page.ToolbarItems` | drawn on the right (badge one with `ShinyNav.Badge`) |
| `NavigationPage.SetHasBackButton(page, false)` | hides the back affordance |
| `NavigationPage.SetBackButtonTitle(page, "Inbox")` | labels it |
| `NavigationPage.SetTitleView(page, view)` | replaces the title outright |
| `NavigationPage.SetTitleIconImageSource(page, src)` | artwork before the title |
| `NavigationPage.SetIconColor(page, color)` | tints the chevron and item artwork |
| `BarBackground` / `BarBackgroundColor` / `BarTextColor` | on the navigation page, as always |

### The one exception: hiding the bar at runtime

`NavigationPage.SetHasNavigationBar(page, false)` **is** honoured — but only as the starting value.
The drawn bar exists because that property was forced to `false` to get the native bar out of the
way, so the page's copy of it is no longer somewhere a later answer can be read from. To toggle at
runtime, use:

```csharp
ShinyNav.SetIsNavBarVisible(page, false);
```

`ShinyNavigationPage.IsNavBarVisible` is the same switch for **every** page at once.

## Large / collapsing title

```xaml
<shiny:ShinyNavigationPage LargeTitleDisplay="Collapsing" />
```

`LargeTitleDisplay` is `Inherit` / `None` / `Always` / `Collapsing`. On the navigation page (and on the
bar) `Inherit` means `None`; on a **page** it means "whatever the navigation page said", which is what
lets one page turn the large title off against a navigation page that turned it on:

```xaml
<ContentPage shiny:ShinyNav.LargeTitleDisplay="None" />
```

`Collapsing` follows the first `ScrollView` or `ItemsView` in the page's content. Name a different one
with `shiny:ShinyNav.ScrollSource="{x:Reference TheList}"` when the page has more than one. Tune it
with `LargeTitleHeight` (52), `LargeTitleCollapseDistance` (48) and `LargeTitleFontSize`. Bind
`ShinyNavBar.CollapseProgress` (0→1) to follow the fold from a view model.

## Per-page overrides (`ShinyNav`)

`Subtitle`, `LargeTitle`, `LargeTitleDisplay`, `ScrollSource`, `IsNavBarVisible`,
`BarBackgroundColor`, `BarTextColor`, `StatusBarStyle`, `TitleAlignment`, `BackButtonIcon`,
`BackButtonCommand`, `BackButtonCommandParameter`, plus `LeftItems`/`RightItems`.

`TitleAlignment` is `Auto` / `Start` / `Center`. On a page, `Auto` means "inherit"; on the navigation
page it means centred on iOS and Mac Catalyst, leading elsewhere. `Center` centres on the **bar**, not
on the gap between the two item groups, so the title stays put as items come and go.

## Intercepting back

```xaml
<ContentPage shiny:ShinyNav.BackButtonCommand="{Binding ConfirmLeaveCommand}" />
```

Nothing is popped for you — call `Navigation.PopAsync()` when you are done. Or handle
`ShinyNavBar.BackButtonPressed` and set `e.Cancel = true`. Without either, the bar pops.

## Appearance (on `ShinyNavigationPage`, applied to every page)

`BarHeight` (56), `BarPadding` (4,0), `HasShadow` (true), `HasSeparator` (false), `ItemSpacing` (2),
`IconSize` (22), `MaxVisibleItems` (3), `OverflowIcon` (unset draws the three-dot glyph),
`MenuTemplate`, `AnimationDuration` (180), `BarIconColor`, `RespectSafeArea` (true),
`StatusBarStyle` (`Auto`), `StatusBarColor`, `TitleFontSize`/`TitleFontFamily`/`TitleFontAttributes`.
Everything unset follows the active theme's tokens.

**`BarIconColor`, not `IconColor`** — `NavigationPage.IconColor` is MAUI's own *attached* property and
is the per-page override; this is only the default for it.

## Status bar & safe area

The bar handles the top inset itself, and it is on by default — nothing to set for the normal case.

- **The background runs to the top of the screen.** `SafeAreaRegions.Container` sits on the view
  *inside* the bar's background — MAUI applies an inset by offsetting the view that carries it, not by
  padding it, so on the bar or on its `Border` the whole background moves down and leaves the strip
  above it in the page's colour. On the content the background stays flush with the top and grows by
  the inset. Everything above it is `SafeAreaEdges="None"`: the bar, the host grid the page's content
  is wrapped in, and the overlay root. All three are `Grid`s and a `Grid` defaults to `Container`, so
  any one left at the default insets first and the bar never reaches the edge. The page's own content
  keeps MAUI's default for its type, so a layout still insets itself out of the home indicator.
- **`RespectSafeArea="False"`** puts the bar under the status bar instead — for a media or camera
  overlay on a full-bleed page.
- **`StatusBarStyle`** is `Auto` by default: the clock and icons are picked from the bar's own
  background by relative luminance (dark bar → white clock). Other values are `LightContent`,
  `DarkContent` and `None` (leave the platform alone); `ShinyNav.StatusBarStyle` overrides it per page,
  with `Inherit` as the "not answered" value.
- **`StatusBarColor`** pins the colour instead of reading the bar's — worth setting when the bar is an
  image or pattern brush, which has no single colour for `Auto` to read. A gradient needs nothing: the
  stop at offset 0 is used, which is the end the status bar sits over.

```xaml
<shiny:ShinyNavigationPage BarBackgroundColor="#3F2B96" BarTextColor="White" />
<!-- nothing else: the status bar takes the colour and its clock goes white -->
```

Platform reality:

| | background behind the clock | clock & icon colour |
|---|---|---|
| iOS / Mac Catalyst | ✅ the bar's, via the safe-area inset | ✅ needs `UIViewControllerBasedStatusBarAppearance` = `false` in `Info.plist` |
| Android (all versions) | ✅ the bar's (15+) / `SetStatusBarColor` (14 and below) | ❌ **does not currently take** — the system re-asserts the theme appearance after our write; ruled out: races, the activity lookup, the AndroidX wrapper |
| Windows / GTK4 / macOS AppKit | no status bar | n/a |

On Android, pick a bar colour that reads against the system's own status bar icons until the
foreground half is fixed.

## Reaching the bar in code

```csharp
var bar = ShinyNavigationPage.GetNavBar(page);   // or nav.CurrentNavBar
bar?.OpenMenu(NavBarSide.Right);
```

`ShinyNavigationPage.NavBarItemInvoked` bubbles a tap on any of its bars.

## Gotchas

- **Pages that are not a `ContentPage` are left entirely alone** — a `TabbedPage` or `FlyoutPage`
  pushed onto the stack keeps its own chrome and gets no drawn bar.
- On a `ShinyContentPage` the wrapper goes around **`PageContent`**, so toasts, dialogs and floating
  panels still float above the bar.
- **iOS swipe-back keeps working.** UIKit disables the edge-swipe pop whenever the bar is hidden, so
  the page puts it back deliberately; `EnableSwipeBackGesture="False"` opts out.
- **macOS AppKit (`net10.0-macos`) caveats.** The bar itself, the items, the badges and the back
  button all render — the wrapper is installed before the page is ever presented, which is what
  avoids the re-parent problem that affects the app-wide Flyout install there. Two things do not:
  the **overflow menu** paints nothing (it is added to a page overlay layer on the tap that opens it,
  the same pre-existing limitation as Toast, Dialogs, in-app Quick Entry and `ShinyTabBar`'s centre
  menu), and a **collapsing large title** leaves a residual band under the bar at the end of the fold
  because that head does not re-measure the row. Prefer `MaxVisibleItems="0"` and
  `LargeTitleDisplay="None"` on AppKit.
- Shell is a different world — it cannot host a `NavigationPage` at all. Present this modally from a
  Shell app, or use Shell's own navigation.
- `ShinyNavBar` can be used standalone at the top of any layout (`LeftItems`, `RightItems`, `Title`,
  `IsBackButtonVisible`), but it owns no navigation — the host does the popping.
