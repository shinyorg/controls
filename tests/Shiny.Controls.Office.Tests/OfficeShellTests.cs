using System.Globalization;
using Shiny.Controls.Office.Icons;
using Shiny.Controls.Office.Shell;
using Shouldly;
using Xunit;

namespace Shiny.Controls.Office.Tests;

/// <summary>
/// The host-agnostic half of the Office application shell: command search ranking, zoom maths, ruler
/// geometry, the heading tree and the words the chrome shows. Both hosts draw from these, so this is
/// where their behaviour is pinned down once.
/// </summary>
public class OfficeShellTests
{
    static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    // ---- command search --------------------------------------------------------------------------

    static OfficeCommandIndex Index(params string[] labels)
    {
        var index = new OfficeCommandIndex();
        foreach (var label in labels)
            index.Add(label, () => { }, "Home");

        return index;
    }

    [Fact]
    public void AnExactLabelBeatsAPrefixWhichBeatsAWordInside()
    {
        var index = Index("Bold all headings", "Grow Font", "Font", "Font Color");

        var results = index.Search("font").Select(x => x.Command.Label).ToList();

        results[0].ShouldBe("Font");
        results[1].ShouldBe("Font Color");
        results[2].ShouldBe("Grow Font");
        results.ShouldNotContain("Bold all headings");
    }

    [Fact]
    public void TiesGoToTheShorterLabel()
    {
        var index = Index("Bold all headings", "Bold");

        index.Search("bold")[0].Command.Label.ShouldBe("Bold");
        index.Search("bol").Select(x => x.Command.Label).ShouldBe(["Bold", "Bold all headings"]);
    }

    [Fact]
    public void KeywordsAndSubsequencesAreFoundButRankBelowTheLabel()
    {
        var index = new OfficeCommandIndex();
        index.Add(new OfficeCommand("Strong", () => { }) { Keywords = ["bold"] });
        index.Add(new OfficeCommand("Bold", () => { }));
        index.Add(new OfficeCommand("Font Colour", () => { }));

        index.Search("bold").Select(x => x.Command.Label).ShouldBe(["Bold", "Strong"]);
        index.Search("fcol").Single().Command.Label.ShouldBe("Font Colour");
    }

    [Fact]
    public void EveryWordOfAQueryCanLandOnAWordOfTheLabel()
        => Index("Insert Table", "Table Properties").Search("ins tab")[0].Command.Label.ShouldBe("Insert Table");

    [Fact]
    public void AddingTheSameIdReplacesRatherThanDuplicates()
    {
        var index = new OfficeCommandIndex();
        index.Add(new OfficeCommand("Bold", () => { }) { Category = "Home" });
        index.Add(new OfficeCommand("Bold", () => { }) { Category = "Home", Shortcut = "Ctrl+B" });

        index.Count.ShouldBe(1);
        index.Commands[0].Shortcut.ShouldBe("Ctrl+B");
    }

    [Fact]
    public async Task ExecuteBestRunsTheTopMatchAndSkipsADisabledOne()
    {
        var ran = new List<string>();
        var index = new OfficeCommandIndex();
        index.Add(new OfficeCommand("Paste", () => ran.Add("paste")) { CanExecute = () => false });
        index.Add(new OfficeCommand("Print", () => ran.Add("print")));

        (await index.ExecuteBestAsync("print"))!.Label.ShouldBe("Print");
        (await index.ExecuteBestAsync("paste")).ShouldBeNull();
        ran.ShouldBe(["print"]);
    }

    [Fact]
    public void AnEmptyQueryFindsNothing() => Index("Bold").Search("  ").ShouldBeEmpty();

    [Fact]
    public void RemoveCategoryDropsAHarvest()
    {
        var index = new OfficeCommandIndex();
        index.Add(new OfficeCommand("Bold", () => { }) { Category = "Home › Font" });
        index.Add(new OfficeCommand("Word Count", () => { }) { Category = "Review" });

        index.RemoveCategory("Home");

        index.Commands.Single().Label.ShouldBe("Word Count");
    }


    // ---- zoom ------------------------------------------------------------------------------------

    [Fact]
    public void OneHundredPercentSitsInTheMiddleOfTheSlider()
    {
        var zoom = OfficeZoomModel.Default;

        zoom.ToSlider(1).ShouldBe(0.5, 1e-9);
        zoom.ToSlider(0.1).ShouldBe(0, 1e-9);
        zoom.ToSlider(5).ShouldBe(1, 1e-9);
        zoom.ToSlider(0.55).ShouldBe(0.25, 1e-9);
        zoom.ToSlider(3).ShouldBe(0.75, 1e-9);
    }

    [Fact]
    public void TheSliderSnapsTo100AndRoundsToWholePercents()
    {
        var zoom = OfficeZoomModel.Default;

        zoom.FromSlider(0.51).ShouldBe(1);
        zoom.FromSlider(0.49).ShouldBe(1);
        zoom.FromSlider(0.25).ShouldBe(0.55);
        zoom.FromSlider(0.8).ShouldBe(3.4);
        zoom.FromSlider(0.51, snap: false).ShouldBe(1.08);
    }

    [Fact]
    public void ToAndFromTheSliderRoundTrip()
    {
        var zoom = OfficeZoomModel.Default;
        foreach (var value in new[] { 0.1, 0.3, 0.75, 1.5, 2, 4.2, 5 })
            zoom.FromSlider(zoom.ToSlider(value), snap: false).ShouldBe(value, 0.011);
    }

    [Theory]
    [InlineData(1.0, 1, 1.1)]
    [InlineData(0.87, 1, 0.9)]
    [InlineData(0.87, -1, 0.8)]
    [InlineData(0.9, -1, 0.8)]
    [InlineData(0.1, -1, 0.1)]
    [InlineData(5.0, 1, 5.0)]
    public void PlusAndMinusLandOnTheNextStep(double from, int direction, double expected)
        => OfficeZoomModel.Default.StepZoom(from, direction).ShouldBe(expected, 1e-9);

    [Fact]
    public void FitPresetsUseThePageAndViewport()
    {
        var zoom = OfficeZoomModel.Default;

        // A 816px page in a 1000px viewport, 24px gutter each side: 952 / 816 = 1.16.
        zoom.Resolve(OfficeZoomPreset.PageWidth, pageWidth: 816, viewportWidth: 1000).ShouldBe(1.16);
        zoom.Resolve(OfficeZoomPreset.WholePage, 816, 1056, 1000, 600).ShouldBe(Math.Floor(552d / 1056 * 100) / 100);
        zoom.Resolve(OfficeZoomPreset.TextWidth, 816, 1056, 1000, 600, textWidth: 624).ShouldBe(1.52);
        zoom.Resolve(OfficeZoomPreset.Percent75).ShouldBe(0.75);
        zoom.Resolve(OfficeZoomPreset.Custom, custom: 9).ShouldBe(5);
    }

    [Theory]
    [InlineData("150", 1.5)]
    [InlineData("150%", 1.5)]
    [InlineData(" 75 % ", 0.75)]
    public void TypedPercentagesParse(string text, double expected)
    {
        OfficeZoomModel.TryParse(text, out var zoom).ShouldBeTrue();
        zoom.ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    public void NonsenseDoesNotParse(string text) => OfficeZoomModel.TryParse(text, out _).ShouldBeFalse();

    [Fact]
    public void ZoomFormatsAsAWholePercent() => OfficeZoomModel.Format(1.234, En).ShouldBe("123%");


    // ---- ruler -----------------------------------------------------------------------------------

    static OfficeRulerModel Ruler() => new()
    {
        PageWidth = 612,
        LeftMargin = 72,
        RightMargin = 72,
        PixelsPerPoint = 1,
        Zoom = 1,
        PageOffset = 10
    };

    [Fact]
    public void TicksAreNumberedFromTheLeftMargin()
    {
        var ruler = Ruler();
        ruler.PixelsPerPoint = 96d / 72;   // 96px to the inch: eighths

        var labels = ruler.Ticks(En).Where(x => x.Label is not null).ToList();

        // 1" of margin to the left of zero and 7.5" of page to the right.
        labels.First().Label.ShouldBe("1");
        labels.First().X.ShouldBe(ruler.PageToView(0), 0.001);
        labels.Select(x => x.Label).ShouldContain("7");
        ruler.Ticks(En).ShouldNotContain(x => Math.Abs(x.X - ruler.MarginToView(0)) < 0.001);
    }

    [Fact]
    public void TicksThinOutWhenZoomedOut()
    {
        var near = Ruler();
        near.PixelsPerPoint = 96d / 72;
        var far = Ruler();
        far.PixelsPerPoint = 96d / 72;
        far.Zoom = 0.2;

        far.Ticks().Count.ShouldBeLessThan(near.Ticks().Count / 3);
    }

    [Fact]
    public void ViewAndPageCoordinatesRoundTrip()
    {
        var ruler = Ruler();
        ruler.Zoom = 1.5;

        ruler.PageToView(100).ShouldBe(160);
        ruler.ViewToPage(160).ShouldBe(100, 1e-9);
        ruler.MarginToView(0).ShouldBe(ruler.PageToView(72));
    }

    [Fact]
    public void HeightTellsTheThreeLeftHandlesApart()
    {
        var ruler = Ruler();
        var x = ruler.MarkerX(OfficeRulerMarker.FirstLineIndent);

        ruler.HitTest(x, 0.2).Marker.ShouldBe(OfficeRulerMarker.FirstLineIndent);
        ruler.HitTest(x, 0.6).Marker.ShouldBe(OfficeRulerMarker.HangingIndent);
        ruler.HitTest(x, 0.9).Marker.ShouldBe(OfficeRulerMarker.LeftIndent);
        ruler.HitTest(ruler.MarkerX(OfficeRulerMarker.RightIndent), 0.8).Marker.ShouldBe(OfficeRulerMarker.RightIndent);
        ruler.HitTest(ruler.MarginToView(200), 0.5).Marker.ShouldBe(OfficeRulerMarker.None);
    }

    [Fact]
    public void DraggingTheFirstLineMovesOnlyTheFirstLine()
    {
        var ruler = Ruler();

        var indents = ruler.DragIndent(OfficeRulerMarker.FirstLineIndent, ruler.MarginToView(36));

        indents.ShouldBe(new OfficeIndents(0, 36, 0));
    }

    [Fact]
    public void DraggingTheHangingTriangleKeepsTheFirstLineWhereItWas()
    {
        var ruler = Ruler();
        ruler.Indents = new OfficeIndents(0, 36, 0);

        var indents = ruler.DragIndent(OfficeRulerMarker.HangingIndent, ruler.MarginToView(18));

        indents.Left.ShouldBe(18);
        indents.FirstLineStart.ShouldBe(36);
        indents.FirstLine.ShouldBe(18);
    }

    [Fact]
    public void DraggingTheBoxMovesBothTogether()
    {
        var ruler = Ruler();
        ruler.Indents = new OfficeIndents(0, -18, 0);

        var indents = ruler.DragIndent(OfficeRulerMarker.LeftIndent, ruler.MarginToView(36));

        indents.ShouldBe(new OfficeIndents(36, -18, 0));
    }

    [Fact]
    public void IndentsSnapAndStayOnThePage()
    {
        var ruler = Ruler();

        ruler.DragIndent(OfficeRulerMarker.FirstLineIndent, ruler.MarginToView(10)).FirstLine.ShouldBe(9);   // snapped to the 1/16" (4.5pt) grid
        ruler.DragIndent(OfficeRulerMarker.FirstLineIndent, ruler.MarginToView(-500)).FirstLineStart.ShouldBe(-72);
        ruler.DragIndent(OfficeRulerMarker.RightIndent, ruler.MarginToView(0)).Right
            .ShouldBe(ruler.TextWidth - OfficeRulerModel.MinimumTextWidth);
    }

    [Fact]
    public void MarginsKeepTheTextColumnOpen()
    {
        var ruler = Ruler();

        var (left, right) = ruler.DragMargin(OfficeRulerMarker.LeftMargin, ruler.PageToView(144));
        left.ShouldBe(144);
        right.ShouldBe(72);

        ruler.DragMargin(OfficeRulerMarker.RightMargin, ruler.PageToView(0)).Right
            .ShouldBe(612 - 72 - OfficeRulerModel.MinimumTextWidth);
    }

    [Fact]
    public void TabsAreAddedInOrderAndRemovedByDraggingOffTheRuler()
    {
        var ruler = Ruler();
        ruler.TabStops = ruler.WithTab(new OfficeTabStop(144));
        ruler.TabStops = ruler.WithTab(new OfficeTabStop(72, OfficeTabAlignment.Center));

        ruler.TabStops.Select(x => x.Position).ShouldBe([72, 144]);
        ruler.HitTest(ruler.MarginToView(144), 0.5).ShouldBe(new OfficeRulerHit(OfficeRulerMarker.TabStop, 1));

        ruler.DragTab(1, ruler.MarginToView(216), 0.5).Select(x => x.Position).ShouldBe([72, 216]);
        ruler.DragTab(1, ruler.MarginToView(216), 3).Select(x => x.Position).ShouldBe([72]);
        ruler.TabAt(ruler.MarginToView(-10)).ShouldBeNull();
    }

    [Fact]
    public void TheTabSelectorCycles()
    {
        OfficeRulerModel.NextAlignment(OfficeTabAlignment.Left).ShouldBe(OfficeTabAlignment.Center);
        OfficeRulerModel.NextAlignment(OfficeTabAlignment.Decimal).ShouldBe(OfficeTabAlignment.Left);
    }

    [Fact]
    public void LengthsFormatInTheRulersUnit()
    {
        var ruler = Ruler();
        ruler.FormatLength(108, En).ShouldBe("1.5\"");

        ruler.Unit = OfficeRulerUnit.Centimeters;
        ruler.FormatLength(OfficeRulerModel.PointsPerCentimeter * 2.5, En).ShouldBe("2.5 cm");
    }


    // ---- navigation ------------------------------------------------------------------------------

    [Fact]
    public void HeadingsNestUnderTheNearestHigherLevel()
    {
        var roots = OfficeHeadingTree.Build(
        [
            new("a", 1, "Intro"),
            new("b", 3, "Skipped a level"),
            new("c", 2, "Background"),
            new("d", 1, "Method"),
            new("e", 2, "Setup")
        ]);

        roots.Select(x => x.Heading.Id).ShouldBe(["a", "d"]);
        roots[0].Children.Select(x => x.Heading.Id).ShouldBe(["b", "c"]);
        roots[0].Children[0].Depth.ShouldBe(1);
        roots[1].Children.Single().Heading.Id.ShouldBe("e");
    }

    [Fact]
    public void CollapsedBranchesAreLeftOutOfTheRows()
    {
        var roots = OfficeHeadingTree.Build([new("a", 1, "A"), new("b", 2, "B"), new("c", 1, "C")]);

        OfficeHeadingTree.Flatten(roots).Count.ShouldBe(3);
        OfficeHeadingTree.Flatten(roots, new HashSet<string> { "a" }).Select(x => x.Heading.Id).ShouldBe(["a", "c"]);
    }

    [Fact]
    public void HeadingFilterAndResultCount()
    {
        var headings = new List<OfficeHeading> { new("a", 1, "Budget"), new("b", 1, "Timeline") };

        OfficeHeadingTree.Filter(headings, "bud").Single().Id.ShouldBe("a");
        OfficeHeadingTree.ResultCount(0).ShouldBe("No results");
        OfficeHeadingTree.ResultCount(1).ShouldBe("1 result");
        OfficeHeadingTree.ResultCount(4).ShouldBe("4 results");
    }


    // ---- words -----------------------------------------------------------------------------------

    [Fact]
    public void StatusSegmentsSayWhatOfficeSays()
    {
        OfficeStatusText.Page(1, 3).ShouldBe("Page 1 of 3");
        OfficeStatusText.Slide(3, 12).ShouldBe("Slide 3 of 12");
        OfficeStatusText.Words(1, culture: En).ShouldBe("1 word");
        OfficeStatusText.Words(1204, culture: En).ShouldBe("1,204 words");
        OfficeStatusText.Words(1204, 12, En).ShouldBe("12 of 1,204 words");
    }

    [Fact]
    public void ExcelAggregatesOnlyForAMultiCellSelection()
    {
        OfficeStatusText.Aggregates([4], 1, En).ShouldBeNull();
        OfficeStatusText.Aggregates([2, 4, 6], 3, En).ShouldBe("Average: 4  Count: 3  Sum: 12");
        OfficeStatusText.Aggregates([], 3, En).ShouldBe("Count: 3");
        OfficeStatusText.Aggregates([1, 2], 2, En).ShouldBe("Average: 1.5  Count: 2  Sum: 3");
    }

    [Fact]
    public void StatusItemsNotifyWhenTheirTextChanges()
    {
        var item = new OfficeStatusItem("words", "0 words");
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        item.Text = "3 words";
        item.Text = "3 words";

        changed.ShouldBe([nameof(OfficeStatusItem.Text)]);
    }

    [Fact]
    public void SaveStateWords()
    {
        OfficeSaveStateText.For(OfficeSaveState.SavedLocally).ShouldBe("Saved locally");
        OfficeSaveStateText.For(OfficeSaveState.Saving).ShouldBe("Saving…");
        OfficeSaveStateText.For(OfficeSaveState.Unsaved).ShouldBe("Unsaved changes");
        OfficeSaveStateText.For(OfficeSaveState.None).ShouldBeNull();
    }

    [Theory]
    [InlineData(4, "Good evening")]
    [InlineData(9, "Good morning")]
    [InlineData(14, "Good afternoon")]
    [InlineData(20, "Good evening")]
    public void TheGreetingFollowsTheClock(int hour, string expected)
        => OfficeBackstageText.Greeting(new DateTime(2026, 9, 28, hour, 0, 0)).ShouldBe(expected);

    [Fact]
    public void FileSizesAndRelativeDates()
    {
        OfficeBackstageText.FormatSize(512, En).ShouldBe("512 bytes");
        OfficeBackstageText.FormatSize(12_000, En).ShouldBe("12 KB");
        OfficeBackstageText.FormatSize(1_468_006, En).ShouldBe("1.4 MB");

        var now = new DateTimeOffset(2026, 9, 28, 15, 0, 0, TimeSpan.Zero);
        OfficeBackstageText.Relative(now.AddSeconds(-10), now).ShouldBe("Just now");
        OfficeBackstageText.Relative(now.AddMinutes(-12), now).ShouldBe("12m ago");
    }

    [Fact]
    public void RecentFilesPinnedFirstThenNewest()
    {
        var now = DateTimeOffset.Now;
        var ordered = OfficeBackstageText.Order(
        [
            new OfficeRecentFile("old", null, now.AddDays(-3)),
            new OfficeRecentFile("new", null, now),
            new OfficeRecentFile("pinned", null, now.AddDays(-9)) { IsPinned = true }
        ]);

        ordered.Select(x => x.Name).ShouldBe(["pinned", "new", "old"]);
    }

    [Fact]
    public void TheBlankTemplateIsAlwaysFirst()
    {
        var templates = OfficeBackstageText.WithBlank(OfficeApp.Excel, [new OfficeTemplate("budget", "Budget")]);

        templates[0].IsBlank.ShouldBeTrue();
        templates[0].Name.ShouldBe("Blank workbook");
        templates[1].Id.ShouldBe("budget");

        var custom = new OfficeTemplate("mine", "My blank") { IsBlank = true };
        OfficeBackstageText.WithBlank(OfficeApp.Word, [new OfficeTemplate("x", "X"), custom])[0].ShouldBeSameAs(custom);
    }

    [Fact]
    public void TheRailHasSaveAsACommandAndOptionsAtTheFoot()
    {
        var entries = OfficeBackstageText.Entries();

        entries.Single(x => x.Page == OfficeBackstagePage.Save).IsCommand.ShouldBeTrue();
        entries[^1].Page.ShouldBe(OfficeBackstagePage.Options);
        entries[^1].StartsFooter.ShouldBeTrue();
        entries.ShouldNotContain(x => x.Page == OfficeBackstagePage.History);
        OfficeBackstageText.Entries(includeHistory: true).ShouldContain(x => x.Page == OfficeBackstagePage.History);
    }

    [Fact]
    public void DocumentInfoListsOnlyWhatItKnows()
    {
        var info = new OfficeDocumentInfo
        {
            Author = "Allan Ritchie",
            SizeBytes = 20_480,
            Statistics = [new("Words", "1,204")]
        };

        info.Properties(En).Select(x => x.Name).ShouldBe(["Size", "Words", "Author"]);
    }


    // ---- app, formats, options -------------------------------------------------------------------

    [Fact]
    public void EachAppHasItsLetterAccentFormatAndViews()
    {
        OfficeAppInfo.For(OfficeApp.Word).Letter.ShouldBe("W");
        OfficeAppInfo.For(OfficeApp.Excel).AccentHex.ShouldBe("#107C41");
        OfficeAppInfo.For(OfficeApp.PowerPoint).NativeFormat.Extension.ShouldBe(".pptx");
        OfficeAppInfo.For(OfficeApp.Word).ViewModes.Select(x => x.Id).ShouldBe(["read", "print", "web"]);
        OfficeAppInfo.For(OfficeApp.Excel).ViewModes.Select(x => x.Id).ShouldBe(["normal", "pageLayout", "pageBreak"]);
        OfficeAppInfo.For(OfficeApp.PowerPoint).ViewModes.Select(x => x.Id).ShouldBe(["normal", "sorter", "reading"]);
    }

    [Fact]
    public void SaveAsLeadsWithTheNativeFormatAndExportOmitsIt()
    {
        foreach (var app in Enum.GetValues<OfficeApp>())
        {
            var info = OfficeAppInfo.For(app);
            info.SaveAsFormats[0].ShouldBe(info.NativeFormat);
            info.ExportFormats.ShouldNotContain(info.NativeFormat);
        }

        OfficeFileFormats.Pdf.FileNameFor("Report.docx").ShouldBe("Report.pdf");
        OfficeFileFormats.Csv.FileNameFor(null).ShouldBe("Document.csv");
    }

    [Theory]
    [InlineData("Allan Ritchie", "AR")]
    [InlineData("cher", "C")]
    [InlineData("Mary Jane Watson", "MW")]
    [InlineData("", "?")]
    public void InitialsComeFromTheName(string name, string expected) => OfficeColorText.Initials(name).ShouldBe(expected);

    [Fact]
    public void OptionsDeriveInitialsUntilTheyAreSet()
    {
        var options = new OfficeShellOptions { UserName = "Allan Ritchie" };
        options.EffectiveInitials.ShouldBe("AR");

        options.Initials = "ajr";
        options.EffectiveInitials.ShouldBe("AJR");
    }

    [Fact]
    public void TheAccentShadeIsDarker()
    {
        var info = OfficeAppInfo.Word;
        info.AccentDark.R.ShouldBeLessThan(info.Accent.Color.R);
        info.AccentDark.B.ShouldBeLessThan(info.Accent.Color.B);
    }


    // ---- layout, styles, icons -------------------------------------------------------------------

    [Fact]
    public void BelowSixHundredPixelsTheShellGoesCompact()
    {
        var phone = OfficeShellLayout.For(420);
        phone.IsCompact.ShouldBeTrue();
        phone.ShowSearchBox.ShouldBeFalse();
        phone.ShowRulers.ShouldBeFalse();
        phone.ShowSidePanes.ShouldBeFalse();
        phone.SimplifiedRibbon.ShouldBeTrue();

        var desktop = OfficeShellLayout.For(1280);
        desktop.IsCompact.ShouldBeFalse();
        desktop.ShowZoomSlider.ShouldBeTrue();

        OfficeShellLayout.For(0).IsCompact.ShouldBeFalse();   // unmeasured is a desktop
        OfficeShellLayout.For(700).ShowZoomSlider.ShouldBeFalse();
    }

    [Fact]
    public void StylePreviewsClampTheirSize()
    {
        var title = OfficeStyleDescriptors.Word.Single(x => x.Id == "Title");
        title.PreviewSize.ShouldBe(18);
        new OfficeStyleDescriptor("tiny", "Tiny") { FontSize = 6 }.PreviewSize.ShouldBe(8);
        OfficeStyleDescriptors.IndexOf(OfficeStyleDescriptors.Word, "heading1").ShouldBe(2);
    }

    [Theory]
    [InlineData(0, 7, 5, 14, 3)]
    [InlineData(5, 2, 5, 14, 2)]
    [InlineData(3, 4, 5, 14, 3)]
    [InlineData(0, 13, 5, 14, 9)]
    [InlineData(0, 2, 5, 3, 0)]
    public void TheStripScrollsTheSelectedStyleIntoView(int first, int selected, int visible, int count, int expected)
        => OfficeStyleDescriptors.ScrollIntoView(first, selected, visible, count).ShouldBe(expected);

    [Fact]
    public void EveryShellIconHasArtwork()
    {
        foreach (var icon in Enum.GetValues<OfficeShellIcon>())
            OfficeIcons.Shapes(icon).ShouldNotBeEmpty($"{icon} draws nothing");
    }
}
