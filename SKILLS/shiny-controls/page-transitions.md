# Page Transitions (both hosts)

Core packages on both hosts, with the same type names. **MAUI**: iOS and Android (other heads accept the
properties and run the platform's own animation). **Blazor**: components over the View Transitions API.
See the Blazor section at the end. Don't emit the MAUI attached properties in Razor, or the Blazor
components in XAML.

Transitions are set **on the page**, never on a navigation call. Do not invent
`PushAsync(page, transition)` or `GoToAsync(route, transition)` overloads; they do not exist. Plain
`Navigation.PushAsync`, `PushModalAsync`, `Shell.GoToAsync` and Shiny MAUI Shell's `INavigator` all
pick the transition up from the page.

## Setup (required for push/pop/modal/hero/tabs/gestures)

```csharp
builder.UseShinyControls(cfg => cfg.UsePageTransitions(
    defaultTransition: PageTransitionType.Default,      // pages that set nothing; Default = platform animation
    duration: TimeSpan.FromMilliseconds(320),
    shellItemTransition: PageTransitionType.SharedAxisX, // Shell tabs + (iOS) flyout items
    fullScreenSwipeBack: false,                          // iOS: swipe back from anywhere
    predictiveBack: true                                 // Android 14+ back preview
));
```

- It registers `ShinyShellRenderer` as the Shell handler on iOS and Android. **An app with its own
  Shell renderer must derive it from `Shiny.Maui.Controls.ShinyShellRenderer`**, not `ShellRenderer`.
  A plain `ShellRenderer` registration silently replaces it, which loses tab/flyout transitions on iOS
  and every Shell transition on Android.
- Android predictive back needs `android:enableOnBackInvokedCallback="true"` on `<application>` in
  `Platforms/Android/AndroidManifest.xml`. Without it, back still works, but there's no preview.

## Page properties (attached, `xmlns:shiny="http://shiny.net/maui/controls"`)

```xml
<ContentPage shiny:PageTransition.Push="Cover"
             shiny:PageTransition.Direction="FromBottom"
             shiny:PageTransition.Pop="Fade"
             shiny:PageTransition.Duration="0:0:0.4">
```

```csharp
PageTransition.SetPush(page, PageTransitionType.SharedAxisX);
PageTransition.SetDirection(page, PageTransitionDirection.FromLeft);
```

- `Pop` left at `Default` runs `Push` in reverse, so set only `Push` for a matched pair.
- `Direction` is the edge the page comes in **from**; the pop leaves toward the same edge. Default:
  `FromRight` (`FromBottom` for `SharedAxisY`).
- Resolution order: the page's own value, then `defaultTransition`, then the platform's animation.

`PageTransitionType`: `Default`, `None`, `Fade`, `FadeThrough`, `Slide`, `Push`, `Cover`, `Reveal`,
`SharedAxisX`, `SharedAxisY`, `SharedAxisZ`, `Zoom`, `CircularReveal` (grows from the last tap and
shrinks back into it), `Flip` (3D), `NativeZoom` (iOS 18 zoom from the hero source for Shell pages;
falls back to `Zoom` with the hero flown elsewhere). There is **no** `SlideUp`; use `Cover` with
`Direction="FromBottom"`.

## Hero (shared element)

Same `PageTransition.Hero` string on a view on each page, and the view flies between them. With no
transition set, the pages crossfade under it.

```xml
<Border shiny:PageTransition.Hero="{Binding Id, StringFormat='card-{0}'}" ... />   <!-- list -->
<Border shiny:PageTransition.Hero="{Binding Id, StringFormat='card-{0}'}" HeightRequest="260" /> <!-- detail -->
```

Hidden views and sources scrolled off screen don't fly.

## Entrance (works on every head, no setup needed)

```xml
<VerticalStackLayout shiny:PageTransition.Entrance="FadeUp"
                     shiny:PageTransition.EntranceStagger="50"
                     shiny:PageTransition.EntranceDelay="120"
                     shiny:PageTransition.EntranceReplay="False">
```

`PageEntranceAnimation`: `None`, `Fade`, `FadeUp`, `SlideIn`, `Scale`. Set it on the **layout** whose
children cascade, not on each child. It plays on first appearance only, unless `EntranceReplay` is set.
Children added while it plays (e.g. `BindableLayout` items loaded right after appearing) animate in too;
children added after it finished appear without animating.

## Platform coverage

| | iOS | Android |
|---|---|---|
| Shell push/pop, hero, tabs | ✅ | ✅ |
| NavigationPage push/pop | ✅ | platform default |
| Modals (`PushModalAsync`) | ✅ (shown `OverFullScreen` unless the page sets a style) | platform default |
| Shell flyout-item switch | ✅ | platform default |
| Swipe-back / predictive back | ✅ | ✅ Android 14+ |

Windows, macOS AppKit and Linux GTK4 ignore the properties, except `Entrance`.

## Blazor

Components, not attached properties. No service registration.

```razor
@* MainLayout.razor - wrap the router's body once *@
<PageTransitionHost DefaultTransition="PageTransitionType.Default" Duration="TimeSpan.FromMilliseconds(320)">
    @Body
</PageTransitionHost>
```

```razor
@* in a page - renders nothing *@
<PageTransition Push="PageTransitionType.Cover" Pop="PageTransitionType.Default"
                Direction="PageTransitionDirection.FromBottom" Duration="TimeSpan.FromMilliseconds(400)" />
```

- Hero = the attribute `data-shiny-hero="card-@item.Id"` on an element on each page (not a component).
  Only elements in view take part, and only the first per name.
- Entrance = `<Entrance Animation="PageEntranceAnimation.FadeUp" Stagger="50" Delay="120">children</Entrance>`.
  It's CSS only, plays on mount, and needs no host.
- Going back is detected by returning to the previous URL (links, `NavigateTo`, browser back/forward),
  so to go back, use `history.back()` or link to the previous page.
- `Default` everywhere = no animation (there's no platform animation on the web). `NativeZoom` = `Zoom`.
- Unsupported browsers and `prefers-reduced-motion` navigate without animation. No gestures, modals or
  tab transitions on Blazor.

