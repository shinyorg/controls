# Ribbon (MAUI + Blazor)

An Office-style tabbed command bar: a strip of tabs over a body of titled command groups. For
applications with more commands than a toolbar can hold — editors, IDEs, analysis tools.

| Package | Namespace | Use when |
|---|---|---|
| **`Shiny.Maui.Controls`** | `Shiny.Maui.Controls.Ribbons` | MAUI — every target, iOS and Android included |
| **`Shiny.Blazor.Controls`** | `Shiny.Blazor.Controls` | Blazor |

It is in the **core** package on both hosts. It used to be in `Shiny.Maui.Controls.Desktop` under
`Shiny.Maui.Controls.Desktop.Ribbons` — never emit that namespace.

**No registration on either host.** It is markup, not a service — do not emit a `UseShinyRibbon()` or
`AddShinyRibbon()` call, there is no such thing.

**On a phone, set `DisplayMode="Simplified"`.** Expanded wants a pointer and enough width for three
rows of small commands. Simplified is one dense row, every item small, group titles dropped — that is
the mobile form of a ribbon, and it is what ImageEditor switches to below 600px wide.

**For app-level navigation a phone still wants a bar, not a ribbon.** Generate `ShinyToolbar` /
`ShinyTabBar` (Blazor) or `ShinyTabbedPage` (MAUI) for that — see toolbar-tabbar.md and tabbedpage.md.
A ribbon is for a document surface with more commands than a bar can hold.

**Size items consistently.** Simplified keeps a label only on items declared `Small`, so a group mixing
`Large` and `Small` renders some items labelled and some as bare icons with no rule a user can see. Pick
one size per bar unless a `Large` item is genuinely meant to dominate.

## The shape

```
Ribbon
└── RibbonTab            one row of the strip
    └── RibbonGroup      a titled box of related commands
        └── items        buttons, toggles, split/menu buttons, separators, hosted content
```

Both hosts are **declarative** — nested elements in XAML, nested components in Razor. There is no
item-list / `ItemsSource` form; never invent one.

## MAUI

`xmlns:shiny="http://shiny.net/maui/controls"` — the same URI as every other core control, so do
**not** emit a `clr-namespace:` prefix for it.

```xml
<shiny:Ribbon ApplicationButtonText="File"
              ApplicationButtonCommand="{Binding OpenFileMenu}"
              DisplayMode="{Binding DisplayMode}"
              SelectedIndex="{Binding TabIndex}">

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
                <shiny:RibbonMenuEntry Text="Chart">
                    <shiny:RibbonMenuEntry Text="Bar" Command="{Binding Chart}" CommandParameter="Bar" />
                    <shiny:RibbonMenuEntry Text="Line" Command="{Binding Chart}" CommandParameter="Line" />
                </shiny:RibbonMenuEntry>
            </shiny:RibbonSplitButton>

            <shiny:RibbonButton Text="Cut"  Size="Small" Icon="cut.png"  Command="{Binding Cut}" />
            <shiny:RibbonButton Text="Copy" Size="Small" Icon="copy.png" Command="{Binding Copy}" />
        </shiny:RibbonGroup>

        <shiny:RibbonGroup Title="Font" ShowDialogLauncher="True"
                           DialogLauncherCommand="{Binding OpenFontDialog}">
            <shiny:RibbonToggleButton Text="Bold"   Size="Small" Icon="bold.png"   IsChecked="{Binding Bold}" />
            <shiny:RibbonToggleButton Text="Italic" Size="Small" Icon="italic.png" IsChecked="{Binding Italic}" />

            <shiny:RibbonSeparator />

            <shiny:RibbonContentItem Size="Small">
                <shiny:FontSizePicker SelectedFontSize="{Binding FontSize}" WidthRequest="86" />
            </shiny:RibbonContentItem>
        </shiny:RibbonGroup>
    </shiny:RibbonTab>

    <!-- contextual: ContextTitle makes it contextual, IsVisible makes it come and go -->
    <shiny:RibbonTab Title="Format" Key="picture"
                     ContextTitle="Picture Tools"
                     IsVisible="{Binding PictureSelected}">
        <shiny:RibbonGroup Title="Adjust" CanCollapse="False">
            <shiny:RibbonButton Text="Crop" Icon="crop.png" Command="{Binding Crop}" />
        </shiny:RibbonGroup>
    </shiny:RibbonTab>
</shiny:Ribbon>
```

## Blazor

```razor
@using Shiny.Blazor.Controls

<Ribbon ApplicationButtonText="File"
        ApplicationButtonClicked="OpenFileMenu"
        @bind-DisplayMode="mode"
        @bind-SelectedKey="tab">

    <QuickAccess>
        <RibbonButton Size="RibbonItemSize.Small" Text="Save" Icon="@SaveSvg" Clicked="Save" />
    </QuickAccess>

    <ChildContent>
        <RibbonTab Title="Home" Key="home">
            <RibbonGroup Title="Clipboard" Priority="30">
                <RibbonSplitButton Text="Paste" Icon="@PasteSvg" Clicked="Paste" Menu="@pasteMenu" />
                <RibbonButton Size="RibbonItemSize.Small" Text="Cut" Icon="@CutSvg" Clicked="Cut" />
            </RibbonGroup>

            <RibbonGroup Title="Font" ShowDialogLauncher="true" DialogLauncherClicked="OpenFont">
                <RibbonToggleButton Size="RibbonItemSize.Small" Text="Bold" Icon="@BoldSvg" @bind-Checked="bold" />
                <RibbonSeparator />
                <RibbonContent Size="RibbonItemSize.Small">
                    <select @bind="fontSize">…</select>
                </RibbonContent>
            </RibbonGroup>
        </RibbonTab>

        <RibbonTab Title="Format" Key="picture" ContextTitle="Picture Tools" Visible="@pictureSelected">
            <RibbonGroup Title="Adjust" CanCollapse="false">
                <RibbonButton Text="Crop" Icon="@CropSvg" Clicked="Crop" />
            </RibbonGroup>
        </RibbonTab>
    </ChildContent>
</Ribbon>

@code {
    List<RibbonMenuEntry> pasteMenu = new()
    {
        new() { Text = "Keep source formatting", OnClick = EventCallback.Factory.Create(this, PasteKeep) },
        new() { IsSeparator = true },
        new() { Text = "Text only", OnClick = EventCallback.Factory.Create(this, PasteText) }
    };
}
```

**Rules that trip generation up:**

- `Size` is a **qualified enum** in Razor: `Size="RibbonItemSize.Small"`, never `Size="Small"`.
- Once you use `<QuickAccess>` you must also name `<ChildContent>` — Blazor's rule for a component
  with more than one `RenderFragment`.
- Dropdown entries are a **`List<RibbonMenuEntry>` parameter** on Blazor (`Menu="@list"`), not child
  components. On MAUI they are markup children. This is the same split `ToolbarItem.Children` uses.

## Columns fall out of the sizes

Nothing declares a column, so never try to. A `Large` item (the default) takes a column to itself —
icon over label — and `Small` items stack up to `SmallItemRows` (3) deep in a shared column. A
`RibbonSeparator` or a large item ends the column and starts a fresh one. Reordering the items in a
Rows are sized to their content per group, so a bar mixing hosted pickers with icon buttons must pin
one height or the groups will not line up with each other: `SmallItemRowHeight = 32` on MAUI, and on
Blazor `--shiny-ribbon-row-h: 32px` set on the `.shiny-ribbon` element itself (a wrapper does not
work — the ribbon sets the property on itself and that wins over inheritance).

group re-flows it; that is the only lever.

Set `Size="Small"` on the secondary commands and leave the primary one large. Three smalls per column
is the shape to aim for.

## Rows, for a run people read across

Column fill is right for interchangeable commands and **wrong for a run**. Bold/italic/underline/strike
and a set of alignments are read left to right; a two-row column turns four of them into a 2×2 block
with underline above italic. It is also what puts a font-name box and a bold button in one column, where
the column takes the box's width and the 16px mark is stretched across all of it.

Put `RibbonRow`s in the group instead — one line each, filled left to right:

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
    </shiny:RibbonRow>
</shiny:RibbonGroup>
```

```razor
<RibbonGroup Title="Font">
    <RibbonRow>
        <RibbonContent Size="RibbonItemSize.Small"><FontPickerButton /></RibbonContent>
    </RibbonRow>
    <RibbonRow>
        <RibbonToggleButton Size="RibbonItemSize.Small" Tooltip="Bold" Icon="@bold" />
        <RibbonToggleButton Size="RibbonItemSize.Small" Tooltip="Italic" Icon="@italic" />
    </RibbonRow>
</RibbonGroup>
```

Rules:

- **No mode to set.** The presence of a row is the signal.
- A group is one or the other. **Do not mix rows and loose items** in a group — a loose item has no
  answer to which row it is on, and the rows win.
- Everything on a row is drawn small whatever it asked for; a large item is two rows tall by
  construction. Put a large item in the group *outside* the rows if the group needs a head.
- A `RibbonSeparator` inside a row is a rule between the items either side of it, not a column break.
- Rows are dropped in the simplified (one dense line) layout.

**When to reach for which**: rows for formatting runs and steppers (`−  100%  +`); columns for a set of
unrelated commands, and for a group with a large head plus two or three smalls beside it.

## Item kinds

| Kind | Notes |
|---|---|
| `RibbonButton` | MAUI: `Command`/`CommandParameter` + `Clicked`. Blazor: `Clicked` |
| `RibbonToggleButton` | `IsChecked` (MAUI) / `Checked` (Blazor), **two-way by default** — bind it and skip the handler. A MAUI toggle's command receives the new bool when no `CommandParameter` is set |
| `RibbonSplitButton` | Face runs the default action, chevron opens the menu. Use when one choice is overwhelmingly the common one |
| `RibbonMenuButton` | Whole face opens the menu; no default action |
| `RibbonSeparator` | Full-height rule + a column break (a rule only, inside a row) |
| `RibbonRow` | A line of items. A group holding rows fills rows instead of columns |
| `RibbonContentItem` (MAUI) / `RibbonContent` (Blazor) | Hosts any view/markup — a picker, a combo, a swatch strip |

Common item properties: `Text`, `Icon`, `Tooltip`, `Description`, `Size`, `AutomationId`, and
enabled/visible (`IsEnabled`/`IsVisible` on MAUI, `Disabled` on Blazor).

**Bind enabled, do not remove.** A command that disappears when it cannot run makes the bar move under
the pointer. A group dims its whole contents in one place: `IsEnabled` (MAUI) / `Disabled` (Blazor).

## Icons

- **MAUI** — `Icon` is an `ImageSource`: a PNG/SVG asset, or a `FontImageSource` glyph. For a *drawn*
  icon set `IconTemplate` to a `DataTemplate` returning a view (it wins over `Icon`, and is
  instantiated per button — never return a shared instance).
- **Blazor** — `Icon` is a string: inline SVG markup, an image URL, or a glyph. Same convention as
  `ToolbarItem.Icon`.

## Contextual tabs

Setting `ContextTitle` captions the coloured band above the strip and marks the tab contextual; binding
its visibility to what the tab is about is what makes it appear. When the showing tab stops being
selectable the ribbon falls back to the nearest one that still is (`RibbonTabChangeReason.Fallback`),
so a vanished selection never leaves an empty body. `ContextColor` overrides the tertiary accent.

## Display mode

`DisplayMode` is two-way on both hosts:

- `Expanded` — strip + open body (default)
- `Collapsed` — strip only; picking a tab peeks the body back, and the next command puts it away
- `Simplified` — one dense row, every item drawn small, group titles dropped

The trailing chevron and a second click on the showing tab both toggle Expanded ⇄ Collapsed.
`AllowCollapse="false"` removes both gestures (code can still set `DisplayMode`).

## Groups that do not fit

A tab wider than the bar folds groups into single buttons — lowest `Priority` first, rightmost breaking
ties. Raise `Priority` on the groups that should survive longest; `CanCollapse="false"` pins one open;
`AllowGroupCollapse="false"` turns it off and lets the body scroll instead. Set `CollapsedIcon` on
Blazor (MAUI falls back to the first item's icon).

## Selection and events

- MAUI: `SelectedIndex` / `SelectedTab` (both two-way), `SelectTab(key)`, `SelectTab(tab)`;
  `TabChanged`, `ItemInvoked`, `GroupDialogLauncherClicked`, `ApplicationButtonClicked`.
- Blazor: `@bind-SelectedKey`, `TabChanged`, `DisplayModeChanged`, `MenuEntrySelected`,
  `ApplicationButtonClicked`, plus each item's own callback.
- MAUI has **`ribbon.Invoke(item)`** — press an item from code so a keyboard shortcut and the button it
  duplicates go down one path. It is also the seam a test presses through.

## Theming

Follows the Shiny theme with no configuration on either host. `AccentColor`, `HeaderBackgroundColor`
and `BodyBackgroundColor` override those three (a `Color` on MAUI, a CSS colour string on Blazor);
everything else stays on the tokens. See styling.md.

## Platform notes

- Dropdowns are drawn above the page (Blazor: the browser's top layer via the popover API, with Escape
  and click-away closing; MAUI: the shared page overlay), so a body only three rows tall never clips a
  menu.
- **macOS AppKit (`net10.0-macos`)** gives no native view to a child added after layout. The ribbon
  builds every tab's body up front and switches with `IsVisible`, so tabs, collapsing and group folding
  all work there. Adding a tab/group/item *after* the fact rebuilds, and dropdown panels are added on
  demand — those two paths are the limit on that head.
- Key tips (Alt-key letter badges) are not implemented on either host.

### Collapsing

The chevron collapses the body and restores it to whatever mode it was in — `Simplified` stays
`Simplified`. `AllowCollapse="false"` removes it.

If you generate a host that rebuilds its ribbon on every state change, carry `DisplayMode` across the
rebuild (read it off the old instance, or two-way bind it). A fresh ribbon starts at its default and
the user's collapse is silently lost.

## Overflow hint

When a tab's groups are wider than the bar the body scrolls, and a fade is drawn on whichever edge
still has content beyond it — otherwise a bar that scrolls is indistinguishable from one that does
not. It is automatic on both hosts; nothing to switch on.

Hosting a `Label` in a MAUI ribbon group: set `VerticalTextAlignment = Center`, not just
`VerticalOptions`. The pinned row height makes the label *be* the row, so `VerticalOptions` has nothing
to centre within and the text falls to the top while neighbouring icons stay centred.
