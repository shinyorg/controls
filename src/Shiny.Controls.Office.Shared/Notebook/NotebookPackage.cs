using System.IO.Compression;
using System.Text.Json;
using Shiny.Controls.Office.Packaging;

namespace Shiny.Controls.Office.Notebook;

/// <summary>
/// Reads and writes the <c>.shinynote</c> container.
/// </summary>
/// <remarks>
/// <para>
/// A zip holding a JSON manifest, one JSON file per page, and the pictures as files:
/// </para>
/// <code>
/// notebook.json          the notebook, its sections, and each page's settings
/// pages/{pageId}.json    that page's items, in z-order
/// media/{itemId}.png     one entry per embedded picture
/// </code>
/// <para>
/// Pages are separate entries rather than one document because a notebook is the one Office-shaped
/// thing here that genuinely grows without bound — a manifest that has to be parsed in full to open
/// the page someone clicked is the wrong shape for that, and a per-page entry is also what makes a
/// page recoverable when a neighbour is corrupt.
/// </para>
/// <para>
/// Pictures stay as files rather than base64 in the page. Base64 costs a third again in size, defeats
/// the zip's own deflate on already-compressed formats, and is the difference between a page that
/// streams and one that has to be held twice in memory while it parses.
/// </para>
/// </remarks>
public static class NotebookPackage
{
    /// <summary>
    /// The format version written into every manifest.
    /// </summary>
    /// <remarks>
    /// Read leniently: a file from a newer writer is opened rather than refused, because every field
    /// added so far has been additive and losing an unknown one is a better outcome than refusing to
    /// show a user their notes. A breaking change gets a new major and an explicit check here.
    /// </remarks>
    public const int FormatVersion = 1;

    public const string ManifestEntry = "notebook.json";

    public static string PageEntry(string pageId) => $"pages/{pageId}.json";

    internal static string MediaEntry(string itemId, string? contentType)
        => $"media/{itemId}{ExtensionFor(contentType)}";

    static string ExtensionFor(string? contentType)
    {
        foreach (var (extension, known) in ImageContentTypes.ByExtension)
        {
            if (known.Equals(contentType, StringComparison.OrdinalIgnoreCase))
                return extension;
        }

        return ".bin";
    }

    // ---- read ----

    internal static NotebookDocument Read(Stream source, string? path)
    {
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);

        var manifest = ReadJson(archive, ManifestEntry, NotebookJsonContext.Default.NotebookDto)
            ?? throw new InvalidDataException($"The package has no {ManifestEntry}.");

        var document = new NotebookDocument(path) { Title = manifest.Title };

        foreach (var sectionDto in manifest.Sections)
        {
            var section = new NotebookSection(
                string.IsNullOrWhiteSpace(sectionDto.Id) ? NotebookDocument.NewId() : sectionDto.Id,
                sectionDto.Title)
            {
                Color = NotebookMapping.ParseColor(sectionDto.Color)
            };

            foreach (var pageRef in sectionDto.Pages)
            {
                var page = NotebookMapping.FromDto(pageRef);
                var pageDto = ReadJson(archive, PageEntry(page.Id), NotebookJsonContext.Default.PageDto);

                if (pageDto is not null)
                {
                    foreach (var itemDto in pageDto.Items)
                    {
                        var media = itemDto.Media is null ? null : ReadBytes(archive, itemDto.Media);
                        page.Items.Add(NotebookMapping.FromDto(itemDto, media));
                    }
                }

                section.Pages.Add(page);
            }

            document.Sections.Add(section);
        }

        // A notebook with nothing in it cannot be navigated to, so it would present as a broken
        // control rather than as an empty one.
        if (document.Sections.Count == 0)
            document.Sections.Add(NotebookDocument.NewSection("Section 1"));

        return document;
    }

    static T? ReadJson<T>(ZipArchive archive, string entryName, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        where T : class
    {
        if (archive.GetEntry(entryName) is not { } entry)
            return null;

        using var stream = entry.Open();
        return JsonSerializer.Deserialize(stream, typeInfo);
    }

    static byte[]? ReadBytes(ZipArchive archive, string entryName)
    {
        if (archive.GetEntry(entryName) is not { } entry)
            return null;

        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        return buffer.ToArray();
    }

    // ---- write ----

    internal static void Write(NotebookDocument document, Stream destination)
    {
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        var manifest = new NotebookDto { Title = document.Title };

        foreach (var section in document.Sections)
        {
            var sectionDto = new SectionDto
            {
                Id = section.Id,
                Title = section.Title,
                Color = NotebookMapping.ToHex(section.Color)
            };

            foreach (var page in section.Pages)
            {
                sectionDto.Pages.Add(NotebookMapping.ToRef(page));
                WritePage(archive, page);
            }

            manifest.Sections.Add(sectionDto);
        }

        WriteJson(archive, ManifestEntry, manifest, NotebookJsonContext.Default.NotebookDto);
    }

    static void WritePage(ZipArchive archive, NotebookPage page)
    {
        var dto = new PageDto();

        foreach (var item in page.Items)
        {
            string? mediaPath = null;

            if (item.Image is { Length: > 0 } bytes)
            {
                mediaPath = MediaEntry(item.Id, item.ImageContentType);

                // Pictures are already compressed; deflating a PNG again costs time and gains nothing.
                var entry = archive.CreateEntry(mediaPath, CompressionLevel.NoCompression);
                using var stream = entry.Open();
                stream.Write(bytes, 0, bytes.Length);
            }

            dto.Items.Add(NotebookMapping.ToDto(item, mediaPath));
        }

        WriteJson(archive, PageEntry(page.Id), dto, NotebookJsonContext.Default.PageDto);
    }

    static void WriteJson<T>(ZipArchive archive, string entryName, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, value, typeInfo);
    }
}
