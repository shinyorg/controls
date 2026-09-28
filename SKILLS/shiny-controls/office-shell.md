# Office Shell (MAUI + Blazor)

The Word / Excel / PowerPoint application window around the Office editors: accent title bar (app
tile, AutoSave, quick access, document name + rename, save status, command search, help, avatar),
ribbon header-end actions (Comments / Editing-Reviewing-Viewing / Share), File backstage, status bar
(segments, Focus, view modes, zoom), zoom dialog, ruler, Styles gallery, navigation pane, side pane.

| Package | Namespace |
|---|---|
| `Shiny.Maui.Controls.Office` | `Shiny.Maui.Controls.Office` (XAML: the `http://shinylib.net/controls` xmlns) |
| `Shiny.Blazor.Controls.Office` | `Shiny.Blazor.Controls.Office` |
| shared models (both) | `Shiny.Controls.Office.Shell`; icons `OfficeShellIcon` in `Shiny.Controls.Office.Icons` |

No extra registration beyond what the Office package already needs (`UseShinyOffice()` on MAUI).
**The shell does no I/O** — every task is an event carrying the choice (template, recent file,
`OfficeFileFormat`); generate host handlers that open/save/download.

## Composition

`OfficeShell` slots: `TitleBar`, `Ribbon`, `Ruler`, `VerticalRuler`, `LeftPane`, content
(`ShellContent` on MAUI — the XAML content property; `ChildContent` on Blazor), `RightPane`,
`StatusBar`, `Backstage` (+ Blazor `Overlay`). Properties: `App` (`OfficeApp.Word|Excel|PowerPoint|OneNote`),
`IsLeftPaneOpen` / `IsRightPaneOpen` (both default false), `LeftPaneWidth` 280 / `RightPaneWidth` 320,
`IsBackstageOpen`, `IsFocusMode` — all two-way. Read-only `ShellLayout` (`OfficeShellLayout`) +
`ShellLayoutChanged`. Methods: Blazor `OpenBackstageAsync/CloseBackstageAsync/SetFocusModeAsync/ToggleFocusModeAsync/SetRightPaneOpenAsync/SetLeftPaneOpenAsync`;
MAUI `OpenBackstage(page?)`, `ToggleFocusMode()`, `ClosePane(view)`, `ShowZoomDialog(statusBar)`.

Parts inside a shell inherit its `App`/accent/compact state — do **not** repeat `App` on each part.

- **MAUI auto-wiring**: a `Ribbon` in the Ribbon slot gets `ApplicationButtonText="File"` (if unset) and its
  File button opens the backstage; it is switched to `Simplified` below 600px. The status bar's Focus
  toggles focus mode; the backstage's `IsOpen` follows `IsBackstageOpen`.
- **Blazor wiring the host writes**: `ApplicationButtonClicked="() => backstage = true"` with
  `@bind-IsBackstageOpen="backstage"`, and `DisplayMode="@(simplified ? RibbonDisplayMode.Simplified : RibbonDisplayMode.Expanded)"`
  fed from `ShellLayoutChanged`. Focus, backstage Back and side-pane close drive the cascaded shell
  automatically; `OfficeRibbonActions`' Comments toggles the shell's right pane (`CommentsTogglesRightPane`).

```razor
<OfficeShell App="OfficeApp.Excel" @bind-IsBackstageOpen="backstage" style="height:100vh">
    <TitleBar><OfficeTitleBar @bind-DocumentName="name" SaveState="saveState" CommandIndex="commands"
                              SaveRequested="SaveAsync" UndoRequested="Undo" RedoRequested="Redo" /></TitleBar>
    <Ribbon>
        <Ribbon @ref="ribbon" ApplicationButtonText="File" ApplicationButtonClicked="() => backstage = true">
            <HeaderEnd><OfficeRibbonActions @bind-EditMode="mode" ShareClicked="Share" /></HeaderEnd>
            <ChildContent>…tabs…</ChildContent>
        </Ribbon>
    </Ribbon>
    <ChildContent><SpreadsheetView … /></ChildContent>
    <StatusBar><OfficeStatusBar Items="status" @bind-Zoom="zoom" /></StatusBar>
    <Backstage><OfficeBackstage Templates="templates" RecentFiles="recent" SaveAsRequested="SaveAsAsync" /></Backstage>
</OfficeShell>
```

```xml
<office:OfficeShell App="PowerPoint" IsBackstageOpen="{Binding Backstage}">
    <office:OfficeShell.TitleBar><office:OfficeTitleBar DocumentName="{Binding Name}" CommandIndex="{Binding Commands}" SaveCommand="{Binding Save}" /></office:OfficeShell.TitleBar>
    <office:OfficeShell.Ribbon><shiny:Ribbon x:Name="Ribbon">…</shiny:Ribbon></office:OfficeShell.Ribbon>
    <office:OfficeShell.StatusBar><office:OfficeStatusBar x:Name="Status" Zoom="{Binding Zoom}" /></office:OfficeShell.StatusBar>
    <office:SlideEditor x:Name="Editor" />
</office:OfficeShell>
```

## Parts (same names both hosts; MAUI events are `EventHandler`, Blazor are `EventCallback`)

- **`OfficeTitleBar`**: `DocumentName` (two-way), `DocumentLocation`, `SaveState` (`OfficeSaveState.None|Saved|SavedLocally|Saving|Unsaved|Error`), `SaveStatusText`, `AutoSave` (two-way), `ShowAutoSave/ShowSave/ShowUndo/ShowRedo/ShowSearch/ShowHelp/ShowAvatar`, `CanUndo/CanRedo`, `QuickAccessItems` (`OfficeQuickAccessItem(id, text, OfficeShellIcon, action)`), `CommandIndex`, `SearchPlaceholder`, `UserName`, `Options` (`OfficeShellOptions`). Events `SaveRequested`, `UndoRequested`, `RedoRequested`, `DocumentRenamed`, `LocationRequested`, `SearchSubmitted` (query matched no command), `HelpRequested`, `AccountRequested`. MAUI also `SaveCommand/UndoCommand/RedoCommand`, `IsCompact`; Blazor `Compact` (null follows the shell), `CommandExecuted`.
- **`OfficeRibbonActions`** (put in `Ribbon.HeaderEndContent` / `<HeaderEnd>`): `ShowComments`, `IsCommentsOpen` (two-way), `ShowEditMode`, `EditMode` (`OfficeEditMode`, two-way), `ShowShare`; `CommentsClicked`, `EditModeChanged`, `ShareClicked`.
- **`OfficeBackstage`**: `IsOpen`, `SelectedPage` (`OfficeBackstagePage`), `DocumentName`, `Templates` (`OfficeTemplate`), `RecentFiles` (`OfficeRecentFile`), `DocumentInfo` (`OfficeDocumentInfo`), `ShowHistory`, `SaveAsFormats`/`ExportFormats` (null = app defaults), `Options`, `PrintPreview` (View / fragment), `HistoryContent`, Blazor `PrintSettings`, `HomeTemplateCount`. Events `TemplateSelected`, `RecentFileSelected`, `OpenRequested`, `SaveRequested`, `SaveAsRequested`(format), `ExportRequested`(format), `PrintRequested`, `ProtectRequested`, `InspectRequested`, `OptionsChanged`, `Closed`. Everything but Print closes the backstage. The rail's Save entry saves, it is not a page.
- **`OfficeStatusBar`**: `Items` (`OfficeStatusItem` — MAUI: add to the get-only `Items` list; Blazor: pass a list, `ObservableCollection` redraws), `ItemClicked`, `ShowFocus` + `FocusRequested`, `ViewModes` (null = app's three), `SelectedViewMode` (id, two-way), `ShowViewModes`, `Zoom` (factor, two-way), `ZoomModel`, `ShowZoom`, `ShowZoomSlider`, `PageWidth/PageHeight/TextWidth/ViewportWidth/ViewportHeight` (enable the dialog's fit presets). Percent button opens the zoom dialog.
- **`OfficeRuler`**: `Orientation`, `PageWidth` (pt), `LeftMargin`/`RightMargin` (pt, two-way), `Indents` (`OfficeIndents(Left, FirstLine, Right)`, two-way), `TabStops` (two-way), `Unit` (`OfficeRulerUnit`), `Zoom`, `PixelsPerPoint` (96/72), `PageOffset` (px of the page's left edge), `ShowIndents`, `AllowMarginDrag`, `TabAlignment`. MAUI events `IndentsChanged`, `TabStopsChanged`, `MarginsChanged(left,right)`; Blazor `@bind-Indents`, `@bind-TabStops`, `@bind-LeftMargin`, `@bind-RightMargin`, `LiveUpdate`.
- **`OfficeStyleGallery`** — a ribbon item, place it directly in a `RibbonGroup`: `Styles` (`OfficeStyleDescriptor`, null = `OfficeStyleDescriptors.Word`), `SelectedStyleId` (two-way), `StyleSelected`; Blazor `Columns`, `Rows`, `ItemWidth`, `PanelFooter`; MAUI inherits `RibbonGallery` (`Columns`, `Rows`, `PanelFooterTemplate`…).
- **`OfficeNavigationPane`**: `Headings` (`OfficeHeading(id, level, text, page?)` in document order), `CurrentHeadingId`, `HeadingSelected`, `SearchText` (two-way), `SearchRequested`, `SearchResults` (`OfficeSearchResult(id, before, match, after, page?)`), `ResultSelected`, `SelectedTab`, `ShowPagesTab` + `PagesContent`, `CloseRequested`.
- **`OfficeSidePane`**: `Title`, content (`PaneContent` / `ChildContent`), `HeaderContent`, `ShowClose`, `CloseRequested` (no handler → closes its shell pane; Blazor `IsLeftPane`).
- **`OfficeDialog`** (OK/Cancel; Blazor wraps `ModalView`): `Title`, content, `IsOpen`, `OkText`, `CancelText`, `ShowCancel`, `Accepted`, `Cancelled`. **`OfficeZoomDialog`**: `ZoomSelected`.
- Icons: MAUI `OfficeShellIconView { Icon = OfficeShellIcon.Save }`; Blazor `<OfficeShellGlyph Icon="OfficeShellIcon.Save" />` or `Icon="@OfficeShellIcons.Svg(OfficeShellIcon.Save)"` on a ribbon item. Never emoji.

## Command search

```csharp
var commands = new OfficeCommandIndex();
commands.AddRibbon(ribbon);                 // MAUI: every tab
using var sync = commands.SyncRibbon(ribbon); // Blazor: after @ref is set; grows as tabs render
commands.Add("Go To", ShowGoTo, "Home › Editing", "Ctrl+G", "jump");
```

## Status words and zoom

`OfficeStatusText.Page(1, 3)`, `.Words(197)`, `.Slide(3, 12)`, `.Aggregates(values, nonEmptyCount)` (Excel;
null for one cell). Keep one `OfficeStatusItem` per segment and set its `Text`. `Zoom` is the editor's
zoom factor — bind the status bar and the editor to the same value. `OfficeZoomModel` (10–500%, snap
100%, step 10%) also does `Resolve(preset, …)` and `Fit(content, viewport)`.

## Rules

- Compact layout below 600px: search → icon, rulers/panes hidden (open state kept), ribbon Simplified, status bar drops Focus/view modes/slider.
- Blazor needs an explicit height on the shell (it fills its container).
- MAUI lists that change after first layout (backstage templates/recents, headings, status segments) may not repaint on AppKit until a resize — set them before the page shows where possible.
