using Shiny.Controls.Office.Document;
using Shiny.Controls.Office.Packaging;
using Shiny.Controls.Office.Skia;
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
        this.Editor.StatisticsChanged += this.OnStatisticsChanged;
        this.Editor.ZoomChanged += this.OnZoomChanged;

        this.UpdateStatus();
        this.UpdateStatistics();
    }

    /// <summary>Page, word count and zoom — what Word's status bar shows along the bottom.</summary>
    void UpdateStatistics()
    {
        var stats = this.Editor.Statistics;
        this.StatisticsLabel.Text =
            $"Page {stats.CurrentPage} of {stats.Pages}   ·   {stats.Words:N0} words   ·   {this.Editor.Zoom * 100:0}%";
    }

    void OnStatisticsChanged(object? sender, EventArgs e) => this.UpdateStatistics();

    void OnZoomChanged(object? sender, double zoom) => this.UpdateStatistics();

    void OnReadMode(object? sender, EventArgs e) => this.Editor.ReadMode = !this.Editor.ReadMode;

    void OnNavigation(object? sender, EventArgs e) => this.Editor.ShowNavigationPane = !this.Editor.ShowNavigationPane;

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

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (this.Handler is null)
        {
            this.Editor.DocumentChanged -= this.OnDocumentChanged;
            this.Editor.DropRejected -= this.OnDropRejected;
            this.Editor.StatisticsChanged -= this.OnStatisticsChanged;
            this.Editor.ZoomChanged -= this.OnZoomChanged;
            this.document?.Dispose();
            this.document = null;
        }
    }
}
