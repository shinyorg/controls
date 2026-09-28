namespace Shiny.Controls.Office.Text;

/// <summary>The Change Case menu — shared by the Word and PowerPoint editors.</summary>
public enum TextCase
{
    /// <summary>First letter of each sentence capitalised.</summary>
    Sentence,
    Lower,
    Upper,

    /// <summary>First letter of Each Word.</summary>
    Capitalize,

    /// <summary>tOGGLE: every letter flipped.</summary>
    Toggle
}
