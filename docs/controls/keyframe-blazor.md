# Keyframe Animation — Blazor

[← All Shiny Controls](../../README.md)

`Shiny.Blazor.Controls.Keyframe` brings the Keyframe model to Blazor on the browser's **Web Animations API**. It uses the same `Keyframes` / `Track` / `Key` components, the same MAUI property names, the same timing (`Shiny.Controls.Keyframe.Shared`) and the same easing text. On top of that it adds a seekable player, choreography with stagger, animated visual-state transitions, implicit property transitions, list item transitions with FLIP moves, and page transitions built from keyframes. Reduced motion is honoured by default everywhere.

```bash
dotnet add package Shiny.Blazor.Controls.Keyframe
```

```razor
@using Shiny.Blazor.Controls.Keyframe
@using Shiny.Controls.Keyframe
```

There is nothing to register.

```razor
<Keyframes Duration="TimeSpan.FromSeconds(1.2)" Iterations="Keyframes.Infinite"
           Direction="PlaybackDirection.Alternate" Fill="FillMode.Both">
    <Track Property="Scale">
        <Key Offset="0" Value="1" />
        <Key Offset="0.5" Value="1.15" Easing="CubicOut" />
        <Key Offset="1" Value="1" />
    </Track>
    <Track Property="BackgroundColor">
        <Key Offset="0" Value="#2563EB" />
        <Key Offset="1" Value="#EC4899" />
    </Track>
    <div class="card">Pulse</div>
</Keyframes>
```

`Keyframes` wraps its content in one element and animates that element. To animate a different element instead, use `Target="@elementRef"` or `TargetId="id"`, and no wrapper is rendered. Tracks render nothing.

## Keyframes

| Parameter | Notes |
|---|---|
| `Duration` | One iteration (default 300ms). |
| `Delay`, `EndDelay` | Before the first iteration and after the last. A negative `Delay` starts partway in. |
| `IterationDelay` | A pause between iterations that holds the pose the last iteration ended on. The Web Animations API has no iteration delay, so it is baked into the keyframes (see Notes). |
| `Iterations` | A number, fractions allowed, or `Keyframes.Infinite`. |
| `IterationStart` | Where in the first iteration to start (0.5 = halfway). |
| `Direction` | `Normal`, `Reverse`, `Alternate`, `AlternateReverse`. |
| `Fill` | `None`, `Forwards`, `Backwards`, `Both`. |
| `Easing` | Applies across the whole iteration, on top of each key's own curve. |
| `AutoPlay` | Default true. |
| `Playing` | `bool?`. Setting it to true plays from the start; false stops. This is the Blazor counterpart of MAUI's trigger actions. |
| `Speed` | The playback rate. A negative value plays backwards, and changing it mid-flight reverses in place. |
| `ReducedMotion` | `Auto` (default), `Play` or `Instant`. |
| `RestoreOnStop` | When true, `Stop()` puts the element back the way it was. Otherwise the values hold where they stopped. |
| `Finished`, `StateChanged` | Callbacks. |

`Track`: `Property` and an optional `TargetId`, which is MAUI's `TargetName`. `Key`: `Offset` (0..1), `Value` and `Easing`. As in CSS, a key's easing shapes the segment that *starts* at that key. If you leave out `Value`, the key takes the element's live value when playback starts, so replaying mid-flight carries on from where the element is instead of snapping.

You can also write keyframes in the spec style, and mix the two freely:

```razor
<KeyFrame Cue="60%" Easing="CubicOut">
    <Setter Property="Opacity" Value="1" />
</KeyFrame>
```

`Cue` accepts `"60%"`, `"0.6"`, `"from"` or `"to"`.

### Properties

| Name | CSS |
|---|---|
| `Opacity` | `opacity` |
| `Scale`, `ScaleX`, `ScaleY` | `scale`. Scale multiplies with ScaleX and ScaleY, as on MAUI. |
| `TranslationX`, `TranslationY` | `translate`. Values are px unless a unit is given; `%` works. |
| `Rotation` | `rotate`, taking the shortest arc (350 → 10 turns forward 20°). |
| `Spin` | `rotate`, turning as far as the keys say. |
| `RotationX`, `RotationY` | `transform: perspective(800px) rotateX() rotateY()` |
| `AnchorX`, `AnchorY` | `transform-origin`, as 0..1 fractions. |
| `BackgroundColor`, `TextColor` | `background-color`, `color`. Colours are written as `oklab()` so they blend in Oklab, matching MAUI. |
| `WidthRequest`, `HeightRequest` | `width`, `height` |
| `Margin`, `Padding` | MAUI thickness syntax: `10`, `10,20` or `l,t,r,b`. |
| anything else | A raw CSS property: `filter`, `border-radius` / `BorderRadius`, `--my-var`. |

When two or more tracks feed the same CSS property (TranslationX and TranslationY, say), they are merged per offset, so transform parts on separate tracks compose and each axis keeps its own curve.

### Easing

Easing takes the same text as on MAUI: named curves (`CubicOut`, `Emphasized`, `BounceOut`, …), `cubic-bezier()`, `steps()`, `spring(damping, frequency)`, `bouncy(bounce)` and `Hold`. A curve CSS has a native name for is passed through. Anything else (springs, bounces, elastics) is sampled into CSS `linear()`, so it plays the exact curve the MAUI engine does.

## Player

Take an `@ref` to a `Keyframes` (or a `Choreography`) to control it from code:

```razor
<Keyframes @ref="player" AutoPlay="false" Duration="TimeSpan.FromSeconds(2)" Fill="FillMode.Both"> ... </Keyframes>

<input type="range" min="0" max="1" step="0.001" @oninput="Scrub" />

@code {
    Keyframes? player;
    Task Scrub(ChangeEventArgs e) => player!.SeekProgress(double.Parse((string)e.Value!, CultureInfo.InvariantCulture));
}
```

`Play()`, `Pause()`, `Resume()`, `Stop()` / `Stop(restore)`, `Finish()`, `Seek(TimeSpan)`, `SeekProgress(double)`, `SetRate(double)`, `PlayAsync(CancellationToken)`, `State`, `Rate`, `IsReducedMotionAsync()`.

`PlayAsync` returns `true` when the animation runs to its end and `false` when it is stopped, replaced or cancelled. It never throws for any of those. The other methods return tasks because they call into the browser, but you don't have to await them.

## Choreography

The storyboard. It composes `Keyframes` (and nested `Choreography`) children into one animation with one player, so they seek, pause and reverse together. (It isn't called Storyboard because that name is taken by the shared engine type.)

```razor
<Choreography Mode="ChoreographyMode.Sequence" Stagger="TimeSpan.FromMilliseconds(100)">
    <Keyframes ...> <div>Plan</div> </Keyframes>
    <Keyframes ...> <div>Build</div> </Keyframes>
</Choreography>
```

| Parameter | Notes |
|---|---|
| `Mode` | `Parallel` (default): child *i* starts at *i* × `Stagger`. `Sequence`: each child starts when the one before ends, with `Stagger` added as a gap. |
| `ApplyTo` | `Self` (default) composes the children. `Children` treats its Keyframes as a template and applies it to every direct child element in turn, which gives a staggered list entrance. |
| `AutoPlay`, `Playing`, `Speed`, `ReducedMotion`, player methods | As for Keyframes. |

```razor
<Choreography ApplyTo="ChoreographyApplyTo.Children" Stagger="TimeSpan.FromMilliseconds(60)">
    <Keyframes Duration="TimeSpan.FromMilliseconds(400)" Fill="FillMode.Both">
        <Track Property="TranslationY"><Key Offset="0" Value="16" Easing="Emphasized" /><Key Offset="1" Value="0" /></Track>
        <Track Property="Opacity"><Key Offset="0" Value="0" /><Key Offset="1" Value="1" /></Track>
    </Keyframes>
    @foreach (var item in items)
    {
        <div class="row">@item</div>
    }
</Choreography>
```

## Visual state transitions

```razor
<VisualStates State="@state" @onpointerdown="..." @onpointerup="...">
    <VisualState Name="Pressed" Style="scale: .96; opacity: .8" />
    <VisualState Name="Busy" Class="busy" />
    <VisualTransition To="Pressed" Duration="TimeSpan.FromMilliseconds(80)" Easing="CubicOut" />
    <VisualTransition From="Pressed" Duration="TimeSpan.FromMilliseconds(450)" Easing="spring(0.35, 12)" />
    <StateAnimation State="Busy">
        <Keyframes Duration="TimeSpan.FromMilliseconds(900)" Iterations="Keyframes.Infinite" Direction="PlaybackDirection.Alternate">
            <Track Property="Opacity"><Key Offset="0" Value="1" /><Key Offset="1" Value="0.55" /></Track>
        </Keyframes>
    </StateAnimation>
    Save
</VisualStates>
```

- A `VisualState` is a set of CSS declarations (`Style`), a class (`Class`), or both.
- `From` and `To` are optional (`*` means any). The most specific transition wins: both named, then To, then From, then neither.
- A transition with no `Duration` runs 150ms. With no transitions at all, a state change snaps. The first render never animates.
- The change is a CSS transition on exactly the properties the two states set (`all` when either state is a class). If you interrupt it halfway, it carries on from the value on screen.
- `StateAnimation` plays its Keyframes on the VisualStates element while that state is active, and stops and restores it when the state is left.

## Implicit property transitions

```razor
<ImplicitTransitions Style="@boxStyle">
    <PropertyTransition Property="TranslationX" Duration="TimeSpan.FromMilliseconds(700)" Easing="spring(0.4, 9)" />
    <PropertyTransition Property="Scale" Easing="bouncy(0.35)" />
    <PropertyTransition Property="BackgroundColor" />
</ImplicitTransitions>
```

Every change to a listed property animates, whatever causes it: a re-render, a class swap or script. Each `PropertyTransition` has its own `Duration` (default 250ms), `Easing` (default Ease) and `Delay`. `Property="All"` is allowed. Transform parts such as Scale cover both the individual CSS property and `transform`. Set `ApplyTo="ImplicitTransitionTarget.Children"` to transition every direct child instead of the wrapper.

## Item transitions

```razor
<TransitionGroup Items="items" Key="i => i.Id" Appear="ItemTransition.FadeUp"
                 Add="ItemTransition.FadeUp" Remove="ItemTransition.SlideRight"
                 Stagger="TimeSpan.FromMilliseconds(40)">
    <ItemTemplate Context="item"><div class="row">@item.Title</div></ItemTemplate>
</TransitionGroup>
```

| Parameter | Notes |
|---|---|
| `Mode` | `Keyframes` (default) animates. `Platform` adds no managed animation, just the `shiny-transition-item` class for your own CSS. `None` never animates: the off switch. |
| `Appear`, `Add`, `Remove` | `None`, `Fade`, `FadeUp`, `FadeDown`, `SlideLeft`, `SlideRight`, `Scale` or `Pop`. Remove plays the preset backwards. |
| `AppearKeyframes`, `AddKeyframes`, `RemoveKeyframes` | Fragments holding a custom `Keyframes`, which win over the presets. |
| `Move` | FLIP moves for items that shift. Default true. |
| `MoveDuration`, `MoveEasing` | How a move runs. Defaults: `Duration`, and Emphasized. |
| `Duration`, `Easing` | How the presets run. Defaults: 250ms, CubicOut. |
| `Stagger` | Added between items that enter or leave together. |

A removed item stays on the page, pinned where it was, until its exit animation finishes. The other items slide into its space at the same time.

## Page transitions

These additions live in the core package's `PageTransitionHost` (see [Page Transitions](page-transitions.md)). The custom part comes from this package.

- **One navigation, one transition:** `Nav.NavigateTo(uri, PageTransitionType.Flip)`, `Nav.NavigateTo(uri, "drop")` or `Nav.NavigateTo(uri, new PageTransitionOverride(...))`. This overrides the pages' own settings for that navigation, and going back from the page it opened reverses it.
- **Custom keyframe transitions**, generated as `::view-transition-old/new` CSS:

```razor
<PageTransitionHost ReducedMotion="PageTransitionReducedMotion.Crossfade">
    <CustomPageTransition Name="drop">
        <Enter>
            <Keyframes Easing="BackOut">
                <Track Property="TranslationY"><Key Offset="0" Value="-60%" /><Key Offset="1" Value="0" /></Track>
            </Keyframes>
        </Enter>
        <Exit>
            <Keyframes>
                <Track Property="Opacity"><Key Offset="0" Value="1" /><Key Offset="1" Value="0" /></Track>
            </Keyframes>
        </Exit>
    </CustomPageTransition>
    @Body
</PageTransitionHost>
```

  Pages pick it with `<PageTransition Custom="drop" />`. `Enter` runs on the incoming page and `Exit` on the outgoing one. `PopEnter` and `PopExit` are optional; if you leave them out, going back plays Exit and Enter backwards. The transition runs for the host's (or the page's) `Duration`. Without the Keyframe package, `PageTransitionHost.RegisterCustomTransition` takes raw `@keyframes`.
- **Reduced motion:** `PageTransitionHost.ReducedMotion` is `Instant` (default, no animation), `Crossfade` (a short fade with no movement and no hero flights) or `Play`.
- **Shared elements** (`data-shiny-hero`) were already supported.

## Reduced motion

`prefers-reduced-motion` is read through `matchMedia` and kept live, so an animation that is already running responds when the setting changes. Every component has `ReducedMotion`:

- `Auto` (default): a one-shot animation jumps to its end state and a loop holds its first frame. State and implicit transitions snap, and list changes apply at once.
- `Play`: always animates. Use it for motion that carries information.
- `Instant`: never animates.

## Notes

- **Razor quirk:** inside `@foreach` and `@if` blocks, Razor reads `<Track>` as the HTML `<track>` void element and won't let it have children. Use `<PropertyTrack>` there; it is the same component under another name. In plain markup the two are interchangeable.
- **Values are text**, as in XAML: `Value="#2563EB"`, `Value="-60%"`. To use a number from code, write `Value="@x.ToString(CultureInfo.InvariantCulture)"`.
- **How `IterationDelay` is emulated:** each pass is lengthened by the gap, with half of it held at each end, so the pause falls between passes whichever direction they run. The start and end delays are pulled in by half a gap to compensate. A whole-iteration `Easing` is folded into each segment's curve (as a sampled `linear()`), so the motion stays exact. The total length matches the shared engine's `Timing.TotalDuration`.
- **Merged transform tracks:** when the tracks share a schedule, their keys are zipped. When they don't, the merged property is sampled at 60 points per iteration, so each axis follows its own curve.
- **Where the work happens:** everything about *what* animates is compiled in .NET (`KeyframeCompiler`). The script only runs `element.animate()`, reads live values, watches reduced motion and does the FLIP measurements. One module is imported per app or Server circuit.
- **Performance:** opacity and the individual transform properties run on the compositor. Width, height, margin and padding trigger layout on every frame.

<!-- TODO: capture screenshots for keyframe (Blazor pages: /keyframes, /choreography, /visualstates, /implicittransitions, /itemtransitions) -->
