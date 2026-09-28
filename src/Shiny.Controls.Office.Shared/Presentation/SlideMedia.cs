using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using D = DocumentFormat.OpenXml.Drawing;

namespace Shiny.Controls.Office.Presentation;

/// <summary>
/// Audio or video embedded in a slide, shown as its poster frame.
/// </summary>
/// <remarks>
/// The bytes are not read with the slide — a two-minute clip would be copied out of the package on every
/// keystroke that reprojects the slide. <see cref="Open"/> reads them when something actually plays.
/// </remarks>
public sealed class SlideMedia
{
    readonly Func<Stream?> open;

    internal SlideMedia(bool isVideo, string? contentType, Func<Stream?> open)
    {
        this.IsVideo = isVideo;
        this.ContentType = contentType;
        this.open = open;
    }

    /// <summary>True for video (<c>a:videoFile</c>), false for audio (<c>a:audioFile</c>).</summary>
    public bool IsVideo { get; }

    /// <summary>The media part's content type, e.g. <c>video/mp4</c>.</summary>
    public string? ContentType { get; }

    /// <summary>Opens the embedded media, or null when the file only links to it.</summary>
    public Stream? Open() => this.open();

    /// <summary>Reads the whole clip into memory.</summary>
    public byte[]? ReadAll()
    {
        using var stream = this.Open();
        if (stream is null)
            return null;

        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}

/// <summary>The XML side of media: finding a picture's clip, and building one to insert.</summary>
static class SlideMediaXml
{
    const string P14 = "http://schemas.microsoft.com/office/powerpoint/2010/main";
    const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>
    /// The clip behind a picture, when its <c>p:nvPr</c> names one.
    /// </summary>
    /// <remarks>
    /// PowerPoint 2010+ stores the embedded bytes under a <c>p14:media r:embed</c> extension and keeps
    /// <c>a:videoFile r:link</c> for older readers; either relationship reaches the same part.
    /// </remarks>
    public static SlideMedia? Read(Picture picture, OpenXmlPart part)
    {
        var nv = picture.NonVisualPictureProperties?.ApplicationNonVisualDrawingProperties;
        if (nv is null)
            return null;

        var file = nv.ChildElements.FirstOrDefault(x => x.LocalName is "videoFile" or "audioFile");
        var media = nv.Descendants().FirstOrDefault(x => x.LocalName == "media" && x.NamespaceUri == P14);
        if (file is null && media is null)
            return null;

        var isVideo = file?.LocalName != "audioFile";
        var ids = new[] { RelationshipId(media, "embed"), RelationshipId(file, "link") }.Where(x => x is not null).ToList();

        foreach (var id in ids)
        {
            var reference = part.DataPartReferenceRelationships.FirstOrDefault(x => x.Id == id);
            if (reference?.DataPart is { } data)
                return new SlideMedia(isVideo, data.ContentType, () => data.GetStream(FileMode.Open, FileAccess.Read));
        }

        return new SlideMedia(isVideo, null, () => null);
    }

    static string? RelationshipId(OpenXmlElement? element, string localName)
        => element?.GetAttributes().FirstOrDefault(x => x.LocalName == localName && x.NamespaceUri == R).Value;

    /// <summary>
    /// A picture that plays a clip: the poster frame as its blip, the clip related twice the way
    /// PowerPoint relates it.
    /// </summary>
    public static Picture Build(string posterId, string linkId, string embedId, bool isVideo, double x, double y, double width, double height, string name)
    {
        var nvPr = new ApplicationNonVisualDrawingProperties();
        nvPr.AppendChild<OpenXmlElement>(isVideo
            ? new D.VideoFromFile { Link = linkId }
            : new D.AudioFromFile { Link = linkId });

        var extensions = new ApplicationNonVisualDrawingPropertiesExtensionList();
        var extension = new ApplicationNonVisualDrawingPropertiesExtension { Uri = "{DAA4B4D4-6D8B-4F34-A5A0-6A35BB3CB21B}" };
        var media = new OpenXmlUnknownElement("p14", "media", P14);
        media.SetAttribute(new OpenXmlAttribute("r", "embed", R, embedId));
        extension.AppendChild(media);
        extensions.AppendChild(extension);
        nvPr.AppendChild(extensions);

        return new Picture(
            new NonVisualPictureProperties(
                new NonVisualDrawingProperties(new D.HyperlinkOnClick { Id = string.Empty, Action = "ppaction://media" }) { Id = 2U, Name = name },
                new NonVisualPictureDrawingProperties(new D.PictureLocks { NoChangeAspect = true }),
                nvPr),
            new BlipFill(
                new D.Blip { Embed = posterId },
                new D.Stretch(new D.FillRectangle())),
            new ShapeProperties(
                new D.Transform2D
                {
                    Offset = new D.Offset { X = OoxmlUnits.PixelsToEmu(x), Y = OoxmlUnits.PixelsToEmu(y) },
                    Extents = new D.Extents { Cx = OoxmlUnits.PixelsToEmu(width), Cy = OoxmlUnits.PixelsToEmu(height) }
                },
                new D.PresetGeometry(new D.AdjustValueList()) { Preset = D.ShapeTypeValues.Rectangle }));
    }

    /// <summary>
    /// A plain dark 16:9 poster, for a clip inserted without one.
    /// </summary>
    /// <remarks>
    /// A picture must have a blip, and the kernel cannot encode an image — the Skia package can. This is
    /// a 16x9 PNG of one charcoal colour; the painter draws the play mark over whatever poster there is.
    /// </remarks>
    public static byte[] PlaceholderPoster { get; } = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAABAAAAAJCAIAAAC0SDtlAAAAE0lEQVR42mPQ1tYjCTGMahgUGgD32kpBmGCvJQAAAABJRU5ErkJggg==");
}
