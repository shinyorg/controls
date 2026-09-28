using Shiny.Controls.Office.Document;
using Shiny.Controls.Office.Icons;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using Shiny.Maui.Controls.Ribbons;
using TextAlignment = Shiny.Controls.Office.Text.TextAlignment;

namespace Shiny.Maui.Controls.Office;

/// <summary>The ribbon: Word's tabs and groups, in Word's order.</summary>
public partial class DocumentEditorView
{
    Label? zoomLabel;

    void BuildBar()
    {
        this.ribbon.Tabs.Clear();
        this.bindings.Clear();
        this.menuBindings.Clear();
        this.extraPickers.Clear();

        this.fontPicker = this.CreateFontPicker();
        this.sizePicker = this.CreateSizePicker();

        // Undo and redo apply whatever the caret is in, so they sit outside the tabs.
        this.ribbon.QuickAccessItems.Clear();
        this.ribbon.QuickAccessItems.Add(this.Button(OfficeIcon.Undo, "Undo (Ctrl+Z)", c => c.Undo(), enabled: c => c.CanUndo));
        this.ribbon.QuickAccessItems.Add(this.Button(OfficeIcon.Redo, "Redo (Ctrl+Y)", c => c.Redo(), enabled: c => c.CanRedo));

        this.ribbon.Tabs.Add(this.HomeTab());
        this.ribbon.Tabs.Add(this.InsertTab());
        this.ribbon.Tabs.Add(this.DesignTab());
        this.ribbon.Tabs.Add(this.LayoutTab());
        this.ribbon.Tabs.Add(this.ReferencesTab());
        this.ribbon.Tabs.Add(this.ReviewTab());
        this.ribbon.Tabs.Add(this.ViewTab());

        this.tableTab = this.TableTab();
        this.ribbon.Tabs.Add(this.tableTab);

        this.ribbon.Tabs.Add(OfficeRibbonItems.ShapesTab(g => this.Run(c => c.InsertShape(g, this.ShapeWidth, this.ShapeHeight))));

        this.RebuildStylesMenu();
        this.RefreshBar();
    }

    // ---- Home ----

    RibbonTab HomeTab()
    {
        var tab = new RibbonTab { Title = "Home", Key = "home" };

        // Clipboard: Paste is the head of the group, as it is in Word.
        var clipboard = new RibbonGroup { Title = "Clipboard", Priority = 110 };
        var paste = new RibbonSplitButton
        {
            Text = "Paste",
            Tooltip = "Paste (Ctrl+V)",
            Size = RibbonItemSize.Large,
            AutomationId = "DocToolbarPaste",
            IconTemplate = OfficeRibbonItems.IconTemplateFor(OfficeIcon.Paste),
            Command = new Command(() => _ = this.PasteAsync(false))
        };

        paste.Menu.Add(new RibbonMenuEntry { Text = "Keep Source Formatting", Command = new Command(() => _ = this.PasteAsync(false)) });
        paste.Menu.Add(new RibbonMenuEntry { Text = "Keep Text Only", Command = new Command(() => _ = this.PasteAsync(true)) });
        this.bindings.Add(new ItemBinding(paste, null, null, false));

        clipboard.Items.Add(paste);
        clipboard.Items.Add(this.AsyncButton(OfficeIcon.Cut, "Cut (Ctrl+X)", async _ => { await this.editor.CutAsync(); this.AfterCommand(); }, enabled: c => !c.Selection.IsEmpty));
        clipboard.Items.Add(this.AsyncButton(OfficeIcon.Copy, "Copy (Ctrl+C)", async _ => { await this.editor.CopyAsync(); this.AfterCommand(); }, enabled: c => !c.Selection.IsEmpty, viewOnly: true));
        clipboard.Items.Add(this.Toggle(WordIcons.FormatPainter, "Format Painter (Ctrl+Shift+C) — pick up formatting, then select text to paint it", c => c.CopyFormatting(), c => c.IsFormatPainterActive));
        tab.Groups.Add(clipboard);

        // Font: the two boxes and the size steppers on top, the marks underneath — Word's own rows.
        var font = new RibbonGroup { Title = "Font", Priority = 100 };

        var fontBoxes = new RibbonRow();
        if (this.fontPicker is not null)
            fontBoxes.Items.Add(OfficeRibbonItems.Host(this.fontPicker));

        if (this.sizePicker is not null)
            fontBoxes.Items.Add(OfficeRibbonItems.Host(this.sizePicker));

        fontBoxes.Items.Add(this.Button(WordIcons.GrowFont, "Increase Font Size (Ctrl+Shift+>)", c => c.GrowFont()));
        fontBoxes.Items.Add(this.Button(WordIcons.ShrinkFont, "Decrease Font Size (Ctrl+Shift+<)", c => c.ShrinkFont()));
        fontBoxes.Items.Add(this.Menu(WordIcons.ChangeCase, "Change Case (Shift+F3)", null,
        [
            Entry("Sentence case.", c => c.ChangeCase(TextCase.Sentence)),
            Entry("lowercase", c => c.ChangeCase(TextCase.Lower)),
            Entry("UPPERCASE", c => c.ChangeCase(TextCase.Upper)),
            Entry("Capitalize Each Word", c => c.ChangeCase(TextCase.Capitalize)),
            Entry("tOGGLE cASE", c => c.ChangeCase(TextCase.Toggle))
        ]));
        fontBoxes.Items.Add(this.Button(OfficeIcon.ClearFormat, "Clear All Formatting (Ctrl+Space)", c => c.ClearFormatting()));
        font.Items.Add(fontBoxes);

        var highlight = this.Toggle(OfficeIcon.Highlight, "Text Highlight Color", c => { _ = this.PickHighlightAsync(); }, c => c.CaretFormat.Highlight is not null);

        font.Items.Add(OfficeRibbonItems.Row(
            this.Toggle(OfficeIcon.Bold, "Bold (Ctrl+B)", c => c.ToggleBold(), c => c.CaretFormat.Bold),
            this.Toggle(OfficeIcon.Italic, "Italic (Ctrl+I)", c => c.ToggleItalic(), c => c.CaretFormat.Italic),
            this.Toggle(OfficeIcon.Underline, "Underline (Ctrl+U)", c => c.ToggleUnderline(), c => c.CaretFormat.Underline),
            this.Toggle(OfficeIcon.Strikethrough, "Strikethrough", c => c.ToggleStrikethrough(), c => c.CaretFormat.Strike),
            this.Toggle(WordIcons.Subscript, "Subscript (Ctrl+=)", c => c.ToggleSubscript(), c => c.CaretFormat.Subscript),
            this.Toggle(WordIcons.Superscript, "Superscript (Ctrl+Shift+=)", c => c.ToggleSuperscript(), c => c.CaretFormat.Superscript),
            new RibbonSeparator(),
            OfficeRibbonItems.Host(this.textColor),
            highlight));
        tab.Groups.Add(font);

        // Paragraph: lists, indents and marks on top; alignment, spacing, shading and borders below.
        var paragraph = new RibbonGroup { Title = "Paragraph", Priority = 90 };
        var shading = this.CreateColorPicker(color =>
        {
            this.editor.Controller?.SetParagraphShading(color);
            this.AfterCommand();
        });

        this.extraPickers.Add(shading);

        paragraph.Items.Add(OfficeRibbonItems.Row(
            this.Toggle(OfficeIcon.BulletList, "Bullets (Ctrl+Shift+L)", c => c.ToggleBulletList(), c => c.CaretFormat.List == ListStyle.Bullet),
            this.Toggle(OfficeIcon.NumberedList, "Numbering", c => c.ToggleNumberedList(), c => c.CaretFormat.List == ListStyle.Numbered),
            this.Toggle(WordIcons.MultilevelList, "Multilevel List — numbered, with Tab and Shift+Tab to change level", c => c.ToggleNumberedList(), c => c.CaretFormat.List == ListStyle.Numbered && c.CaretFormat.ListLevel > 0),
            this.Button(OfficeIcon.Outdent, "Decrease Indent (Ctrl+Shift+M)", c => c.ChangeIndent(-1), enabled: c => c.CaretFormat.IndentLeft > 0 || c.CaretFormat.ListLevel > 0),
            this.Button(OfficeIcon.Indent, "Increase Indent (Ctrl+M)", c => c.ChangeIndent(1)),
            this.Toggle(WordIcons.ShowMarks, "Show/Hide ¶ (Ctrl+Shift+8)", c => c.ShowFormattingMarks = !c.ShowFormattingMarks, c => c.ShowFormattingMarks, viewOnly: true)));

        paragraph.Items.Add(OfficeRibbonItems.Row(
            this.Toggle(OfficeIcon.AlignLeft, "Align Left (Ctrl+L)", c => c.SetAlignment(TextAlignment.Left), c => c.CaretFormat.Alignment == TextAlignment.Left),
            this.Toggle(OfficeIcon.AlignCenter, "Center (Ctrl+E)", c => c.SetAlignment(TextAlignment.Center), c => c.CaretFormat.Alignment == TextAlignment.Center),
            this.Toggle(OfficeIcon.AlignRight, "Align Right (Ctrl+R)", c => c.SetAlignment(TextAlignment.Right), c => c.CaretFormat.Alignment == TextAlignment.Right),
            this.Toggle(OfficeIcon.AlignJustify, "Justify (Ctrl+J)", c => c.SetAlignment(TextAlignment.Justify), c => c.CaretFormat.Alignment == TextAlignment.Justify),
            this.LineSpacingMenu(),
            OfficeRibbonItems.Host(shading),
            this.Menu(WordIcons.Borders, "Borders", null,
            [
                Entry("Bottom Border", c => c.SetParagraphBorders(ParagraphBorderPreset.Bottom), c => c.CaretFormat.Borders is { Bottom: true, Top: false }),
                Entry("Top Border", c => c.SetParagraphBorders(ParagraphBorderPreset.Top), c => c.CaretFormat.Borders is { Top: true, Bottom: false }),
                Entry("Outside Borders", c => c.SetParagraphBorders(ParagraphBorderPreset.Outside), c => c.CaretFormat.Borders is { Top: true, Bottom: true, Left: true }),
                Entry("No Border", c => c.SetParagraphBorders(ParagraphBorderPreset.None), c => c.CaretFormat.Borders is null),
                Separator,
                Entry("Horizontal Line", c => c.InsertHorizontalLine())
            ])));
        tab.Groups.Add(paragraph);

        // Styles: the gallery proper is the shell's; this is the dropdown every Word user knows.
        var styles = new RibbonGroup { Title = "Styles", Priority = 80 };
        this.stylesMenu = new RibbonMenuButton
        {
            Text = "Normal",
            Tooltip = "Styles",
            Size = RibbonItemSize.Large,
            AutomationId = "DocToolbarStyles",
            IconTemplate = OfficeRibbonItems.IconTemplateFor(WordIcons.Styles)
        };

        this.bindings.Add(new ItemBinding(this.stylesMenu, null, null, false));
        styles.Items.Add(this.stylesMenu);
        tab.Groups.Add(styles);

        // Editing: find, replace, select all.
        var editing = new RibbonGroup { Title = "Editing", Priority = 60 };
        editing.Items.Add(OfficeRibbonItems.HostLarge(this.findBar));
        editing.Items.Add(this.Button(WordIcons.Replace, "Replace (Ctrl+H)", _ => this.ShowReplacePanel(), text: "Replace"));
        editing.Items.Add(this.Button(WordIcons.SelectAll, "Select All (Ctrl+A)", c => c.SelectAll(), text: "Select", viewOnly: true));
        tab.Groups.Add(editing);

        return tab;
    }

    RibbonMenuButton LineSpacingMenu()
    {
        var entries = DocumentEditorController.LineSpacingPresets
            .Select(x => Entry(x.ToString("0.0#", System.Globalization.CultureInfo.CurrentCulture), c => c.SetLineSpacing(x), c => Math.Abs(c.CaretFormat.LineSpacing - x) < 0.01))
            .ToList();

        entries.Add(Separator);
        entries.Add(Entry("Add/Remove Space Before Paragraph", c => c.ToggleSpaceBefore()));
        entries.Add(Entry("Add/Remove Space After Paragraph", c => c.ToggleSpaceAfter()));

        return this.Menu(WordIcons.LineSpacing, "Line and Paragraph Spacing", null, entries);
    }

    /// <summary>
    /// Refills the Styles dropdown from the document — built-in styles first, then its own.
    /// </summary>
    /// <remarks>
    /// Rebuilt when a document is attached and after a style is applied: applying a built-in the
    /// document lacked adds its definition, which can bring others (its basedOn chain) with it.
    /// </remarks>
    void RebuildStylesMenu()
    {
        if (this.stylesMenu is null)
            return;

        this.stylesMenu.Menu.Clear();
        this.menuBindings.RemoveAll(x => x.Entry.BindingContext is "style");

        if (this.editor.Controller is not { } controller)
            return;

        foreach (var style in controller.AvailableStyles)
        {
            var id = style.Id;
            var entry = new RibbonMenuEntry
            {
                Text = style.Name,
                BindingContext = "style",
                Command = new Command(() =>
                {
                    this.editor.Controller?.ApplyStyle(id);
                    this.AfterCommand();
                })
            };

            this.stylesMenu.Menu.Add(entry);
            this.menuBindings.Add(new MenuBinding(entry, c => c.CurrentStyleId == id));
        }
    }

    string StyleDisplayName(string styleId)
        => this.editor.Controller?.AvailableStyles.FirstOrDefault(x => x.Id == styleId)?.Name ?? styleId;

    // ---- Insert ----

    RibbonTab InsertTab()
    {
        var tab = new RibbonTab { Title = "Insert", Key = "insert" };

        var pages = new RibbonGroup { Title = "Pages", Priority = 100 };
        pages.Items.Add(this.Button(WordIcons.BlankPage, "Blank Page", c => c.InsertBlankPage(), text: "Blank Page"));
        pages.Items.Add(this.Button(OfficeIcon.PageBreak, "Page Break (Ctrl+Enter)", c => c.InsertPageBreak(), text: "Page Break"));
        pages.Items.Add(this.Menu(WordIcons.SectionBreak, "Section Break", "Section Break",
        [
            Entry("Next Page", c => c.InsertSectionBreak(SectionBreakType.NextPage)),
            Entry("Continuous", c => c.InsertSectionBreak(SectionBreakType.Continuous))
        ], enabled: c => !c.IsInTable));
        tab.Groups.Add(pages);

        var tables = new RibbonGroup { Title = "Tables", Priority = 95 };
        tables.Items.Add(this.AsyncButton(OfficeIcon.Table, "Table", _ => this.InsertTableAsync(), text: "Table", large: true));
        tab.Groups.Add(tables);

        var illustrations = new RibbonGroup { Title = "Illustrations", Priority = 90 };
        illustrations.Items.Add(this.AsyncButton(OfficeIcon.Picture, "Picture", _ => this.InsertPictureAsync(), text: "Picture", large: true));
        tab.Groups.Add(illustrations);

        var links = new RibbonGroup { Title = "Links", Priority = 85 };
        links.Items.Add(this.AsyncButton(WordIcons.Hyperlink, "Link (Ctrl+K)", c => this.EditHyperlinkAsync(c), text: "Link"));
        links.Items.Add(this.AsyncButton(WordIcons.Bookmark, "Bookmark", c => this.BookmarkAsync(c), text: "Bookmark"));
        links.Items.Add(this.Button(WordIcons.RemoveLink, "Remove Link", c => c.RemoveHyperlink(), text: "Remove Link", enabled: c => c.CurrentHyperlink is not null));
        links.Items.Add(this.Button(OfficeIcon.Next, "Open Link", _ => this.editor.OpenLinkAtCaret(), text: "Open Link", enabled: c => c.CurrentHyperlink is not null, viewOnly: true));
        tab.Groups.Add(links);

        var comments = new RibbonGroup { Title = "Comments", Priority = 80 };
        comments.Items.Add(this.AsyncButton(WordIcons.Comment, "New Comment (Ctrl+Alt+M)", c => this.NewCommentAsync(c), text: "Comment", large: true));
        tab.Groups.Add(comments);

        var chrome = new RibbonGroup { Title = "Header & Footer", Priority = 75 };
        chrome.Items.Add(this.AsyncButton(OfficeIcon.Header, "Header", _ => this.EditChromeAsync(header: true), text: "Header"));
        chrome.Items.Add(this.AsyncButton(OfficeIcon.Footer, "Footer", _ => this.EditChromeAsync(header: false), text: "Footer"));

        var numbers = new List<(string, Action<DocumentEditorController>, Func<DocumentEditorController, bool>?)>();
        foreach (var placement in new[] { PageNumberPlacement.Header, PageNumberPlacement.Footer })
        {
            foreach (var position in new[] { PageNumberPosition.Left, PageNumberPosition.Center, PageNumberPosition.Right })
            {
                var where = placement;
                var side = position;
                numbers.Add(Entry($"{(where == PageNumberPlacement.Header ? "Top" : "Bottom")} of Page — {side}", c => c.InsertPageNumber(where, side)));
            }
        }

        numbers.Add(Separator);
        numbers.Add(Entry("Current Position", c => c.InsertPageNumberField()));
        chrome.Items.Add(this.Menu(OfficeIcon.PageNumber, "Page Number", "Page Number", numbers));
        tab.Groups.Add(chrome);

        var text = new RibbonGroup { Title = "Text", Priority = 70 };
        text.Items.Add(this.AsyncButton(WordIcons.DateTime, "Date & Time", c => this.InsertDateTimeAsync(c), text: "Date & Time"));
        tab.Groups.Add(text);

        var symbols = new RibbonGroup { Title = "Symbols", Priority = 65 };
        symbols.Items.Add(this.Button(WordIcons.Symbol, "Symbol", _ => this.ShowSymbolPanel(), text: "Symbol"));
        symbols.Items.Add(this.Button(WordIcons.HorizontalLine, "Horizontal Line", c => c.InsertHorizontalLine(), text: "Line"));
        tab.Groups.Add(symbols);

        return tab;
    }

    // ---- Design ----

    RibbonTab DesignTab()
    {
        var tab = new RibbonTab { Title = "Design", Key = "design" };
        var background = new RibbonGroup { Title = "Page Background", Priority = 100 };

        background.Items.Add(this.Menu(OfficeIcon.Watermark, "Watermark", "Watermark",
        [
            Entry("CONFIDENTIAL", c => c.SetWatermarkText("CONFIDENTIAL"), c => c.WatermarkText == "CONFIDENTIAL"),
            Entry("DRAFT", c => c.SetWatermarkText("DRAFT"), c => c.WatermarkText == "DRAFT"),
            Entry("DO NOT COPY", c => c.SetWatermarkText("DO NOT COPY"), c => c.WatermarkText == "DO NOT COPY"),
            Entry("ASAP", c => c.SetWatermarkText("ASAP"), c => c.WatermarkText == "ASAP"),
            Entry("Custom Watermark…", c => _ = this.CustomWatermarkAsync(c)),
            Entry("Picture Watermark…", c => { _ = this.PickWatermarkAsync(); }),
            Separator,
            Entry("Remove Watermark", c =>
            {
                c.SetWatermarkText(null);
                this.Watermark = null;
            })
        ], large: true));

        var pageColor = this.CreateColorPicker(color =>
        {
            this.editor.Controller?.SetPageColor(color);
            this.AfterCommand();
        });

        this.extraPickers.Add(pageColor);
        background.Items.Add(OfficeRibbonItems.Row(
            OfficeRibbonItems.Host(pageColor),
            this.Button(WordIcons.PageColor, "No Page Color", c => c.SetPageColor(null), enabled: c => c.PageColor is not null)));

        tab.Groups.Add(background);
        return tab;
    }

    // ---- Layout ----

    RibbonTab LayoutTab()
    {
        var tab = new RibbonTab { Title = "Layout", Key = "layout" };

        var setup = new RibbonGroup { Title = "Page Setup", Priority = 100 };

        setup.Items.Add(this.Menu(OfficeIcon.PageMargins, "Margins", "Margins",
            PageMarginPresets.All.Select(preset => Entry(
                $"{preset.Name} — {preset.Description}",
                c => c.SetPageMargins(preset.Margins),
                c => SameMargins(c.PageMargins, preset.Margins))), large: true));

        setup.Items.Add(this.Menu(OfficeIcon.Landscape, "Orientation", "Orientation",
        [
            Entry("Portrait", c => c.SetPageOrientation(PageOrientation.Portrait), c => c.PageOrientation == PageOrientation.Portrait),
            Entry("Landscape", c => c.SetPageOrientation(PageOrientation.Landscape), c => c.PageOrientation == PageOrientation.Landscape)
        ]));

        setup.Items.Add(this.Menu(WordIcons.PageSize, "Size", "Size",
            PaperSize.Presets.Select(size => Entry($"{size.Name} — {size.Description}", c => c.SetPaperSize(size), c => c.PaperSize == size))));

        setup.Items.Add(this.Menu(WordIcons.Columns, "Columns", "Columns",
        [
            Entry("One", c => c.SetColumns(1), c => c.ColumnCount == 1),
            Entry("Two", c => c.SetColumns(2), c => c.ColumnCount == 2),
            Entry("Three", c => c.SetColumns(3), c => c.ColumnCount == 3)
        ]));

        setup.Items.Add(this.Menu(OfficeIcon.PageBreak, "Breaks", "Breaks",
        [
            Entry("Page", c => c.InsertPageBreak()),
            Separator,
            Entry("Section Break — Next Page", c => c.InsertSectionBreak(SectionBreakType.NextPage)),
            Entry("Section Break — Continuous", c => c.InsertSectionBreak(SectionBreakType.Continuous))
        ]));

        tab.Groups.Add(setup);

        var paragraph = new RibbonGroup { Title = "Paragraph", Priority = 90 };
        paragraph.Items.Add(OfficeRibbonItems.Row(
            this.Button(OfficeIcon.Outdent, "Decrease Indent", c => c.ChangeIndent(-1), text: "Indent −"),
            this.Button(OfficeIcon.Indent, "Increase Indent", c => c.ChangeIndent(1), text: "Indent +"),
            this.Menu(OfficeIcon.Indent, "Special Indent", "Special",
            [
                Entry("None", c => c.SetIndents(null, null, 0), c => Math.Abs(c.CaretFormat.IndentFirstLine) < 0.5),
                Entry("First Line (0.5\")", c => c.SetIndents(null, null, DocumentEditorController.IndentStep), c => c.CaretFormat.IndentFirstLine > 0.5),
                Entry("Hanging (0.5\")", c => c.SetIndents(null, null, -DocumentEditorController.IndentStep), c => c.CaretFormat.IndentFirstLine < -0.5)
            ])));

        paragraph.Items.Add(OfficeRibbonItems.Row(
            this.Menu(WordIcons.LineSpacing, "Spacing Before", "Before",
                new double[] { 0, 6, 12, 18, 24 }.Select(x => Entry($"{x:0} pt", c => c.SetParagraphSpacing(x, null), c => Math.Abs(c.CaretFormat.SpaceBefore - x) < 0.5))),
            this.Menu(WordIcons.LineSpacing, "Spacing After", "After",
                new double[] { 0, 6, 8, 12, 18, 24 }.Select(x => Entry($"{x:0} pt", c => c.SetParagraphSpacing(null, x), c => Math.Abs(c.CaretFormat.SpaceAfter - x) < 0.5)))));

        tab.Groups.Add(paragraph);
        return tab;
    }

    static bool SameMargins(PageMargins a, PageMargins b)
        => Math.Abs(a.Left - b.Left) < 1 && Math.Abs(a.Right - b.Right) < 1 && Math.Abs(a.Top - b.Top) < 1 && Math.Abs(a.Bottom - b.Bottom) < 1;

    // ---- References ----

    RibbonTab ReferencesTab()
    {
        var tab = new RibbonTab { Title = "References", Key = "references" };

        var toc = new RibbonGroup { Title = "Table of Contents", Priority = 100 };
        toc.Items.Add(this.Button(WordIcons.TableOfContents, "Table of Contents — from Heading 1 to 3", c => c.InsertTableOfContents(), text: "Table of Contents", large: true));
        toc.Items.Add(this.Button(WordIcons.UpdateTable, "Update Table", c => c.UpdateTableOfContents(), text: "Update Table", enabled: c => c.HasTableOfContents));
        tab.Groups.Add(toc);

        var notes = new RibbonGroup { Title = "Footnotes", Priority = 90 };
        notes.Items.Add(this.AsyncButton(WordIcons.Footnote, "Insert Footnote", c => this.InsertFootnoteAsync(c), text: "Insert Footnote", large: true));
        notes.Items.Add(this.Button(OfficeIcon.Next, "Next Footnote", c => c.NextFootnote(), text: "Next Footnote", enabled: c => c.Footnotes.Count > 0, viewOnly: true));
        tab.Groups.Add(notes);

        return tab;
    }

    // ---- Review ----

    RibbonTab ReviewTab()
    {
        var tab = new RibbonTab { Title = "Review", Key = "review" };

        var proofing = new RibbonGroup { Title = "Proofing", Priority = 100 };
        proofing.Items.Add(this.Toggle(OfficeIcon.SpellCheck, "Check spelling", c => c.IsSpellCheckEnabled = !c.IsSpellCheckEnabled, c => c.IsSpellCheckEnabled, text: "Spelling", large: true));
        proofing.Items.Add(this.Button(OfficeIcon.Previous, "Previous misspelling", _ => this.GoToSpellingError(backwards: true), enabled: c => c.IsSpellCheckEnabled));
        proofing.Items.Add(this.Button(OfficeIcon.Next, "Next misspelling", _ => this.GoToSpellingError(backwards: false), enabled: c => c.IsSpellCheckEnabled));
        proofing.Items.Add(this.AsyncButton(WordIcons.WordCount, "Word Count (Ctrl+Shift+G)", c => this.ShowWordCountAsync(c), text: "Word Count", viewOnly: true));
        tab.Groups.Add(proofing);

        var comments = new RibbonGroup { Title = "Comments", Priority = 90 };
        comments.Items.Add(this.AsyncButton(WordIcons.Comment, "New Comment (Ctrl+Alt+M)", c => this.NewCommentAsync(c), text: "New Comment", large: true));
        comments.Items.Add(this.Menu(WordIcons.DeleteComment, "Delete", "Delete",
        [
            Entry("Delete", c => c.DeleteComment()),
            Entry("Delete All Comments in Document", c => c.DeleteAllComments())
        ], enabled: c => c.Comments.Count > 0));
        comments.Items.Add(this.Button(OfficeIcon.Previous, "Previous Comment", c => c.PreviousComment(), text: "Previous", enabled: c => c.Comments.Count > 0, viewOnly: true));
        comments.Items.Add(this.Button(OfficeIcon.Next, "Next Comment", c => c.NextComment(), text: "Next", enabled: c => c.Comments.Count > 0, viewOnly: true));
        comments.Items.Add(this.Toggle(WordIcons.Comment, "Show Comments", c => c.ShowComments = !c.ShowComments, c => c.ShowComments, text: "Show", viewOnly: true));
        tab.Groups.Add(comments);

        var tracking = new RibbonGroup { Title = "Tracking", Priority = 80 };
        tracking.Items.Add(this.Toggle(WordIcons.TrackChanges, "Track Changes (Ctrl+Shift+E)", c => c.IsTrackingChanges = !c.IsTrackingChanges, c => c.IsTrackingChanges, text: "Track Changes", large: true));
        tab.Groups.Add(tracking);

        var changes = new RibbonGroup { Title = "Changes", Priority = 70 };
        changes.Items.Add(this.Menu(WordIcons.Accept, "Accept", "Accept",
        [
            Entry("Accept and Move to Next", c => c.AcceptChange()),
            Entry("Accept All Changes", c => c.AcceptAllChanges())
        ], large: true, enabled: c => c.Revisions.Count > 0));
        changes.Items.Add(this.Menu(WordIcons.Reject, "Reject", "Reject",
        [
            Entry("Reject and Move to Next", c => c.RejectChange()),
            Entry("Reject All Changes", c => c.RejectAllChanges())
        ], large: true, enabled: c => c.Revisions.Count > 0));
        changes.Items.Add(this.Button(OfficeIcon.Previous, "Previous Change", c => c.PreviousChange(), text: "Previous", enabled: c => c.Revisions.Count > 0, viewOnly: true));
        changes.Items.Add(this.Button(OfficeIcon.Next, "Next Change", c => c.NextChange(), text: "Next", enabled: c => c.Revisions.Count > 0, viewOnly: true));
        tab.Groups.Add(changes);

        return tab;
    }

    // ---- View ----

    RibbonTab ViewTab()
    {
        var tab = new RibbonTab { Title = "View", Key = "view" };

        var views = new RibbonGroup { Title = "Views", Priority = 100 };
        views.Items.Add(this.Toggle(WordIcons.ReadMode, "Read Mode", _ => this.ReadMode = !this.ReadMode, _ => this.ReadMode, text: "Read Mode", large: true, viewOnly: true));
        views.Items.Add(this.Toggle(OfficeIcon.PrintLayout, "Print Layout", _ => this.editor.PageLayout = DocumentPageLayout.Print, _ => this.editor.PageLayout == DocumentPageLayout.Print, text: "Print Layout", large: true, viewOnly: true));
        views.Items.Add(this.Toggle(OfficeIcon.FitWidth, "Web Layout — one continuous column", _ => this.editor.PageLayout = DocumentPageLayout.Reflow, _ => this.editor.PageLayout == DocumentPageLayout.Reflow, text: "Web Layout", large: true, viewOnly: true));
        tab.Groups.Add(views);

        var show = new RibbonGroup { Title = "Show", Priority = 90 };
        show.Items.Add(this.Toggle(WordIcons.NavigationPane, "Navigation Pane", _ => this.ShowNavigationPane = !this.ShowNavigationPane, _ => this.ShowNavigationPane, text: "Navigation Pane", viewOnly: true));
        show.Items.Add(this.Toggle(WordIcons.ShowMarks, "Formatting Marks", c => c.ShowFormattingMarks = !c.ShowFormattingMarks, c => c.ShowFormattingMarks, text: "Formatting Marks", viewOnly: true));
        tab.Groups.Add(show);

        // Minus, readout, plus - on one row, in that order.
        var zoom = new RibbonGroup { Title = "Zoom", Priority = 80 };
        this.zoomLabel = new Label
        {
            FontSize = 13,
            MinimumWidthRequest = 42,
            HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Center,
            VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center
        };

        this.zoomLabel.SetDynamicResource(Label.TextColorProperty, Shiny.Maui.Controls.Themes.ShinyThemeKeys.Color.OnSurfaceVariant);

        zoom.Items.Add(OfficeRibbonItems.Row(
            this.Button(OfficeIcon.ZoomOut, "Zoom out", _ => this.StepZoom(-1), enabled: _ => this.editor.Zoom > ZoomStops[0] + 0.001, viewOnly: true),
            OfficeRibbonItems.Host(this.zoomLabel),
            this.Button(OfficeIcon.ZoomIn, "Zoom in", _ => this.StepZoom(1), enabled: _ => this.editor.Zoom < ZoomStops[^1] - 0.001, viewOnly: true)));

        zoom.Items.Add(OfficeRibbonItems.Row(
            this.Button(OfficeIcon.ZoomIn, "100%", _ => this.SetZoom(1.0), text: "100%", viewOnly: true),
            this.Button(OfficeIcon.FitWidth, "Fit the page to the window", _ => this.FitToWidth(), text: "Page Width", viewOnly: true)));

        tab.Groups.Add(zoom);
        return tab;
    }

    // ---- Table (contextual) ----

    RibbonTab TableTab()
    {
        var tab = new RibbonTab { Title = "Table", Key = "table", ContextTitle = "Table Tools", IsVisible = false };

        var rows = new RibbonGroup { Title = "Rows & Columns", Priority = 100 };
        rows.Items.Add(OfficeRibbonItems.Row(
            this.Button(OfficeIcon.InsertRow, "Insert Above", c => c.InsertTableRowAbove(), text: "Above"),
            this.Button(OfficeIcon.InsertRow, "Insert Below", c => c.InsertTableRowBelow(), text: "Below")));
        rows.Items.Add(OfficeRibbonItems.Row(
            this.Button(OfficeIcon.InsertColumn, "Insert Left", c => c.InsertTableColumnLeft(), text: "Left"),
            this.Button(OfficeIcon.InsertColumn, "Insert Right", c => c.InsertTableColumnRight(), text: "Right")));
        rows.Items.Add(this.Menu(WordIcons.DeleteTable, "Delete", "Delete",
        [
            Entry("Delete Rows", c => c.DeleteTableRow()),
            Entry("Delete Columns", c => c.DeleteTableColumn()),
            Entry("Delete Table", c => c.DeleteTable())
        ], large: true));
        tab.Groups.Add(rows);

        var merge = new RibbonGroup { Title = "Merge", Priority = 90 };
        merge.Items.Add(this.Button(WordIcons.MergeCells, "Merge Cells — select across the cells first", c => c.MergeTableCells(), text: "Merge Cells", enabled: c => c.CanMergeTableCells));
        merge.Items.Add(this.Button(WordIcons.SplitCells, "Split Cells", c => c.SplitTableCell(), text: "Split Cells"));
        tab.Groups.Add(merge);

        return tab;
    }

    // ---- zoom ----

    /// <summary>The zoom stops the buttons step through.</summary>
    static readonly double[] ZoomStops = [0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0];

    void StepZoom(int direction)
    {
        var current = this.editor.Zoom;

        var next = direction > 0
            ? ZoomStops.FirstOrDefault(z => z > current + 0.001, ZoomStops[^1])
            : ZoomStops.LastOrDefault(z => z < current - 0.001, ZoomStops[0]);

        this.SetZoom(next);
    }

    void SetZoom(double value)
    {
        this.Zoom = value;
        this.editor.Zoom = value;
        this.RefreshBar();
    }

    /// <summary>Sets the zoom so the page exactly spans the window. Only in print layout.</summary>
    void FitToWidth()
    {
        if (this.editor.Controller is not { IsPaginated: true } controller)
            return;

        var available = this.editor.Width;
        var page = controller.PageWidth;

        if (available <= 0 || page <= 0)
            return;

        this.SetZoom(available / page);
    }
}
