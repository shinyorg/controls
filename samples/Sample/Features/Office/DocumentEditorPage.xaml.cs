using Shiny.Controls.Office.Document;
using Shiny.Controls.Office.Packaging;
using Shiny.Controls.Office.Skia;
using Shiny.Controls.Office.Shell;
using Shiny.Controls.Office.Spelling;
using Shiny.Maui.Controls.Office;

namespace Sample.Features.Office;

/// <summary>
/// The .docx editor, with the platform's own spell checker.
/// </summary>
/// <remarks>
/// <para>
/// The toolbar carries the insert gallery — shapes, a table and a picture — plus the highlight split
/// button. Everything it inserts is inline, so it flows with the text; select one and drag a handle to
/// resize it. Dragging an image file onto the canvas from the desktop does the same thing as the
/// picture button, on the platforms that have a file drag.
/// </para>
/// <para>
/// Nothing here registers a checker: <c>Shiny.Maui.Controls.Office</c> installs the platform one —
/// UITextChecker on iOS, NSSpellChecker on macOS, Android's text services, the Windows COM checker —
/// as soon as the package is touched. Right-click (or long-press) a red-underlined word for
/// corrections; the same menu offers Ignore and Add to dictionary, and the last of those writes to
/// the user's real dictionary, shared with every other app on the device.
/// </para>
/// </remarks>
public partial class DocumentEditorPage : ContentPage
{
    WordDocument? document;
    bool dark;
    int marginPreset;

    public DocumentEditorPage()
    {
        this.InitializeComponent();
        SampleSourceCode.Attach(this);
        this.UpdateStatus();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (this.document is not null)
            return;

        var bytes = SampleOfficeDocuments.BuildDocument();
        this.document = await WordDocument.OpenAsync(new MemoryStream(bytes), editable: true);
        this.Editor.Document = this.document;
        this.Editor.DocumentChanged += this.OnDocumentChanged;
        this.Editor.DropRejected += this.OnDropRejected;
        this.Editor.SaveRequested += this.OnSaveRequested;
        this.Editor.SaveAsRequested += this.OnSaveAsRequested;
        this.Editor.ExportRequested += this.OnExportRequested;
        this.Editor.PrintRequested += this.OnPrintRequested;
        this.Editor.DocumentOpened += this.OnDocumentOpened;
        this.Editor.RecentFiles =
        [
            new OfficeRecentFile("Quarterly report.docx", "Documents", DateTimeOffset.Now.AddHours(-3)) { IsPinned = true },
            new OfficeRecentFile("Letter to landlord.docx", "Documents", DateTimeOffset.Now.AddDays(-2))
        ];

        this.UpdateStatus();
    }

    // The shell does no I/O: Save, Save As, Export and Print are the host's. This sample writes to the
    // cache directory and says where, which is enough to show each one arriving.

    void OnSaveRequested(object? sender, EventArgs e)
    {
        if (this.document is null)
            return;

        var path = Path.Combine(FileSystem.CacheDirectory, (this.Editor.DocumentName ?? "Document1") + ".docx");
        File.WriteAllBytes(path, this.document.ToArray());
        this.Editor.SaveState = OfficeSaveState.SavedLocally;
        this.StatusLabel.Text = $"Saved to {path}";
    }

    void OnSaveAsRequested(object? sender, OfficeFileFormat format)
        => this.OnExportRequested(sender, format);

    void OnExportRequested(object? sender, OfficeFileFormat format)
    {
        if (this.document is null)
            return;

        var path = Path.Combine(FileSystem.CacheDirectory, format.FileNameFor(this.Editor.DocumentName));

        if (format.Id == OfficeFileFormats.Pdf.Id)
        {
            using var stream = File.Create(path);
            var pages = this.Editor.ExportPdf(stream);
            this.StatusLabel.Text = $"Exported {pages} page(s) to {path}";
        }
        else if (format.Id == OfficeFileFormats.Docx.Id)
        {
            File.WriteAllBytes(path, this.document.ToArray());
            this.StatusLabel.Text = $"Saved a copy to {path}";
        }
        else
        {
            this.StatusLabel.Text = $"{format.Id} export is left to the host.";
        }
    }

    /// <summary>MAUI has no print dialog of its own; the PDF is what a host would hand to the platform's.</summary>
    void OnPrintRequested(object? sender, EventArgs e)
        => this.OnExportRequested(sender, OfficeFileFormats.Pdf);

    void OnDocumentOpened(object? sender, WordDocument opened)
    {
        // The view opened a template itself; the page owns disposal, so it takes the new one over.
        var previous = this.document;
        this.document = opened;
        previous?.Dispose();
        this.StatusLabel.Text = $"New document from a template: {this.Editor.DocumentName}";
    }

    void OnToggleShell(object? sender, EventArgs e) => this.Editor.ShowShell = !this.Editor.ShowShell;

    /// <summary>
    /// A dropped file the editor would not take.
    /// </summary>
    /// <remarks>
    /// Worth wiring in a sample because the alternative is what it looks like when it is not wired: a
    /// drop that appears to have worked and did nothing.
    /// </remarks>
    void OnDropRejected(object? sender, OfficeDropRejected e)
        => this.StatusLabel.Text = e.FileName.Length > 0
            ? $"{e.FileName}: {e.Reason}"
            : e.Reason;

    void OnToggleToolbar(object? sender, EventArgs e) => this.Editor.ShowToolbar = !this.Editor.ShowToolbar;

    void OnToggleTheme(object? sender, EventArgs e)
    {
        this.dark = !this.dark;
        // null, not DocumentTheme.Light: unset means "follow the app appearance", which is the
        // behaviour worth demoing. Passing Light would pin it and hide that.
        this.Editor.Theme = this.dark ? DocumentTheme.Dark : null;
    }

    void OnToggleSpelling(object? sender, EventArgs e)
    {
        this.Editor.IsSpellCheckEnabled = !this.Editor.IsSpellCheckEnabled;
        this.UpdateStatus();
    }

    /// <summary>
    /// Steps through the margin presets.
    /// </summary>
    /// <remarks>
    /// The toolbar already carries this gallery as an action sheet; the button is here to show the
    /// controller API a host with its own chrome would call, and it is the only route to it when the
    /// toolbar is hidden. The presets come from <c>PageMarginPresets</c> rather than being listed
    /// again, which is the same list the Blazor sample and both toolbars offer.
    /// </remarks>
    void OnCycleMargins(object? sender, EventArgs e)
    {
        if (this.Editor.Controller is not { } controller)
            return;

        this.marginPreset = (this.marginPreset + 1) % PageMarginPresets.All.Count;
        controller.SetPageMargins(PageMarginPresets.All[this.marginPreset].Margins);

        this.UpdateStatus();
    }

    void OnDocumentChanged(object? sender, EventArgs e) => this.UpdateStatus();

    void UpdateStatus()
    {
        var available = SpellCheckers.Default.IsAvailable;

        this.SpellButton.Text = this.Editor.IsSpellCheckEnabled ? "Spelling: on" : "Spelling: off";
        this.SpellButton.IsEnabled = available;

        this.MarginButton.Text = $"Margins: {PageMarginPresets.All[this.marginPreset].Name}";
        this.MarginButton.IsEnabled = this.document is not null;

        this.StatusLabel.Text = available
            ? $"Spell checker: {SpellCheckers.Default.GetType().Name} ({SpellCheckers.Default.DefaultLanguage}). Right-click or long-press an underlined word."
            : "No platform spell checker on this target — set DocumentEditorView.SpellChecker to supply one.";
    }

    // No OnHandlerChanged teardown, deliberately. Shell drops this page's handler whenever another
    // flyout item is picked, but keeps the page and gives it a new handler on the way back. Disposing
    // the document there left the editor holding a dead package: the return trip's layout pass read it
    // and threw out of LayoutSubviews, and on iOS 26+ that unwinds through UIKit's observation tracking
    // and leaves it pointing at a dead stack frame - the app then spins and crashes later in
    // setLeftBarButtonItem, nowhere near the cause. Swapping the document off the editor there instead
    // is no better: it rebuilds the ribbon in the middle of Shell's handler teardown, leaving hosted
    // views handler-less under live parents. The page lives as long as the Shell, so the document does too.
}
