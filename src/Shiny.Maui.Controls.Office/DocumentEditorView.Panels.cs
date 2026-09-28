using Shiny.Controls.Office.Document;
using Shiny.Controls.Office.Packaging;
using Shiny.Controls.Office.Text;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// The prompts, floating panels and side panes the ribbon's commands open.
/// </summary>
/// <remarks>
/// One-field questions use the platform's own prompt — a comment's text, a bookmark's name — because it
/// is the prompt people already know on every platform. The two that need more than one field, Replace
/// and the symbol grid, float over the page instead: a modal would hide the document the replace is
/// being checked against.
/// </remarks>
public partial class DocumentEditorView
{
    Border? replacePanel;
    Border? symbolPanel;
    Border? navigationPane;
    VerticalStackLayout? navigationList;
    Border? exitReadMode;
    string navigationSignature = string.Empty;
    DocumentPageLayout layoutBeforeReadMode = DocumentPageLayout.Print;

    // ---- shortcuts that open UI ----

    void OnShortcutRequested(object? sender, WordCommand command)
    {
        if (this.editor.Controller is not { } controller)
            return;

        switch (command)
        {
            case WordCommand.Find:
                this.ribbon.SelectedIndex = 0;
                this.findBar.Focus();
                break;

            case WordCommand.Replace:
                this.ShowReplacePanel();
                break;

            case WordCommand.Hyperlink:
                _ = this.EditHyperlinkAsync(controller);
                break;

            case WordCommand.NewComment:
                _ = this.NewCommentAsync(controller);
                break;

            case WordCommand.WordCount:
                _ = this.ShowWordCountAsync(controller);
                break;
        }
    }

    Task PasteAsync(bool plainText) => this.RunAsync(async () => await this.editor.PasteAsync(plainText));

    async Task RunAsync(Func<Task> action)
    {
        await action();
        this.AfterCommand();
    }

    // ---- insert ----

    async Task PickHighlightAsync()
    {
        var (chosen, color) = await OfficeMenus.PickHighlightAsync(OfficeMenus.PageOf(this));
        if (!chosen)
            return;

        this.editor.Controller?.SetHighlight(color);
        this.AfterCommand();
    }

    async Task InsertTableAsync()
    {
        if (await OfficeMenus.PickTableAsync(OfficeMenus.PageOf(this)) is not { } size)
            return;

        this.editor.Controller?.InsertTable(size.Rows, size.Columns);
        this.AfterCommand();
    }

    async Task InsertPictureAsync()
    {
        var (image, rejected) = await OfficeMenus.PickImageAsync(OfficeMenus.PageOf(this));

        if (rejected is not null)
        {
            this.DropRejected?.Invoke(this, rejected);
            return;
        }

        if (image is null)
            return;

        this.InsertImage(image);
    }

    void InsertImage(OfficePickedImage image)
    {
        this.editor.Controller?.InsertImage(
            image.Data,
            image.ContentType,
            this.PictureWidth,
            name: Path.GetFileNameWithoutExtension(image.FileName));

        this.AfterCommand();
    }

    /// <summary>
    /// Attaches the drop gesture to the editor surface — not the ribbon, so a drop onto the Bold button
    /// is not a drop into the document.
    /// </summary>
    void AttachDrop()
    {
        var drop = new DropGestureRecognizer { AllowDrop = true };
        drop.Drop += this.OnDropAsync;
        this.editor.GestureRecognizers.Add(drop);
    }

    async void OnDropAsync(object? sender, DropEventArgs e)
    {
        if (this.IsReadOnly || this.Document is null)
            return;

        try
        {
            foreach (var image in await OfficeFileDrop.ReadImagesAsync(e))
                this.InsertImage(image);
        }
        catch (Exception ex)
        {
            this.DropRejected?.Invoke(this, new OfficeDropRejected(string.Empty, ex.Message));
        }
    }

    /// <summary>Writes the running head or foot, seeded with whatever is there now.</summary>
    async Task EditChromeAsync(bool header)
    {
        if (this.editor.Controller is not { } controller || OfficeMenus.PageOf(this) is not { } page)
            return;

        var existing = controller.ChromeText(header);

        var typed = await page.DisplayPromptAsync(
            header ? "Header" : "Footer",
            header ? "Shown at the top of every page" : "Shown at the bottom of every page",
            "Set",
            "Cancel",
            initialValue: existing ?? string.Empty);

        if (typed is null)
            return;

        // An empty line removes it, which is the only way back out of having one.
        var text = string.IsNullOrWhiteSpace(typed) ? null : typed;

        if (header)
            controller.SetHeaderText(text);
        else
            controller.SetFooterText(text);

        this.AfterCommand();
    }

    /// <summary>
    /// Insert ▸ Link (Ctrl+K): the address, then — when nothing is selected — the text to show.
    /// </summary>
    async Task EditHyperlinkAsync(DocumentEditorController controller)
    {
        if (OfficeMenus.PageOf(this) is not { } page || this.IsReadOnly)
            return;

        var existing = controller.CurrentHyperlink;

        var address = await page.DisplayPromptAsync(
            existing is null ? "Insert Link" : "Edit Link",
            "Address — a web address, or #bookmark for a place in this document",
            "OK",
            "Cancel",
            placeholder: "https://",
            initialValue: existing?.Target ?? string.Empty,
            keyboard: Keyboard.Url);

        if (string.IsNullOrWhiteSpace(address))
            return;

        string? display = null;
        if (controller.Selection.IsEmpty)
        {
            display = await page.DisplayPromptAsync(
                "Text to display",
                null,
                "OK",
                "Cancel",
                initialValue: existing?.Text ?? address);

            if (display is null)
                return;
        }

        controller.InsertHyperlink(address, display);
        this.AfterCommand();
    }

    /// <summary>Insert ▸ Bookmark: add one here, or jump to one already in the document.</summary>
    async Task BookmarkAsync(DocumentEditorController controller)
    {
        if (OfficeMenus.PageOf(this) is not { } page)
            return;

        const string Add = "Add a bookmark here…";
        var names = controller.Bookmarks.Select(x => x.Name).ToList();

        var choice = names.Count == 0
            ? Add
            : await page.DisplayActionSheet("Bookmark", "Cancel", null, [Add, .. names.Select(x => "Go to " + x)]);

        if (choice is null or "Cancel")
            return;

        if (choice.StartsWith("Go to ", StringComparison.Ordinal))
        {
            controller.GoToBookmark(choice["Go to ".Length..]);
            this.AfterCommand();
            return;
        }

        var name = await page.DisplayPromptAsync(
            "Bookmark",
            "A name — starts with a letter; letters, digits and underscores only",
            "Add",
            "Cancel");

        if (string.IsNullOrWhiteSpace(name))
            return;

        if (!DocumentEditorController.IsValidBookmarkName(name.Trim()))
        {
            await page.DisplayAlert("Bookmark", $"\"{name}\" is not a valid bookmark name.", "OK");
            return;
        }

        controller.InsertBookmark(name.Trim());
        this.AfterCommand();
    }

    async Task NewCommentAsync(DocumentEditorController controller)
    {
        if (OfficeMenus.PageOf(this) is not { } page || this.IsReadOnly)
            return;

        var text = await page.DisplayPromptAsync("New Comment", $"Comment by {controller.Author}", "Post", "Cancel");
        if (string.IsNullOrWhiteSpace(text))
            return;

        controller.AddComment(text);
        this.AfterCommand();
    }

    async Task InsertFootnoteAsync(DocumentEditorController controller)
    {
        if (OfficeMenus.PageOf(this) is not { } page || this.IsReadOnly)
            return;

        var text = await page.DisplayPromptAsync("Insert Footnote", "The note's text", "Insert", "Cancel");
        if (text is null)
            return;

        controller.InsertFootnote(text);
        this.AfterCommand();
    }

    async Task InsertDateTimeAsync(DocumentEditorController controller)
    {
        if (OfficeMenus.PageOf(this) is not { } page)
            return;

        var formats = DocumentEditorController.DateTimeFormats(DateTime.Now).Distinct().ToArray();
        var choice = await page.DisplayActionSheet("Date & Time", "Cancel", null, formats);

        if (choice is null || choice == "Cancel")
            return;

        controller.InsertText(choice);
        this.AfterCommand();
    }

    async Task CustomWatermarkAsync(DocumentEditorController controller)
    {
        if (OfficeMenus.PageOf(this) is not { } page)
            return;

        var text = await page.DisplayPromptAsync("Custom Watermark", "Text drawn diagonally across every page", "Set", "Cancel", initialValue: controller.WatermarkText ?? string.Empty);
        if (text is null)
            return;

        controller.SetWatermarkText(string.IsNullOrWhiteSpace(text) ? null : text);
        this.AfterCommand();
    }

    /// <summary>
    /// Picks a picture and sets it as the display watermark, or clears one already there.
    /// </summary>
    async Task PickWatermarkAsync()
    {
        if (this.Watermark is not null)
        {
            this.Watermark = null;
            this.RefreshBar();
            return;
        }

        var (image, rejected) = await OfficeMenus.PickImageAsync(OfficeMenus.PageOf(this));

        if (rejected is not null || image is null)
            return;

        // Turned onto the diagonal, which is where a stamp goes.
        this.Watermark = new OfficeWatermark
        {
            Image = image.Data,
            RotationDegrees = 315
        };

        this.RefreshBar();
    }

    async Task ShowWordCountAsync(DocumentEditorController controller)
    {
        if (OfficeMenus.PageOf(this) is not { } page)
            return;

        var stats = controller.Statistics;
        var culture = System.Globalization.CultureInfo.CurrentCulture;

        var lines = string.Join(Environment.NewLine,
            $"Pages: {stats.Pages.ToString("N0", culture)}",
            $"Words: {stats.Words.ToString("N0", culture)}",
            $"Characters (no spaces): {stats.CharactersNoSpaces.ToString("N0", culture)}",
            $"Characters (with spaces): {stats.CharactersWithSpaces.ToString("N0", culture)}",
            $"Paragraphs: {stats.Paragraphs.ToString("N0", culture)}",
            $"Lines: {stats.Lines.ToString("N0", culture)}");

        if (stats.SelectedWords > 0)
            lines += Environment.NewLine + $"Selected words: {stats.SelectedWords.ToString("N0", culture)}";

        await page.DisplayAlert("Word Count", lines, "Close");
    }

    /// <summary>Steps to the next misspelling, selects it and opens the spelling menu on it.</summary>
    async void GoToSpellingError(bool backwards)
    {
        if (this.editor.Controller is not { } controller)
            return;

        if (await controller.GoToNextSpellingErrorAsync(backwards) is null)
            return;

        await this.editor.ShowSpellingMenuForCaretAsync();
    }

    // ---- replace ----

    /// <summary>
    /// Find and Replace (Ctrl+H), floating at the top right of the page.
    /// </summary>
    void ShowReplacePanel()
    {
        if (this.editor.Controller is not { } controller)
            return;

        if (this.replacePanel is not null)
        {
            this.CloseReplacePanel();
            return;
        }

        var find = new Entry { Placeholder = "Find what", Text = controller.Find.Query, FontSize = 14 };
        var replace = new Entry { Placeholder = "Replace with", FontSize = 14 };
        var matchCase = new CheckBox { IsChecked = controller.Find.Options.MatchCase };
        var wholeWord = new CheckBox { IsChecked = controller.Find.Options.WholeWord };
        var status = new Label { FontSize = 12 };
        status.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

        void Search()
        {
            controller.Find.Options = new FindOptions { MatchCase = matchCase.IsChecked, WholeWord = wholeWord.IsChecked };
            controller.Find.Query = find.Text ?? string.Empty;
            status.Text = controller.Find.Status;
        }

        find.TextChanged += (_, _) => Search();
        matchCase.CheckedChanged += (_, _) => Search();
        wholeWord.CheckedChanged += (_, _) => Search();

        Button Action(string text, Action action) => new()
        {
            Text = text,
            FontSize = 13,
            Padding = new Thickness(10, 4),
            HeightRequest = 32,
            Command = new Command(action)
        };

        var buttons = new HorizontalStackLayout
        {
            Spacing = 6,
            Children =
            {
                Action("Find Next", () =>
                {
                    Search();
                    controller.Find.FindNext();
                    status.Text = controller.Find.Status;
                }),
                Action("Replace", () =>
                {
                    Search();
                    controller.ReplaceCurrent(replace.Text ?? string.Empty);
                    status.Text = controller.Find.Status;
                    this.AfterCommand();
                }),
                Action("Replace All", () =>
                {
                    Search();
                    var count = controller.ReplaceAll(replace.Text ?? string.Empty);
                    status.Text = count == 1 ? "1 replacement" : $"{count} replacements";
                    this.AfterCommand();
                }),
                Action("Close", this.CloseReplacePanel)
            }
        };

        View Labelled(CheckBox box, string text)
        {
            var label = new Label { Text = text, FontSize = 13, VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center };
            label.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
            return new HorizontalStackLayout { Spacing = 2, Children = { box, label } };
        }

        var content = new VerticalStackLayout
        {
            Spacing = 8,
            Padding = new Thickness(14, 12),
            WidthRequest = 360,
            Children =
            {
                find,
                replace,
                new HorizontalStackLayout { Spacing = 12, Children = { Labelled(matchCase, "Match case"), Labelled(wholeWord, "Whole words only") } },
                buttons,
                status
            }
        };

        this.replacePanel = this.Float(content, LayoutOptions.End, LayoutOptions.Start, "DocReplacePanel");
        Search();
        find.Focus();
    }

    void CloseReplacePanel()
    {
        if (this.replacePanel is null)
            return;

        this.overlay.Remove(this.replacePanel);
        this.replacePanel = null;
        this.editor.FocusEditor();
    }

    /// <summary>A panel floating over the page, in the theme's surface colours.</summary>
    Border Float(View content, LayoutOptions horizontal, LayoutOptions vertical, string automationId)
    {
        var stroke = new SolidColorBrush(Colors.Gray);
        stroke.SetDynamicResource(SolidColorBrush.ColorProperty, ShinyThemeKeys.Color.OutlineVariant);

        var panel = new Border
        {
            Content = content,
            Stroke = stroke,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Margin = new Thickness(12),
            HorizontalOptions = horizontal,
            VerticalOptions = vertical,
            AutomationId = automationId,
            Shadow = new Shadow { Radius = 12, Opacity = 0.25f, Offset = new Point(0, 4) }
        };

        panel.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Surface);
        this.overlay.Add(panel);
        return panel;
    }

    // ---- symbols ----

    /// <summary>Insert ▸ Symbol: a grid of the common special characters.</summary>
    void ShowSymbolPanel()
    {
        if (this.symbolPanel is not null)
        {
            this.overlay.Remove(this.symbolPanel);
            this.symbolPanel = null;
            return;
        }

        var grid = new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            WidthRequest = 320,
            Padding = new Thickness(8)
        };

        foreach (var symbol in DocumentEditorController.CommonSymbols)
        {
            var value = symbol;
            var cell = new Button
            {
                Text = value,
                FontSize = 16,
                WidthRequest = 36,
                HeightRequest = 36,
                Padding = 0,
                Margin = 1,
                Command = new Command(() =>
                {
                    this.editor.Controller?.InsertSymbol(value);
                    this.AfterCommand();
                })
            };

            SemanticProperties.SetDescription(cell, $"Insert {value}");
            grid.Children.Add(cell);
        }

        var close = new Button { Text = "Close", FontSize = 13, HorizontalOptions = LayoutOptions.End, Margin = new Thickness(8) };
        close.Command = new Command(() =>
        {
            if (this.symbolPanel is not null)
                this.overlay.Remove(this.symbolPanel);

            this.symbolPanel = null;
            this.editor.FocusEditor();
        });

        this.symbolPanel = this.Float(new VerticalStackLayout { Children = { grid, close } }, LayoutOptions.End, LayoutOptions.Start, "DocSymbolPanel");
    }

    // ---- navigation pane ----

    void ApplyNavigationPane()
    {
        if (!this.ShowNavigationPane)
        {
            if (this.navigationPane is not null)
                this.body.Remove(this.navigationPane);

            this.navigationPane = null;
            this.navigationList = null;
            this.RefreshBar();
            return;
        }

        this.navigationList = new VerticalStackLayout { Spacing = 2, Padding = new Thickness(8) };

        var title = new Label { Text = "Navigation", FontSize = 15, FontAttributes = FontAttributes.Bold, Margin = new Thickness(8, 8, 8, 4) };
        title.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurface);

        this.navigationPane = new Border
        {
            WidthRequest = 230,
            StrokeThickness = 0,
            AutomationId = "DocNavigationPane",
            Content = new Grid
            {
                RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) },
                Children = { title, new ScrollView { Content = this.navigationList } }
            }
        };

        Grid.SetRow((BindableObject)((Grid)this.navigationPane.Content).Children[1], 1);
        this.navigationPane.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerLow);

        this.body.Add(this.navigationPane);
        Grid.SetColumn(this.navigationPane, 0);

        this.navigationSignature = string.Empty;
        this.RefreshNavigationPane();
        this.RefreshBar();
    }

    /// <summary>Refills the heading list when the headings have changed — not on every keystroke.</summary>
    void RefreshNavigationPane()
    {
        if (this.navigationList is null || this.editor.Controller is not { } controller)
            return;

        var headings = controller.Headings();
        var signature = string.Join("\n", headings.Select(x => $"{x.Level}|{x.Paragraph}|{x.Text}"));
        if (signature == this.navigationSignature)
            return;

        this.navigationSignature = signature;
        this.navigationList.Children.Clear();

        if (headings.Count == 0)
        {
            var empty = new Label { Text = "Headings in the document appear here.", FontSize = 13, Opacity = 0.7 };
            empty.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
            this.navigationList.Children.Add(empty);
            return;
        }

        foreach (var heading in headings)
        {
            var target = heading.Paragraph;
            var label = new Label
            {
                Text = heading.Text,
                FontSize = 13,
                LineBreakMode = LineBreakMode.TailTruncation,
                Padding = new Thickness(4 + ((heading.Level - 1) * 14), 4, 4, 4),
                FontAttributes = heading.Level == 1 ? FontAttributes.Bold : FontAttributes.None
            };

            label.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
            label.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() =>
                {
                    this.editor.Controller?.GoToParagraph(target);
                    this.editor.FocusEditor();
                })
            });

            this.navigationList.Children.Add(label);
        }
    }

    // ---- read mode ----

    void ApplyReadMode()
    {
        if (this.ReadMode)
        {
            this.layoutBeforeReadMode = this.editor.PageLayout;
            this.editor.PageLayout = DocumentPageLayout.Reflow;
            this.editor.IsReadOnly = true;

            var exit = new Button { Text = "Exit Read Mode", FontSize = 13, Padding = new Thickness(14, 6), AutomationId = "DocExitReadMode" };
            exit.Command = new Command(() => this.ReadMode = false);

            this.exitReadMode = this.Float(exit, LayoutOptions.End, LayoutOptions.Start, "DocReadModeBar");
        }
        else
        {
            this.editor.PageLayout = this.layoutBeforeReadMode;
            this.editor.IsReadOnly = this.IsReadOnly;

            if (this.exitReadMode is not null)
                this.overlay.Remove(this.exitReadMode);

            this.exitReadMode = null;
        }

        this.ApplyChromeVisibility();
        this.RefreshBar();
    }

    /// <summary>The ribbon shows when the toolbar is on and read mode is off.</summary>
    void ApplyChromeVisibility() => this.ribbon.IsVisible = this.ShowToolbar && !this.ReadMode;
}
