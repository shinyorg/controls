using System.Text.RegularExpressions;
using Shiny.Blazor.Controls.Office;
using Shiny.Controls.Office.Document;
using Shiny.Controls.Office.Spreadsheet.View;
using Shouldly;
using Xunit;
using Path = System.IO.Path;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// Blazor halves of the Office device-pass leftovers: the Word and Excel backstage template pictures,
/// and touch-sized status-bar buttons.
/// </summary>
public class OfficeDeviceLeftoverTests
{
    [Fact]
    public void WordAndExcelTemplatesGetPicturesOnceTheBackstageAsks()
    {
        TemplateThumbnailCache.EnsureWord();
        TemplateThumbnailCache.EnsureSpreadsheet();

        var word = TemplateThumbnailCache.Word.ShouldNotBeNull();
        word.Select(x => x.Id).ShouldBe(WordTemplates.All.Select(x => x.Id));
        word.Where(x => !x.IsBlank).ShouldAllBe(x => x.Thumbnail!.StartsWith("data:image/png;base64,"));

        var sheets = TemplateThumbnailCache.Spreadsheet.ShouldNotBeNull();
        sheets.Select(x => x.Id).ShouldBe(SpreadsheetTemplates.All.Select(x => x.Id));
        sheets.Where(x => !x.IsBlank).ShouldAllBe(x => x.Thumbnail!.StartsWith("data:image/png;base64,"));

        // Drawn once: a second visit hands back the same list.
        TemplateThumbnailCache.EnsureWord();
        TemplateThumbnailCache.Word.ShouldBeSameAs(word);
    }

    [Fact]
    public void StatusBarButtonsGrowToATouchTargetOnACoarsePointer()
    {
        var css = File.ReadAllText(Path.Combine(FindSrcRoot(), "Shiny.Blazor.Controls.Office", "Shell", "OfficeStatusBar.razor.css"));
        var coarse = Regex.Match(css, @"@media \(pointer: coarse\)\s*\{(?<body>[\s\S]*?)\n\}");
        coarse.Success.ShouldBeTrue("the status bar's 22px buttons need a touch-sized hit area");

        var body = coarse.Groups["body"].Value;
        Regex.IsMatch(body, @"\.office-status__btn\s*\{[^}]*min-width:\s*32px").ShouldBeTrue();
        Regex.IsMatch(body, @"\.office-status__btn\s*\{[^}]*height:\s*28px").ShouldBeTrue();
    }

    static string FindSrcRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(Path.Combine(candidate, "Shiny.Blazor.Controls")))
                return candidate;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate src/ from the test output directory.");
    }
}
