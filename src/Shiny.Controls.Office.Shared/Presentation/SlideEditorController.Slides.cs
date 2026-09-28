using DocumentFormat.OpenXml.Packaging;
using Shiny.Controls.Office.Editing;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;

namespace Shiny.Controls.Office.Presentation;

/// <summary>
/// The deck-level tabs: Design, Transitions, Animations, Slide Show, the Insert tab's charts, media and
/// header &amp; footer, tables, sections, the outline, and Find &amp; Replace.
/// </summary>
public sealed partial class SlideEditorController
{
    // ---- Design ----

    /// <summary>Applies a built-in theme to the whole deck.</summary>
    public void ApplyTheme(SlideThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (this.IsReadOnly)
            return;

        this.Execute(new ApplySlideThemeCommand(theme.Colors, theme.MajorFont, theme.MinorFont, theme.Name));
    }

    /// <summary>Applies a colour variant, keeping the theme's fonts.</summary>
    public void ApplyColorVariant(SlideColorScheme colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (!this.IsReadOnly)
            this.Execute(new ApplySlideThemeCommand(colors));
    }

    /// <summary>Design ▸ Slide Size. Scales the content to fit unless told not to.</summary>
    public void SetSlideSize(double width, double height, bool scaleContent = true)
    {
        if (this.IsReadOnly)
            return;

        this.ClearSelection();
        this.Execute(new SetSlideSizeCommand(width, height, scaleContent));
    }

    /// <summary>Format Background, for this slide or — with <paramref name="applyToAll"/> — every slide.</summary>
    public void SetBackground(SlideBackgroundSpec? background, bool applyToAll = false)
    {
        if (this.IsReadOnly || this.Count == 0)
            return;

        this.Execute(new SetSlideBackgroundCommand(applyToAll ? Enumerable.Range(0, this.Count).ToList() : [this.Index], background));
    }

    /// <summary>Reset Background: the slide inherits its layout's again.</summary>
    public void ResetBackground(bool applyToAll = false) => this.SetBackground(null, applyToAll);

    // ---- Transitions ----

    /// <summary>The current slide's transition, or null for none.</summary>
    public SlideTransition? CurrentTransition => this.Current?.Transition;

    /// <summary>Sets the current slide's transition — or every slide's, for Apply To All.</summary>
    public void SetTransition(SlideTransition? transition, bool applyToAll = false)
    {
        if (this.IsReadOnly || this.Count == 0)
            return;

        var slides = applyToAll ? Enumerable.Range(0, this.Count).ToList() : [this.Index];
        var commands = slides.Select(i => (IEditCommand<SlideDeck>)new SetSlideTransitionCommand(i, transition)).ToList();
        this.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand<SlideDeck>("Apply to all", commands));
    }

    /// <summary>Picks a transition from the gallery, keeping the slide's timing.</summary>
    public void SetTransitionKind(SlideTransitionKind kind)
    {
        var current = this.CurrentTransition;
        var fresh = SlideTransition.Create(kind) with
        {
            AdvanceOnClick = current?.AdvanceOnClick ?? true,
            AdvanceAfter = current?.AdvanceAfter
        };

        this.SetTransition(kind == SlideTransitionKind.None && fresh.AdvanceOnClick && fresh.AdvanceAfter is null ? null : fresh);
    }

    /// <summary>Changes one aspect of the current transition — direction, duration, timing.</summary>
    public void UpdateTransition(Func<SlideTransition, SlideTransition> change)
    {
        var current = this.CurrentTransition ?? new SlideTransition(SlideTransitionKind.None, SlideTransitionDirection.FromBottom, TimeSpan.Zero);
        this.SetTransition(change(current));
    }

    /// <summary>Transitions ▸ Apply To All: every slide takes this slide's transition.</summary>
    public void ApplyTransitionToAll() => this.SetTransition(this.CurrentTransition, applyToAll: true);

    // ---- Animations ----

    /// <summary>The current slide's animation list — the animation pane.</summary>
    public IReadOnlyList<SlideAnimation> Animations => this.Current?.Animations ?? [];

    /// <summary>The animations on the selected shape, with their positions in <see cref="Animations"/>.</summary>
    public IReadOnlyList<(int Index, SlideAnimation Animation)> SelectionAnimations
        => this.Selection is { Id: > 0 } shape
            ? this.Animations.Select((a, i) => (i, a)).Where(x => x.a.ShapeId == shape.Id).ToList()
            : [];

    /// <summary>
    /// The animation gallery: replaces the selected shapes' effects with this one — or, with
    /// <paramref name="add"/>, adds it after them, which is PowerPoint's Add Animation.
    /// </summary>
    public void Animate(SlideAnimationEffect effect, bool add = false)
    {
        if (this.IsReadOnly || this.Current is not { } slide)
            return;

        var ids = this.SelectedShapes.Select(i => slide.Shapes[i].Id).Where(x => x > 0).Distinct().ToList();
        if (ids.Count == 0)
            return;

        var list = this.Animations.ToList();
        if (!add)
        {
            // A shape's existing effect is replaced in place, keeping its trigger and its place in the
            // order; a shape with none gets one on the end.
            foreach (var id in ids)
            {
                var at = list.FindIndex(x => x.ShapeId == id);
                list.RemoveAll(x => x.ShapeId == id);
                var fresh = SlideAnimation.Create(id, effect);
                if (at >= 0)
                    list.Insert(Math.Min(at, list.Count), fresh);
                else
                    list.Add(fresh);
            }
        }
        else
        {
            list.AddRange(ids.Select(id => SlideAnimation.Create(id, effect)));
        }

        this.Execute(new SetSlideAnimationsCommand(this.Index, list));
    }

    /// <summary>Removes the selected shapes' animations — the gallery's None.</summary>
    public void RemoveAnimations()
    {
        if (this.IsReadOnly || this.Current is not { } slide)
            return;

        var ids = this.SelectedShapes.Select(i => slide.Shapes[i].Id).ToHashSet();
        this.SetAnimations(this.Animations.Where(x => !ids.Contains(x.ShapeId)).ToList());
    }

    /// <summary>Replaces the whole list — what the animation pane's reorder, retime and delete send.</summary>
    public void SetAnimations(IReadOnlyList<SlideAnimation> animations)
    {
        if (!this.IsReadOnly && this.Count > 0)
            this.Execute(new SetSlideAnimationsCommand(this.Index, animations));
    }

    /// <summary>Changes one animation in the list — its trigger, duration, delay or direction.</summary>
    public void UpdateAnimation(int index, Func<SlideAnimation, SlideAnimation> change)
    {
        var list = this.Animations.ToList();
        if (index < 0 || index >= list.Count)
            return;

        list[index] = change(list[index]);
        this.SetAnimations(list);
    }

    /// <summary>Moves an animation earlier (negative) or later (positive) in the list — Reorder Animation.</summary>
    public void MoveAnimation(int index, int direction)
    {
        var list = this.Animations.ToList();
        var target = index + Math.Sign(direction);
        if (index < 0 || index >= list.Count || target < 0 || target >= list.Count)
            return;

        (list[index], list[target]) = (list[target], list[index]);
        this.SetAnimations(list);
    }

    public void RemoveAnimation(int index)
    {
        var list = this.Animations.ToList();
        if (index < 0 || index >= list.Count)
            return;

        list.RemoveAt(index);
        this.SetAnimations(list);
    }

    /// <summary>Draw the numbered click markers beside animated shapes — on while the Animations tab is up.</summary>
    public bool ShowAnimationMarkers { get; set; }

    /// <summary>
    /// The numbered markers PowerPoint draws beside each animated shape in edit mode, in viewport
    /// coordinates: the click the effect plays on, stacked when a shape has several.
    /// </summary>
    public IEnumerable<(string Label, SlideRect Rect)> AnimationMarkers()
    {
        if (!this.ShowAnimationMarkers || this.Mode != SlideViewMode.Single || this.Current is not { } slide)
            yield break;

        var schedule = SlideAnimationTimeline.Schedule(slide.Animations);
        var perShape = new Dictionary<uint, int>();
        const double size = 16;

        foreach (var item in schedule)
        {
            var shape = slide.Shapes.FirstOrDefault(x => x.Id == item.Animation.ShapeId && x.IsEditable);
            if (shape is null || this.BoundsOf(shape) is not { } bounds)
                continue;

            var stack = perShape.GetValueOrDefault(item.Animation.ShapeId);
            perShape[item.Animation.ShapeId] = stack + 1;

            yield return (item.Click.ToString(System.Globalization.CultureInfo.InvariantCulture),
                new SlideRect(bounds.X - size - 2, bounds.Y + stack * (size + 2), size, size));
        }
    }

    // ---- Slide Show ----

    /// <summary>Hide Slide: the slide stays in the deck but the show skips it.</summary>
    public void ToggleHideSlide()
    {
        if (!this.IsReadOnly && this.Current is { } slide)
            this.Execute(new SetSlideHiddenCommand(this.Index, !slide.IsHidden));
    }

    /// <summary>A show over this deck, from the first slide or from this one — F5 and Shift+F5.</summary>
    public SlideShowController CreateShow(bool fromCurrent)
    {
        this.ClearSelection();
        return new SlideShowController(this.deck, fromCurrent ? this.Index : 0);
    }

    // ---- Insert ----

    /// <summary>Inserts a chart with its data and selects it. Sized to the slide when no size is given.</summary>
    public void AddChart(SlideChart chart, double? x = null, double? y = null, double? width = null, double? height = null)
    {
        ArgumentNullException.ThrowIfNull(chart);
        if (this.IsReadOnly || this.deck.PartAt(this.Index) is not { } part || this.deck.TreeAt(this.Index) is null)
            return;

        var w = width ?? this.Deck.SlideWidth * 0.6;
        var h = height ?? this.Deck.SlideHeight * 0.6;
        var left = x ?? (this.Deck.SlideWidth - w) / 2;
        var top = y ?? (this.Deck.SlideHeight - h) / 2;

        var chartPart = part.AddNewPart<ChartPart>();
        SlideChartXml.Feed(chartPart, SlideChartXml.Write(chart));
        this.deck.MarkPartDirty(chartPart);

        this.AddElement(SlideChartXml.Frame(part.GetIdOfPart(chartPart), left, top, w, h, "Chart"));
    }

    /// <summary>Replaces the selected chart's data — the data sheet's OK.</summary>
    public void SetChartData(SlideChart chart)
    {
        if (!this.IsReadOnly && this.SelectedChart is not null)
            this.Execute(new SetChartDataCommand(this.Index, this.selected, chart));
    }

    /// <summary>
    /// Inserts audio or video: the clip embedded the way PowerPoint 2010+ embeds it, drawn as its poster
    /// frame with a play mark over it.
    /// </summary>
    /// <param name="poster">An encoded image for the poster frame; a plain dark one is used without.</param>
    public void AddMedia(byte[] data, string contentType, bool isVideo, byte[]? poster = null, string posterContentType = "image/png", string name = "Media")
    {
        ArgumentNullException.ThrowIfNull(data);
        if (this.IsReadOnly || data.Length == 0 || this.deck.PartAt(this.Index) is not { } part || this.deck.TreeAt(this.Index) is null)
            return;

        var extension = contentType switch
        {
            "video/mp4" => ".mp4",
            "video/quicktime" => ".mov",
            "video/webm" => ".webm",
            "audio/mpeg" => ".mp3",
            "audio/mp4" or "audio/x-m4a" => ".m4a",
            "audio/wav" or "audio/x-wav" => ".wav",
            _ => isVideo ? ".mp4" : ".mp3"
        };

        var media = this.deck.CreateMediaPart(contentType, extension);
        using (var stream = new MemoryStream(data, writable: false))
            media.FeedData(stream);

        var link = isVideo ? part.AddVideoReferenceRelationship(media).Id : part.AddAudioReferenceRelationship(media).Id;
        var embed = part.AddMediaReferenceRelationship(media).Id;

        var posterId = this.deck.AddImagePart(this.Index, poster ?? SlideMediaXml.PlaceholderPoster, poster is null ? "image/png" : posterContentType);
        if (posterId is null)
            return;

        var (w, h) = isVideo ? (this.Deck.SlideWidth * 0.5, this.Deck.SlideWidth * 0.5 * 9 / 16) : (64d, 64d);
        this.AddElement(SlideMediaXml.Build(posterId, link, embed, isVideo, (this.Deck.SlideWidth - w) / 2, (this.Deck.SlideHeight - h) / 2, w, h, name));
    }

    /// <summary>Insert ▸ Icons: a small preset shape in the theme's first accent.</summary>
    public void AddIcon(Shapes.ShapeGeometry geometry, double size = 96)
        => this.AddShape(geometry, (this.Deck.SlideWidth - size) / 2, (this.Deck.SlideHeight - size) / 2, size, size, this.ThemeColorScheme?.Accent1);

    /// <summary>The Header &amp; Footer dialog's Apply (this slide) or Apply to All.</summary>
    public void ApplyHeaderFooter(SlideHeaderFooter settings, bool applyToAll)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (this.IsReadOnly || this.Count == 0)
            return;

        this.ClearSelection();
        this.Execute(new ApplyHeaderFooterCommand(applyToAll ? Enumerable.Range(0, this.Count).ToList() : [this.Index], settings));
    }

    /// <summary>What the current slide's date, number and footer are, to fill the dialog in.</summary>
    public SlideHeaderFooter CurrentHeaderFooter => this.Current is { } slide ? SlideHeaderFooter.Of(slide) : new SlideHeaderFooter();

    // ---- Tables ----

    (int Row, int Column)? cellAnchor;

    /// <summary>The block of cells selected in the table being edited — the active cell and the one Shift+click extended from.</summary>
    public (int Row, int Column, int ToRow, int ToColumn)? CellSelection
        => this.activeCell is { } active
            ? (Math.Min(active.Row, (this.cellAnchor ?? active).Row), Math.Min(active.Column, (this.cellAnchor ?? active).Column),
               Math.Max(active.Row, (this.cellAnchor ?? active).Row), Math.Max(active.Column, (this.cellAnchor ?? active).Column))
            : null;

    /// <summary>Extends the cell selection to a cell — Shift+click inside a table.</summary>
    public void ExtendCellSelection(int row, int column)
    {
        if (this.activeCell is not { } active)
            return;

        this.cellAnchor ??= active;
        this.activeCell = (row, column);
        this.RaiseChanged();
    }

    /// <summary>Rows and columns in and out, merge and split — Table Layout.</summary>
    public void EditTable(SlideTableEdit edit)
    {
        if (this.IsReadOnly || this.SelectedTable is null)
            return;

        var (row, column, toRow, toColumn) = this.CellSelection ?? (0, 0, 0, 0);
        var index = this.selected;

        this.Execute(new EditSlideTableCommand(this.Index, index, edit, row, column, toRow, toColumn));
        this.cellAnchor = null;

        // Deleting the last row or column deletes the table.
        if (this.Current?.Shapes.ElementAtOrDefault(index)?.Table is not { } table)
        {
            this.ClearSelection();
            return;
        }

        // The caret stays in the table, on a cell that still exists.
        var r = Math.Clamp(edit == SlideTableEdit.InsertRowBelow ? row + 1 : row, 0, table.Rows.Count - 1);
        var c = Math.Clamp(edit == SlideTableEdit.InsertColumnRight ? column + 1 : column, 0, (table.Rows.ElementAtOrDefault(r)?.Count ?? 1) - 1);
        this.selected = index;
        this.IsEditingText = true;
        this.activeCell = (r, c);
        this.anchor = this.caret = this.Position(0, 0);
        this.RefreshCaretFormat();
        this.RaiseChanged();
    }

    /// <summary>Table Design ▸ Shading for the selected cells, or the whole table with none selected.</summary>
    public void SetCellFill(ArgbColor? color)
    {
        if (this.IsReadOnly || this.SelectedTable is not { } table)
            return;

        var (row, column, toRow, toColumn) = this.CellSelection ?? (0, 0, table.Rows.Count - 1, table.ColumnWidths.Count - 1);
        this.Execute(new SetTableCellFillCommand(this.Index, this.selected, row, column, toRow, toColumn, color));
    }

    /// <summary>Table Design ▸ Table Styles.</summary>
    public void SetTableStyle(SlideTableStyleOption style)
    {
        if (!this.IsReadOnly && this.SelectedTable is not null)
            this.Execute(new SetTableStyleCommand(this.Index, this.selected, style.Id, null));
    }

    /// <summary>Table Design ▸ Table Style Options.</summary>
    public void SetTableStyleFlags(SlideTableStyleFlags flags)
    {
        if (!this.IsReadOnly && this.SelectedTable is not null)
            this.Execute(new SetTableStyleCommand(this.Index, this.selected, null, flags));
    }

    // ---- Sections ----

    /// <summary>The deck's sections — see <see cref="SlideDeck.Sections"/>.</summary>
    public IReadOnlyList<SlideSection> Sections => this.deck.Sections;

    /// <summary>Add Section: a new section starting at the current slide.</summary>
    public void AddSection(string name = "Untitled Section")
    {
        if (!this.IsReadOnly && this.Count > 0)
            this.Execute(new EditSlideSectionCommand(SlideSectionEdit.Add, this.Index, name));
    }

    public void RenameSection(int section, string name)
    {
        if (!this.IsReadOnly)
            this.Execute(new EditSlideSectionCommand(SlideSectionEdit.Rename, section, name));
    }

    /// <summary>Remove Section — keeping its slides, unless <paramref name="withSlides"/>.</summary>
    public void RemoveSection(int section, bool withSlides = false)
    {
        if (!this.IsReadOnly)
            this.Execute(new EditSlideSectionCommand(withSlides ? SlideSectionEdit.RemoveWithSlides : SlideSectionEdit.Remove, section));
    }

    public void MoveSection(int section, int direction)
    {
        if (!this.IsReadOnly)
            this.Execute(new EditSlideSectionCommand(direction < 0 ? SlideSectionEdit.MoveUp : SlideSectionEdit.MoveDown, section));
    }

    // ---- Outline ----

    /// <summary>Every slide's outline — the Outline view's contents.</summary>
    public IReadOnlyList<SlideOutlineEntry> Outline
        => this.deck.Slides.Select((slide, i) => SlideOutlineEntry.Of(slide, i)).ToList();

    /// <summary>Writes one slide's outline back — title, body, or both.</summary>
    public void SetOutline(int slide, string? title, string? body)
    {
        if (this.IsReadOnly)
            return;

        this.Execute(new SetSlideOutlineCommand(slide, title, body is null ? null : SlideOutlineEntry.ParseBody(body)));
    }

    // ---- Find & Replace ----

    /// <summary>
    /// Replaces the match the selection is on and steps to the next one. Returns false when there was no
    /// match under the selection.
    /// </summary>
    public bool ReplaceCurrent(string replacement)
    {
        if (this.IsReadOnly || !this.Find.IsSearching || !this.IsEditingText)
            return false;

        var range = this.TextSelection.Normalized();
        if (!this.Find.Matches.Any(x => x.Range == range))
        {
            // Not on a match yet: the first press finds one, as PowerPoint's Replace does.
            this.Find.FindNext();
            return false;
        }

        this.InsertText(replacement);
        this.Find.Invalidate();
        this.Find.FindNext();
        return true;
    }

    /// <summary>Replaces every match in the deck as one undo step. Returns how many were replaced.</summary>
    public int ReplaceAll(string replacement)
    {
        if (this.IsReadOnly || !this.Find.IsSearching)
            return 0;

        var matches = this.Find.Matches.ToList();
        if (matches.Count == 0)
            return 0;

        this.ClearSelection();

        // Last to first, so each match's offsets are still right when its turn comes.
        var commands = new List<IEditCommand<SlideDeck>>();
        foreach (var match in matches.OrderByDescending(x => x.Slide).ThenByDescending(x => x.Shape).ThenByDescending(x => x.Paragraph).ThenByDescending(x => x.Start))
        {
            commands.Add(new DeleteSlideRangeCommand(match.Range));
            if (replacement.Length > 0)
                commands.Add(new InsertSlideTextCommand(match.Position, replacement));
        }

        this.Execute(new CompositeCommand<SlideDeck>("Replace all", commands));
        this.deck.Undo.BreakCoalescing();
        this.Find.Invalidate();
        return matches.Count;
    }
}
