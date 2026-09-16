# Idle Sign-out

Warn a user who has stopped using the app, count down, and sign them out if they don't answer.

**Status:** design only, nothing implemented. Several decisions are still open (see
[Open decisions](#open-decisions)); the names below use `IdleSignout` as a working title.
**Scope:** full MAUI ↔ Blazor parity.

## Requirements

1. The idle time before the warning is configurable. The warning itself is optional: without one,
   the user is signed out as soon as the idle time runs out.
2. The warning shows a countdown. If nobody answers it, the user is signed out. Signing out is
   raised as an event (both hosts), and on Blazor it can also be a URI to navigate to.
3. A standard modal is built in, and apps can replace its content with their own.
4. The control has no knowledge of auth. It raises events and the app does the auth work.
5. It does **not** run on every page. Pages are covered by rule or by opting in or out, without
   repeating code on each page.

## Architecture: one service for the app, and a presenter

A control placed on each page was rejected. It duplicates code everywhere, and every navigation
would reset the idle clock because each page would own its own timer. Instead the feature is split
the same way QuickEntry is (`src/Shiny.Maui.Controls/QuickEntry/IQuickEntryPresenter.cs`): a service
decides what happens, and a presenter only puts a surface on screen.

| Piece | Role | Lifetime |
|---|---|---|
| `IIdleSignoutService` | The brain. It owns the clock, the activity detection, which pages are covered, the signed-in switch, and every event. | MAUI: singleton. Blazor: **scoped**, so one user's timer can't leak to other users on Blazor Server |
| `IIdleSignoutPresenter` | The face. It shows and hides the warning, and turns the user's choice into `StayAsync`/`SignOutAsync`. It has no timer and makes no decisions. | Registers itself with the service while mounted |
| `IdleSignoutPresenter` control | The built-in presenter you can place yourself, with a `WarningTemplate` override | Blazor: in a layout. MAUI: on the Shell/App |
| Default presenter | Used automatically when none is placed: `ModalView` on Blazor, a `PageOverlay` layer on MAUI | Registered in DI |

In the simple case there is **no markup**: register the service, set the coverage rule, and handle
`TimedOut`. Placing the presenter is only for customising the warning.

### Service contract (sketch)

```csharp
public interface IIdleSignoutService
{
    IdleState State { get; }          // Inactive, Active, Warning, TimedOut, Suspended
    TimeSpan Remaining { get; }
    bool IsActive { get; set; }       // app sets this after sign-in/sign-out, separate from page coverage

    void ReportActivity();            // app-defined activity: SignalR traffic, API calls, ...
    IDisposable Suspend();            // video playback, uploads, long operations

    event Func<IdleWarningEventArgs, Task> Warning;
    event Func<KeepAliveEventArgs, Task> KeepAlive;       // user chose to stay; refresh the token here. Failure leads to sign-out
    event Func<IdleTimedOutEventArgs, Task> TimedOut;     // args.Reason
}

public enum IdleSignoutReason { Timeout, UserChoseSignOut, OtherTab, KeepAliveFailed, ResumedExpired }
```

### Presenter contract (sketch)

```csharp
public interface IIdleSignoutPresenter
{
    bool IsSupported { get; }
    Task ShowAsync(IdleWarningContext context);
    Task HideAsync();
}

public sealed class IdleWarningContext
{
    public TimeSpan Remaining { get; }   // changes as the countdown runs; drives the template
    public Task StayAsync();
    public Task SignOutAsync();
}
```

This interface also leaves room for a later `.Desktop` presenter that uses a native OS window or
notification when the app is in the background, as QuickEntry does.

### Registration (sketch)

```csharp
// both hosts
o.IdleTimeout     = TimeSpan.FromMinutes(15);   // time until the warning, or until sign-out when there's no warning
o.WarningDuration = TimeSpan.FromSeconds(60);   // null = no warning
o.IsMonitored     = ctx => ctx.PageType != typeof(LoginPage);

// Blazor only
o.SignOutUri      = "/authentication/logout";   // NavigateTo(uri, forceLoad: true), fits OIDC endpoints
o.IncludeRoutes   = ["/app/**"];
o.ExcludeRoutes   = ["/app/help"];
o.SyncAcrossTabs  = true;
```

### Presenter placement (sketch)

```razor
@* Blazor: once, in the layout *@
<IdleSignoutPresenter>
    <WarningTemplate Context="w">
        <p>Signing you out in @w.Remaining.Seconds s</p>
        <ShinyButton OnClick="w.StayAsync">Stay signed in</ShinyButton>
        <ShinyButton OnClick="w.SignOutAsync">Sign out now</ShinyButton>
    </WarningTemplate>
</IdleSignoutPresenter>
```

```xml
<!-- MAUI: once, on the Shell or the App -->
<shiny:IdleSignoutPresenter>
    <shiny:IdleSignoutPresenter.WarningTemplate>
        <DataTemplate x:DataType="shiny:IdleWarningContext">…</DataTemplate>
    </shiny:IdleSignoutPresenter.WarningTemplate>
</shiny:IdleSignoutPresenter>
```

## Auth

The control knows nothing about auth. It watches for inactivity and raises events.

- **Signing out is async and can fail**, so the events are `Func<…, Task>`. "Stay" and "time out"
  are separate events:
  - `KeepAlive`: the app refreshes its token or pings the server. If that fails (the server session
    already expired), the service signs out with `KeepAliveFailed`.
  - `TimedOut`: the app signs out. `SignOutUri` covers the no-code case on Blazor.
- **A client-side idle timer is a convenience, not security.** The server still needs its own
  sliding expiration, or closing the tab leaves the user signed in forever. This goes in the docs.
- `IdleSignoutReason` reaches the app so the login page can say "You were signed out due to
  inactivity".

## Page coverage

Three ways, coarsest first. Most apps need only one.

1. **A rule at registration:** `IsMonitored` (both hosts), plus `IncludeRoutes`/`ExcludeRoutes` on
   Blazor.
2. **A per-page marker**, which overrides the rule in either direction:
   - Blazor: `@attribute [IdleSignout]`, `[IdleSignout(Enabled = false)]`, `[IdleSignout(Minutes = 5)]`
   - MAUI: `shiny:IdleSignout.IsEnabled="True"` and `shiny:IdleSignout.Timeout="0:05:00"` (attached properties)
3. **Blazor only:** a coverage rule based on the layout, if wanted. See the decision below.

A page setting can be a different timeout, not just on or off, for example 2 minutes on a payments
page.

**Pieces already in the repo to reuse**
- Blazor: `samples/Sample.Blazor/App.razor` already cascades `routeData.PageType` to the layout, so
  the host reads the page type from there without reflecting over routes. `LocationChanged` covers
  navigation.
- MAUI: `ShinyTabBarBehavior` already follows Shell `Navigated`, and `PageOverlay.CurrentPage()` /
  `LeafPage` already find the page the user is on. Don't find the page through `OnParentSet`, which
  never sees it.

### Moving between covered and uncovered pages

| Move | Behavior |
|---|---|
| Covered page → uncovered page | Pause the clock. A warning that's showing is cancelled; it isn't a sign-out. |
| Uncovered page → covered page | Start the clock fresh. Time on a public page doesn't count. |
| Covered page → covered page | The clock keeps running. Navigating counts as activity, so it resets. |
| Page with a shorter timeout | Apply it on arrival. If the user is already past it, show the warning now rather than signing out without one. |
| `IsActive == false` (nobody signed in) | Pause the clock, whatever the page. A login page someone forgot to exclude can't sign anyone out. |

### Presenter edge cases

| Situation | Behavior |
|---|---|
| No presenter mounted when the warning is due | **Fail closed**: skip the warning, sign out when the warning period ends, and log an error. |
| No presenter placed anywhere | Use the default presenter from DI. |
| Several mounted (nested layouts, several MAUI windows) | Blazor: the most recently mounted wins. MAUI: the presenter for the active window wins. |

## What else it has to handle

1. **Timestamps, not a ticking countdown.** Store the time of the last activity and compare it to the
   clock. Laptops sleep, iOS suspends backgrounded apps, and Chrome throttles background-tab timers
   to about once a second. When the app wakes up past the timeout, sign out immediately
   (`ResumedExpired`) rather than showing a warning nobody saw.
   - Blazor: the `visibilitychange` event. MAUI: the app resume event, plus an optional
     "sign out when the app is backgrounded".
2. **Multiple browser tabs (Blazor).** Activity in one tab resets every tab, and signing out in one
   signs out all of them (`OtherTab`). Use `BroadcastChannel`, falling back to `storage` events.
   MAUI runs in one process, so it only has to consider multiple windows, which share the monitor.
3. **What counts as activity.** Pointer, key, touch, wheel and scroll by default, with the set
   configurable, plus `ReportActivity()`. While the warning is showing, moving the mouse does
   **not** dismiss it; the user has to choose.
4. **Pausing:** `Suspend()` and `IsActive`.
5. **Blazor Server cost.** The timer and listeners stay in JS, throttled. .NET is called only when
   the warning is due, never on each `mousemove`.
6. **Accessibility (WCAG 2.2.1 Timing Adjustable).** The warning must appear at least 20 seconds
   before sign-out and offer a simple way to extend (enforce or warn on a shorter `WarningDuration`).
   Announce the countdown through `aria-live` / `SemanticProperties` at intervals (60s, 30s, 10s),
   not every second.
7. **Privacy while the warning shows.** Blur or cover what's behind it (`BlurBackdrop` on Blazor, an
   equivalent on MAUI). Escape and backdrop clicks don't dismiss it.
8. **Testability.** Inject `TimeProvider` so the whole state machine runs in unit tests without real
   timers.

## Per host

| | Blazor | MAUI |
|---|---|---|
| Service | scoped `IIdleSignoutService`, `AddShinyIdleSignout` (TryAdd, and included in the `AddShinyControls` umbrella) | singleton, installed through `UseShinyControls` |
| Activity detection | a JS module listening on `document` | a separate hook per platform (below) |
| Current page | cascaded `PageType` + `LocationChanged` | Shell `Navigated` + `PageOverlay.CurrentPage()` |
| Default presenter | built on `ModalView` | a `PageOverlay` layer (or `IDialogService`; its first-page bug was fixed 2026-07-31) |
| Tabs / windows | `BroadcastChannel` | one monitor shared by all windows |
| Sleep / resume | `visibilitychange` + timestamps | resume event + timestamps |

### MAUI activity hooks (most of the work)

MAUI has no global touch or key hook, so each platform needs its own:

| Platform | Hook |
|---|---|
| iOS / Mac Catalyst | a window gesture recognizer with `CancelsTouchesInView = false` (and keys through `pressesBegan`) |
| Android | a wrapper around `Window.Callback` on `DispatchTouchEvent` / `DispatchKeyEvent` |
| Windows | `AddHandler(PointerPressed / KeyDown, handledEventsToo: true)` on the root |
| macOS (AppKit) | an `NSEvent` local event monitor |
| Linux (GTK4) | an event controller on the window |

None of these may swallow or cancel the input. Remember the memories about Android touch
cancellation and alternate app heads missing handler mappers: install the hooks through the
window's lifecycle, not through handler mappers.

### Shared logic

The logic that doesn't depend on a host (the state machine, coverage evaluation, timestamps, the
page timeout override) is plain C# that both hosts use. It's too small for a new `.Shared` package:
link one source file into both cores or duplicate it, as Motion Icons shares its spec.

## Implementation outline

1. Shared state machine + `TimeProvider` tests (both test projects).
2. Blazor: JS module (activity, throttling, `BroadcastChannel`, visibility), service,
   `IdleSignoutPresenter` + default `ModalView` presenter, `[IdleSignout]` attribute, bUnit tests.
3. MAUI: platform activity hooks, service, `IdleSignoutPresenter` + `PageOverlay` presenter,
   attached properties, headless tests.
4. Samples: `samples/Sample/Features/IdleSignout/` and `samples/Sample.Blazor/Pages/`, using a
   deliberately short timeout (for example 20s idle / 20s warning) and an event log. Wire them into
   `AppShell.xaml` (**and** `samples/Sample.MacOS/AppShell.xaml`, which drifts), `MauiProgram.cs`,
   and `Catalog.cs`.
5. Verify: Blazor live in a browser (a focused tab, see the background-tab throttling memory),
   including two tabs and sleep/resume; MAUI on the iOS and Android simulators plus AppKit.
6. Required docs per `CLAUDE.md`: `docs/controls/idle-signout.md` + README index row,
   `SKILLS/shiny-controls/idle-signout.md` + `SKILL.md` reference, release notes, the documentation
   site folder, the homepage card (Status & Feedback?), the sidebar node, and
   `TODO: capture screenshots for idle-signout`.

## Open decisions

1. **Name.** `IdleSignout` commits the feature to signing out. `IdleTimeout` would also allow a kiosk
   "reset to home screen" use, which costs almost nothing (just a different `TimedOut` handler).
2. **Kiosk "reset to home":** in scope or not? This is tied to the name.
3. **Page timeout overrides:** only shorter than the app setting (recommended, so one page can't
   loosen the policy), or longer too?
4. **Server session expiry as a second trigger:** e.g. a `SessionExpiresAt` so the warning also
   appears when the token is about to expire while the user is active. Now or later?
5. **Should mounting the presenter define coverage?** Recommendation: **no.** If it did, forgetting
   the presenter on one layout would silently switch sign-out off there. Coverage stays explicit.
6. **MAUI default presenter:** a dedicated `PageOverlay` layer, or reuse `IDialogService`?
7. **Should moving the mouse dismiss the warning?** Recommendation: no, require a click.
