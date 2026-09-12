# Ribbon

[← All Shiny Controls](../../README.md)

The desktop ribbon: a strip of tabs over a body of titled command groups, in the shape Office made the
convention for applications with more commands than a toolbar can hold. Tabs, groups, large and small
buttons, toggles, split and menu buttons, contextual tabs, a quick access row, a collapsing body, and
groups that fold themselves into buttons when the window gets narrow.

It ships in the **core** package on both hosts, and targets everything they do — iOS and Android
included.

```bash
dotnet add package Shiny.Maui.Controls             # MAUI
dotnet add package Shiny.Blazor.Controls           # Blazor
```

> **Moved.** The MAUI ribbon used to live in `Shiny.Maui.Controls.Desktop`, whose target frameworks
> stop at the desktop ones. That put it out of reach of every control in the core package that might
> want it — the [Image Editor](image-editor.md) now does — since core cannot reference the add-on that
> references it. The namespace changed with it, from `Shiny.Maui.Controls.Desktop.Ribbons` to
> `Shiny.Maui.Controls.Ribbons`. **Markup is unaffected**: the ribbon was always mapped onto the
> `http://shiny.net/maui/controls` URI, so `shiny:Ribbon` reads exactly as before. Only a C# `using`
> needs changing.

The ribbon needs no registration on either host — it is markup, not a service.

> **Still a desktop shape at heart.** Expanded, it wants a pointer to hover with and enough width for
> three rows of small commands. On a phone, set `DisplayMode="Simplified"` — one dense row, every item
> small, group titles dropped — which is what the [Image Editor](image-editor.md) does below its width
> breakpoint. For app-level navigation a phone still wants a
> [`ShinyToolbar` or `ShinyTabBar`](toolbar-tabbar.md) rather than a ribbon.

---

## The shape

Everything is authored declaratively — nested elements on MAUI, nested components on Blazor:

```
Ribbon
└── RibbonTab            "Home", "Insert", "Review" — one row of the strip
    └── RibbonGroup      "Clipboard", "Font" — a titled box of related commands
        └── items        buttons, toggles, split/menu buttons, separators, your own content
```

### MAUI

```xml
<shiny:Ribbon ApplicationButtonText="File"
              ApplicationButtonCommand="{Binding OpenFileMenu}"
              DisplayMode="{Binding DisplayMode}">

    <shiny:Ribbon.QuickAccessItems>
        <shiny:RibbonButton Text="Save" Size="Small" Icon="save.png" Command="{Binding Save}" />
        <shiny:RibbonButton Text="Undo" Size="Small" Icon="undo.png" Command="{Binding Undo}" />
    </shiny:Ribbon.QuickAccessItems>

    <shiny:RibbonTab Title="Home" Key="home">

        <shiny:RibbonGroup Title="Clipboard" Priority="30">
            <shiny:RibbonSplitButton Text="Paste" Icon="paste.png" Command="{Binding Paste}">
                <shiny:RibbonMenuEntry Text="Keep source formatting" Command="{Binding PasteKeep}" />
                <shiny:RibbonMenuEntry Text="Text only" Command="{Binding PasteText}" />
                <shiny:RibbonMenuEntry IsSeparator="True" />
                <shiny:RibbonMenuEntry Text="Paste special…" Command="{Binding PasteSpecial}" />
            </shiny:RibbonSplitButton>

            <shiny:RibbonButton Text="Cut"  Size="Small" Icon="cut.png"  Command="{Binding Cut}" />
            <shiny:RibbonButton Text="Copy" Size="Small" Icon="copy.png" Command="{Binding Copy}" />
        </shiny:RibbonGroup>

        <shiny:RibbonGroup Title="Font" ShowDialogLauncher="True"
                           DialogLauncherCommand="{Binding OpenFontDialog}">
            <shiny:RibbonToggleButton Text="Bold"   Size="Small" Icon="bold.png"   IsChecked="{Binding Bold}" />
            <shiny:RibbonToggleButton Text="Italic" Size="Small" Icon="italic.png" IsChecked="{Binding Italic}" />

            <shiny:RibbonSeparator />

            <!-- any view at all can sit in a group -->
            <shiny:RibbonContentItem Size="Small">
                <shiny:FontSizePicker SelectedFontSize="{Binding FontSize}" WidthRequest="86" />
            </shiny:RibbonContentItem>
        </shiny:RibbonGroup>
    </shiny:RibbonTab>
</shiny:Ribbon>
```

`xmlns:shiny="http://shiny.net/maui/controls"` — the same prefix as the core controls, which is now
literally where the ribbon lives.

### Blazor

```razor
@using Shiny.Blazor.Controls

<Ribbon ApplicationButtonText="File"
        ApplicationButtonClicked="OpenFileMenu"
        @bind-DisplayMode="mode"
        @bind-SelectedKey="tab">

    <QuickAccess>
        <RibbonButton Size="RibbonItemSize.Small" Text="Save" Icon="@SaveSvg" Clicked="Save" />
        <RibbonButton Size="RibbonItemSize.Small" Text="Undo" Icon="@UndoSvg" Clicked="Undo" />
    </QuickAccess>

    <ChildContent>
        <RibbonTab Title="Home" Key="home">

            <RibbonGroup Title="Clipboard" Priority="30">
                <RibbonSplitButton Text="Paste" Icon="@PasteSvg" Clicked="Paste" Menu="@pasteMenu" />
                <RibbonButton Size="RibbonItemSize.Small" Text="Cut"  Icon="@CutSvg"  Clicked="Cut" />
                <RibbonButton Size="RibbonItemSize.Small" Text="Copy" Icon="@CopySvg" Clicked="Copy" />
            </RibbonGroup>

            <RibbonGroup Title="Font" ShowDialogLauncher="true" DialogLauncherClicked="OpenFontDialog">
                <RibbonToggleButton Size="RibbonItemSize.Small" Text="Bold"   Icon="@BoldSvg"   @bind-Checked="bold" />
                <RibbonToggleButton Size="RibbonItemSize.Small" Text="Italic" Icon="@ItalicSvg" @bind-Checked="italic" />

                <RibbonSeparator />

                <RibbonContent Size="RibbonItemSize.Small">
                    <select @bind="fontSize">…</select>
                </RibbonContent>
            </RibbonGroup>
        </RibbonTab>
    </ChildContent>
</Ribbon>

@code {
    List<RibbonMenuEntry> pasteMenu = new()
    {
        new() { Text = "Keep source formatting", OnClick = … },
        new() { Text = "Text only", OnClick = … },
        new() { IsSeparator = true },
        new() { Text = "Paste special…", OnClick = … }
    };
}
```

> `<QuickAccess>` and `<ChildContent>` have to be named once you use either — that is Blazor's rule for
> a component with more than one `RenderFragment`, not the ribbon's.

---

## Columns fall out of the sizes

Nothing declares a column. A `Large` item takes one to itself — icon over label, the shape a primary
command wants — and `Small` items stack up to `SmallItemRows` (three, the ribbon convention) deep in a
shared column. A `RibbonSeparator`, or a large item, ends the current column and starts a fresh one.

```
┌──────────┬──────────────────┬─────────┐
│          │ Cut              │         │   Paste = Large  → its own column
│  Paste   │ Copy             │ Bullets │   Cut/Copy/Format = three Small → one column
│    ▾     │ Format painter   │         │   Bullets = Large → the next column
└──────────┴──────────────────┴─────────┘
```

### Rows only line up if you say how tall they are

Each group lays out its own columns, so by default a group's rows are as tall as whatever is in that
group. Put a 32px picker in one group and icon buttons in the next and the two groups end up on
different lines, with their captions on different baselines — every group correct on its own, the bar
ragged as a whole.

A bar that mixes hosted controls with icon buttons should pin one row height:

```csharp
new Ribbon { SmallItemRows = 2, SmallItemRowHeight = 32 }   // MAUI
```

```css
.my-toolbar ::deep .shiny-ribbon { --shiny-ribbon-row-h: 32px; }  /* Blazor */
```

On Blazor the custom property has to be set on the `.shiny-ribbon` element itself, not on a wrapper
around it: the ribbon declares the same property in its own rule, and that beats an inherited value.

One consequence catches hosted content on MAUI: a pinned row means the hosted view **is** the row
height, so there is no spare room for `VerticalOptions` to centre it in. A `Label` dropped into a group
draws its text at the top of its own box while the icon buttons beside it centre their glyphs — which
reads as that one item sitting too high. Set `VerticalTextAlignment` as well as `VerticalOptions`.

On MAUI the columns are built in code; on Blazor they are a CSS grid with `grid-auto-flow: column`,
which places items down a column and only then moves across.

## Rows, for a run people read across

Column fill is right for a group of interchangeable commands and wrong for a **run**. Bold, italic,
underline and strikethrough are read left to right; filling a two-row column turns them into a 2×2 block
with underline above italic. It is also what puts a font-name box and a bold button in the same column,
where the column takes the box's width and the 16px mark is stretched across all 150px of it.

Put `RibbonRow`s in a group and it lays them out as rows instead — which is what Office actually draws:

```xml
<shiny:RibbonGroup Title="Font">
    <shiny:RibbonRow>
        <shiny:RibbonContentItem Size="Small"><shiny:FontPicker /></shiny:RibbonContentItem>
        <shiny:RibbonContentItem Size="Small"><shiny:FontSizePicker /></shiny:RibbonContentItem>
    </shiny:RibbonRow>
    <shiny:RibbonRow>
        <shiny:RibbonToggleButton Size="Small" Tooltip="Bold" />
        <shiny:RibbonToggleButton Size="Small" Tooltip="Italic" />
        <shiny:RibbonToggleButton Size="Small" Tooltip="Underline" />
        <shiny:RibbonToggleButton Size="Small" Tooltip="Strikethrough" />
    </shiny:RibbonRow>
</shiny:RibbonGroup>
```

```razor
<RibbonGroup Title="Font">
    <RibbonRow>
        <RibbonContent Size="RibbonItemSize.Small"><FontPickerButton /></RibbonContent>
        <RibbonContent Size="RibbonItemSize.Small"><FontSizePickerButton /></RibbonContent>
    </RibbonRow>
    <RibbonRow>
        <RibbonToggleButton Size="RibbonItemSize.Small" Tooltip="Bold" Icon="@bold" />
        <RibbonToggleButton Size="RibbonItemSize.Small" Tooltip="Italic" Icon="@italic" />
    </RibbonRow>
</RibbonGroup>
```

There is **no mode to set**: the presence of a row is the signal, and a group is one or the other. Rows
and loose items are not mixed, because an item with no row of its own has no answer to which row it is
on. A `RibbonSeparator` inside a row is a rule between the items either side of it rather than a full
column break, and everything on a row is drawn small — a large item is icon-over-label and two rows tall
by construction, so one on a row would break the row height every other group is lined up to.

Rows are dropped in the simplified layout, which is one dense line by definition.

That is the whole of a ribbon's layout language, and it is why reordering a group's items re-flows it
with nothing else touched.

## Item kinds

| Kind | What it is |
|---|---|
| `RibbonButton` | A plain command. `Command`/`CommandParameter` and `Clicked` on MAUI, `Clicked` on Blazor |
| `RibbonToggleButton` | Stays pressed. `IsChecked` (MAUI) / `Checked` (Blazor) is two-way; bind it and skip the handler |
| `RibbonSplitButton` | Face runs the default action, chevron opens the dropdown |
| `RibbonMenuButton` | The whole face opens the dropdown; no default action |
| `RibbonSeparator` | A full-height rule, and a break in the column flow |
| `RibbonRow` | A line of items. A group holding them fills rows instead of columns |
| `RibbonContentItem` (MAUI) / `RibbonContent` (Blazor) | Hosts arbitrary content — a picker, a combo, a swatch strip |

Every item carries `Text`, `Icon`, `Tooltip`, `Description`, `Size`, and enabled/visible flags.

**Dropdown entries** are `RibbonMenuEntry`: `Text`, `Icon`, `IsChecked` (draws a tick), `IsSeparator`,
and nestable `Children` that fly out as a submenu. They are declared as markup children on MAUI and
passed as a `List<RibbonMenuEntry>` on Blazor — the same split `ShinyToolbar` already uses, because
XAML has no comfortable way to write a nested object graph inline and Razor does.

## Icons

- **MAUI** — `Icon` is an `ImageSource`, so a PNG, an SVG, or a `FontImageSource` glyph all work. For
  an icon that is *drawn* rather than loaded, set `IconTemplate` to a `DataTemplate` returning a view;
  it wins over `Icon` and is instantiated per drawn button, so it must not return a shared instance.
- **Blazor** — `Icon` is a string: inline SVG/HTML markup, an image URL, or a glyph. Exactly what a
  `ToolbarItem` takes, so one icon convention covers both controls.

## Enabled and visible

Bind `IsEnabled`/`Disabled` rather than removing an item: a command that disappears when it cannot run
makes the bar move under the pointer. A group can dim its whole contents in one place — `IsEnabled` on
MAUI, `Disabled` on Blazor — without every item having to be bound.

---

## Contextual tabs

Nothing special declares one. Setting `ContextTitle` captions the coloured band above the strip and
marks the tab contextual; binding its visibility to whatever the tab is *about* is what makes it come
and go:

```xml
<shiny:RibbonTab Title="Format" Key="picture"
                 ContextTitle="Picture Tools"
                 IsVisible="{Binding PictureSelected}">
```
```razor
<RibbonTab Title="Format" Key="picture" ContextTitle="Picture Tools" Visible="@pictureSelected">
```

When the showing tab stops being selectable — hidden, disabled or removed — the ribbon falls back to
the nearest tab that still is, so a vanished selection never leaves an empty body. MAUI reports that
as `RibbonTabChangeReason.Fallback` on `TabChanged`; Blazor as the same reason on
`RibbonTabChangedEventArgs`.

A contextual tab underlines in `ContextColor` (the theme's tertiary by default) rather than the accent
the permanent tabs use, so it reads as a different kind of thing rather than the selected one of the
same kind.

## Collapsing the ribbon

`DisplayMode` is two-way on both hosts:

| Mode | |
|---|---|
| `Expanded` | Tab strip plus the open body. The default |
| `Collapsed` | Only the strip. Picking a tab peeks the body back; the next command puts it away again |
| `Simplified` | One dense row — every item drawn small, group titles dropped |

The chevron at the trailing end of the strip toggles Expanded ⇄ Collapsed, and so does a second click
on the tab already showing. `AllowCollapse="false"` removes both; `DisplayMode` still works from code.

## Groups that do not fit

When the showing tab is wider than the bar, groups fold into a single button that opens the whole group
in a popup — lowest `Priority` first, rightmost breaking ties. Items are never dropped individually,
because half a group is worse than a closed one. Raise `Priority` on the groups that should survive
longest, and set `CanCollapse="false"` on one that must stay open. `AllowGroupCollapse="false"` turns
the whole behaviour off and lets the body scroll horizontally instead, which is the better answer when
every group is small.

The decision needs real measured widths, so on Blazor it is made in `ribbon.js` and handed back to the
component. It degrades cleanly: with no JS module — prerendering, a locked-down host — every group
stays open and the body scrolls.

---

## Ribbon reference

| Property | MAUI | Blazor | Description |
|---|---|---|---|
| Tabs | `Tabs` (content property) | `ChildContent` | The `RibbonTab` children |
| Quick access | `QuickAccessItems` | `QuickAccess` fragment | Small icon commands pinned to the strip's trailing end |
| Selection | `SelectedIndex`, `SelectedTab` (two-way), `SelectTab(key)` | `SelectedKey` (two-way) | Which tab is showing |
| `DisplayMode` | two-way | two-way | Expanded / Collapsed / Simplified |
| `AllowCollapse` | ✓ | ✓ | Offer the collapse chevron and the second-click gesture. Default true |
| `AllowGroupCollapse` | ✓ | ✓ | Let groups fold into buttons. Default true |
| `ShowGroupTitles` | ✓ | ✓ | Draw each group's caption. Default true |
| `ShowTabStrip` | ✓ | ✓ | Draw the strip at all — false gives a single-tab ribbon that is really a toolbar |
| `SmallItemRows` | ✓ | ✓ | How deep small items stack before a new column. Default 3 |
| `SmallItemRowHeight` | ✓ | `--shiny-ribbon-row-h` | One fixed height for every small-item row. Default 0/24px = size to content |
| Application button | `ApplicationButtonText`, `ApplicationButtonCommand` | `ApplicationButtonText`, `ApplicationButtonClicked` | The accented "File" button at the head of the strip. Null leaves it out |
| Colours | `AccentColor`, `HeaderBackgroundColor`, `BodyBackgroundColor` | same, as CSS colour strings | Fall back to the theme |
| `ShowTooltips` | ✓ | — | MAUI uses the Shiny `Tooltip` control; Blazor uses the browser's own `title` |

**Events** — MAUI: `TabChanged`, `ItemInvoked`, `GroupDialogLauncherClicked`, `ApplicationButtonClicked`.
Blazor: `TabChanged`, `SelectedKeyChanged`, `DisplayModeChanged`, `MenuEntrySelected`,
`ApplicationButtonClicked`, plus each item's own callback.

**MAUI also has `ribbon.Invoke(item)`** — press an item from code, running its command, flipping a
toggle and raising `ItemInvoked` exactly as a click would. It exists because a keyboard shortcut and
the button it duplicates should go down one path rather than two, and because a `TapGestureRecognizer`
cannot be raised from a test.

### RibbonTab

`Title`, `Key`, visibility (`IsVisible` / `Visible`), enabled (`IsEnabled` / `Enabled`), `ContextTitle`,
`ContextColor`, and the `RibbonGroup` children. `Key` falls back to `Title`.

### RibbonGroup

`Title`, `Priority`, `CanCollapse`, disabled, `ShowDialogLauncher` + its command/callback and tooltip,
`CollapsedIcon`, and the item children. The dialog launcher is the small corner arrow — the convention
for "there is more of this than fits here".

---

## Theming

Both hosts follow the [Shiny theme](styling.md) with no configuration: MAUI resolves
`ShinyThemeKeys.Color.*` through dynamic resources, Blazor through the `--shiny-color-*` custom
properties, and both flip with light/dark. `AccentColor`, `HeaderBackgroundColor` and
`BodyBackgroundColor` override just those three; everything else stays on the theme.

## Platform notes

- **Dropdowns** are drawn above the page rather than inside the bar, which is what stops a menu being
  clipped by a body only three rows tall. On Blazor that is the browser's top layer via the popover
  API, with Escape and a click-away both closing the panel. On MAUI it is the shared page overlay.
- **macOS AppKit (`net10.0-macos`)** gives no native view to a child added after the page has been laid
  out. The ribbon is built for that: every tab's body is created up front and switched with
  `IsVisible`, so tab switching, collapsing and group folding all work. Adding a tab, group or item
  *after* the fact does rebuild, and the dropdown panels are added on demand — those are the two paths
  that head cannot follow. Every other desktop target is unaffected.
- **Collapsed group buttons** fall back to the first item's icon on MAUI; on Blazor set `CollapsedIcon`
  explicitly, since a group's items are components it cannot read an icon out of.
- **Key tips** (the Alt-key letter badges) are not implemented on either host.

## Samples

- MAUI — `samples/Sample/Features/Ribbon/` (the **Ribbon** page in the demo app)
- Blazor — `samples/Sample.Blazor/Pages/RibbonPage.razor` (`/ribbon`)

Both build the same bar — Home / Insert / View plus a contextual Picture Tools tab — with switches for
the display mode, the contextual tab and group collapsing, and a log of every command the bar raises.

## Collapsing

The chevron at the right of the tab strip puts the body away and brings it back. Re-opening returns
the bar to **the mode it was in when you collapsed it**, not to `Expanded` — collapsing a `Simplified`
bar and re-opening it used to hand back the full three-row layout, so on a narrow window the chevron
was a one-way trip to a bar three times the height of the one you had just put away. A `DisplayMode`
set any other way — a binding, a host's own width rule — is remembered the same way.

`AllowCollapse="false"` removes the chevron.

> **If the host rebuilds the ribbon, it has to carry `DisplayMode` across.** A collapsed bar is state
> that lives on the instance, so a host that replaces the whole ribbon on every change — the
> [Image Editor](image-editor.md) does — has to read the mode back before it throws the old one away,
> or the chevron appears to do nothing: collapse it, touch a command, and the bar is simply open again.

## A bar that scrolls says so

Groups fold into buttons when a tab is too wide for the bar, but with `AllowGroupCollapse` off — or at
a width where even the collapsed groups do not fit — the body scrolls instead. A scrolling bar looks
exactly like one that does not: the last group ends flush at the edge and nothing says another follows.

Both hosts draw a fade on whichever edge still has content past it, and drop it once that edge is
reached. There is nothing to switch on. The platform scroll indicator is not the answer here — it is
hidden deliberately, and on iOS and Android it only appears once a scroll is already under way, which
is after the moment the user needed to be told.

On MAUI the fade is two input-transparent overlays tinted from the body's resolved background; a colour
token cannot reach a gradient stop, so the brush is rebuilt from the colour the body actually ended up
with. On Blazor `ribbon.js` marks each scroller with which side overflows and the stylesheet draws an
inset shadow in the surface colour — an inset shadow rather than a mask, which would fade the bar's own
background out and show the page through its edge.
