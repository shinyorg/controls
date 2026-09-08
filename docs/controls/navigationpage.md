# NavigationPage with left & right toolbar items (MAUI)

[← All Shiny Controls](../../README.md)

`ShinyNavigationPage` **is** a `NavigationPage` — `PushAsync`, `PopAsync`, `PopToRootAsync`, `InsertPageBefore`, `RemovePage`, the modal stack, page lifecycle, Android's hardware back button and `Pushed`/`Popped`/`PoppedToRoot` all still work, unchanged. What it adds is a bar with items on the **left** as well as the right.

No platform's native bar has a left slot to give you: it belongs to the back button on iOS, Android and WinUI alike, and AppKit and GTK4 have no bar at all. So the native bar is hidden and `ShinyNavBar` draws its own — which is also what makes the overflow menu, the badges, the motion icons and the collapsing large title render identically on **every** MAUI head.

> **MAUI only.** There is no Blazor equivalent; the nearest shape there is `ShinyToolbar` inside `AppLayout`.

```xml
<shiny:ShinyNavigationPage x:Class="MyApp.MainNav" LargeTitleDisplay="Collapsing">
    <x:Arguments>
        <local:InboxPage />
    </x:Arguments>
</shiny:ShinyNavigationPage>
```

The items are declared on the **page**, the way `ToolbarItems` already are:

```xml
<ContentPage Title="Inbox" shiny:ShinyNav.Subtitle="12 unread">

    <shiny:ShinyNav.LeftItems>
        <shiny:NavBarItem Icon="menu" Command="{Binding OpenDrawerCommand}" />
    </shiny:ShinyNav.LeftItems>

    <shiny:ShinyNav.RightItems>
        <shiny:NavBarItem Icon="search" Command="{Binding SearchCommand}" />
        <shiny:NavBarItem Icon="bell" Badge="3" Command="{Binding AlertsCommand}" />
        <shiny:NavBarItem Text="Mark all read" Order="Secondary" Command="{Binding MarkAllCommand}" />
        <shiny:NavBarItem IsSeparator="True" Order="Secondary" />
        <shiny:NavBarItem Text="Delete all" Order="Secondary" IsDestructive="True" Command="{Binding DeleteCommand}" />
    </shiny:ShinyNav.RightItems>
    ...
</ContentPage>
```

`NavBarItem` **derives from `ToolbarItem`**, so `Text`, `IconImageSource`, `Command`, `IsEnabled`, `IsDestructive`, `Clicked`, `Order` and `Priority` mean exactly what they already mean — it just adds motion icons (`Icon`, `IconSource`, `IconPathData`, `Motion`), a `Badge`, `Display`, `IsVisible`, `IsSeparator` and `Tag`. Both collections are typed `ToolbarItem`, and a page's own `Page.ToolbarItems` are drawn on the right **automatically**, so adopting the page never means rewriting a toolbar.

`Order="Secondary"` folds an item into the overflow menu however much room there is; anything past `MaxVisibleItems` (3 per side by default) folds in behind it.

Everything MAUI already gives a `NavigationPage` is honoured rather than reinvented — `Page.Title`, `Page.ToolbarItems`, `SetHasBackButton`, `SetBackButtonTitle`, `SetTitleView`, `SetTitleIconImageSource`, `SetIconColor`, and `BarBackground`/`BarBackgroundColor`/`BarTextColor`. The one exception is `SetHasNavigationBar`: it is honoured as the *starting* value, but that property is the slot this page had to take over to hide the native bar, so the runtime switch is `ShinyNav.SetIsNavBarVisible(page, false)` (or `IsNavBarVisible` on the navigation page, for all of them at once).

`LargeTitleDisplay="Collapsing"` gives the iOS-style oversized title that folds into the bar as the page scrolls — it finds the first `ScrollView` or `ItemsView` in the page on its own, and `ShinyNav.ScrollSource` names a different one. A single page opts out with `shiny:ShinyNav.LargeTitleDisplay="None"`.

## The status bar and the safe area

The bar runs to the top of the screen and takes the inset itself, on by default.

`SafeAreaRegions.Container` sits on the view **inside** the bar's background, not on the background and not on the bar. MAUI applies an inset by offsetting whatever view carries it rather than by padding it, so putting it on the bar or on its `Border` moves the whole thing down and leaves the strip above it painted in the page's colour — which is the exact failure this is here to fix. On the content, the background stays flush with the top of the bar and grows by the inset, so it fills the status bar, notch and Dynamic Island strip while the title and the items stay clear of them. That is also what makes the status bar appear to take the bar's colour — neither iOS nor Android 15 has a status bar background to set; what shows behind the clock is whatever the app paints there. Three layers above it are `SafeAreaEdges="None"` for the same reason — the bar itself, the grid a page's content is wrapped in, and the overlay root every Shiny page grows. All three are `Grid`s, and a `Grid` defaults to `Container`; any one of them left at the default insets first and the bar can never reach the top edge. The page's own content keeps MAUI's default for its type, so a layout still insets itself out of the home indicator without the wrapper reaching into it.

`RespectSafeArea="False"` puts the bar under the status bar instead, for a media or camera overlay on a full-bleed page.

The **foreground** — the clock and the icons — is `StatusBarStyle`, `Auto` by default: it reads the bar's own background by relative luminance and picks white on a dark bar, black on a light one. `LightContent`, `DarkContent` and `None` (leave the platform alone) pin it, and `ShinyNav.StatusBarStyle` overrides it for one page. `StatusBarColor` pins the colour the status bar is told about, which is worth setting for a bar painted with an image or pattern brush — a gradient needs nothing, since the stop at offset 0 is the end the status bar sits over.

```xml
<shiny:ShinyNavigationPage BarBackgroundColor="#3F2B96" BarTextColor="White" />
<!-- nothing else: the status bar takes the colour and its clock goes white -->
```

| | Background behind the clock | Clock & icon colour |
|---|---|---|
| iOS / Mac Catalyst | ✅ the bar's, through the safe-area inset | ✅ needs `UIViewControllerBasedStatusBarAppearance` = `false` in `Info.plist` — UIKit's rule, and the same one MAUI's own `BarTextColor` handling lives under |
| Android 15+ | ✅ the bar's, through the safe-area inset | ❌ **does not currently take** — see below |
| Android 14 and below | ✅ filled with `SetStatusBarColor` | ❌ **does not currently take** — see below |
| Windows, GTK4, macOS AppKit | no status bar | — |

> **Known limitation — the icon colour does not apply on Android.** The style resolves correctly and
> both the AndroidX and the platform write execute, but the system re-asserts the theme's appearance
> afterwards (`adb shell dumpsys window | grep mLastAppearance` still reports `LIGHT_STATUS_BARS`).
> It is not a race, not the activity lookup, and not the AndroidX wrapper — all three were ruled out
> on an API 36 emulator. The **background** half is unaffected and works: what shows behind the clock
> is the bar painting through the top inset. Until this is fixed, pick a bar colour on Android that
> reads against the system's own icon colour.

iOS's edge-swipe-back keeps working: UIKit disables it whenever the bar is hidden, so the page puts it back deliberately (`EnableSwipeBackGesture="False"` opts out).

> **macOS AppKit note.** The bar, its items, badges and back button all render on `net10.0-macos` — the wrapper is installed before the page is presented, which sidesteps the re-parenting problem that affects the app-wide Flyout install there. Two things do not: the overflow menu paints nothing (it is added to a page overlay layer on the tap that opens it — the same pre-existing limitation as Toast, Dialogs and in-app Quick Entry), and a collapsing large title leaves a residual band under the bar at the end of the fold, because that head does not re-measure the row.
