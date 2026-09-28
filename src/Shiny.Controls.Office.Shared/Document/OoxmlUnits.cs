namespace Shiny.Controls.Office;

/// <summary>
/// The measurement units OOXML uses, converted to pixels at 96 dpi.
/// </summary>
/// <remarks>
/// Every format in the family invents its own unit, and mixing them up produces layouts that are wrong
/// by an exact ratio — content 1/20th the intended size, or 12,700 times too large — which reads as a
/// mysterious rendering bug rather than a unit error.
/// </remarks>
public static class OoxmlUnits
{
    /// <summary>English Metric Units per inch. DrawingML positions and sizes are in these.</summary>
    public const double EmuPerInch = 914400;

    public const double PixelsPerInch = 96;
    public const double PointsPerInch = 72;

    /// <summary>DrawingML geometry: EMU to pixels.</summary>
    public static double EmuToPixels(long emu) => emu / EmuPerInch * PixelsPerInch;

    public static long PixelsToEmu(double pixels) => (long)Math.Round(pixels / PixelsPerInch * EmuPerInch);

    /// <summary>WordprocessingML sizes are in twentieths of a point.</summary>
    public static double TwipsToPixels(double twips) => twips / 20 / PointsPerInch * PixelsPerInch;

    /// <summary>
    /// Pixels back to twentieths of a point, for writing a WordprocessingML measurement.
    /// </summary>
    /// <remarks>
    /// Rounded rather than truncated: a margin the user asked for in inches lands on a whole number of
    /// twips either way, and truncating turns 1440 into 1439 for any value that arrives a hair light
    /// from a floating-point round trip.
    /// </remarks>
    public static int PixelsToTwips(double pixels) => (int)Math.Round(pixels / PixelsPerInch * PointsPerInch * 20);

    public static double InchesToPixels(double inches) => inches * PixelsPerInch;

    /// <summary>Run font size in WordprocessingML is in half-points.</summary>
    public static double HalfPointsToPixels(double halfPoints) => halfPoints / 2 / PointsPerInch * PixelsPerInch;

    /// <summary>DrawingML font size is in hundredths of a point.</summary>
    public static double HundredthPointsToPixels(double hundredths) => hundredths / 100 / PointsPerInch * PixelsPerInch;

    public static double PointsToPixels(double points) => points / PointsPerInch * PixelsPerInch;

    /// <summary>Pixels back to points, for showing a size in the units a picker uses.</summary>
    public static double PixelsToPointsApprox(double pixels) => Math.Round(pixels / PixelsPerInch * PointsPerInch, 1);

    /// <summary>DrawingML angles are in 60,000ths of a degree.</summary>
    public static double AngleToDegrees(int angle) => angle / 60000d;

    /// <summary>
    /// The XML value of an enum-typed attribute, e.g. <c>"roundRect"</c> for a preset geometry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// OpenXml 3.x models these as record structs whose <c>ToString()</c> returns
    /// <c>"ShapeTypeValues { }"</c> — a compiling, non-throwing, entirely useless string that silently
    /// matches nothing. The only reliable source of the spec's own token is the serialised attribute.
    /// </para>
    /// <para>
    /// Matched on local name across every namespace, and deliberately not through
    /// <c>GetAttribute(localName, "")</c>. That overload <b>throws</b> <see cref="KeyNotFoundException"/>
    /// when the element does not allow the attribute in the namespace asked for — and half of these
    /// are namespaced. DrawingML writes <c>prst</c> unqualified, WordprocessingML writes
    /// <c>w:type</c>, so the same call works for a shape's geometry and takes the whole view down for
    /// a page break or a header reference.
    /// </para>
    /// </remarks>
    public static string? EnumAttribute(DocumentFormat.OpenXml.OpenXmlElement? element, string localName)
    {
        if (element is null)
            return null;

        foreach (var attribute in element.GetAttributes())
        {
            if (String.Equals(attribute.LocalName, localName, StringComparison.Ordinal))
                return String.IsNullOrEmpty(attribute.Value) ? null : attribute.Value;
        }

        return null;
    }

    /// <summary>Any attribute's value by local name, whatever its namespace. See <see cref="EnumAttribute"/>.</summary>
    public static string? Attribute(DocumentFormat.OpenXml.OpenXmlElement? element, string localName)
        => EnumAttribute(element, localName);
}
