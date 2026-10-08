using System.Buffers.Binary;
using Shiny.Maui.Controls.Camera.Media;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Camera.Tests;

/// <summary>
/// The platform-free half of the media pipeline: size limits, the pass-through rule, and the EXIF splicing
/// Android and Windows rely on to keep metadata (and orientation) across a re-encode.
/// </summary>
public class MediaImagingTests
{
    [Fact]
    public void Max_dimension_folds_into_width_and_height_with_the_tightest_limit_winning()
    {
        MediaImageRequest.ResolveLimits(2048, 0, 0).ShouldBe((2048, 2048));
        MediaImageRequest.ResolveLimits(2048, 1024, 0).ShouldBe((1024, 2048));
        MediaImageRequest.ResolveLimits(0, 800, 600).ShouldBe((800, 600));
        MediaImageRequest.ResolveLimits(500, 800, 600).ShouldBe((500, 500));
        MediaImageRequest.ResolveLimits(0, 0, 0).ShouldBe((0, 0));
    }


    [Fact]
    public void Fit_keeps_aspect_and_never_upscales()
    {
        var request = Request(maxWidth: 1000, maxHeight: 1000);

        request.Fit(4000, 3000).ShouldBe((1000, 750));
        request.Fit(3000, 4000).ShouldBe((750, 1000));
        request.Fit(800, 600).ShouldBe((800, 600));

        // width-only limit leaves a tall picture's height free
        Request(maxWidth: 1000).Fit(2000, 8000).ShouldBe((1000, 4000));
    }


    [Fact]
    public void Png_always_bakes_the_rotation()
    {
        Request(rotate: false).BakesRotation.ShouldBeFalse();
        Request(rotate: false, format: MediaImageFormat.Png).BakesRotation.ShouldBeTrue();
    }


    [Fact]
    public void A_full_quality_upright_jpeg_passes_through_untouched()
    {
        var jpeg = Jpeg(JpegExif.CreateOrientationSegment(1));

        MediaImaging.CanPassThrough(jpeg, Request(quality: 100), 4000, 3000).ShouldBeTrue();
    }


    [Fact]
    public void A_sideways_jpeg_is_reencoded_when_rotation_is_wanted()
    {
        // The Android bug: CameraX stores sensor-orientation pixels with orientation 6. Passing that through
        // with RotateImage on would hand back a picture that is sideways to anything ignoring EXIF.
        var jpeg = Jpeg(JpegExif.CreateOrientationSegment(6));

        MediaImaging.CanPassThrough(jpeg, Request(quality: 100), 4000, 3000).ShouldBeFalse();
        MediaImaging.CanPassThrough(jpeg, Request(quality: 100, rotate: false), 4000, 3000).ShouldBeTrue();
    }


    [Fact]
    public void Pass_through_is_refused_for_anything_that_changes_the_bytes()
    {
        var jpeg = Jpeg(JpegExif.CreateOrientationSegment(1));

        MediaImaging.CanPassThrough(jpeg, Request(quality: 92), 4000, 3000).ShouldBeFalse();
        MediaImaging.CanPassThrough(jpeg, Request(quality: 100, preserve: false), 4000, 3000).ShouldBeFalse();
        MediaImaging.CanPassThrough(jpeg, Request(quality: 100, format: MediaImageFormat.Png), 4000, 3000).ShouldBeFalse();
        MediaImaging.CanPassThrough(jpeg, Request(quality: 100, maxWidth: 2000), 4000, 3000).ShouldBeFalse();
        MediaImaging.CanPassThrough(jpeg, Request(quality: 100, maxWidth: 2000), null, null).ShouldBeFalse();
        MediaImaging.CanPassThrough(jpeg, Request(quality: 100, maxWidth: 8000, maxHeight: 8000), 4000, 3000).ShouldBeTrue();
    }


    [Fact]
    public void Orientation_is_read_in_both_byte_orders()
    {
        JpegExif.ReadOrientation(Jpeg(JpegExif.CreateOrientationSegment(8))).ShouldBe(8);
        JpegExif.ReadOrientation(Jpeg(RichSegment(littleEndian: true, orientation: 6))).ShouldBe(6);
        JpegExif.ReadOrientation(Jpeg(RichSegment(littleEndian: false, orientation: 3))).ShouldBe(3);
    }


    [Fact]
    public void Missing_or_garbage_exif_reads_as_upright()
    {
        JpegExif.ReadOrientation(Jpeg()).ShouldBe(1);
        JpegExif.ReadOrientation([1, 2, 3, 4, 5]).ShouldBe(1);
        JpegExif.ExtractExifSegment(Jpeg()).ShouldBeNull();
    }


    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Normalize_corrects_orientation_dimensions_and_drops_the_thumbnail_link(bool littleEndian)
    {
        var segment = RichSegment(littleEndian, orientation: 6);

        JpegExif.Normalize(segment, 1, 1024, 768);

        var tiff = segment.AsSpan(10);
        ReadOrientationEntry(tiff, littleEndian).ShouldBe(1);
        ReadNextIfd(tiff, littleEndian).ShouldBe(0u);
        ReadExifDimensions(tiff, littleEndian).ShouldBe((1024u, 768u));
        // the rest of the metadata is untouched
        JpegExif.ReadOrientation(Jpeg(segment)).ShouldBe(1);
    }


    [Fact]
    public void Insert_replaces_existing_exif_and_sits_after_jfif()
    {
        var encoded = Jpeg(JpegExif.CreateOrientationSegment(3), includeJfif: true);
        var replacement = JpegExif.CreateOrientationSegment(6);

        var result = JpegExif.InsertSegment(encoded, replacement);

        JpegExif.ReadOrientation(result).ShouldBe(6);
        CountExifSegments(result).ShouldBe(1);
        // SOI, then APP0, then our APP1
        result[2].ShouldBe((byte)0xFF);
        result[3].ShouldBe((byte)0xE0);
        var afterApp0 = 4 + BinaryPrimitives.ReadUInt16BigEndian(result.AsSpan(4));
        result[afterApp0 + 1].ShouldBe((byte)0xE1);
        // image data survives
        result[^2].ShouldBe((byte)0xFF);
        result[^1].ShouldBe((byte)0xD9);
    }


    [Fact]
    public void Dropped_metadata_still_keeps_the_orientation_when_pixels_are_not_rotated()
    {
        // RotateImage=false + PreserveMetadata=false must not produce a sideways picture with nothing left
        // to say so — that was exactly the original Android failure.
        var source = Jpeg(RichSegment(littleEndian: true, orientation: 6));
        var encoded = Jpeg(includeJfif: true);

        var result = MediaImaging.ApplyJpegMetadata(encoded, source, Request(rotate: false, preserve: false), 6, 300, 400);

        JpegExif.ReadOrientation(result).ShouldBe(6);
        JpegExif.ExtractExifSegment(result)!.Length.ShouldBe(JpegExif.CreateOrientationSegment(6).Length);
    }


    [Fact]
    public void Rotated_output_without_metadata_carries_no_exif()
    {
        var source = Jpeg(RichSegment(littleEndian: true, orientation: 6));
        var encoded = Jpeg(includeJfif: true);

        var result = MediaImaging.ApplyJpegMetadata(encoded, source, Request(preserve: false), 6, 300, 400);

        JpegExif.ExtractExifSegment(result).ShouldBeNull();
    }


    [Fact]
    public void Preserved_metadata_is_copied_with_the_orientation_reset_after_rotation()
    {
        var source = Jpeg(RichSegment(littleEndian: false, orientation: 6));
        var encoded = Jpeg(includeJfif: true);

        var result = MediaImaging.ApplyJpegMetadata(encoded, source, Request(), 6, 300, 400);

        var segment = JpegExif.ExtractExifSegment(result)!;
        segment.Length.ShouldBe(JpegExif.ExtractExifSegment(source)!.Length);
        JpegExif.ReadOrientation(result).ShouldBe(1);
        ReadExifDimensions(segment.AsSpan(10), false).ShouldBe((300u, 400u));
    }


    [Fact]
    public void Service_resolves_per_call_values_over_defaults()
    {
        var service = new MediaService(new MediaServiceOptions
        {
            CompressionQuality = 80,
            MaxDimension = 2048,
            MaxWidth = 1600,
            RotateImage = true,
            PreserveMetadata = false
        });

        var defaults = service.Resolve(null, null, null, null, null, null, null);
        defaults.ShouldBe(new MediaImageRequest(MediaImageFormat.Jpeg, 80, 1600, 2048, true, false));

        var overridden = service.Resolve(MediaImageFormat.Png, 150, 0, 0, 500, false, true);
        overridden.ShouldBe(new MediaImageRequest(MediaImageFormat.Png, 100, 0, 500, false, true));
    }


    [Fact]
    public void New_options_default_to_unset_and_the_service_to_upright_with_metadata()
    {
        var photo = new PhotoCaptureOptions();
        photo.MaxWidth.ShouldBeNull();
        photo.MaxHeight.ShouldBeNull();
        photo.RotateImage.ShouldBeNull();
        photo.PreserveMetadata.ShouldBeNull();

        var pick = new MediaPickOptions();
        pick.RotateImage.ShouldBeNull();
        pick.PreserveMetadata.ShouldBeNull();

        var service = new MediaServiceOptions();
        service.RotateImage.ShouldBeTrue();
        service.PreserveMetadata.ShouldBeTrue();
        service.MaxWidth.ShouldBe(0);
        service.MaxHeight.ShouldBe(0);
    }


    static MediaImageRequest Request(
        MediaImageFormat format = MediaImageFormat.Jpeg,
        int quality = 92,
        int maxWidth = 0,
        int maxHeight = 0,
        bool rotate = true,
        bool preserve = true
    ) => new(format, quality, maxWidth, maxHeight, rotate, preserve);


    // SOI, optional JFIF APP0, optional APP1, a stub SOS with a couple of data bytes, EOI
    static byte[] Jpeg(byte[]? exif = null, bool includeJfif = false)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };
        if (includeJfif)
            bytes.AddRange([0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0]);
        if (exif is not null)
            bytes.AddRange(exif);
        bytes.AddRange([0xFF, 0xDA, 0x00, 0x02, 0x12, 0x34, 0xFF, 0xD9]);
        return [.. bytes];
    }


    // IFD0: Make, Orientation, ExifIFD pointer; next IFD -> IFD1 (thumbnail). ExifIFD: PixelX (LONG), PixelY (SHORT).
    static byte[] RichSegment(bool littleEndian, int orientation)
    {
        var tiff = new byte[8 + 2 + 3 * 12 + 4 + 2 + 2 * 12 + 4 + 2 + 4];

        void U16(int at, int v) { if (littleEndian) BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(at), (ushort)v); else BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(at), (ushort)v); }
        void U32(int at, uint v) { if (littleEndian) BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(at), v); else BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(at), v); }

        tiff[0] = tiff[1] = (byte)(littleEndian ? 'I' : 'M');
        U16(2, 42);
        U32(4, 8);

        var ifd0 = 8;
        U16(ifd0, 3);
        Entry(ifd0 + 2, 0x010F, 2, 4, 0x41424300); // Make "ABC\0" inline
        Entry(ifd0 + 14, 0x0112, 3, 1, 0);
        U16(ifd0 + 14 + 8, orientation);
        var exifIfd = ifd0 + 2 + 36 + 4;
        Entry(ifd0 + 26, 0x8769, 4, 1, (uint)exifIfd);
        var ifd1 = exifIfd + 2 + 24 + 4;
        U32(ifd0 + 2 + 36, (uint)ifd1);

        U16(exifIfd, 2);
        Entry(exifIfd + 2, 0xA002, 4, 1, 4000);
        Entry(exifIfd + 14, 0xA003, 3, 1, 0);
        U16(exifIfd + 14 + 8, 3000);
        U32(exifIfd + 26, 0);

        U16(ifd1, 0);
        U32(ifd1 + 2, 0);

        var segment = new byte[4 + 6 + tiff.Length];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(segment.Length - 2));
        "Exif\0\0"u8.CopyTo(segment.AsSpan(4));
        tiff.CopyTo(segment.AsSpan(10));
        return segment;

        void Entry(int at, int tag, int type, uint count, uint value)
        {
            U16(at, tag);
            U16(at + 2, type);
            U32(at + 4, count);
            U32(at + 8, value);
        }
    }


    static int ReadOrientationEntry(ReadOnlySpan<byte> tiff, bool le)
    {
        var ifd0 = (int)U32(tiff, 4, le);
        var count = U16(tiff, ifd0, le);
        for (var i = 0; i < count; i++)
        {
            var e = ifd0 + 2 + i * 12;
            if (U16(tiff, e, le) == 0x0112)
                return U16(tiff, e + 8, le);
        }
        return -1;
    }


    static uint ReadNextIfd(ReadOnlySpan<byte> tiff, bool le)
    {
        var ifd0 = (int)U32(tiff, 4, le);
        return U32(tiff, ifd0 + 2 + U16(tiff, ifd0, le) * 12, le);
    }


    static (uint, uint) ReadExifDimensions(ReadOnlySpan<byte> tiff, bool le)
    {
        var ifd0 = (int)U32(tiff, 4, le);
        var exif = 0;
        for (var i = 0; i < U16(tiff, ifd0, le); i++)
        {
            var e = ifd0 + 2 + i * 12;
            if (U16(tiff, e, le) == 0x8769)
                exif = (int)U32(tiff, e + 8, le);
        }

        uint x = 0, y = 0;
        for (var i = 0; i < U16(tiff, exif, le); i++)
        {
            var e = exif + 2 + i * 12;
            var tag = U16(tiff, e, le);
            var value = U16(tiff, e + 2, le) == 3 ? U16(tiff, e + 8, le) : U32(tiff, e + 8, le);
            if (tag == 0xA002) x = value;
            if (tag == 0xA003) y = value;
        }
        return (x, y);
    }


    static int CountExifSegments(byte[] jpeg)
    {
        var count = 0;
        var offset = 2;
        while (offset + 4 <= jpeg.Length && jpeg[offset] == 0xFF && jpeg[offset + 1] != 0xDA)
        {
            if (jpeg[offset + 1] == 0xE1)
                count++;
            offset += 2 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(offset + 2));
        }
        return count;
    }


    static ushort U16(ReadOnlySpan<byte> s, int at, bool le)
        => le ? BinaryPrimitives.ReadUInt16LittleEndian(s[at..]) : BinaryPrimitives.ReadUInt16BigEndian(s[at..]);

    static uint U32(ReadOnlySpan<byte> s, int at, bool le)
        => le ? BinaryPrimitives.ReadUInt32LittleEndian(s[at..]) : BinaryPrimitives.ReadUInt32BigEndian(s[at..]);
}
