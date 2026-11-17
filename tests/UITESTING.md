# UI tests

Two end-to-end suites drive the real sample apps. Unit tests live in the other `tests/*` projects.

| Suite | Drives | With |
|---|---|---|
| `Sample.UITests` | the MAUI sample on iOS, Android, macOS (AppKit), Linux (GTK4), Windows | [MAUI DevFlow](https://github.com/dotnet/maui-labs) agent + TUnit |
| `Sample.Blazor.UITests` | the Blazor WebAssembly sample in Chromium, Firefox, WebKit | Playwright + TUnit |

Both have a **route sweep** that opens every page the sample has (read from `AppShell.xaml` / the `@page`
directives, so new pages are swept automatically) and fails on a crash, a navigation that doesn't land,
a logged error, or a console error. Per-control tests then exercise behaviour.

## Quick start: one line, any platforms

From the repo root (`./uitest.ps1` takes the same arguments on Windows):

```bash
./uitest ios                        # one platform
./uitest ios android                # several, run one after another
./uitest mobile                     # ios + android
./uitest all                        # every head this OS can run, plus Blazor
./uitest blazor                     # Chromium; also blazor-webkit, blazor-firefox, blazor-publish
./uitest blazor-mobile              # the whole Blazor suite as an iPhone 13 (WebKit, touch, 390px)
./uitest blazor-android             # ...as a 360px Android phone (Chromium, touch)
./uitest android --filter Slider    # one class; Slider,Ribbon for several, or a raw /*/*/X/* filter
./uitest ios --no-build             # reuse the last build/install
./uitest ios --attach --port 9223   # drive an app that is already running
```

It builds the test projects once into `artifacts/uitest`, runs the platforms one at a time (they share
build output), writes each platform's log to `artifacts/uitest-logs/<platform>.log`, prints a summary
table, and exits non-zero if anything failed. The sections below are what it does under the hood.

## Running the MAUI suite

One run drives one head. Pick it with `SHINY_UITEST_PLATFORM`; the fixture builds the head, installs/launches
it, finds the DevFlow agent's port, and waits for the home page.

```bash
# iOS simulator (macOS host) — boots the first iPhone simulator unless SHINY_UITEST_DEVICE is set
SHINY_UITEST_PLATFORM=ios     dotnet run --project tests/Sample.UITests

# Android — uses the attached device/emulator, otherwise cold-boots SHINY_UITEST_AVD (devflow-tests-api35)
SHINY_UITEST_PLATFORM=android dotnet run --project tests/Sample.UITests

# macOS AppKit head (samples/Sample.MacOS)
SHINY_UITEST_PLATFORM=macos   dotnet run --project tests/Sample.UITests

# Linux GTK4 head (samples/Sample.Linux) — on Linux, or on macOS with `brew install gtk4`
SHINY_UITEST_PLATFORM=linux   dotnet run --project tests/Sample.UITests

# Windows WinUI head — run on a Windows machine
SHINY_UITEST_PLATFORM=windows dotnet run --project tests/Sample.UITests
```

| Variable | Meaning |
|---|---|
| `SHINY_UITEST_PLATFORM` | `ios` · `android` · `macos` · `linux` · `windows` (default: this OS's desktop head) |
| `SHINY_UITEST_ATTACH=1` | The app is already running — just connect. Nothing is built or launched, and a crashed app is not relaunched. |
| `SHINY_UITEST_NOBUILD=1` | Launch the last build without rebuilding. |
| `SHINY_UITEST_PORT` | Agent host port. Default: asked of the DevFlow broker, then 9223. Android forwards a free port. |
| `SHINY_UITEST_DEVICE` | iOS simulator UDID/name, or Android adb serial. |
| `SHINY_UITEST_AVD` | Emulator to boot when no Android device is attached. |

Filter with TUnit's tree filter: `-- --treenode-filter "/*/*/SliderTests/*"`. Results — screenshots per route,
and a screenshot + error log for every failed test — land in
`tests/Sample.UITests/bin/Debug/net11.0/uitest-results/<platform>/`.

Tests run one at a time (there is one UI). If a test crashes the app, the next test relaunches it, so one
crash fails one test.

## Running the Blazor suite

```bash
dotnet run --project tests/Sample.Blazor.UITests                                  # dev server, Chromium
SHINY_BLAZOR_BROWSER=webkit  dotnet run --project tests/Sample.Blazor.UITests     # Safari's engine
SHINY_BLAZOR_BROWSER=firefox dotnet run --project tests/Sample.Blazor.UITests
SHINY_BLAZOR_MODE=publish    dotnet run --project tests/Sample.Blazor.UITests     # trimmed Release build
SHINY_BLAZOR_URL=https://… dotnet run --project tests/Sample.Blazor.UITests       # an already-served app
SHINY_BLAZOR_DEVICE=iphone   dotnet run --project tests/Sample.Blazor.UITests     # as a phone: iphone | android | any Playwright device
```

### Phones

Three layers keep the controls honest on mobile browsers:

- **`MobileFitTests`** runs in *every* Blazor run. It opens each gallery page on a 360px phone and fails
  when the page scrolls sideways, something hangs off either edge, a button is cut off where nothing can
  scroll it back, or text is sliced by a clipping container. Content a control scrolls on purpose (a
  grid, a code block) or clips by design (marquee, carousel, cover flow, or anything marked
  `data-fit-clip`) is left alone. Screenshots land in `uitest-results/<browser>/screenshots/phone/`.
- **`TouchGestureTests`** drives every drag-based control with a real finger — Chromium's CDP touch
  input, through `Touch` on `BlazorTest`. A mouse never meets the two things that break touch: the
  browser deciding at touch-down that the gesture is a scroll (`touch-action`), and the `pointercancel`
  it fires mid-drag. Use `TouchInput.VisibleCentreOf` to aim: on a phone an element's centre is often
  under another pane. Chromium only (`[RequiresTouchInput]`).
- **`SHINY_BLAZOR_DEVICE`** (`./uitest blazor-mobile` / `blazor-android`) runs the whole suite as a
  phone. Tests that only make sense at desktop width — a ribbon that folds its groups on a phone, a
  panel that collapses below 900px, a mouse-wheel gesture — say so with `[SkipOnPhone("why")]`. A skip
  that hides a real mobile bug is worse than a failure, so the reason has to be a design decision.

Writing a test that should also pass on a phone: scroll before you measure
(`ScrollIntoViewIfNeededAsync`, or `TouchInput.BoxOf`) — raw `Page.Mouse` coordinates below the fold
land on nothing, or on the gallery's bottom tab bar. Scroll with `ScrollContentAsync`, not
`Mouse.WheelAsync`, which mobile WebKit cannot synthesise. Every context runs in `en-US`, so dates and
times format the same everywhere.

`SHINY_BLAZOR_MODE=publish` matters: interop DTOs that lose constructors under trimming and similar failures
only show up in a published build. `SHINY_BLAZOR_HEADED=1` shows the browser; `SHINY_BLAZOR_PARALLEL` sets
how many pages run at once (default 4). Playwright installs the browser on first run.

## Writing tests

### MAUI (`tests/Sample.UITests/Features/<Area>Tests.cs`)

```csharp
public class SliderTests : UITest
{
    [Test]
    public async Task Value_Snaps_To_Step()
    {
        await Open("slider", "SliderPage");                       // Shell route + page type (or a marker AutomationId)
        await Driver.SetProperty("Value", "37", automationId: "TemperatureSlider");
        await Driver.WaitForProperty("TemperatureSlider", "Value", "40");
        await Assert.That(await Driver.GetProperty("Text", automationId: "TemperatureLabel")).Contains("40");
    }

    [Test, SkipOn(UIPlatform.MacOS, "AppKit has no pan gesture for the thumb")]
    public async Task Drag_Moves_Thumb() { … }
}
```

Rules that keep these stable across five platforms:

- **Assert on state, not pixels.** Read bindable properties back (`GetProperty`, `WaitForProperty`) and
  query for elements. Screenshots are evidence, never the assertion.
- **`SetProperty` is the main seam.** Many Shiny controls handle input themselves, so a synthetic `Tap`
  has nothing to invoke. Set the bindable the gesture would have set, then assert what the control did with it.
  Use `Tap` for plain Buttons and controls with a TapGestureRecognizer `Command`.
- **Give elements an `AutomationId` in the sample page** rather than finding by index or text. It is set-once
  in MAUI — never rebind it.
- **Every test navigates first.** No test depends on another's state; order is not guaranteed.
- **Platform gaps are explicit**: `[SkipOn(UIPlatform.X, "why")]` or `[OnlyOn(...)]`, with the reason.
  The macOS and Linux heads ship their own `AppShell.xaml` and drop pages — check the head has the route.
- Finish interaction tests with `await AssertNoAppErrors();` when the action could plausibly throw.

### Blazor (`tests/Sample.Blazor.UITests/Features/<Area>Tests.cs`)

```csharp
public class SliderTests : BlazorTest
{
    [Test]
    public async Task Keyboard_Moves_Value()
    {
        await Open("slider");
        var slider = Page.GetByRole(AriaRole.Slider).First;
        await slider.FocusAsync();
        await slider.PressAsync("ArrowRight");
        await Expect(slider).ToHaveAttributeAsync("aria-valuenow", "51");
        await AssertNoConsoleErrors();
    }
}
```

- Prefer role/label locators (`GetByRole`, `GetByLabel`, `GetByText`), then the control's own CSS class
  (`.shiny-…`), then add a `data-testid` to the sample page.
- Use Playwright's retrying `Expect(...)` assertions rather than reading once.
- Real input works here (mouse, keyboard, touch emulation) — use it; that's what the browser suite is for.
- End with `AssertNoConsoleErrors()`.
