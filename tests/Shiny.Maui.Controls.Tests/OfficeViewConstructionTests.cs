using Shiny.Maui.Controls.Office;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// The composite Office views can be constructed at all.
/// </summary>
/// <remarks>
/// <para>
/// Thin on purpose, and worth having: each of these assembles a few dozen children in its constructor,
/// and an initialisation call placed above the fields it reads throws every single time without any
/// test noticing — nothing here is reached by a painter test, and a XAML page that names the control
/// still compiles.
/// </para>
/// <para>
/// <c>NotebookEditorView</c> shipped that way: <c>ApplyAccent()</c> ran before the section tabs and
/// the page list existed, so the control could not be put on a page at all. Through Shell it surfaced
/// as a native crash with no managed frames, which points nowhere near a constructor.
/// </para>
/// </remarks>
[Collection(ApplicationResourcesCollection.Name)]
public class OfficeViewConstructionTests
{
    public OfficeViewConstructionTests()
    {
        // The ribbon tints through the theme probe, which needs a dispatcher, and Application.Current
        // is process-wide — a fresh one keeps implicit styles from leaking across the collection.
        TestDispatcherProvider.Install();
        _ = new Application();
    }

    [Fact]
    public void ANotebookEditorViewCanBeBuilt()
    {
        var view = new NotebookEditorView();

        view.Content.ShouldNotBeNull();
        view.Dispose();
    }

    [Fact]
    public void ANotebookEditorCanBeBuilt()
        => new NotebookEditor().ShouldNotBeNull();
}
