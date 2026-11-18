# Page Transitions

[← All Shiny Controls](../../README.md)

Per-page push, pop and modal transitions for `Shell` and `NavigationPage` stacks, shared-element
("hero") flights between pages, animated Shell tab switches, an iOS full-screen swipe-back, the
Android 14 predictive back preview, and an entrance cascade for a page's content.

The transition is normally set **on the page**. Nothing about navigation changes: `Navigation.PushAsync`,
`PushModalAsync`, `Shell.GoToAsync` and any library built on them (Shiny MAUI Shell included) pick
it up from the page, so no navigation API has to know transitions exist. When one navigation needs
something different, [per-call overloads](#per-call-overloads) take a transition for that call, and
[custom animators](#custom-animators) let you write your own.

> **Both hosts**, core packages. **MAUI**: native on iOS and Android; on Windows, macOS (AppKit),
> Mac Catalyst and Linux (GTK4) the same properties run as managed property animations on the pages,
> with the limits listed under [Platform support](#platform-support). **Blazor**: the browser's View
> Transitions API; see [Blazor](#blazor).

## Setup

```csharp
builder.UseShinyControls(cfg => cfg.UsePageTransitions(
    defaultTransition: PageTransitionType.Default,      // for pages that set nothing; Default = the platform's
    duration: TimeSpan.FromMilliseconds(320),
    shellItemTransition: PageTransitionType.SharedAxisX, // Shell tabs and flyout items
    fullScreenSwipeBack: true,                           // iOS
    predictiveBack: true,                                // Android 14+
    modalTransition: PageTransitionType.Default,         // modal pages that set nothing; Default = defaultTransition
    reducedMotion: PageTransitionReducedMotion.Crossfade,// Crossfade | None | Ignore
    defaultAnimator: null                                // a custom IPageTransitionAnimator for pages that set nothing
));
```

`UsePageTransitions` registers `ShinyShellRenderer` as the Shell handler on iOS and Android. **If your
app registers its own Shell renderer, derive it from `ShinyShellRenderer`** instead of `ShellRenderer`.
A plain `ShellRenderer` registration replaces it, and tab and flyout-item transitions (and on Android,
all Shell transitions) stop.

```csharp
public class MyShellRenderer : Shiny.Maui.Controls.ShinyShellRenderer { ... }
```

## On the page

```xml
<ContentPage xmlns:shiny="http://shiny.net/maui/controls"
             shiny:PageTransition.Push="Cover"
             shiny:PageTransition.Direction="FromBottom">
```

| Property | Meaning |
|---|---|
| `PageTransition.Push` | How the page arrives. |
| `PageTransition.Pop` | How it leaves. Left at `Default`, the push runs in reverse, so setting only `Push` gives a matched pair. |
| `PageTransition.Direction` | The edge it comes in from (`FromRight`, `FromLeft`, `FromBottom`, `FromTop`). A pop leaves toward the same edge. |
| `PageTransition.Duration` | Overrides the app-wide duration for this page. |
| `PageTransition.Modal` | How the page is presented and dismissed when it is shown with `PushModalAsync`, in place of Push/Pop. Left at `Default`, a modal uses Push (and Pop). |
| `PageTransition.Animator` | A custom [`IPageTransitionAnimator`](#custom-animators) for this page's push, pop and modal. It wins over the page's types. |

A page that sets nothing uses `defaultAnimator`, then `defaultTransition` (`modalTransition` first
for a modal), and if those are unset too, the platform's own animation runs untouched.

### Resolution order

For the page that moves (the incoming page on a push, the outgoing one on a pop), highest first:

1. A one-shot transition from a [per-call overload](#per-call-overloads).
2. The page's `Animator`.
3. The page's own type: `Modal` for a modal, then `Pop` for a pop, then `Push`.
4. The app's `defaultAnimator`.
5. `modalTransition` for a modal, then `defaultTransition`.
6. The platform's own animation (or a crossfade when the pages share a [hero](#hero-shared-element)).

Then [reduced motion](#reduced-motion) applies. `PageTransition.ResolveTransition(from, to, isPop, isModal)`
returns what a navigation will run, if you want to check it.

## Transitions

| Type | What it looks like |
|---|---|
| `Default` | Not set at this level. |
| `None` | No animation. |
| `Fade` | The incoming page fades in over the outgoing one. |
| `FadeThrough` | Material fade through: out, then in with a slight grow. For unrelated screens. |
| `Slide` | The platform-style push, from any edge, with the outgoing page drifting a little the other way. |
| `Push` | Both pages move together, edge to edge. |
| `Cover` | The incoming page slides over a page that stays put. |
| `Reveal` | The outgoing page slides away and uncovers the incoming one. |
| `SharedAxisX` / `SharedAxisY` | Material shared axis: both pages shift a little while crossfading. For steps in a flow. |
| `SharedAxisZ` | Material shared axis on Z: grow in, grow past and fade. For parent/child. |
| `Zoom` | Grows in from slightly smaller while fading in. |
| `CircularReveal` | Grows out of a circle from the point last tapped, and shrinks back into it on pop. |
| `Flip` | Turns over in 3D around the vertical axis (horizontal axis for `FromTop`/`FromBottom`). |
| `NativeZoom` | iOS 18's own zoom transition out of the page's hero source, with interactive swipe and pinch to dismiss. Shell pages on iOS 18+. Elsewhere (a `NavigationPage`, a modal, Android, older iOS) it falls back to `Zoom`, with the hero flown. |

## Per-call overloads

Extension methods on `INavigation` and `Shell` carry a transition for one navigation, ahead of anything
the page or app sets. `INavigation` and `Shell` themselves are unchanged; the overloads hand the
transition over as a one-shot and then call the ordinary method, so events, lifecycle and the returned
task are MAUI's own.

```csharp
await Navigation.PushAsync(new DetailPage(), PageTransitionType.SharedAxisZ);
await Navigation.PushAsync(new SheetPage(), PageTransitionType.Cover, PageTransitionDirection.FromBottom, TimeSpan.FromMilliseconds(400));
await Navigation.PopAsync(PageTransitionType.Fade);
await Navigation.PushModalAsync(new EditorPage(), PageTransitionType.Zoom);
await Navigation.PopModalAsync(PageTransitionType.Fade);
await Shell.Current.GoToAsync("details", PageTransitionType.FadeThrough);
await Shell.Current.GoToAsync("details", PageTransitionType.Cover, parameters: new Dictionary<string, object> { ["id"] = 4 });

// Each also takes a custom animator.
await Navigation.PushAsync(new DetailPage(), new SwingAnimator());
```

Each takes `direction`, `duration` and `animated` (`animated: false`, or `PageTransitionType.None`,
navigates with no animation). A `GoToAsync` override applies to every page that call pushes or pops.

The overloads reach a few places the page properties can't: Android `NavigationPage` and modals, and
Mac Catalyst, where they ask the head not to animate and run the transition as a managed animation.
`PopModalAsync(type)` on a managed head plays the exit on the live modal before dismissing it, so a
modal dismiss is a full two-page transition there.

## Custom animators

> **Write it as Keyframes:** `KeyframePageTransition` in `Shiny.Maui.Controls.Keyframe` is an
> `IPageTransitionAnimator` whose `Enter`, `Exit`, `PopEnter` and `PopExit` are Keyframes. Without the
> pop parts, a pop plays the push backwards. See [Keyframe](keyframe.md#page-transitions).


Implement `IPageTransitionAnimator` and set it per call, on a page (`PageTransition.Animator`) or for
the app (`defaultAnimator:`).

```csharp
public interface IPageTransitionAnimator
{
    Task AnimateAsync(PageTransitionContext context, CancellationToken cancellationToken);
}

public sealed record PageTransitionContext(Page? From, Page? To, bool IsPop, bool IsModal, TimeSpan Duration, Size Bounds)
{
    public PageTransitionDirection Direction { get; init; }
    public VisualElement? FromSurface { get; }   // what to animate for From on this head
    public VisualElement? ToSurface { get; }     // what to animate for To on this head
}
```

```csharp
public sealed class SwingAnimator : IPageTransitionAnimator
{
    public async Task AnimateAsync(PageTransitionContext ctx, CancellationToken ct)
    {
        var length = (uint)ctx.Duration.TotalMilliseconds;
        if (!ctx.IsPop && ctx.ToSurface is { } incoming)
        {
            incoming.Opacity = 0;
            incoming.Rotation = -6;
            incoming.Scale = 0.86;
            await Task.WhenAll(
                incoming.FadeToAsync(1, length / 2),
                incoming.RotateToAsync(0, length, Easing.SpringOut),
                incoming.ScaleToAsync(1, length, Easing.SpringOut),
                ctx.FromSurface?.FadeToAsync(0.4, length) ?? Task.CompletedTask
            );
        }
        // ... the pop
    }
}
```

What the host guarantees:

- Both pages are in the tree and laid out when `AnimateAsync` runs. The incoming page is stacked above
  the outgoing one on a push and below it on a pop.
- The navigation stays open until the task completes (on Android, for at most 30 seconds).
- Afterwards it resets `TranslationX/Y`, `Scale`, `ScaleX/Y`, `Rotation`, `RotationX/Y`, `Opacity` and
  `AnchorX/Y` on both pages and both surfaces, so leave them wherever the animation ends. An exception
  thrown by the animator is logged and the navigation still completes.
- The token is cancelled when the navigation is torn down: a second navigation, or a back gesture.
- **Animate `FromSurface` / `ToSurface`, not the pages.** A page's own transform moves its native view
  on iOS, Android, Windows and macOS (AppKit), but not on Linux (GTK4), where pages sit in a `Gtk.Stack`
  that ignores it; there the surface is the page's `Content` (the page background stays put).
- `From` is `null` on the managed heads whenever the head has already taken the outgoing page off screen,
  which is every push and pop except a modal shown over a presenter that stays visible. Handle it.
- An interactive back gesture (iOS swipe-back, Android predictive back) never runs a custom animator; it
  uses the page's `Push`/`Pop` type, or the platform's swipe.
- Under reduced motion (`Crossfade` or `None`), custom animators are replaced too.

To build on a built-in, `await PageTransition.AnimateAsync(context, PageTransitionType.Fade, ct)` runs
any built-in type as property animations on the context's surfaces.

## Reduced motion

When the OS asks for less motion, page transitions follow `reducedMotion:`:

| Value | Effect |
|---|---|
| `Crossfade` (default) | Every transition set by a page, the app or a call (custom animators included) becomes a crossfade of at most 200 ms, with no hero flights. |
| `None` | They become `None`. |
| `Ignore` | They run as configured. |

Pages left on the platform's own animation are left to the platform's own reduced-motion handling. The
OS setting is read on iOS (Reduce Motion), Android ("Remove animations") and Windows (animation effects
off). The macOS, Mac Catalyst and Linux heads use the core package's `net11.0` build, which can't read
the setting, so transitions run as configured there.

## Hero (shared element)

Give a view on one page and a view on the next the same `PageTransition.Hero` name. When either page
is pushed or popped, the view flies from where it is to where it lands, resizing on the way, while the
rest of the page transitions. If neither page sets a transition, the pages crossfade, so the hero
has something to fly over.

```xml
<!-- list page -->
<Image Source="{Binding Photo}" shiny:PageTransition.Hero="{Binding Id, StringFormat='photo-{0}'}" />

<!-- detail page -->
<Image Source="{Binding Photo}" HeightRequest="260" shiny:PageTransition.Hero="{Binding Id, StringFormat='photo-{0}'}" />
```

Only views that are showing take part. A source that has been scrolled off screen does not fly.

## Entrance

Set on a layout: its children animate in one after another when the page appears. This is plain MAUI
animation, so it works on every head and needs no `UsePageTransitions`.

```xml
<VerticalStackLayout shiny:PageTransition.Entrance="FadeUp"
                     shiny:PageTransition.EntranceStagger="50"
                     shiny:PageTransition.EntranceDelay="120">
```

`Fade`, `FadeUp`, `SlideIn` and `Scale`. It plays the first time the page appears; set
`EntranceReplay="True"` to play every time. The children are hidden from the moment the property is
set, so the first frame never shows them in place. A child added while the cascade is playing, such as
`BindableLayout` items arriving from a network call just after the page appears, animates in too, after
`EntranceDelay`; a child added after the cascade has finished just appears.

## Shell tabs and flyout items

`shellItemTransition` animates switching Shell tabs (with a direction taken from the tab order) and,
on iOS, switching flyout items. `PageTransition.Tab` overrides it per tab or item, on a `ShellContent`,
`Tab` (`ShellSection`) or `ShellItem`/`FlyoutItem`; the most specific one set wins (content, then tab,
then item):

```xml
<TabBar>
    <Tab Title="Feed" shiny:PageTransition.Tab="SharedAxisX"> ... </Tab>
    <Tab Title="Camera" shiny:PageTransition.Tab="Fade"> ... </Tab>
</TabBar>
<FlyoutItem Title="Settings" shiny:PageTransition.Tab="FadeThrough"> ... </FlyoutItem>
```

Tab and flyout-item transitions are iOS and Android only. MAUI switches Android tabs without animating, so on Android this adds
an animation where there was none. On iOS 26+, the outgoing item is faded from a snapshot, never the
incoming one: fading a view that holds a Liquid Glass tab bar from zero leaves its icons invisible.

## Gestures

- **iOS swipe-back** follows the finger with the page's own pop transition and lets go either way.
  `fullScreenSwipeBack` lets it start anywhere on the page, not only the left edge. It obeys the same
  rules as the edge swipe (MAUI's back-button behaviour, the root page), and never starts on top of
  something that scrolls sideways.
- **Android 14 predictive back** shows the page underneath while the back gesture is held: the page
  shrinks and drifts with the finger, release pops it, cancel puts it back. Pages that refuse a pop
  (`OnBackButtonPressed`) and open overlays still get the back press as usual. It needs this on the
  application element in `AndroidManifest.xml`:

```xml
<application android:enableOnBackInvokedCallback="true" ... >
```

## Modals

A modal uses the page's `PageTransition.Modal`, else its Push (and Pop for the dismiss), else
`modalTransition`, else `defaultTransition`. So a page can push with `Slide` and present modally with
`Cover` from the bottom:

```xml
<ContentPage shiny:PageTransition.Push="Slide"
             shiny:PageTransition.Modal="Cover"
             shiny:PageTransition.Direction="FromBottom">
```

On iOS, a modal page with a transition is presented over its presenter (`OverFullScreen`, unless the
page sets its own `ModalPresentationStyle`) with that transition, and dismissed with it in reverse. On
Windows the modal's transition runs as a managed animation; Windows only keeps the presenter on screen
under a modal that sets a background (it treats the modal as possibly see-through), so give the modal a
`BackgroundColor` for a Fade or Cover over the page underneath. Android modals keep MAUI's slide-up,
which is hard-wired in MAUI's modal manager, unless you use the per-call `PushModalAsync(page, type)` /
`PopModalAsync(type)`, which run the transition as a managed animation over the presenter. macOS and
Linux modals are native sheets, windows and dialogs and keep their own presentation.

## Managed heads (Windows, macOS, Mac Catalyst, Linux)

These heads have no navigation hook to animate both pages natively, so the transition runs as MAUI
property animations (`TranslationX/Y`, `Scale`, `Opacity`, `RotationX/Y`, `Clip`) on the pages, from
the same XAML. What that means in practice:

- **Only the incoming page animates.** These heads take the outgoing page off screen as the new one
  goes on, so a transition plays its incoming half over the full duration. Where a type moves only the
  outgoing page (a `Cover` or `Fade` pop, a `Reveal` push), the incoming page fades in instead. The
  exceptions are a modal over a presenter that stays visible (Windows, with a modal background) and the
  per-call `PopModalAsync(type)`, which run both pages.
- The incoming page is put in the transition's starting state before the head first draws it, then
  animated once the head has shown it.
- **Windows**: WinUI's own NavigationPage and Shell slide is switched off while a transition runs.
  NavigationPage, Shell and modals are covered.
- **macOS (AppKit)**: NavigationPage and Shell are covered. The head doesn't say whether a push was
  animated, so `PushAsync(page, false)` still runs the page's transition; use the per-call
  `PushAsync(page, PageTransitionType.None)` instead. Modals are native sheets and keep their own
  presentation.
- **Mac Catalyst**: the core package has no Catalyst build, so Catalyst runs on the managed path, but
  NavigationPage, Shell and modals are UIKit underneath and run UIKit's own animation, which core can't
  switch off from shared code. Page properties are therefore ignored there; the per-call overloads and
  custom animators work (they ask UIKit not to animate and run the transition managed).
- **Linux (GTK4)**: Shell is covered. A `NavigationPage` is a `Gtk.Stack` that always runs its own slide,
  so NavigationPage transitions keep it, and modals are dialogs. A page's transform is ignored by GTK,
  so the page's `Content` is animated instead (the page background stays put), `Flip` runs as
  `FadeThrough` and `CircularReveal` as `Zoom`.
- `CircularReveal` grows from the page centre (these heads don't record the last touch). `Flip` has no
  perspective on macOS.
- **Heroes** fly: the hero on the incoming page starts where its partner was on the outgoing page and
  moves and resizes into place, inside the page's own transition.
- Shell tab and flyout-item transitions, swipe-back and predictive back are iOS/Android only.

## Blazor

The same names on the Blazor side, in the core `Shiny.Blazor.Controls` package, on the browser's
[View Transitions API](https://developer.mozilla.org/docs/Web/API/View_Transition_API). There's no
service to register. Wrap the router's `@Body` in the layout:

```razor
@* MainLayout.razor *@
<PageTransitionHost DefaultTransition="PageTransitionType.Default"
                    Duration="TimeSpan.FromMilliseconds(320)">
    @Body
</PageTransitionHost>
```

Then each page says how it arrives:

```razor
@page "/details/{Id:int}"

<PageTransition Push="PageTransitionType.Cover" Direction="PageTransitionDirection.FromBottom" />
```

- Links, `NavigateTo` and the browser's back and forward buttons all animate. Returning to the page
  before counts as going back, so it runs the transition in reverse.
- Only the content inside the host animates. The app bar and nav panel around it stay put.
- `Default` all the way down means no animation, because a browser has no platform animation to keep.
  `NativeZoom` is `Zoom`.
- **Hero**: put `data-shiny-hero="name"` on an element on each page. Only elements in view take part,
  and only the first element with a given name on a page.
- **Entrance**: `<Entrance Animation="PageEntranceAnimation.FadeUp" Stagger="50" Delay="120">` wraps
  the children that cascade in. It's plain CSS, plays whenever it mounts, and needs no host.
- **One navigation, one transition:** `Nav.NavigateTo(uri, PageTransitionType.Flip)`, a custom
  transition's name (`Nav.NavigateTo(uri, "drop")`) or a `PageTransitionOverride` sets the transition
  for that navigation only, overriding the pages' own settings. Going back from the page it opened
  reverses it.
- **Custom transitions from Keyframes** (with `Shiny.Blazor.Controls.Keyframe`): a
  `<CustomPageTransition Name="drop">` inside the host, with `Enter`, `Exit` and optional `PopEnter`
  and `PopExit` Keyframes, is compiled to view-transition CSS. A page picks it with
  `<PageTransition Custom="drop" />`; without the pop parts, going back plays Exit and Enter
  backwards. It runs for the host's (or page's) `Duration`. Without the Keyframe package,
  `PageTransitionHost.RegisterCustomTransition` takes raw `@keyframes`.
- **Reduced motion:** `PageTransitionHost.ReducedMotion` is `Instant` (the default: no animation),
  `Crossfade` (a short fade, no movement or hero flights) or `Play`.
- Browsers without view transitions navigate without animation; users who prefer reduced motion get
  `PageTransitionHost.ReducedMotion` (the entrance cascade also respects reduced motion).
- Gestures, modals and Shell tabs are MAUI concepts with no Blazor counterpart here.

## Platform support

"Managed" means a property animation on the incoming page (see [Managed heads](#managed-heads-windows-macos-mac-catalyst-linux));
"per call" means only through the [per-call overloads](#per-call-overloads).

| | iOS | Android | Windows | macOS (AppKit) | Mac Catalyst | Linux (GTK4) |
|---|---|---|---|---|---|---|
| Shell push / pop | ✅ | ✅ | managed | managed | per call (managed) | managed |
| `NavigationPage` push / pop | ✅ | per call (managed) | managed | managed | per call (managed) | Gtk.Stack slide |
| Modals | ✅ | per call (managed, both pages) | managed | native sheet | per call (managed) | native dialog |
| Custom animator | ✅ both pages | ✅ Shell, both pages; per call elsewhere | managed (`From` null) | managed (`From` null) | per call (`From` null) | Shell (`From` null) |
| Per-call overloads | ✅ | ✅ | ✅ | ✅ (not modals) | ✅ | Shell only |
| Hero | ✅ | ✅ (Shell) | managed | managed | per call | managed |
| Shell tabs (`ShellItemTransition`, `Tab`) | ✅ | ✅ | platform default | platform default | platform default | platform default |
| Shell flyout items | ✅ | platform default | platform default | platform default | platform default | platform default |
| Swipe-back / predictive back | ✅ | ✅ (Android 14+) | — | — | — | — |
| Reduced motion (OS setting) | ✅ | ✅ | ✅ | not read | not read | not read |
| Entrance | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |

<!-- TODO: capture screenshots for page-transitions -->
