# Page Transitions (both hosts)

Core packages on both hosts, with the same type names. **MAUI**: native on iOS and Android; managed
property animations on Windows, macOS (AppKit), Mac Catalyst and Linux (GTK4) (see Platform coverage).
**Blazor**: components over the View Transitions API. See the Blazor section at the end. Don't emit the
MAUI attached properties in Razor, or the Blazor components in XAML.

Prefer setting the transition **on the page**: plain `Navigation.PushAsync`, `PushModalAsync`,
`Shell.GoToAsync` and Shiny MAUI Shell's `INavigator` all pick it up from the page. For a one-off, the
per-call overloads below exist (MAUI only). There is no `StoryboardTransition` or `SharedTransition`
type; use `IPageTransitionAnimator` and `PageTransition.Hero`.

## Setup (required for push/pop/modal/hero/tabs/gestures)

```csharp
builder.UseShinyControls(cfg => cfg.UsePageTransitions(
    defaultTransition: PageTransitionType.Default,      // pages that set nothing; Default = platform animation
    duration: TimeSpan.FromMilliseconds(320),
    shellItemTransition: PageTransitionType.SharedAxisX, // Shell tabs + (iOS) flyout items
    fullScreenSwipeBack: false,                          // iOS: swipe back from anywhere
    predictiveBack: true,                                // Android 14+ back preview
    modalTransition: PageTransitionType.Default,         // modal pages that set nothing
    reducedMotion: PageTransitionReducedMotion.Crossfade,// Crossfade | None | Ignore
    defaultAnimator: null                                // IPageTransitionAnimator for pages that set nothing
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
- `shiny:PageTransition.Modal="Cover"` is used for `PushModalAsync`/`PopModalAsync` instead of Push/Pop
  (unset, a modal uses Push and Pop).
- `PageTransition.SetAnimator(page, new MyAnimator())` sets a custom animator (code; it's an object).
- Resolution order: per-call overload > page `Animator` > page type (`Modal` for a modal, `Pop` for a
  pop, then `Push`) > `defaultAnimator` > `modalTransition` (modals) > `defaultTransition` > platform.
  Then reduced motion: anything set becomes a ≤200 ms crossfade (or `None`), custom animators included.

## Per-call overloads (MAUI, extension methods, `using Shiny.Maui.Controls;`)

```csharp
await Navigation.PushAsync(page, PageTransitionType.SharedAxisZ);
await Navigation.PushAsync(page, PageTransitionType.Cover, PageTransitionDirection.FromBottom, TimeSpan.FromMilliseconds(400));
await Navigation.PopAsync(PageTransitionType.Fade);
await Navigation.PushModalAsync(page, PageTransitionType.Zoom);
await Navigation.PopModalAsync(PageTransitionType.Fade);
await Shell.Current.GoToAsync("details", PageTransitionType.FadeThrough, parameters: new Dictionary<string, object> { ["id"] = 4 });
await Navigation.PushAsync(page, new SwingAnimator());   // every overload also takes an IPageTransitionAnimator
```

Signature shape: `(page?, type, direction = Default, TimeSpan? duration = null, bool animated = true)`;
`GoToAsync` adds `IDictionary<string, object>? parameters = null` last. `animated: false` or `None` = no
animation. They beat anything the page sets. They are the only way to animate Android
`NavigationPage`/modal navigations and Mac Catalyst navigations.

## Custom animator

Prefer Keyframes over a hand-written animator when the motion is declarative — `KeyframePageTransition`
(`Shiny.Maui.Controls.Keyframe`) is an `IPageTransitionAnimator`:

```xml
<kf:KeyframePageTransition x:Key="Drop">
  <kf:KeyframePageTransition.Enter>
    <kf:Keyframes Duration="0:0:0.5">
      <kf:Track Property="TranslationY"><kf:Key Offset="0" Value="-700" Easing="spring(0.55, 11)" /><kf:Key Offset="1" Value="0" /></kf:Track>
    </kf:Keyframes>
  </kf:KeyframePageTransition.Enter>
  <kf:KeyframePageTransition.Exit> … </kf:KeyframePageTransition.Exit>
</kf:KeyframePageTransition>
```

`Enter`/`Exit` play on the incoming/outgoing page; `PopEnter`/`PopExit` are optional (without them a pop
plays the push backwards). Use it via `shiny:PageTransition.Animator="{StaticResource Drop}"`, the
`DefaultAnimator`, or `Navigation.PushAsync(page, drop)`. Hand-written:

```csharp
public sealed class SwingAnimator : IPageTransitionAnimator
{
    public async Task AnimateAsync(PageTransitionContext ctx, CancellationToken ct)
    {
        var ms = (uint)ctx.Duration.TotalMilliseconds;
        if (!ctx.IsPop && ctx.ToSurface is { } incoming)
        {
            incoming.Opacity = 0;
            incoming.Scale = 0.86;
            await Task.WhenAll(
                incoming.FadeToAsync(1, ms / 2),
                incoming.ScaleToAsync(1, ms, Easing.SpringOut),
                ctx.FromSurface?.FadeToAsync(0.4, ms) ?? Task.CompletedTask);
        }
        else if (ctx.IsPop && ctx.FromSurface is { } outgoing)
        {
            await outgoing.FadeToAsync(0, ms);
        }
    }
}
```

`PageTransitionContext(Page? From, Page? To, bool IsPop, bool IsModal, TimeSpan Duration, Size Bounds)`
plus `Direction`, `FromSurface`, `ToSurface`. Rules for generated animators:

- Animate `FromSurface`/`ToSurface`, not `From`/`To` (on Linux GTK the surface is the page's Content).
- Null-check both: `From` is null on Windows/macOS/Catalyst/Linux whenever the outgoing page is already off screen.
- Don't reset anything at the end; the host resets TranslationX/Y, Scale(X/Y), Rotation(X/Y), Opacity, AnchorX/Y.
- Honour `ct` (stop animations; ending with `OperationCanceledException` is fine).
- `await PageTransition.AnimateAsync(ctx, PageTransitionType.Fade, ct)` runs a built-in inside your own.
- Interactive back gestures never run it (they use the page's Push/Pop type).

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

## Shell tabs

`shellItemTransition` for all tabs/flyout items; per tab or item `shiny:PageTransition.Tab="Fade"` on a
`ShellContent`, `Tab` or `ShellItem`/`FlyoutItem` (most specific wins). iOS and Android only.

## Platform coverage

| | iOS | Android | Windows | macOS AppKit | Mac Catalyst | Linux GTK4 |
|---|---|---|---|---|---|---|
| Shell push/pop | ✅ | ✅ | managed | managed | per call | managed |
| NavigationPage push/pop | ✅ | per call | managed | managed | per call | Gtk.Stack's own slide |
| Modals | ✅ (`OverFullScreen` unless the page sets a style) | per call | managed | native sheet | per call | native dialog |
| Custom animator | ✅ | ✅ Shell; per call elsewhere | managed | managed | per call | Shell |
| Hero | ✅ | ✅ Shell | managed | managed | per call | managed |
| Shell tabs / flyout items | ✅ | ✅ tabs | — | — | — | — |
| Swipe-back / predictive back | ✅ | ✅ 14+ | — | — | — | — |

"Managed" = property animations on the **incoming page only** (the head removes the outgoing page);
a type that moves only the outgoing page (Cover pop, Reveal push) fades the incoming page in. On
Windows a modal over a page keeps the presenter visible only if the modal sets a `BackgroundColor`.
`Entrance` works on every head.

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
- One-off: `Nav.NavigateTo(uri, PageTransitionType.Flip)` or `Nav.NavigateTo(uri, "drop")` (a custom
  transition's name) overrides the pages for that navigation; going back reverses it.
- Custom from Keyframes (needs `Shiny.Blazor.Controls.Keyframe`): `<CustomPageTransition Name="drop">`
  with `<Enter>`, `<Exit>`, optional `<PopEnter>`/`<PopExit>` Keyframes, placed **inside the host in the
  layout** (one on a page is unregistered before the transition runs). Pages pick it with
  `<PageTransition Custom="drop" />`. It runs for the host's/page's `Duration`.
- Reduced motion: `PageTransitionHost.ReducedMotion` = `Instant` (default, no animation) | `Crossfade` | `Play`.
- Unsupported browsers navigate without animation. No gestures, modals or tab transitions on Blazor.

