# TabbedPage & TabBar (MAUI)

[← All Shiny Controls](../../README.md)

An improved `TabbedPage`: motion icons in the tabs, per-tab badges, an animated transition between tabs, tab content that is built the first time you reach it, and a raised **centre button** that presents the current page's actions. The same bar drops onto a `Shell` without changing a line of its structure.

> **Blazor note.** `Shiny.Blazor.Controls` has a `ShinyTabBar` component, but it is a *different*, simpler control with its own API (see below) — not the other half of this one.

Nothing in it touches a platform SDK, so it renders on every MAUI head, **including AppKit (`net10.0-macos`) and GTK4**, where MAUI's own `TabbedPage` does not go.

```xml
<shiny:ShinyTabbedPage x:Class="MyApp.MainTabs" Transition="Slide" IndicatorStyle="Pill">

    <shiny:ShinyTabbedPage.CenterButton>
        <shiny:TabCenterButton Icon="plus" Mode="Menu" />
    </shiny:ShinyTabbedPage.CenterButton>

    <shiny:ShinyTabItem Title="Home" Icon="home" Route="home">
        <views:HomeView />
    </shiny:ShinyTabItem>

    <shiny:ShinyTabItem Title="Chat" Icon="message" Route="chat" Badge="3">
        <shiny:ShinyTabItem.ContentTemplate>
            <DataTemplate><views:ChatView /></DataTemplate>
        </shiny:ShinyTabItem.ContentTemplate>
    </shiny:ShinyTabItem>
</shiny:ShinyTabbedPage>
```

Inline `Content` is built with the markup; a `ContentTemplate` is built the first time its tab is selected and then kept, so four tabs behind templates cost one view tree on launch rather than four. The template may inflate a plain `View` **or a whole `ContentPage`**, which is adopted: its `Content` is hosted, its `Title` fills in a tab that has none, and its `BindingContext` is mirrored onto the hosted view.

An adopted page does **not** get `OnAppearing`. MAUI raises page lifecycle from the platform, for the page the platform actually presented — and this one never is, so `SendAppearing()` on it does nothing at all. Implement `ITabAware` on the content, the page, or its view model instead:

```csharp
public class InboxViewModel : ITabAware
{
    public void OnTabAppearing() => this.StartPolling();
    public void OnTabDisappearing() => this.StopPolling();
}
```

`Transition` takes the same `StateTransition` as `StateView` and `Wizard`. `Slide` is the default and is direction-aware — a tab later in the list enters from the right, an earlier one from the left.

**The centre button** is not a tab and never becomes the selection. What it presents belongs to the page rather than to the bar, in the same shape as `ToolbarItems`:

```xml
<ContentPage shiny:ShinyTabs.Badge="{Binding UnreadText}">
    <shiny:ShinyTabs.Actions>
        <shiny:TabActionCollection>
            <shiny:TabAction Text="New message" Icon="edit" Command="{Binding ComposeCommand}" />
            <shiny:TabAction Text="Empty inbox" Icon="trash" IsDestructive="True" Command="{Binding EmptyCommand}" />
        </shiny:TabActionCollection>
    </shiny:ShinyTabs.Actions>
</ContentPage>
```

`ShinyTabs.MenuContent` (or `MenuContentTemplate`) hands the bar a whole view instead, for a menu that is not a list of rows. `Mode="Menu"` falls back to a plain click when neither the page nor the button declares anything, so a centre button that is only ever a button behaves like one.

Both halves are optional and both are template-driven: leave `CenterButton` null for an ordinary bar, `TabCenterButton.ContentTemplate` replaces the circle entirely, and `ShinyTabBar.MenuTemplate` replaces everything inside the popup card while the bar keeps the backdrop, the anchoring and the animation.

**The indicator travels between tabs.** `IndicatorTransition="Slide"` (the default) moves one indicator horizontally from the old tab to the new one, shaped by `IndicatorEasing` and `AnimationDuration`. This is separate from `Transition`, which animates the *content* — they compose. Sliding needs measured geometry, so until the bar has been laid out it falls back to drawing inside the cell, which keeps the first frame correct rather than parking a zero-width indicator in the corner.

**Selection animations.** `SelectionAnimation` gives you `Scale` (default), `Lift`, `Bounce`, `Fade`, `Indicator` or `None`. For anything else, implement `ITabAnimator` and set `ShinyTabBar.Animator` — it is called once per tab whose selected state actually changed, never on a restyle or a badge update, and gets the cell, icon, label and indicator handed over separately:

```csharp
public class SpinAnimator : ITabAnimator
{
    public Task AnimateAsync(TabAnimationContext context)
        => context.Icon?.RotateToAsync(context.IsSelected ? 360 : 0, context.Duration) ?? Task.CompletedTask;
}
```

Too many tabs fold themselves away. `MaxVisibleTabs` is `0` by default, which means the bar works the cap out from its own width and `MinTabWidth` (72) — so six tabs fit on a tablet and become four plus a **More** on a phone, and the split is recomputed on rotation or a window resize. The cap counts the More tab itself, the More tab draws as selected while the tab on screen is one it folded away, and a folded tab's badge moves into the menu row with it. Set `MaxVisibleTabs` to pin the count, or `OverflowTitle`/`OverflowIcon` to relabel the tab. In code: `HasOverflow`, `OverflowItems`, `OpenOverflow()` and `SelectOverflowItem(item)`; the cell's `AutomationId` is `tab-more`.

The bar comes in two styles. `BarStyle` is `Docked` by default — welded to the bottom edge, square corners, background painting down through the home indicator so no strip of page shows beneath it. `BarStyle="Floating"` turns it into a capsule inset from the page edges with the content running the full height of the page underneath it, the way iOS's own tab bar now floats. Floating supplies its own margin (16 either side, 8 below) and a capsule corner radius of half the bar height; set `BarMargin` or `BarCornerRadius` explicitly and yours wins. Floating implies `ContentBehindTabBar`, so pad the bottom of anything scrollable by roughly `BarHeight` or its last row sits under the capsule.

`BarBackgroundOpacity` (`1` by default) makes the bar translucent. It fades the **background only** — icons, labels, badges and the indicator stay fully opaque, because a bar whose tabs fade with it is unreadable long before the background gets interesting. The alpha goes on the colour rather than on a view (opacity multiplies down the tree, so a child cannot undo it), multiplies into any alpha the colour already had, and follows a theme swap. Pair it with `ContentBehindTabBar`, or `BarStyle="Floating"` which implies it — without content running underneath there is nothing behind the glass to see.

Both menus — the centre button's and the overflow tab's — close on any change of selected tab, whether that came from a tap, `GoTo`, or a binding on `SelectedIndex`. A menu belongs to the tab it was opened over. A reselect leaves it alone.

The bar respects the bottom safe area by default (`RespectSafeArea`): docked, the background paints all the way to the bottom of the screen while the tabs sit above the home indicator; floating, the whole capsule is inset instead so the gap under it survives. Docked, that is done by reading the window's inset and padding it into the surface — `SafeAreaEdges` does not reach a view laid out inside a chain of `Auto` rows, and every layer that declares `Container` double-counts it once the background really does reach the edge.

Every tab cell carries an `AutomationId` of `tab-<route>`, with `tab-center` on the centre button and `tab-more` on the overflow tab, so UI tests address tabs by name.

Badges live wherever the count does: on the `ShinyTabItem` or `ShellContent` when it must show on a tab the user has never opened, and on the page when the page computes it. The page's value wins, but only for the tab that page is showing. `Badge=""` draws a dot; `Badge=null` draws nothing.

**In Shell**, add the behavior and keep everything else:

```xml
<Shell.Behaviors>
    <shiny:ShinyTabBarBehavior>
        <shiny:ShinyTabBar IndicatorStyle="Pill" />
    </shiny:ShinyTabBarBehavior>
</Shell.Behaviors>

<TabBar>
    <Tab Title="Home" shiny:ShinyTabs.Icon="home">
        <ShellContent ContentTemplate="{DataTemplate local:HomePage}" Route="home" />
    </Tab>
</TabBar>
```

It hides the platform bar, mirrors the Shell's own tabs into the bar, docks it over whichever page is showing, and turns a tap back into a `CurrentItem` change — so routes, deep links, `ShellContent`'s lazy loading and each tab's navigation stack all keep working. The bar's `Items` are managed by the behavior, which is why per-tab chrome goes on the Shell elements with `ShinyTabs`.
