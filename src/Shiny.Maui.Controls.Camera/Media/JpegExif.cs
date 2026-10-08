using System.Buffers.Binary;

namespace Shiny.Maui.Controls.Camera.Media;

/// <summary>
/// Just enough JPEG/EXIF handling to carry metadata across a re-encode: read the orientation, lift the EXIF
/// (APP1) segment out of a source, correct the fields a re-encode makes stale, and splice it into the output.
/// </summary>
/// <remarks>
/// Android's <c>Bitmap.Compress</c> and the Windows <c>BitmapEncoder</c> both write a JPEG with no EXIF at
/// all, so without this every resize or quality change silently dropped the capture date, the camera model,
/// GPS and — the one that actually breaks things — the orientation. Working on bytes rather than through a
/// platform EXIF API keeps the behaviour identical on both. Apple does not use this: ImageIO carries the
/// whole property set natively, HEIC included.
/// </remarks>
static class JpegExif
{
    const ushort OrientationTag = 0x0112;
    const ushort ExifIfdPointerTag = 0x8769;
    const ushort PixelXDimensionTag = 0xA002;
    const ushort PixelYDimensionTag = 0xA003;
    const int TiffStart = 10; // FF E1, two length bytes, "Exif\0\0"

    /// <summary>True when the bytes start with a JPEG SOI marker.</summary>
    public static bool IsJpeg(ReadOnlySpan<byte> data)
        => data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;

    /// <summary>The EXIF orientation (1–8) of a JPEG, or 1 when there is none or the data is not a JPEG.</summary>
    public static int ReadOrientation(ReadOnlySpan<byte> jpeg)
    {
        var range = FindExifSegment(jpeg);
        if (range is not { } r)
            return 1;

        var value = ReadOrientationFromSegment(jpeg.Slice(r.Start, r.Length));
        return value is >= 1 and <= 8 ? value : 1;
    }

    /// <summary>A copy of the whole EXIF APP1 segment (marker and length included), or null when there is none.</summary>
    public static byte[]? ExtractExifSegment(ReadOnlySpan<byte> jpeg)
        => FindExifSegment(jpeg) is { } r ? jpeg.Slice(r.Start, r.Length).ToArray() : null;

    /// <summary>True for the orientations that swap width and height (the 90° family).</summary>
    public static bool SwapsDimensions(int orientation) => orientation is >= 5 and <= 8;

    /// <summary>
    /// Rewrite the fields of an EXIF segment that a re-encode makes wrong: the orientation (1 once the
    /// rotation has been baked into the pixels), the Exif pixel dimensions, and the link to the embedded
    /// thumbnail, which still shows the old size and orientation and is dropped rather than left to disagree.
    /// Patches in place; anything it cannot find is left alone.
    /// </summary>
    public static void Normalize(Span<byte> segment, int orientation, int width, int height)
    {
        if (!TryGetTiff(segment, out var tiff, out var littleEndian))
            return;

        var ifd0 = ReadUInt32(tiff, 4, littleEndian);
        if (!TryEntryCount(tiff, ifd0, littleEndian, out var count))
            return;

        uint exifIfd = 0;
        for (var i = 0; i < count; i++)
        {
            var entry = (int)ifd0 + 2 + i * 12;
            var tag = ReadUInt16(tiff, entry, littleEndian);
            if (tag == OrientationTag)
                WriteUInt16(tiff, entry + 8, (ushort)orientation, littleEndian);
            else if (tag == ExifIfdPointerTag)
                exifIfd = ReadUInt32(tiff, entry + 8, littleEndian);
        }

        // next-IFD link after IFD0 points at IFD1, the thumbnail; zero it so readers stop at IFD0
        var next = (int)ifd0 + 2 + count * 12;
        if (next + 4 <= tiff.Length)
            WriteUInt32(tiff, next, 0, littleEndian);

        if (exifIfd == 0 || !TryEntryCount(tiff, exifIfd, littleEndian, out var exifCount))
            return;

        for (var i = 0; i < exifCount; i++)
        {
            var entry = (int)exifIfd + 2 + i * 12;
            var tag = ReadUInt16(tiff, entry, littleEndian);
            if (tag is not (PixelXDimensionTag or PixelYDimensionTag))
                continue;

            var value = tag == PixelXDimensionTag ? width : height;
            var type = ReadUInt16(tiff, entry + 2, littleEndian);
            if (type == 3) // SHORT
                WriteUInt16(tiff, entry + 8, (ushort)Math.Min(value, ushort.MaxValue), littleEndian);
            else if (type == 4) // LONG
                WriteUInt32(tiff, entry + 8, (uint)value, littleEndian);
        }
    }

    /// <summary>
    /// The smallest valid EXIF segment: IFD0 with an orientation and nothing else. Written when the caller
    /// keeps the pixels as stored but drops the metadata — without it the picture would come out sideways,
    /// because the orientation was the only thing saying which way is up.
    /// </summary>
    public static byte[] CreateOrientationSegment(int orientation)
    {
        // "Exif\0\0" + TIFF header (8) + entry count (2) + one entry (12) + next IFD (4)
        const int payload = 6 + 8 + 2 + 12 + 4;
        var segment = new byte[2 + 2 + payload];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), payload + 2);
        "Exif\0\0"u8.CopyTo(segment.AsSpan(4));

        var tiff = segment.AsSpan(TiffStart);
        "MM"u8.CopyTo(tiff);
        BinaryPrimitives.WriteUInt16BigEndian(tiff[2..], 42);
        BinaryPrimitives.WriteUInt32BigEndian(tiff[4..], 8);
        BinaryPrimitives.WriteUInt16BigEndian(tiff[8..], 1);
        BinaryPrimitives.WriteUInt16BigEndian(tiff[10..], OrientationTag);
        BinaryPrimitives.WriteUInt16BigEndian(tiff[12..], 3); // SHORT
        BinaryPrimitives.WriteUInt32BigEndian(tiff[14..], 1);
        BinaryPrimitives.WriteUInt16BigEndian(tiff[18..], (ushort)orientation);
        // next IFD offset stays 0
        return segment;
    }

    /// <summary>
    /// Insert <paramref name="segment"/> into <paramref name="jpeg"/>, replacing any EXIF it already has.
    /// It goes straight after SOI, or after a JFIF APP0 when the encoder wrote one — the order every reader
    /// expects.
    /// </summary>
    public static byte[] InsertSegment(byte[] jpeg, ReadOnlySpan<byte> segment)
    {
        if (!IsJpeg(jpeg))
            return jpeg;

        // drop any EXIF the encoder wrote first, so there is only ever one
        if (FindExifSegment(jpeg) is { } existing)
            jpeg = [.. jpeg.AsSpan(0, existing.Start), .. jpeg.AsSpan(existing.Start + existing.Length)];

        var insertAt = 2;
        if (jpeg.Length > 6 && jpeg[2] == 0xFF && jpeg[3] == 0xE0)
            insertAt = 4 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(4));

        return [.. jpeg.AsSpan(0, insertAt), .. segment, .. jpeg.AsSpan(insertAt)];
    }

    static (int Start, int Length)? FindExifSegment(ReadOnlySpan<byte> jpeg)
    {
        if (!IsJpeg(jpeg))
            return null;

        var offset = 2;
        while (offset + 4 <= jpeg.Length)
        {
            if (jpeg[offset] != 0xFF)
                return null;

            var marker = jpeg[offset + 1];
            if (marker == 0xFF)
            {
                offset++; // fill byte
                continue;
            }

            // start of scan or end of image: no metadata segments after this point
            if (marker is 0xDA or 0xD9)
                return null;

            var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg[(offset + 2)..]);
            if (length < 2 || offset + 2 + length > jpeg.Length)
                return null;

            if (marker == 0xE1 && length >= 8 && jpeg.Slice(offset + 4, 6).SequenceEqual("Exif\0\0"u8))
                return (offset, length + 2);

            offset += 2 + length;
        }
        return null;
    }

    static int ReadOrientationFromSegment(ReadOnlySpan<byte> segment)
    {
        if (segment.Length < TiffStart + 8)
            return 1;

        var tiff = segment[TiffStart..];
        bool littleEndian;
        if (tiff[0] == (byte)'I' && tiff[1] == (byte)'I')
            littleEndian = true;
        else if (tiff[0] == (byte)'M' && tiff[1] == (byte)'M')
            littleEndian = false;
        else
            return 1;

        var ifd0 = littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(tiff[4..]) : BinaryPrimitives.ReadUInt32BigEndian(tiff[4..]);
        if (ifd0 + 2 > tiff.Length)
            return 1;

        var count = littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(tiff[(int)ifd0..]) : BinaryPrimitives.ReadUInt16BigEndian(tiff[(int)ifd0..]);
        for (var i = 0; i < count; i++)
        {
            var entry = (int)ifd0 + 2 + i * 12;
            if (entry + 12 > tiff.Length)
                return 1;

            var tag = littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(tiff[entry..]) : BinaryPrimitives.ReadUInt16BigEndian(tiff[entry..]);
            if (tag == OrientationTag)
                return littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(tiff[(entry + 8)..]) : BinaryPrimitives.ReadUInt16BigEndian(tiff[(entry + 8)..]);
        }
        return 1;
    }

    static bool TryGetTiff(Span<byte> segment, out Span<byte> tiff, out bool littleEndian)
    {
        tiff = default;
        littleEndian = false;
        if (segment.Length < TiffStart + 8)
            return false;

        tiff = segment[TiffStart..];
        if (tiff[0] == (byte)'I' && tiff[1] == (byte)'I')
            littleEndian = true;
        else if (!(tiff[0] == (byte)'M' && tiff[1] == (byte)'M'))
            return false;

        return true;
    }

    static bool TryEntryCount(Span<byte> tiff, uint ifd, bool littleEndian, out int count)
    {
        count = 0;
        if (ifd < 8 || ifd + 2 > tiff.Length)
            return false;

        count = ReadUInt16(tiff, (int)ifd, littleEndian);
        // never trust a count that runs off the end of the segment
        return ifd + 2 + count * 12 <= tiff.Length;
    }

    static ushort ReadUInt16(Span<byte> s, int at, bool le)
        => le ? BinaryPrimitives.ReadUInt16LittleEndian(s[at..]) : BinaryPrimitives.ReadUInt16BigEndian(s[at..]);

    static uint ReadUInt32(Span<byte> s, int at, bool le)
        => le ? BinaryPrimitives.ReadUInt32LittleEndian(s[at..]) : BinaryPrimitives.ReadUInt32BigEndian(s[at..]);

    static void WriteUInt16(Span<byte> s, int at, ushort value, bool le)
    {
        if (le) BinaryPrimitives.WriteUInt16LittleEndian(s[at..], value);
        else BinaryPrimitives.WriteUInt16BigEndian(s[at..], value);
    }

    static void WriteUInt32(Span<byte> s, int at, uint value, bool le)
    {
        if (le) BinaryPrimitives.WriteUInt32LittleEndian(s[at..], value);
        else BinaryPrimitives.WriteUInt32BigEndian(s[at..], value);
    }
}
