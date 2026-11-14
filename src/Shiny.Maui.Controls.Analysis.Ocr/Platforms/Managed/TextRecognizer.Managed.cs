
namespace Shiny.Maui.Controls.Analysis.Ocr;

public partial class TextRecognizer
{
    // No managed OCR engine on the platform-agnostic target; returns nothing.
    private partial Task<List<RecognizedText>> RecognizeCoreAsync(VideoFrame frame, TextRecognitionOptions options, CancellationToken ct)
        => Task.FromResult(new List<RecognizedText>());

    private partial Task<RecognizedDocument> RecognizeDocumentCoreAsync(VideoFrame frame, CancellationToken ct)
        => Task.FromResult(new RecognizedDocument([], null));
}
