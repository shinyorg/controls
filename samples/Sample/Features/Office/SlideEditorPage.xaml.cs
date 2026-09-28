using Shiny.Controls.Office.Packaging;
using Shiny.Controls.Office.Presentation;
using Shiny.Controls.Office.Skia;

namespace Sample.Features.Office;

/// <summary>
/// The .pptx editor.
/// </summary>
/// <remarks>
/// <para>
/// Tap a shape to select it, then drag it or its handles. Double-tap to put a caret inside its text.
/// Physical keys need a platform hook — see <c>SlideEditor.HandleKey</c> — but tapping, dragging,
/// typing and every toolbar command work without one.
/// </para>
/// <para>
/// The toolbar's insert gallery — shapes, a table, a picture — is the same one the document editor
/// has, and drops a new object in the middle of the slide with it already selected. Dragging an image
/// file onto the canvas places it where it landed instead.
/// </para>
/// </remarks>
public partial class SlideEditorPage : ContentPage
{
    SlideDeck? deck;
    int edits;
    bool dark;

    public SlideEditorPage()
    {
        this.InitializeComponent();
        SampleSourceCode.Attach(this);
        this.UpdateStatus();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (this.deck is not null)
            return;

        var bytes = SampleOfficeDocuments.BuildDeck();
        this.deck = await SlideDeck.OpenAsync(new MemoryStream(bytes), editable: true);
        this.Editor.Deck = this.deck;
        this.Editor.DeckChanged += this.OnDeckChanged;
        this.Editor.DropRejected += this.OnDropRejected;
        this.Editor.StatusChanged += (_, _) => this.UpdateStatus();

        this.UpdateStatus();
    }

    void OnToggleToolbar(object? sender, EventArgs e) => this.Editor.ShowToolbar = !this.Editor.ShowToolbar;

    void OnToggleTheme(object? sender, EventArgs e)
    {
        this.dark = !this.dark;
        // null, not SlideTheme.Light: unset means "follow the app appearance", which is the
        // behaviour worth demoing. Passing Light would pin it and hide that.
        this.Editor.Theme = this.dark ? SlideTheme.Dark : null;
    }

    /// <summary>A dropped file the editor would not take, said out loud rather than swallowed.</summary>
    void OnDropRejected(object? sender, OfficeDropRejected e)
        => this.StatusLabel.Text = e.FileName.Length > 0 ? $"{e.FileName}: {e.Reason}" : e.Reason;

    void OnDeckChanged(object? sender, EventArgs e)
    {
        this.edits++;
        this.UpdateStatus();
    }

    void UpdateStatus()
        => this.StatusLabel.Text = this.deck is null
            ? "loading"
            : $"Slide {this.Editor.CurrentSlideIndex + 1} of {this.Editor.SlideCount} · {this.Editor.ViewMode} · {Math.Round(this.Editor.EffectiveZoom * 100)}% · {this.edits} edits{(this.deck.IsDirty ? " · unsaved" : string.Empty)}";

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (this.Handler is null)
        {
            this.Editor.DeckChanged -= this.OnDeckChanged;
            this.Editor.DropRejected -= this.OnDropRejected;
            this.deck?.Dispose();
            this.deck = null;
        }
    }
}
