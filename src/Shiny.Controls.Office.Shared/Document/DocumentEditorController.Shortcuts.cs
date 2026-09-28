using TextAlignment = Shiny.Controls.Office.Text.TextAlignment;

namespace Shiny.Controls.Office.Document;

/// <summary>A Word command a keyboard shortcut can run.</summary>
public enum WordCommand
{
    None,
    Bold,
    Italic,
    Underline,
    Copy,
    Cut,
    Paste,
    PasteText,
    Undo,
    Redo,
    SelectAll,

    /// <summary>Ctrl+F. Handled by the host, which owns the find box.</summary>
    Find,

    /// <summary>Ctrl+H. Handled by the host, which owns the replace dialog.</summary>
    Replace,

    /// <summary>Ctrl+K. Handled by the host, which owns the link dialog.</summary>
    Hyperlink,

    AlignLeft,
    AlignCenter,
    AlignRight,
    Justify,
    GrowFont,
    ShrinkFont,
    Subscript,
    Superscript,
    PageBreak,
    LineSpacingSingle,
    LineSpacingOneAndHalf,
    LineSpacingDouble,
    Heading1,
    Heading2,
    Heading3,
    NormalStyle,
    ClearFormatting,
    IncreaseIndent,
    DecreaseIndent,
    BulletList,
    CopyFormat,
    PasteFormat,
    ChangeCase,
    ShowFormattingMarks,

    /// <summary>Ctrl+Alt+M. Handled by the host, which asks for the comment's text.</summary>
    NewComment,
    TrackChanges,

    /// <summary>Ctrl+Shift+G. Handled by the host, which shows the dialog.</summary>
    WordCount,

    /// <summary>Escape: puts the format painter down.</summary>
    Cancel
}


/// <summary>Word's keyboard shortcuts, shared by both hosts so they cannot disagree.</summary>
public static class WordShortcuts
{
    /// <summary>
    /// The command a key combination means, or <see cref="WordCommand.None"/>.
    /// </summary>
    /// <param name="key">
    /// The key as the platform names it — a character (<c>"b"</c>, <c>"="</c>, <c>"&gt;"</c>, <c>"1"</c>)
    /// or a name (<c>"Enter"</c>, <c>"F3"</c>, <c>"Escape"</c>). Case-insensitive for letters.
    /// </param>
    /// <param name="command">Ctrl, or Cmd on Apple platforms.</param>
    /// <param name="shift">Shift.</param>
    /// <param name="alt">Alt / Option.</param>
    public static WordCommand Resolve(string? key, bool command, bool shift, bool alt)
    {
        if (string.IsNullOrEmpty(key))
            return WordCommand.None;

        if (!command)
        {
            return key switch
            {
                "F3" when shift => WordCommand.ChangeCase,
                "Escape" or "Esc" => WordCommand.Cancel,
                _ => WordCommand.None
            };
        }

        var k = key.Length == 1 ? key.ToLowerInvariant() : key;

        if (alt)
        {
            return k switch
            {
                "1" => WordCommand.Heading1,
                "2" => WordCommand.Heading2,
                "3" => WordCommand.Heading3,
                "m" => WordCommand.NewComment,
                "v" when shift => WordCommand.PasteText,
                _ => WordCommand.None
            };
        }

        if (shift)
        {
            return k switch
            {
                ">" or "." => WordCommand.GrowFont,
                "<" or "," => WordCommand.ShrinkFont,
                "+" or "=" => WordCommand.Superscript,
                "z" => WordCommand.Redo,
                "c" => WordCommand.CopyFormat,
                "v" => WordCommand.PasteFormat,
                "n" => WordCommand.NormalStyle,
                "l" => WordCommand.BulletList,
                "m" => WordCommand.DecreaseIndent,
                "e" => WordCommand.TrackChanges,
                "g" => WordCommand.WordCount,
                "8" or "*" => WordCommand.ShowFormattingMarks,
                _ => WordCommand.None
            };
        }

        return k switch
        {
            "b" => WordCommand.Bold,
            "i" => WordCommand.Italic,
            "u" => WordCommand.Underline,
            "c" => WordCommand.Copy,
            "x" => WordCommand.Cut,
            "v" => WordCommand.Paste,
            "z" => WordCommand.Undo,
            "y" => WordCommand.Redo,
            "a" => WordCommand.SelectAll,
            "f" => WordCommand.Find,
            "h" => WordCommand.Replace,
            "k" => WordCommand.Hyperlink,
            "l" => WordCommand.AlignLeft,
            "e" => WordCommand.AlignCenter,
            "r" => WordCommand.AlignRight,
            "j" => WordCommand.Justify,
            "=" => WordCommand.Subscript,
            "]" => WordCommand.GrowFont,
            "[" => WordCommand.ShrinkFont,
            "Enter" => WordCommand.PageBreak,
            "1" => WordCommand.LineSpacingSingle,
            "5" => WordCommand.LineSpacingOneAndHalf,
            "2" => WordCommand.LineSpacingDouble,
            " " or "Space" or "Spacebar" => WordCommand.ClearFormatting,
            "m" => WordCommand.IncreaseIndent,
            _ => WordCommand.None
        };
    }

    /// <summary>True for the commands the host has to handle itself, because they need its UI or its clipboard.</summary>
    public static bool IsHostCommand(WordCommand command) => command is
        WordCommand.Find or WordCommand.Replace or WordCommand.Hyperlink or WordCommand.NewComment or
        WordCommand.WordCount or WordCommand.Copy or WordCommand.Cut or WordCommand.Paste or WordCommand.PasteText;
}


public sealed partial class DocumentEditorController
{
    /// <summary>
    /// Runs a keyboard command. Returns false for one the host has to handle — see
    /// <see cref="WordShortcuts.IsHostCommand"/> — and for <see cref="WordCommand.None"/>.
    /// </summary>
    /// <remarks>
    /// Navigation, selection and the undo pair work on a read-only document; everything that edits does
    /// nothing there and still reports the key as handled, so it does not fall through to the platform.
    /// </remarks>
    public bool Execute(WordCommand command)
    {
        switch (command)
        {
            case WordCommand.None:
                return false;

            case WordCommand.SelectAll:
                this.SelectAll();
                return true;

            case WordCommand.ShowFormattingMarks:
                this.ShowFormattingMarks = !this.ShowFormattingMarks;
                return true;

            case WordCommand.Cancel:
                if (!this.IsFormatPainterActive)
                    return false;

                this.CancelFormatPainter();
                return true;
        }

        if (WordShortcuts.IsHostCommand(command))
            return false;

        if (this.IsReadOnlyDocument)
            return true;

        switch (command)
        {
            case WordCommand.Bold: this.ToggleBold(); break;
            case WordCommand.Italic: this.ToggleItalic(); break;
            case WordCommand.Underline: this.ToggleUnderline(); break;
            case WordCommand.Undo: this.Undo(); break;
            case WordCommand.Redo: this.Redo(); break;
            case WordCommand.AlignLeft: this.SetAlignment(TextAlignment.Left); break;
            case WordCommand.AlignCenter: this.SetAlignment(TextAlignment.Center); break;
            case WordCommand.AlignRight: this.SetAlignment(TextAlignment.Right); break;
            case WordCommand.Justify: this.SetAlignment(TextAlignment.Justify); break;
            case WordCommand.GrowFont: this.GrowFont(); break;
            case WordCommand.ShrinkFont: this.ShrinkFont(); break;
            case WordCommand.Subscript: this.ToggleSubscript(); break;
            case WordCommand.Superscript: this.ToggleSuperscript(); break;
            case WordCommand.PageBreak: this.InsertPageBreak(); break;
            case WordCommand.LineSpacingSingle: this.SetLineSpacing(1.0); break;
            case WordCommand.LineSpacingOneAndHalf: this.SetLineSpacing(1.5); break;
            case WordCommand.LineSpacingDouble: this.SetLineSpacing(2.0); break;
            case WordCommand.Heading1: this.ApplyHeading(1); break;
            case WordCommand.Heading2: this.ApplyHeading(2); break;
            case WordCommand.Heading3: this.ApplyHeading(3); break;
            case WordCommand.NormalStyle: this.ApplyStyle("Normal"); break;
            case WordCommand.ClearFormatting: this.ClearFormatting(); break;
            case WordCommand.IncreaseIndent: this.ChangeIndent(1); break;
            case WordCommand.DecreaseIndent: this.ChangeIndent(-1); break;
            case WordCommand.BulletList: this.ToggleBulletList(); break;
            case WordCommand.CopyFormat: this.CopyFormatting(); break;
            case WordCommand.PasteFormat: this.ApplyFormatPainter(); break;
            case WordCommand.ChangeCase: this.CycleCase(); break;
            case WordCommand.TrackChanges: this.IsTrackingChanges = !this.IsTrackingChanges; break;
            default: return false;
        }

        return true;
    }

    /// <summary>
    /// Resolves and runs a key combination in one call. Returns the command so a host can handle the
    /// ones that need it (<see cref="WordShortcuts.IsHostCommand"/>) and knows when to stop the key.
    /// </summary>
    public WordCommand HandleShortcut(string? key, bool command, bool shift, bool alt, out bool handled)
    {
        var resolved = WordShortcuts.Resolve(key, command, shift, alt);
        handled = this.Execute(resolved);
        return resolved;
    }
}
