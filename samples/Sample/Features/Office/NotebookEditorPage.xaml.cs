using Shiny.Controls.Office.Notebook;
using Shiny.Controls.Office.Packaging;
using Shiny.Controls.Office.Skia;

namespace Sample.Features.Office;

/// <summary>
/// The free-form notebook.
/// </summary>
/// <remarks>
/// <para>
/// Double-tap empty canvas to start writing anywhere on the page. Tap an item to select it, then drag
/// it or its handles; drag empty space to marquee-select with a mouse, or to pan with a finger. The
/// <b>Draw</b> tab holds the pen, the highlighter, the eraser and the lasso — each one a mode the
/// pointer stays in until another is picked, so tapping the lit tool again puts it down.
/// </para>
/// <para>
/// Physical keys need a platform hook — see <c>NotebookEditor.HandleKey</c> — but writing, drawing,
/// dragging and every toolbar command work without one.
/// </para>
/// </remarks>
public partial class NotebookEditorPage : ContentPage
{
    NotebookDocument? notebook;
    int edits;
    bool dark;

    public NotebookEditorPage()
    {
        this.InitializeComponent();
        SampleSourceCode.Attach(this);
        this.UpdateStatus();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (this.notebook is not null)
            return;

        this.notebook = SampleNotebook.Build();
        this.Editor.Notebook = this.notebook;
        this.Editor.NotebookChanged += this.OnNotebookChanged;
        this.Editor.DropRejected += this.OnDropRejected;

        this.UpdateStatus();
    }

    void OnToggleToolbar(object? sender, EventArgs e) => this.Editor.ShowToolbar = !this.Editor.ShowToolbar;

    void OnToggleNavigation(object? sender, EventArgs e) => this.Editor.ShowNavigation = !this.Editor.ShowNavigation;

    void OnToggleTheme(object? sender, EventArgs e)
    {
        this.dark = !this.dark;

        // null, not NotebookTheme.Light: unset means "follow the app appearance", which is the
        // behaviour worth demoing. Passing Light would pin it and hide that.
        this.Editor.Theme = this.dark ? NotebookTheme.Dark : null;
    }

    /// <summary>Writes the notebook out as a <c>.shinynote</c> package, next to the app's data.</summary>
    async void OnSave(object? sender, EventArgs e)
    {
        if (this.notebook is null)
            return;

        var path = Path.Combine(FileSystem.AppDataDirectory, "sample.shinynote");

        try
        {
            await this.notebook.SaveAsAsync(path);
            this.StatusLabel.Text = $"saved to {path}";
        }
        catch (Exception ex)
        {
            this.StatusLabel.Text = $"save failed: {ex.Message}";
        }
    }

    /// <summary>A dropped file the editor would not take, said out loud rather than swallowed.</summary>
    void OnDropRejected(object? sender, OfficeDropRejected e)
        => this.StatusLabel.Text = e.FileName.Length > 0 ? $"{e.FileName}: {e.Reason}" : e.Reason;

    void OnNotebookChanged(object? sender, EventArgs e)
    {
        this.edits++;
        this.UpdateStatus();
    }

    void UpdateStatus()
        => this.StatusLabel.Text = this.notebook is null
            ? "loading"
            : $"{this.notebook.Sections.Count} sections · {this.notebook.AllPages().Count()} pages · " +
              $"{this.edits} edits{(this.notebook.IsDirty ? " · unsaved" : string.Empty)}";

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (this.Handler is null)
        {
            this.Editor.NotebookChanged -= this.OnNotebookChanged;
            this.Editor.DropRejected -= this.OnDropRejected;
            this.notebook?.Dispose();
            this.notebook = null;
        }
    }
}
