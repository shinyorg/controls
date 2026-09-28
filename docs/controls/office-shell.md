# Office Shell

[← All Shiny Controls](../../README.md)

The application window around the Office editors — the chrome that makes a `DocumentEditor`,
`SpreadsheetView` or `SlideEditor` look like Word, Excel or PowerPoint rather than a canvas with a
toolbar. Modelled on the Microsoft 365 web apps (and TextSpace's faithful Word clone): an accent
title bar with quick access and a command search, the ribbon with Comments / mode / Share at its right
end, rulers, a navigation pane, a comments pane, a status bar with view modes and zoom, and the File
backstage.

Everything is a part you can use on its own, and `OfficeShell` arranges them. The shell reads and
writes **no files**: every task — open, save, save as PDF, print, pick a template — is an event carrying
what was chosen, so the host decides what it means where it runs.

```bash
dotnet add package Shiny.Maui.Controls.Office      # MAUI
dotnet add package Shiny.Blazor.Controls.Office    # Blazor
```

Namespaces: `Shiny.Maui.Controls.Office` / `Shiny.Blazor.Controls.Office` for the parts, and
`Shiny.Controls.Office.Shell` for the shared models (`OfficeApp`, `OfficeCommandIndex`,
`OfficeStatusItem`, `OfficeZoomModel`, `OfficeRulerModel`, `OfficeTemplate`…). Icons are
`OfficeShellIcon` in `Shiny.Controls.Office.Icons`.

## The parts

| Part | What it is |
|---|---|
| `OfficeShell` | The container: slots for title bar, ribbon, ruler, vertical ruler, left pane, content, right pane, status bar, backstage. Owns focus mode, the backstage overlay and the responsive layout |
| `OfficeTitleBar` | AutoSave switch, Save / Undo / Redo + extra quick access, document name with a rename dropdown, save status ("Saved locally" / "Saving…" / "Unsaved changes"), the "Search for tools, help, and more" command search, Help, account avatar |
| `OfficeRibbonActions` | Comments toggle (`IsCommentsOpen`, two-way — pressed while the pane is open), Editing / Reviewing / Viewing dropdown, Share — for the ribbon's header-end slot. Icons only in the shell's compact layout (`Compact` — null follows the shell; the same name on both hosts) |
| `OfficeBackstage` | The File page: accent rail with Back, Home, New, Open, Info, Save, Save As, Print, Export, History (optional), Options |
| `OfficeStatusBar` | Editor-fed segments on the left; Focus, three view-mode buttons, zoom − / slider / + / percentage on the right. The icon buttons draw small but hit at least 32×28 (MAUI always; Blazor on a coarse pointer) |
| `OfficeZoomDialog` / `OfficeDialog` | Word's Zoom dialog (200 / 100 / 75 / page width / text width / whole page / custom), and the plain OK/Cancel dialog it is built on. On Blazor `OfficeDialog` wraps the core `ModalView` |
| `OfficeRuler` | Word's ruler — inches or cm, margin shading, draggable first-line / hanging / left / right indents, tab stops and the tab-kind selector; horizontal or vertical |
| `OfficeStyleGallery` | The "AaBbCcDd" Styles gallery — a ribbon item |
| `OfficeNavigationPane` | Search box, Headings tree, optional Pages tab, Results |
| `OfficeSidePane` | A titled pane with a close button, for Comments or anything else |
| `OfficeShellIconView` (MAUI) / `OfficeShellGlyph` + `OfficeShellIcons.Svg()` (Blazor) | The shell's icon set |

The app (`OfficeApp.Word` / `Excel` / `PowerPoint` / `OneNote`) sets the accent (Word blue `#185ABD`,
Excel green `#107C41`, PowerPoint red `#C43E1C`), the default name ("Document1",
"Book1", "Presentation1"), the Save As / Export formats and the status bar's view modes (Read / Print /
Web, Normal / Page Layout / Page Break Preview, Normal / Slide Sorter / Reading View). Set it on the
shell; the parts inside inherit it.

## Blazor

```razor
<OfficeShell App="OfficeApp.Word"
             @bind-IsBackstageOpen="backstage"
             @bind-IsRightPaneOpen="comments"
             ShellLayoutChanged="l => simplified = l.SimplifiedRibbon"
             style="height:100vh">
    <TitleBar>
        <OfficeTitleBar @bind-DocumentName="name" SaveState="saveState" @bind-AutoSave="autoSave"
                        CanUndo="canUndo" CanRedo="canRedo" CommandIndex="commands"
                        SaveRequested="SaveAsync" UndoRequested="Undo" RedoRequested="Redo"
                        UserName="Allan Ritchie" SearchSubmitted="FindInDocument" />
    </TitleBar>
    <Ribbon>
        <Ribbon @ref="ribbon" ApplicationButtonText="File" ApplicationButtonClicked="() => backstage = true"
                DisplayMode="@(simplified ? RibbonDisplayMode.Simplified : RibbonDisplayMode.Expanded)">
            <HeaderEnd><OfficeRibbonActions @bind-EditMode="mode" ShareClicked="Share" /></HeaderEnd>
            <ChildContent>
                <RibbonTab Title="Home">
                    <RibbonGroup Title="Styles">
                        <OfficeStyleGallery @bind-SelectedStyleId="styleId" StyleSelected="ApplyStyle" />
                    </RibbonGroup>
                </RibbonTab>
            </ChildContent>
        </Ribbon>
    </Ribbon>
    <Ruler><OfficeRuler PageWidth="612" @bind-Indents="indents" @bind-TabStops="tabs" Zoom="zoom" PageOffset="pageLeft" /></Ruler>
    <LeftPane><OfficeNavigationPane Headings="headings" HeadingSelected="GoTo" SearchRequested="Search" SearchResults="hits" /></LeftPane>
    <ChildContent><DocumentEditor @ref="editor" Document="document" Zoom="zoom" /></ChildContent>
    <RightPane><OfficeSidePane Title="Comments">…</OfficeSidePane></RightPane>
    <StatusBar><OfficeStatusBar Items="status" @bind-Zoom="zoom" @bind-SelectedViewMode="view" /></StatusBar>
    <Backstage>
        <OfficeBackstage Templates="templates" RecentFiles="recent" DocumentInfo="info" Options="options"
                         TemplateSelected="NewFromAsync" RecentFileSelected="OpenAsync" OpenRequested="BrowseAsync"
                         SaveRequested="SaveAsync" SaveAsRequested="SaveAsAsync" ExportRequested="ExportAsync"
                         PrintRequested="PrintAsync">
            <PrintPreview><img src="@previewUrl" /></PrintPreview>
        </OfficeBackstage>
    </Backstage>
</OfficeShell>

@code {
    readonly OfficeCommandIndex commands = new();
    Ribbon? ribbon;
    IDisposable? sync;

    protected override void OnAfterRender(bool first)
    {
        if (first && ribbon is not null)
            sync = commands.SyncRibbon(ribbon);   // the search finds every rendered ribbon command
    }
}
```

The shell cascades itself, so the parts pick up its app, accent and compact layout; the status bar's
Focus button, the backstage's Back and a side pane's close drive the shell directly, and
`OfficeRibbonActions`' Comments button opens and closes the right pane (and draws pressed — a filled
ground with an accent outline — whenever the pane is open, however it was opened). Escape leaves the backstage
and then focus mode. The width comes from a `ResizeObserver` in `officeShell.js`; without the script
the shell stays at the desktop layout.

## MAUI

```xml
<office:OfficeShell App="Word" IsBackstageOpen="{Binding Backstage}" IsRightPaneOpen="{Binding Comments}">
    <office:OfficeShell.TitleBar>
        <office:OfficeTitleBar DocumentName="{Binding Name}" SaveState="{Binding SaveState}"
                               CanUndo="{Binding CanUndo}" CommandIndex="{Binding Commands}"
                               SaveCommand="{Binding Save}" UndoCommand="{Binding Undo}" RedoCommand="{Binding Redo}" />
    </office:OfficeShell.TitleBar>
    <office:OfficeShell.Ribbon>
        <shiny:Ribbon x:Name="Ribbon"> … </shiny:Ribbon>
    </office:OfficeShell.Ribbon>
    <office:OfficeShell.StatusBar>
        <office:OfficeStatusBar x:Name="Status" Zoom="{Binding Zoom}" />
    </office:OfficeShell.StatusBar>
    <office:OfficeShell.Backstage>
        <office:OfficeBackstage Templates="{Binding Templates}" RecentFiles="{Binding Recent}"
                                SaveAsRequested="OnSaveAs" ExportRequested="OnExport" />
    </office:OfficeShell.Backstage>
    <office:DocumentEditor x:Name="Editor" />   <!-- ShellContent is the content property -->
</office:OfficeShell>
```

```csharp
commands.AddRibbon(Ribbon);                          // MAUI sees every tab, opened or not
Status.Items.Add(pageItem = new OfficeStatusItem("page", "Page 1 of 1") { IsClickable = true });
Status.Items.Add(wordsItem = new OfficeStatusItem("words", "0 words"));
// later, as the caret moves:
pageItem.Text = OfficeStatusText.Page(page, pages);
wordsItem.Text = OfficeStatusText.Words(count);
```

MAUI differences: the layout is `ShellLayout` / `ShellLayoutChanged` (as on Blazor); a `Ribbon` in the
Ribbon slot is wired automatically — its File button opens the backstage (and gets "File" as its text if
it had none), and below the compact width it is switched to `Simplified` (and back); an
`OfficeStatusBar`'s Focus button toggles focus mode; the zoom dialog is hosted by the shell. A bar
in the title bar, ribbon, ruler or status bar slot that is hidden (`IsVisible = false`) takes its row
with it — the shell hides the slot too, so no empty band is left where it was. Everything
is built up front and shown/hidden, so the AppKit head renders it; lists that change after layout
(backstage templates/recents, headings, status segments) may not repaint on AppKit until a resize.
Public seams for tests and keyboard shortcuts: `OfficeTitleBar.Search/SubmitSearchAsync/Rename`,
`OfficeStatusBar.ZoomIn/ZoomOut/SetZoomFromSlider/OpenZoomDialog`, `OfficeRuler.BeginDrag/DragTo/EndDrag/TapAt`,
`OfficeBackstage.SelectPage/ChooseTemplate/ChooseSaveAs/ChooseExport/Save/Close`, `OfficeShell.ToggleFocusMode/OpenBackstage`.

## Command search

`OfficeCommandIndex` is the list the title bar searches. Fill it from the ribbon
(`AddRibbon(ribbon)`, or `SyncRibbon(ribbon)` to keep it current) and add anything the ribbon does not
carry:

```csharp
commands.Add("Go To", () => ShowGoTo(), "Home › Editing", "Ctrl+G", "jump", "page");
commands.Add(new OfficeCommand("Word Count", ShowWordCount) { Category = "Review", CanExecute = () => document is not null });
```

Ranking: whole label, then label prefix, then a word inside the label ("font" → "Grow Font"), then
keywords and category, then a subsequence ("fcol" → "Font Colour"); ties go to the shorter label.
Enter runs the highlighted match; a query that matches nothing raises `SearchSubmitted` so the host can
search the document instead. MAUI's ribbon list covers every tab; Blazor's covers the tabs that have
rendered (see [Ribbon](ribbon.md#ribbon-reference)).

## Status bar and zoom

Segments are `OfficeStatusItem`s (observable: set `Text`, `IsVisible`, `IsClickable`). The words are in
`OfficeStatusText`: `Page(1, 3)` → "Page 1 of 3", `Words(197)` → "197 words", `Slide(3, 12)`,
`Aggregates(values, count)` → Excel's "Average: 4  Count: 3  Sum: 12" (null for a single cell).

`ShowFitToWindow` adds PowerPoint's "Fit slide to current window" button after the percentage;
`IsFitted` draws it pressed and `FitToWindowRequested` (both hosts; `FitToWindow()` on MAUI,
`FitToWindowAsync()` on Blazor) asks the host to fit — the slide editor passes its fitted zoom back
through `Zoom`. See [Slide Editor](slide-editor.md#the-powerpoint-window) for the full PowerPoint wiring.

`Zoom` is a factor (1 = 100%) — the same unit every editor's `Zoom` takes, so bind them together.
`OfficeZoomModel` holds the range, the snap points (100%) and the step (10%); the slider is
piecewise — its left half is the minimum to 100% and its right half 100% to the maximum, so 100% sits
in the middle. `OfficeZoomModel.Default` is 10–500%, but give a status bar the **range its editor
actually clamps to**, or the slider runs past the point where the page stops growing. The editor views
do this themselves: Word's status bar is 25–400% (`DocumentController.MinimumZoom`/`MaximumZoom`),
Excel's 10–400% (`SpreadsheetController.MinZoom`/`MaxZoom`) and PowerPoint's 10–400%
(`SlideController.MinimumZoom`/`MaximumZoom`).
`OpeningZoom(pageWidth, viewportWidth)` is the zoom a page opens at when nobody chose one — 100%, or
page-width fit when the viewport is narrower than the page. Give the
status bar `PageWidth`/`PageHeight`/`TextWidth`/`ViewportWidth`/`ViewportHeight` (same units, e.g.
pixels at 100%) and the zoom dialog's Page width / Text width / Whole page presets light up.

## Ruler

Pure: give it `PageWidth`, `LeftMargin`, `RightMargin` (points), `Indents` (`OfficeIndents(Left,
FirstLine, Right)` — Word's model: `FirstLine` is relative to `Left`, negative for a hanging indent),
`TabStops`, `Unit`, `Zoom`, `PixelsPerPoint` (96/72) and `PageOffset` (where the page's left edge is,
in pixels from the ruler's left edge — the editor's scroll and centring). Dragging the top triangle
moves the first line; the bottom triangle moves the wrapped lines and keeps the first line where it
was; the box moves both. A click on the text span adds a tab of the selector's kind; dragging a tab off
the ruler removes it. Everything snaps to 1/16" (or 0.25 cm) and the text column never drops below
half an inch. Blazor reports on release (`LiveUpdate="true"` for every step); MAUI reports live.
`Orientation="Vertical"` draws the page's height with top/bottom margins and no indents.

## Backstage

`Templates` (`OfficeTemplate`: `Id`, `Name`, `Description`, `Thumbnail` URL, local path or `data:` URI, `Category`,
`IsBlank`, `Open` stream factory, `Tag`) — the blank one is added when missing. The editor views give
their built-in templates pictures the first time the backstage opens (Word: the first page, Excel: the
top of the first sheet, PowerPoint: the title slide); `OfficeTemplateThumbnails.Word()` /
`.Spreadsheet()` / `.Slides()` (in `Shiny.Controls.Office.Skia`) return those lists, and
`OfficeTemplateThumbnails.With(templates, render)` pictures your own — a template that fails to draw
keeps its plain tile, and one that already has a `Thumbnail` is left alone. `RecentFiles`
(`OfficeRecentFile`: `Name`, `Location`, `LastOpened`, `IsPinned`, `App`, `Tag`) — pinned first, then
newest. `DocumentInfo` (`OfficeDocumentInfo`: title, author, location, created, modified, size and
app `Statistics` like Words / Pages / Slides). Save As and Export default to the app's formats
(`OfficeFileFormats`: docx/xlsx/pptx, pdf, txt, html, csv, png, jpg) and raise the chosen
`OfficeFileFormat` (`Id`, `Extension`, `MimeType`, `FileNameFor(name)`). Options edits an
`OfficeShellOptions` (user name, initials, theme System/Light/Dark, AutoSave) in place and raises
`OptionsChanged` — the shell applies none of it; theming and saving are the host's.

## Responsive

Below 600px (`OfficeShellLayout.CompactWidth`) the title bar's search collapses to an icon and the save
status hides, rulers and side panes step away (their open state is kept), the ribbon should go
Simplified, `OfficeRibbonActions` draws Comments / mode / Share as icons only (so the tab strip keeps
room on a phone; `Compact` overrides it), and the status bar drops Focus, the view modes and the slider.
Below 900px the zoom slider goes on its own.

On Android the back button closes what is open before it leaves the page: a ribbon dropdown, an
`OfficeDialog` (as Cancel), the backstage, then focus mode. `UseShinyControls()` registers the hook.

## Focus mode

`IsFocusMode` hides the title bar, ribbon, rulers, panes and status bar; a floating **Exit Focus**
button (and Escape on Blazor) brings them back. Read mode is the same switch — pair it with the
editor's own read-only/reflow setting.

TODO: capture screenshots for office-shell.
