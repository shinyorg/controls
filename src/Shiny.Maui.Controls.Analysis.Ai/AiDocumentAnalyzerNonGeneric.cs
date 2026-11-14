using System.Text.Json;
using Microsoft.Extensions.AI;
using Shiny.Controls.Camera;

namespace Shiny.Maui.Controls.Analysis.Ai;

/// <summary>
/// The zero-setup AI document scanner: an <see cref="AiDocumentAnalyzer{TDocument}"/> that extracts the
/// schema-free <see cref="AiDocument"/> (document type + summary + a flat list of label/value fields). Use this
/// when you just want "point at a document, get its fields back" without defining your own payload type. It's
/// trim/AOT-safe out of the box because it wires up the source-generated <see cref="AiDocumentJsonContext"/>.
/// For a fixed schema (invoice, ID, …) use <see cref="AiDocumentAnalyzer{TDocument}"/> with your own type.
/// </summary>
public class AiDocumentAnalyzer : AiDocumentAnalyzer<AiDocument>
{
    /// <param name="chatClient">The MEAI chat client the frame is sent to. Must support image input (a vision model).</param>
    public AiDocumentAnalyzer(IChatClient chatClient) : base(chatClient)
        => this.SerializerOptions = new JsonSerializerOptions(AiDocumentJsonContext.Default.Options);
}
