
namespace Shiny.Maui.Controls.Analysis.Ocr;

/// <summary>Carries the recognized text blocks to <see cref="OcrAnalyzer.TextRecognized"/> subscribers.</summary>
public class TextRecognizedEventArgs(IReadOnlyList<RecognizedText> blocks) : EventArgs
{
    /// <summary>The text blocks recognized in the frame (never empty when the event is raised).</summary>
    public IReadOnlyList<RecognizedText> Blocks { get; } = blocks;
}
