using System.Text.RegularExpressions;
using Shiny.Blazor.Controls.Office;
using Shiny.Controls.Office.Skia;
using Shiny.Controls.Office.Spreadsheet;
using Shouldly;
using Xunit;
using Path = System.IO.Path;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// A pinned Office theme scopes the matching light/dark token set over the whole view, not just the
/// canvas.
/// </summary>
/// <remarks>
/// Reported on the spreadsheet: <c>Theme="SpreadsheetTheme.Dark"</c> on a light page painted a dark grid
/// under a light ribbon and a light formula bar. The chrome is plain CSS over the
/// <c>--shiny-color-*</c> tokens, and nothing told those tokens the view had been pinned.
/// </remarks>
public class OfficeThemeScopeTests
{
    [Fact]
    public void UnpinnedViewsCarryNoScope()
    {
        OfficeScheme.ScopeClass((SpreadsheetTheme?)null).ShouldBeNull();
        OfficeScheme.ScopeClass((DocumentTheme?)null).ShouldBeNull();
        OfficeScheme.ScopeClass((SlideTheme?)null).ShouldBeNull();
        OfficeScheme.ScopeClass((NotebookTheme?)null).ShouldBeNull();
    }

    [Fact]
    public void ADarkPinScopesTheDarkTokens()
    {
        Classes(OfficeScheme.ScopeClass(SpreadsheetTheme.Dark)).ShouldBe(new[] { "shiny-theme-dark", "shiny-office-scoped" });
        Classes(OfficeScheme.ScopeClass(DocumentTheme.Dark)).ShouldBe(new[] { "shiny-theme-dark", "shiny-office-scoped" });
        Classes(OfficeScheme.ScopeClass(SlideTheme.Dark)).ShouldBe(new[] { "shiny-theme-dark", "shiny-office-scoped" });
        Classes(OfficeScheme.ScopeClass(NotebookTheme.Dark)).ShouldBe(new[] { "shiny-theme-dark", "shiny-office-scoped" });
    }

    [Fact]
    public void ALightPinScopesTheLightTokens()
    {
        // Pinning light on a dark page has to force the chrome light just as hard.
        Classes(OfficeScheme.ScopeClass(SpreadsheetTheme.Light)).ShouldBe(new[] { "shiny-theme-light", "shiny-office-scoped" });
        Classes(OfficeScheme.ScopeClass(DocumentTheme.Light)).ShouldBe(new[] { "shiny-theme-light", "shiny-office-scoped" });
        Classes(OfficeScheme.ScopeClass(SlideTheme.Light)).ShouldBe(new[] { "shiny-theme-light", "shiny-office-scoped" });
        Classes(OfficeScheme.ScopeClass(NotebookTheme.Light)).ShouldBe(new[] { "shiny-theme-light", "shiny-office-scoped" });
    }

    [Fact]
    public void ACustomThemeScopesByItsGroundNotByReference()
    {
        var custom = SpreadsheetTheme.Dark with { GridLine = new ArgbColor(255, 0x50, 0x50, 0x50) };
        Classes(OfficeScheme.ScopeClass(custom)).ShouldContain("shiny-theme-dark");
    }

    /// <summary>
    /// Every Office view that pairs a pinnable theme with its own chrome carries the scope on its root,
    /// and gives that root the scope's ink.
    /// </summary>
    [Theory]
    [InlineData("SpreadsheetView", "shiny-spreadsheet-host")]
    [InlineData("DocumentEditorView", "shiny-doc-editor-view")]
    [InlineData("SlideEditorView", "shiny-slide-editor-view")]
    [InlineData("NotebookEditorView", "shiny-notebook-editor-view")]
    public void ViewRootCarriesTheScope(string view, string rootClass)
    {
        var dir = Path.Combine(FindSrcRoot(), "Shiny.Blazor.Controls.Office");
        var razor = File.ReadAllText(Path.Combine(dir, view + ".razor"));
        razor.ShouldContain($"<div class=\"{rootClass} @OfficeScheme.ScopeClass(this.Theme)",
            customMessage: "unscoped, a pinned theme repaints the canvas and leaves the ribbon and bars on the page's palette");

        var css = File.ReadAllText(Path.Combine(dir, view + ".razor.css"));
        Regex.IsMatch(css, $@"\.{rootClass}\.shiny-office-scoped\s*\{{[^}}]*color:\s*var\(--shiny-color-on-surface")
            .ShouldBeTrue("chrome that inherits its text colour would keep the page's ink on the scoped ground");
    }

    /// <summary>The scope only works because the theme stylesheet re-derives the tokens on a container.</summary>
    [Fact]
    public void ThemeStylesheetDerivesTokensUnderAContainerScope()
    {
        var css = File.ReadAllText(Path.Combine(FindSrcRoot(), "Shiny.Blazor.Controls", "wwwroot", "css", "shiny-theme.css"));
        Regex.IsMatch(css, @":root,\s*\.shiny-theme-dark,\s*\.shiny-theme-light\s*\{[^}]*--shiny-color-surface:").ShouldBeTrue();
        Regex.IsMatch(css, @"(^|\n)\.shiny-theme-dark\s*\{").ShouldBeTrue();
    }

    static string[] Classes(string? value)
        => (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);

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
