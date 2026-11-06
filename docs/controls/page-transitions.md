# Page Transitions

[← All Shiny Controls](../../README.md)

Per-page push, pop and modal transitions for `Shell` and `NavigationPage` stacks, shared-element
("hero") flights between pages, animated Shell tab switches, an iOS full-screen swipe-back, the
Android 14 predictive back preview, and an entrance cascade for a page's content.

The transition is set **on the page**. Nothing about navigation changes: `Navigation.PushAsync`,
`PushModalAsync`, `Shell.GoToAsync` and any library built on them (Shiny MAUI Shell included) pick
it up from the page, so no navigation API has to know transitions exist.

> **Both hosts**, core packages. **MAUI**: iOS and Android; on Windows, macOS (AppKit) and Linux (GTK4)
> the properties are accepted and ignored, so the platform's own animation runs. **Blazor**: the browser's
> View Transitions API; see [Blazor](#blazor).

## Setup

```csharp
builder.UseShinyControls(cfg => cfg.UsePageTransitions(
    defaultTransition: PageTransitionType.Default,      // for pages that set nothing; Default = the platform's
    duration: TimeSpan.FromMilliseconds(320),
    shellItemTransition: PageTransitionType.SharedAxisX, // Shell tabs and flyout items
    fullScreenSwipeBack: true,                           // iOS
    predictiveBack: true                                 // Android 14+
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

A page that sets nothing uses `defaultTransition`, and if that is `Default` too, the platform's own
animation runs untouched.

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
set, so the first frame never shows them in place.

## Shell tabs and flyout items

`shellItemTransition` animates switching Shell tabs (with a direction taken from the tab order) and,
on iOS, switching flyout items. MAUI switches Android tabs without animating, so on Android this adds
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

On iOS, `PushModalAsync` with a page that sets a transition presents it over its presenter
(`OverFullScreen`, unless the page sets its own `ModalPresentationStyle`) with that transition, and
dismisses it with the pop. Android modals keep MAUI's slide-up, which is hard-wired in MAUI's modal
manager.

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
- Browsers without view transitions, and users who prefer reduced motion, navigate without animation
  (the entrance cascade also respects reduced motion).
- Gestures, modals and Shell tabs are MAUI concepts with no Blazor counterpart here.

## Platform support

| | iOS | Android |
|---|---|---|
| Shell push / pop | ✅ | ✅ |
| `NavigationPage` push / pop | ✅ | platform default |
| Modals | ✅ | platform default |
| Hero | ✅ | ✅ (Shell) |
| Shell tabs | ✅ | ✅ |
| Shell flyout items | ✅ | platform default |
| Swipe-back / predictive back | ✅ | ✅ (Android 14+) |
| Entrance | every head | every head |

<!-- TODO: capture screenshots for page-transitions -->
