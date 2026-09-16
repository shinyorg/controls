using System.Runtime.CompilerServices;
using Shiny.Controls.Office.Document;
using Shiny.Controls.Office.Notebook;
using Shiny.Controls.Office.Presentation;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Spreadsheet.View;
using Shiny.Controls.Office.Text;
using Shouldly;
using Xunit;

namespace Shiny.Controls.Office.Tests;

/// <summary>
/// The documents belong to the app and outlive the controls showing them. A controller subscribed to
/// its document must not be kept alive by it: the controller's own <c>Changed</c> holds the control, and
/// through the control the whole page, so every visit to a page over a view-model-held document leaked
/// one more - and no host reliably calls Dispose.
/// </summary>
public class ControllerLifetimeTests
{
    sealed class Fixed : ITextMeasurer
    {
        public TextMetrics Measure(ReadOnlySpan<char> text, TextStyle style)
            => new(text.Length * 8, style.FontSize * 0.8, style.FontSize * 0.2);

        public TextMetrics LineMetrics(TextStyle style)
            => new(0, style.FontSize * 0.8, style.FontSize * 0.2);
    }

    static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [Fact]
    public void ANotebookDoesNotKeepItsEditorControllerAlive()
    {
        var document = NotebookDocument.Create();
        var reference = Abandon(() => new NotebookEditorController(document, new Fixed()));

        Collect();

        reference.TryGetTarget(out _).ShouldBeFalse();

        // The orphaned forwarders unhook themselves on the next raise rather than throwing.
        Should.NotThrow(document.NotifyStructureChanged);
    }

    [Fact]
    public void ANotebookStillDrivesALiveController()
    {
        var document = NotebookDocument.Create();
        var controller = new NotebookEditorController(document, new Fixed());
        var changed = 0;
        var edited = 0;
        controller.Changed += (_, _) => changed++;
        controller.Edited += (_, _) => edited++;

        Collect();
        document.NotifyStructureChanged();

        changed.ShouldBeGreaterThan(0);
        edited.ShouldBeGreaterThan(0);
        GC.KeepAlive(controller);
    }

    [Fact]
    public async Task ADocumentDoesNotKeepItsEditorControllerAlive()
    {
        using var document = await WordDocument.OpenAsync(new MemoryStream(DocumentFixture.Build()), editable: true);
        var reference = Abandon(() => new DocumentEditorController(document, new Fixed()));

        Collect();

        reference.TryGetTarget(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task ADeckDoesNotKeepItsEditorControllerAlive()
    {
        using var source = new MemoryStream(SlideFixture.Build(), writable: false);
        var deck = await SlideDeck.OpenAsync(source, editable: true);
        var reference = Abandon(() => new SlideEditorController(deck, new Fixed()));

        Collect();

        reference.TryGetTarget(out _).ShouldBeFalse();
        GC.KeepAlive(deck);
    }

    [Fact]
    public void AWorkbookDoesNotKeepItsControllerAlive()
    {
        using var workbook = Workbook.Create("Sheet1");
        var reference = Abandon(() => new SpreadsheetController(workbook, workbook["Sheet1"]));

        Collect();

        reference.TryGetTarget(out _).ShouldBeFalse();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference<T> Abandon<T>(Func<T> create) where T : class
    {
        var controller = create();

        // A host always hooks Changed - that subscription is what carried the control into the leak.
        return new WeakReference<T>(controller);
    }
}
