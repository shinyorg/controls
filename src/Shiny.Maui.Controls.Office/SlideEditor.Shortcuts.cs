using TextAlignment = Shiny.Controls.Office.Text.TextAlignment;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// PowerPoint's keyboard shortcuts beyond the shared <see cref="EditorKey"/> set, for a host routing
/// platform keys into <see cref="SlideEditor.HandleShortcut"/>.
/// </summary>
public enum SlideShortcut
{
    /// <summary>F5 — the show from the first slide.</summary>
    SlideShowFromBeginning,

    /// <summary>Shift+F5 — the show from this slide.</summary>
    SlideShowFromCurrent,

    /// <summary>Ctrl+Shift+&gt;.</summary>
    GrowFont,

    /// <summary>Ctrl+Shift+&lt;.</summary>
    ShrinkFont,

    /// <summary>Ctrl+L.</summary>
    AlignLeft,

    /// <summary>Ctrl+E.</summary>
    AlignCenter,

    /// <summary>Ctrl+R.</summary>
    AlignRight,

    /// <summary>Ctrl+J.</summary>
    Justify,

    /// <summary>Ctrl+G.</summary>
    Group,

    /// <summary>Ctrl+Shift+G.</summary>
    Ungroup,

    /// <summary>Ctrl+Shift+=.</summary>
    Superscript,

    /// <summary>Ctrl+=.</summary>
    Subscript,

    /// <summary>Ctrl+Space.</summary>
    ClearFormatting,

    /// <summary>Shift+Enter.</summary>
    LineBreak,

    /// <summary>Ctrl+K.</summary>
    InsertLink,

    /// <summary>Ctrl+H.</summary>
    Replace
}

public partial class SlideEditor
{
    /// <summary>
    /// A shortcut the editor recognises but a surrounding view acts on — starting the show, opening
    /// the link dialog, Replace. <see cref="SlideEditorView"/> handles these.
    /// </summary>
    public event EventHandler<SlideShortcut>? ShortcutRequested;

    /// <summary>
    /// Routes one of PowerPoint's shortcuts to the editor. Returns false when there was nothing for it
    /// to act on.
    /// </summary>
    public bool HandleShortcut(SlideShortcut shortcut)
    {
        if (this.controller is not { } c)
            return false;

        switch (shortcut)
        {
            case SlideShortcut.SlideShowFromBeginning or SlideShortcut.SlideShowFromCurrent or SlideShortcut.InsertLink or SlideShortcut.Replace:
                this.ShortcutRequested?.Invoke(this, shortcut);
                return true;
        }

        if (this.IsReadOnly)
            return false;

        switch (shortcut)
        {
            case SlideShortcut.GrowFont: c.GrowFont(1); break;
            case SlideShortcut.ShrinkFont: c.GrowFont(-1); break;
            case SlideShortcut.AlignLeft: c.SetParagraphAlignment(TextAlignment.Left); break;
            case SlideShortcut.AlignCenter: c.SetParagraphAlignment(TextAlignment.Center); break;
            case SlideShortcut.AlignRight: c.SetParagraphAlignment(TextAlignment.Right); break;
            case SlideShortcut.Justify: c.SetParagraphAlignment(TextAlignment.Justify); break;
            case SlideShortcut.Group: c.Group(); break;
            case SlideShortcut.Ungroup: c.Ungroup(); break;
            case SlideShortcut.Superscript: c.ToggleSuperscript(); break;
            case SlideShortcut.Subscript: c.ToggleSubscript(); break;
            case SlideShortcut.ClearFormatting: c.ClearFormatting(); break;
            case SlideShortcut.LineBreak: c.InsertLineBreak(); break;
            default: return false;
        }

        this.Invalidate();
        return true;
    }
}
