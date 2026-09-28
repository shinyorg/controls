using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Shiny.Controls.Office.Shell;
using WordprocessingDocumentType = DocumentFormat.OpenXml.WordprocessingDocumentType;

namespace Shiny.Controls.Office.Document;

/// <summary>
/// The built-in Word templates the backstage offers — Blank, Report and Letter — generated in code, so
/// the package ships no binary assets.
/// </summary>
/// <remarks>
/// Each is a real <c>.docx</c> written with Word's own style definitions (the same ones
/// <see cref="DocumentEditorController.ApplyStyle"/> writes), so a document started from one opens in
/// Word with its headings in Word's gallery under their usual names.
/// </remarks>
public static class WordTemplates
{
    public const string BlankId = "blank";
    public const string ReportId = "word-report";
    public const string LetterId = "word-letter";

    /// <summary>Blank, Report and Letter, each with an <see cref="OfficeTemplate.Open"/> that writes the file.</summary>
    public static IReadOnlyList<OfficeTemplate> All { get; } =
    [
        new OfficeTemplate(BlankId, "Blank document")
        {
            IsBlank = true,
            Category = "Featured",
            Open = _ => Task.FromResult<Stream>(Create(BlankId))
        },
        new OfficeTemplate(ReportId, "Report")
        {
            Description = "Title, headings and body text ready to fill in",
            Category = "Featured",
            Open = _ => Task.FromResult<Stream>(Create(ReportId))
        },
        new OfficeTemplate(LetterId, "Letter")
        {
            Description = "A block-style business letter",
            Category = "Featured",
            Open = _ => Task.FromResult<Stream>(Create(LetterId))
        }
    ];

    /// <summary>Opens a template as an editable document — the template's own <see cref="OfficeTemplate.Open"/>, or blank.</summary>
    public static async Task<WordDocument> OpenAsync(OfficeTemplate? template, CancellationToken cancellationToken = default)
    {
        Stream stream = template?.Open is { } open
            ? await open(cancellationToken).ConfigureAwait(false)
            : Create(BlankId);

        await using (stream.ConfigureAwait(false))
            return await WordDocument.OpenAsync(stream, editable: true, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes the template with this id (blank for an unknown id) to a new, rewound stream.</summary>
    public static MemoryStream Create(string id)
    {
        var output = new MemoryStream();

        using (var package = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document, autoSave: true))
        {
            var main = package.AddMainDocumentPart();
            var body = new Body();
            main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(body);

            WordBuiltInStyles.Ensure(main, "Normal");

            // Word's own document defaults - Calibri 11pt, 8pt after, 1.08 lines. Without them a
            // reader falls back to the format's 10pt Times, which is not what "Blank document" means.
            main.StyleDefinitionsPart!.Styles!.PrependChild(new DocDefaults(
                new RunPropertiesDefault(new RunPropertiesBaseStyle(
                    new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri", EastAsia = "Calibri", ComplexScript = "Calibri" },
                    new FontSize { Val = "22" },
                    new FontSizeComplexScript { Val = "22" },
                    new Languages { Val = "en-US" })),
                new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                    new SpacingBetweenLines { After = "160", Line = "259", LineRule = LineSpacingRuleValues.Auto }))));

            switch (id)
            {
                case ReportId:
                    foreach (var style in new[] { "Title", "Subtitle", "Heading1", "Heading2" })
                        WordBuiltInStyles.Ensure(main, style);

                    body.Append(
                        Para("Report Title", "Title"),
                        Para("Subtitle or date", "Subtitle"),
                        Para("Summary", "Heading1"),
                        Para("Summarise the purpose of this report and its main findings in a few sentences. Replace any of this text by selecting it and typing."),
                        Para("Background", "Heading1"),
                        Para("Describe the context the reader needs before the findings."),
                        Para("Findings", "Heading1"),
                        Para("First finding", "Heading2"),
                        Para("Explain what was found and why it matters."),
                        Para("Second finding", "Heading2"),
                        Para("Explain what was found and why it matters."),
                        Para("Recommendations", "Heading1"),
                        Para("List the actions this report recommends."));
                    break;

                case LetterId:
                    WordBuiltInStyles.Ensure(main, "NoSpacing");

                    body.Append(
                        Para("Your Name", "NoSpacing"),
                        Para("Street Address", "NoSpacing"),
                        Para("City, State ZIP", "NoSpacing"),
                        Para(string.Empty),
                        Para(DateTime.Today.ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture)),
                        Para(string.Empty),
                        Para("Recipient Name", "NoSpacing"),
                        Para("Company", "NoSpacing"),
                        Para("Street Address", "NoSpacing"),
                        Para("City, State ZIP", "NoSpacing"),
                        Para(string.Empty),
                        Para("Dear Recipient,"),
                        Para("Start your letter here. State the reason you are writing in the first paragraph."),
                        Para("Use the following paragraphs to give the details, and close with what you would like to happen next."),
                        Para("Sincerely,"),
                        Para(string.Empty),
                        Para("Your Name"));
                    break;

                default:
                    body.Append(Para(string.Empty));
                    break;
            }

            // Letter paper with Word's Normal margins (one inch all round).
            body.Append(new SectionProperties(
                new PageSize { Width = 12240U, Height = 15840U },
                new PageMargin { Top = 1440, Right = 1440U, Bottom = 1440, Left = 1440U, Header = 720U, Footer = 720U, Gutter = 0U }));
        }

        output.Position = 0;
        return output;
    }

    static Paragraph Para(string text, string? styleId = null)
    {
        var paragraph = new Paragraph();
        if (styleId is not null)
            paragraph.Append(new ParagraphProperties(new ParagraphStyleId { Val = styleId }));

        if (text.Length > 0)
            paragraph.Append(new Run(new DocumentFormat.OpenXml.Wordprocessing.Text(text) { Space = SpaceProcessingModeValues.Preserve }));

        return paragraph;
    }
}
