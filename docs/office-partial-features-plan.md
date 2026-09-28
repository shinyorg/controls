# Office editors — partial and missing features

What the 2026-09-28 Office parity push (Word / Excel / PowerPoint look-and-feel, Office shell) left
partial, deliberately skipped, or unverified — with where the code is today, so each item can be
picked up cold.

**Status:** backlog. Nothing here is scheduled.
**Applies to:** `Shiny.Controls.Office.Shared` (engine), `Shiny.Controls.Office.Skia` (painters),
`Shiny.Maui.Controls.Office` + `Shiny.Blazor.Controls.Office` (hosts), the core `Ribbon` on both hosts.
**Rules that apply to every item:** MAUI/Blazor parity; every OOXML write round-trips and an unmodified
file still saves byte-identical; schema sequence order respected (Word/Excel/PowerPoint refuse
out-of-order XML); one undo transaction per command; update `docs/controls/*.md`, the skill, and the
docs-site release notes (see `CLAUDE.md`).

Path roots below: `SH` = `src/Shiny.Controls.Office.Shared`, `SK` = `src/Shiny.Controls.Office.Skia`,
`MO` = `src/Shiny.Maui.Controls.Office`, `BO` = `src/Shiny.Blazor.Controls.Office`,
`MR`/`BR` = `src/Shiny.{Maui,Blazor}.Controls/Ribbon`.

## Suggested order

| # | Item | Size | Why this position |
|---|---|---|---|
| X1 | Disposed-document guard + ribbon rebuild while detached | S | A host doing the obvious thing crashes iOS (see below) |
| X2 | MAUI desktop key hook (shortcuts, modifiers, multi-select, Ctrl-click) | M | Unblocks W7, W8, E13 (MAUI), P17 in one go |
| W1+W2 | Word per-section page setup, then columns | L | Both live in pagination; do sections first |
| E10 | Excel row auto-height for wrapped text | S | Most visible Excel gap |
| E11 | Excel chart editing + reading chart styling | M | Charts are inserted but frozen |
| P18 | Slide character spacing + live autofit | S | Saved but not drawn |
| W3 | Tracked formatting changes + paragraph joins | M | Completes track changes |
| W5 / E12 / P22 | Comments: Word replies/resolve, Excel threaded, slide comments | M each | Share one pane UI (`OfficeSidePane`) |
| rest | see sections | | |

Sizes: S ≈ a day, M ≈ a few days, L ≈ a week+.

---

## Cross-cutting

### X1. Editors crash when the host disposes a document the view still holds — S
- **Today:** Shell drops a page's handler but reuses the page. Disposing the document on handler loss and
  coming back runs a layout that reads it: `MO/DocumentEditor.cs:277` `OnSizeAllocated` →
  `DocumentController.Resize` (`SH/Document/DocumentController.cs:178`) → `DocumentEditorView.RefreshBar`
  (`MO/DocumentEditorView.cs:773`) → `PageColor` (`MO/DocumentEditorView.Ribbon.cs:330`) →
  `WordDocument.Main` (`SH/Document/WordDocument.cs:189`) → `ObjectDisposedException`. On iOS 26+ an
  exception out of `LayoutSubviews` corrupts UIKit's observation tracking and the app later spins/crashes
  in an unrelated call (`setLeftBarButtonItem`). The samples were fixed (they no longer dispose); the
  library still has the trap.
- **Do:** add `public bool IsDisposed` to `OfficeDocument` (`SH/Packaging/OfficeDocument.cs:21`, private
  `disposed` today; `Workbook.cs:803`, `PresentationDocument.cs:473` share it). Guard
  `DocumentController.Resize`/`EnsureLayout`, `DocumentEditor.OnSizeAllocated`/`Rebuild`
  (`MO/DocumentEditor.cs:93`), `RefreshBar`, and the spreadsheet/slide equivalents — treat a disposed
  document as "no document".
- **Also:** setting `Document = null` while the page is detached rebuilds the ribbon without a
  MauiContext ("MauiContext should have been set on parent"): `MR/Ribbon.Render.cs:25` `Rebuild` only
  checks `root is null`. Defer rebuilds while `Handler is null` and replay in `OnHandlerChanged`
  (`MR/Ribbon.cs:466`); callers at `Ribbon.cs:240, 252, 611, 778, 794`, `Ribbon.Properties.cs:14`.
- **Test:** headless — dispose then `Resize`; set `Document = null` on a handler-less view.

### X2. MAUI has no desktop key hook — M
- **Today:** Word `HandleShortcut` (`MO/DocumentEditor.Word.cs:43`) / `HandleKey`
  (`MO/DocumentEditor.cs:1007`), Excel `SpreadsheetView.HandleKey`, slides `HandleShortcut`
  (`MO/SlideEditor.Shortcuts.cs:72`) all exist but nothing calls them on MAUI; the host must. Touch events
  (`SKTouchEventArgs`) carry no modifiers. `MO/HiddenInputKeys.cs` covers Android Del/arrows only; the
  AppKit canvas (`MO/Platforms/AppKit/SkiaCanvasNSView.cs:162`) overrides mouse only.
- **Do:** one per-platform hook in the Office package that routes key-down + modifier state to the
  focused editor: Mac Catalyst/iOS hardware keyboard `PressesBegan`, AppKit `NSEvent` local monitor (the
  pattern exists in `src/Shiny.Maui.Controls.Desktop/Platforms/MacOS/QuickEntryPlatform.cs:197`), WinUI
  `KeyDown`, GTK key controller. Expose current modifiers so pointer handlers can read them.
- **Unblocks:** W7 (shortcuts), W8 (Ctrl-click links → `ActivateLinkAt`,
  `SH/Document/DocumentEditorController.Insert.cs:83`), E13 (Ctrl-click ranges on MAUI), P17 (sets
  `SlideEditor.ShiftHeld`/`ControlHeld`, `MO/SlideEditor.cs:330`). Remove the "host must call HandleKey"
  caveats from `docs/controls/document-editor.md:302, 367`, `spreadsheet.md:452`, `slide-editor.md:45`.

### X3. Blazor command search only sees rendered tabs — S/M
- **Today:** `BR/RibbonTab.razor:8` renders only the active tab, so `IndexCommand`
  (`BR/Ribbon.razor.cs:498`) never sees the others; Word and PowerPoint paper over it with curated lists
  (`BO/DocumentEditorView.Shell.cs:167`, `BO/SlideEditorView.Shell.cs:660`). Documented at
  `BR/RibbonCommandInfo.cs:8`, `docs/controls/ribbon.md:401`.
- **Do:** render every tab once off-screen (`display:none`) on first load to index it, or add a
  declarative item model the index can walk. Then delete the curated lists.

### X4. AppKit doesn't repaint lists changed after layout — S
- **Today:** navigation pane headings (`MO/Shell/OfficeNavigationPane.cs:216`), backstage templates/rail
  (`MO/Shell/OfficeBackstage.cs:328, 431`), status segments (`MO/Shell/OfficeStatusBar.cs:373, 480`) add
  children late; `net10.0-macos` may not paint them until a resize (a known AppKit trait in this repo).
- **Do:** pool rows built up front and toggle `IsVisible`/text, or force the native parent to re-layout
  after `Children` change. Verify on `samples/Sample.MacOS`.

### X5. MAUI ruler applies ~350 ms after the drag — S
- **Today:** `MO/Shell/OfficeRuler.cs:242` `OnPan` → `EndDrag` (:208) raises nothing; edits commit on a
  350 ms settle timer (`MO/DocumentEditorView.Shell.cs:850`).
- **Do:** raise `DragCompleted` from `EndDrag`, commit immediately, keep the timer as a fallback.

---

## Word (DocumentEditor)

### W1. Per-section page setup — L (do before W2)
- **Today:** `WordDocument.ReadPageSetup` (`SH/Document/WordDocument.cs:448`) reads only the last
  `w:sectPr`; one `PageSetup` (`SH/Document/DocumentModel.cs:319`) feeds `Paginate`
  (`SH/Document/DocumentPages.cs:287`, `DocumentController.cs:315`), the painter
  (`SK/DocumentPainter.cs:194, 286`) and the PDF exporter (`SK/DocumentPdfExporter.cs:148`). Section
  breaks only force a page break (`SH/Document/DocumentLayout.cs:170`). Each section's setup *is* saved.
- **Do:** read every `w:sectPr` (paragraph-level ones end their section), give each `DocumentPage` its own
  `PageSetup` (size, orientation, margins, header/footer refs), lay each section at its own text width,
  paint and export per page. Continuous section breaks keep the page.
- **Doc to update:** `docs/controls/document-editor.md:325`.

### W2. Multi-column layout — M/L
- **Today:** `SetColumnsCommand` writes `w:cols` (`SH/Document/DocumentFormattingCommands.cs:285`);
  `ColumnCount`/`SetColumns` (`DocumentEditorController.Insert.cs:256`). Layout is one flow at one width
  (`SH/Document/DocumentLayout.cs:65`: "a reflow engine… one continuous column").
- **Do:** after W1, flow a section's lines into N column boxes per page (equal widths + `w:space`, then
  `w:col` explicit widths, column breaks `w:br type="column"`, `w:sep` line). Caret/hit-testing must map
  through the column box.
- **Doc:** `document-editor.md:324`; skill `document-editor.md:554`.

### W3. Track formatting changes and paragraph joins — M
- **Today:** `RevisionKind { Insertion, Deletion }` (`SH/Document/DocumentReview.cs:21`), parsed from
  `w:ins`/`w:del` only (`WordDocument.Parts.cs:177`). Existing `w:rPrChange` is preserved
  (`WordParagraphEditor.Properties.cs:123`) but never created. Joins aren't tracked
  (`DocumentReviewCommands.cs:219`).
- **Do:** when tracking, formatting commands wrap the old `rPr`/`pPr` in `w:rPrChange`/`w:pPrChange`
  (author, date, id); add `RevisionKind.Formatting` with its own mark and Accept (drop the change element)
  / Reject (restore it). Track a deleted paragraph mark as `w:pPr/w:rPr/w:del`; render ¶ struck; accept
  joins, reject restores.
- **Doc:** `document-editor.md:269, 328`.

### W4. Real multilevel lists — S/M
- **Today:** two fixed list definitions (`SH/Document/WordListDefinitions.cs:28`); numbered cycles
  1./a./i. (:48) and Tab walks levels; the Multilevel button is just a numbered list
  (`MO/DocumentEditorView.Ribbon.cs:126`, `BO/DocumentEditorView.razor:188`). `ListStyle` has
  None/Bullet/Numbered (`SH/Text/ListFormatting.cs:10`).
- **Do:** add multilevel presets (1. / 1.1. / 1.1.1., Legal, Heading-linked) as distinct `w:abstractNum`
  definitions with their own nsid; a picker gallery on both hosts.

### W5. Comment replies and resolve — M
- **Today:** flat `DocumentComment` (`SH/Document/DocumentReview.cs:17`), add/delete/next/previous
  (`DocumentEditorController.Review.cs:162`); no `commentsEx` part anywhere.
- **Do:** read/write `WordprocessingCommentsExPart` (`w15:commentEx paraIdParent`, `done`) keyed by the
  comment's last paragraph `w14:paraId`; thread model; Reply and Resolve in the comments pane
  (`OfficeSidePane`) on both hosts. Share the pane UI with E12/P22.

### W6. Persist the picture watermark — S
- **Today:** text watermark persisted (`DocumentEditorController.Insert.cs:310`); picture watermark is
  display-only (`SH/Theming/OfficeWatermark.cs:22`); the reader reports non-textpath VML as not rendered
  (`SH/Document/WordBodyReader.cs:432`).
- **Do:** write a header VML shape with `v:imagedata` + image part (Word's "Picture watermark", washout),
  read it back into `OfficeWatermark`.

### W7 / W8. MAUI shortcuts and Ctrl-click links
Covered by X2. Until then MAUI uses the "Open Link" button (`MO/DocumentEditor.Word.cs:157`).

---

## Excel (Spreadsheet)

### E9. Dynamic arrays (UNIQUE, SORT, FILTER, SEQUENCE, spills) — L
- **Today:** `CalcValue` already carries `CalcArray` (`SH/Spreadsheet/Calc/CalcValue.cs:20`) but the engine
  stores one value per cell (`SH/Spreadsheet/Calc/CalcEngine.cs:14`, write at :130). Skipped on purpose
  (`Calc/Functions/ModernFunctions.cs:8`).
- **Do:** spill store (anchor → extent), `#SPILL!` when blocked, dependency edges for the spilled range,
  `A1#` references, the ghost-border paint, then the functions. Round-trip `cm`/`metadata` parts Excel uses
  for dynamic-array formulas.

### E10. Row auto-height for wrapped text — S
- **Today:** wrap paints inside the row (`SK/SpreadsheetPainter.cs:410, 487`) but rows never grow; row
  metrics from `GetRowHeight` (`SpreadsheetController.cs:776`); column auto-fit counts characters (:1025).
  Stale comment at `SpreadsheetController.cs:909` says the grid doesn't wrap — it does.
- **Do:** measure wrapped cells with the text measurer and grow rows without `customHeight` on
  wrap/edit/column-resize/font change; honour `customHeight`; make column auto-fit measure too.

### E11. Chart editing and styling — M
- **Today:** `SH/Spreadsheet/Data/SheetCharts.cs` models kind/series/title/legend only; insert, move,
  resize, delete commands (:449); painter uses fixed palette (`SK/ChartPainter.cs:12`). Docs:
  `spreadsheet.md:432`.
- **Do:** a Chart Design / Format contextual tab: change type, edit data range + series, title text, legend
  position, axis titles + number format, series colours, chart styles. Read `c:spPr`/`c:txPr` so Excel
  charts keep their look. Share the painter with slides (`SK/SlideChartPainter.cs`) where possible.

### E12. Threaded comments — M
- **Today:** legacy notes only (`SH/Spreadsheet/Data/NotesAndLinks.cs:3`); skill `spreadsheet.md:533`.
- **Do:** `WorksheetThreadedCommentsPart` + `WorkbookPersonPart`, reply model, the fallback legacy note
  Excel writes alongside; the comments pane gets threads (shared UI with W5).

### E13. Multi-range (Ctrl-click) selection — M
- **Today:** `SpreadsheetSelection` has one `Range` (`SH/Spreadsheet/View/SpreadsheetSelection.cs:19`);
  Ctrl in `PointerDown` only follows links (`SpreadsheetController.cs:332`); MAUI can't see modifiers
  (X2); saved with one `sqref` (`Worksheet.Layout.cs:158`).
- **Do:** `Ranges` list with an active range; Ctrl-click/drag adds; painter draws all; commands that make
  sense on multiple areas (format, clear, statistics) iterate; others refuse like Excel does.

### E14. Print areas, sheet page setup, and export beyond the used range — M
- **Today:** pagination from A1 to `UsedRange` (cells only) on Letter/Normal margins
  (`SpreadsheetController.Shell.cs:63`, `SK/SpreadsheetExport.cs:109`); charts past the last used column
  are clipped. `pageSetup`/`printOptions`/`rowBreaks` known only for ordering (`SheetXml.cs:34`).
- **Do:** extend the printable range with chart anchors; `_xlnm.Print_Area`; a `pageMargins`/`pageSetup`
  model (orientation, paper, fit to N pages, gridlines/headings); manual breaks; a Page Setup dialog. Feeds
  E16.

### E15. Named cell styles and real table styles — M
- **Today:** cell styles applied as direct formatting (`Formatting/CellStylePresets.cs:16`); table colours
  approximated from the style family (`Data/SheetTables.cs:28`).
- **Do:** write `cellStyleXfs` + `cellStyles` and `xfId` so a cell remembers "Good"; embed the 60
  built-in table style definitions (or their dxf equivalents) and support custom `tableStyles`.

### E16. Real Page Layout / Page Break Preview — M (after E14)
- **Today:** overlays on the grid (`SK/SpreadsheetPainter.cs:189, 1340`, `SheetViewMode` in
  `SpreadsheetController.Shell.cs:18`).
- **Do:** page-by-page layout with gaps, margins, header/footer editing; draggable breaks; persist
  `sheetView@view`.

---

## PowerPoint (SlideEditor)

### P17. MAUI multi-select
Covered by X2 (`SlideEditor.ShiftHeld`/`ControlHeld`, `MO/SlideEditor.cs:330`). Marquee works today.

### P18. Character spacing and live autofit — S
- **Today:** `spc` written (`SH/Presentation/ShapeTextEditor.cs:544`) but not read (`SlideReader` run
  parse ~:830), not in `TextStyle`, not measured/drawn. Autofit scale is computed only when set
  (`SlideEditorController.Format.cs:293`).
- **Do:** add `CharacterSpacing` to `TextStyle`, read it, apply in `SkiaTextMeasurer` + `ShapeTextPainter`
  (and Word's `w:spacing` on runs can reuse it). Re-run `FitScale` / shape grow after text edits when
  `Autofit != None`.

### P19. Presenter view with a separate audience window (Blazor) — M
- **Today:** in-window side panel (`BO/SlideView.razor:24`); fullscreen only (`BO/wwwroot/slideView.js`).
- **Do:** `window.open` an audience page, sync slide/animation step via `BroadcastChannel`, use the Window
  Management API to place it on the second screen where granted. MAUI: consider a second window on
  desktop (`Application.OpenWindow`).

### P20. Embedded workbook for slide charts — S/M
- **Today:** data only in reference caches (`SH/Presentation/SlideCharts.cs:74`), so PowerPoint can't
  "Edit Data".
- **Do:** generate an xlsx with the existing `Workbook` model, add it as `EmbeddedPackagePart` +
  `c:externalData`; on read, prefer the embedded workbook's values.

### P21. Inline media playback — M
- **Today:** MAUI hands off to `Launcher.OpenAsync` (`MO/SlideView.Presenting.cs:163`); Blazor plays in a
  full-window overlay (`BO/wwwroot/slideView.js:88`). See also `docs/slide-media-plan.md`.
- **Do:** position a player over the shape's bounds during the show (MediaElement on MAUI, `<video>` over
  the canvas rect on Blazor), honour start/trim/volume/loop.

### P22. Slide comments — M
- **Today:** none; the Comments button is hidden (`BO/SlideEditorView.razor:1058`,
  `MO/SlideEditorView.Shell.cs:34`).
- **Do:** read/write modern `p188` comments (plus legacy `p:cmLst`/`commentAuthors` read), markers on the
  slide, the shared comments pane (W5/E12), then show the Comments button.

### P23. Theme fonts on Blazor WASM — S
- **Today:** WASM has no system fonts; only Carlito (Calibri) and Caladea (Cambria) are bundled
  (`BO/OfficeFonts.cs:24`, `SK/OfficeFontRegistry.cs`); substitutions in `SK/SkiaTextMeasurer.cs:41`.
  Georgia/Garamond fall to Caladea, Verdana/Trebuchet/Century Gothic to Carlito, so the Slate and
  Botanical themes look alike in the browser.
- **Do:** bundle OFL look-alikes (e.g. Gelasio for Georgia, EB Garamond, a Verdana-metric sans), register
  them in `Bundled`, add to the substitution table. Watch the WASM payload size — consider lazy loading.

---

## Not verified on a device yet

From the 2026-09-28 iOS-simulator and Android-emulator passes (all three editors were exercised; these
weren't):

- **Word:** find & replace dialog, track-changes accept/reject on device, Read mode, zoom slider by drag.
- **Excel:** conditional formatting dialogs, sort, fill-handle drag (on touch the corner handles resize
  the selection — decide whether touch gets a fill affordance), data validation dropdown.
- **PowerPoint:** Reading view, Outline view, fill/outline/rotate on iOS, Slide Master view.
- **All:** VoiceOver/TalkBack, Windows (WinUI) and macOS AppKit (`samples/Sample.MacOS`) runs — both are
  compile-only; iOS 26.5 simulator after the revisit-hang fix.

## Bugs noticed during the 2026-09-28 screenshot pass (iPad, MAUI)

Small, unfixed, found while posing the docs screenshots:

- **Word — comment balloon clipped:** with both side panes open, the default fit zoom (88%) cuts the
  right edge of the margin balloon. Fit should account for the balloon column.
- **Word — spelling squiggles vanish after posting a comment** until a later zoom/pane change re-checks.
- **Word — Styles gallery ignores the caret:** in a Heading 1 paragraph the gallery doesn't highlight
  Heading 1 (`CurrentStyleId` → `OfficeStyleGallery.SelectedStyleId` binding on MAUI).
- **Excel — status-bar Average unformatted:** shows `477.1214285714`; Excel formats aggregates with the
  selection's number format (`$477.12`).
- **Excel — backstage "Monthly budget" thumbnail shifted left**, column A cut off
  (`SK/OfficeTemplateThumbnails`).
- **PowerPoint — Shape Fill / Outline swatches don't reflect the selection** (show last-used red on a
  yellow, no-outline star). Decide: last-used (Office behaviour for the split button) vs current.
- **PowerPoint — deselecting a shape lands on the neighbouring tab** ("Shapes") instead of Home.
- **PowerPoint — zoom ignored in Slide Sorter** (tile size fixed while the readout changes).
- **Sample — Slide Editor page sets no `UserName`**, so the avatar shows "?".
- **DevFlow (harness, not app):** `ui screenshot` captures the wrong simulator when two are booted — use
  `xcrun simctl io <udid> screenshot`.

## Bugs noticed during the 2026-09-28 screenshot pass (Blazor)

- **Excel — Tab steals focus:** Tab commits and moves the selection but browser focus jumps to the sheet
  tab strip, so typing across a row stops. Enter is fine. Prevent default Tab in the grid's key handler.
- **Word — contextual Table tab:** the ribbon grows much taller (Delete wraps under Rows & Columns); after
  the tab appears, clicking a cell showed no caret, and dragging across cells makes a text selection, not
  a cell selection.
- **PowerPoint — clipped ribbon labels:** "Slide Size" (Design), "Add Animation" and "Effect Options"
  (Animations).
- **Word — margin balloons clipped by the open Comments pane** (same as the MAUI item above — fix once in
  the fit-zoom/balloon-column logic).
- **Sample content is stale and shows in the docs screenshots:** the Word sample text says comments are
  "not shown", and the slide deck says Word and PowerPoint are "read-only". Refresh the sample documents
  (then retake `document-editor/*s1/s2` and `slide-editor/*` shots).
