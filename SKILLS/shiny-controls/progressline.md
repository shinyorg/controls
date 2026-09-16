# ProgressLine

The thin determinate/indeterminate line that runs across the **top or bottom of the window** while something is loading. Available on both MAUI and Blazor.

## ProgressLine vs ProgressBar

They are siblings, not modes of one control. Pick by asking whether the thing has a slot in your layout:

| | `ProgressBar` | `ProgressLine` |
|---|---|---|
| Placement | Fills the slot you gave it in a layout | Page chrome - pins itself to a window edge |
| Typical driver | `Value` bound to a view model | A code path, via `IProgressLineService` |
| Track | Visible by default | Transparent by default |
| Corners | Rounded (4) | Square (0), so the line meets the window edges |
| Chrome awareness | None | Clears the nav bar, tab bar and safe area |

The drawing is shared - `ProgressLine` composes a `ProgressBar` internally - so `BarColor`, `UseGradient`, `PulseEnabled`, `IsIndeterminate` and the animated fill behave identically on both.

## MAUI

**Namespace**: `Shiny.Maui.Controls`
**XAML Namespace**: `http://shiny.net/maui/controls` (prefix: `shiny`)

### Declarative

```xml
<ContentPage xmlns:shiny="http://shiny.net/maui/controls">

    <!-- Declared here, rendered across the top of the page. -->
    <shiny:ProgressLine Position="Top"
                        Value="{Binding Progress}"
                        BarColor="#F97316"
                        LineHeight="4" />

    <ScrollView>
        ...
    </ScrollView>
</ContentPage>
```

**The line does not render where you declare it.** It removes itself from that layout and installs onto the page edge named by `Position`. That is the point of the control - it is chrome, not content. `Dock="False"` keeps it inline, which is what you want under a header you are drawing yourself.

### Driven from code

Registered by `UseShinyControls()`. No markup on the page at all:

```csharp
public class MyViewModel(IProgressLineService progressLine)
{
    async Task LoadAsync()
    {
        using var run = progressLine.Start(c =>
        {
            c.Position = ProgressLinePosition.Top;
            c.BarColor = Colors.Orange;
        });

        run.SetProgress(0.4);   // 0..1, or report nothing and let it trickle
        await DoWorkAsync();
    }   // Dispose == Complete: sweeps to 100%, then fades
}
```

### Properties

| Property | Type | Default | Description |
|---|---|---|---|
| Position | ProgressLinePosition | Top | `Top` or `Bottom` |
| Value | double | 0 | Current progress (TwoWay) |
| Minimum / Maximum | double | 0 / 100 | Range |
| IsIndeterminate | bool | false | Sweeping animation instead of a fill |
| BarColor | Color? | null → theme Primary | Fill color |
| TrackColor | Color? | Transparent | The unfilled remainder |
| LineHeight | double | 3 | Thickness |
| CornerRadius | double | 0 | Corner radius of the fill |
| UseGradient | bool | false | Enable gradient fill |
| GradientStartColor | Color? | null → theme Primary | Gradient start |
| GradientEndColor | Color? | null → theme Tertiary | Gradient end |
| PulseEnabled | bool | false | Shimmer sheen along the fill |
| PulseColor / PulseLength / PulseSpeed | Color / double / int | White / 0.4 / 800 | Sheen settings |
| AnimateProgress | bool | true | Slide the fill instead of snapping |
| ProgressAnimationDuration | int | 250 | Slide length in ms; `0` snaps |
| ProgressAnimationEasing | Easing | CubicOut | Slide curve |
| IsActive | bool | true | The **animated** show/hide switch |
| FadeDuration | int | 200 | `IsActive` fade length in ms |
| Dock | bool | true | Relocate onto the page edge |
| AutoInset | bool | true | Offset past the nav/tab bar and safe area |
| Offset | Thickness | 0 | Extra margin on top of `AutoInset` |
| Bar | ProgressBar | - | Read-only; the inner bar, for styling not surfaced here |

Bind **`IsActive`**, not `IsVisible`. `IsVisible` is `VisualElement`'s and cuts straight to hidden with no fade.

### How the inset is resolved

One rule: **a bar earns an offset exactly when it is painted inside the same coordinate space the line is** - that is, when it is a descendant of the line's own overlay root.

| Arrangement | Inset |
|---|---|
| `ShinyTabBar` docked over a Shell page (`ShinyTabBarBehavior`) | The bar's height |
| `ShinyNavBar` inside the page's overlay root | The bar's height |
| `ShinyNavigationPage` (bar wraps the overlay root in a two-row grid) | 0 - the root already starts below it |
| `ShinyTabbedPage` (bar is a sibling of the hosted page) | 0 |
| Native `NavigationPage` / `TabbedPage` | 0 - MAUI already excludes their chrome from the content area |
| Shell's native nav bar, and Shell's bottom tabs when the item has more than one section | 0 - same as NavigationPage / TabbedPage |
| Nothing on that edge | The safe-area inset (Apple heads only) |

Measured height wins over the declared `BarHeight` where available, because `ShinyTabBar.RespectSafeArea` folds the home indicator into its own height - adding a safe-area inset on top would double-count it.

Call `RefreshLayout()` after changing the height of a bar the line sits against; rotation and window resizes are handled already.

## Blazor

**Namespace**: `Shiny.Blazor.Controls`

```razor
<ProgressLine Position="ProgressLinePosition.Top"
              Value="@progress"
              BarColor="#F97316"
              LineHeight="4" />
```

There is no re-parenting on Blazor - the component is `position: fixed` at the viewport edge, so declaring it anywhere works.

### Blazor-only parameters

| Parameter | Type | Default | Description |
|---|---|---|---|
| Anchor | ProgressLineAnchor | Viewport | `Container` uses `position: absolute` against the nearest positioned ancestor |
| RespectSafeArea | bool | true | Clear the notch/home indicator via `env(safe-area-inset-*)` |
| Offset | string | "0px" | Extra distance from the edge, as a CSS length |
| CornerRadius | string | "0" | CSS length |
| BarColor / TrackColor / Gradient* / PulseColor | string | theme token / transparent | Any CSS color |

Colors are CSS strings on Blazor, and default to theme custom properties (`var(--shiny-color-primary, …)`). The token prefix is `--shiny-color-*`.

To push the line below an `AppLayout` header without the component knowing that header exists, set the custom property on any ancestor:

```css
.my-shell { --shiny-progressline-offset: 64px; }
```

### Driven from code

Register with `AddShinyControls()` (or `AddShinyProgressLine()`) and put **one** host in the layout:

```razor
@* MainLayout.razor *@
<ProgressLineHost />
```

```csharp
@inject IProgressLineService ProgressLine

using var run = this.ProgressLine.Start(c => c.BarColor = "#F59E0B");
run.SetProgress(0.6);
```

The service is **Scoped, not Singleton** - it owns the active run list, which is per-user state. A singleton would run one user's loading line across every connected user's window on Blazor Server.

## The service

Identical shape on both hosts.

```csharp
IProgressLineHandle Start(Action<ProgressLineConfig>? configure = null);
bool IsRunning { get; }
void CompleteAll();
```

```csharp
double Progress { get; }        // 0..1
bool IsComplete { get; }
void SetProgress(double p);     // backwards values are ignored
void Complete();                // sweep to 100%, then fade
void Cancel();                  // end without the sweep - abandoned, not finished
void Dispose();                 // == Complete()
```

### Behaviours worth knowing

- **Runs are reference-counted.** Two overlapping operations produce one line that stays up until the slower of them lands - not two lines, and not a line that vanishes when the first one finishes.
- **The slowest run is the one shown**, not the average. Averaging lets a quick call drag the bar most of the way across while the slow one it is waiting on has barely started.
- **Progress never goes backwards.** A value below the current one is ignored; a bar that runs backwards reads as a fault.
- **The trickle never completes.** With nothing reported, the line advances a fraction of the remaining distance to `TrickleCeiling` (0.9) each tick, so it decelerates and never arrives. Completion has to come from the caller.
- **MAUI re-resolves the current page each tick**, so a line started before a navigation moves with it rather than drawing onto a page the user has left.

### ProgressLineConfig

Appearance settings mirror the control's, plus:

| Property | Type | Default | Description |
|---|---|---|---|
| Indeterminate | bool | false | Sweep instead of a fill |
| Trickle | bool | true | Creep forward between reports |
| StartProgress | double | 0.08 | Where the line jumps to on appearing |
| TrickleCeiling | double | 0.9 | The asymptote the trickle approaches |
| TrickleInterval | TimeSpan | 400ms | How often the trickle advances |
| TrickleRate | double | 0.12 | Fraction of the remaining distance per tick |
| Configure | Action<ProgressLine>? | null | **MAUI** - last word on the line before it is shown |

## Gotchas

- A MAUI line declared in markup **disappears from where you wrote it**. That is `Dock="True"` working. Use `Dock="False"` for an inline bar.
- Bind `IsActive`, never `IsVisible`.
- Don't reach for `ProgressLine` for an inline bar in a list row or a card - that is `ProgressBar`.
- On Blazor, one `<ProgressLineHost />` per layout. A second one renders a second line.
- `Colors.Transparent` is the MAUI `TrackColor` default; passing `null` there means "use the theme token", which is the opposite of what you want for a page-edge line.
