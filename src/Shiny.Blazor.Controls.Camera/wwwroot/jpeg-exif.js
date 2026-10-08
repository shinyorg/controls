// Carries a JPEG's EXIF across a canvas re-encode for IMediaService picks (Shiny.Blazor.Controls.Camera).
//
// canvas.toBlob writes no metadata at all, so every resize or quality change used to drop the capture date,
// camera details and GPS. This lifts the source's APP1 segment, corrects the fields the re-encode makes
// stale, and splices it into the output. It mirrors JpegExif in Shiny.Maui.Controls.Camera — keep the two
// in step.

const ORIENTATION = 0x0112;
const EXIF_IFD = 0x8769;
const PIXEL_X = 0xA002;
const PIXEL_Y = 0xA003;
const TIFF_START = 10; // FF E1, two length bytes, "Exif\0\0"

export function isJpeg(bytes) {
    return bytes.length > 3 && bytes[0] === 0xFF && bytes[1] === 0xD8 && bytes[2] === 0xFF;
}

function findExif(bytes) {
    if (!isJpeg(bytes)) return null;
    let offset = 2;
    while (offset + 4 <= bytes.length) {
        if (bytes[offset] !== 0xFF) return null;
        const marker = bytes[offset + 1];
        if (marker === 0xFF) { offset++; continue; }
        if (marker === 0xDA || marker === 0xD9) return null;

        const length = (bytes[offset + 2] << 8) | bytes[offset + 3];
        if (length < 2 || offset + 2 + length > bytes.length) return null;

        if (marker === 0xE1 && length >= 8 &&
            bytes[offset + 4] === 0x45 && bytes[offset + 5] === 0x78 && bytes[offset + 6] === 0x69 &&
            bytes[offset + 7] === 0x66 && bytes[offset + 8] === 0 && bytes[offset + 9] === 0)
            return { start: offset, length: length + 2 };

        offset += 2 + length;
    }
    return null;
}

/** A copy of the EXIF APP1 segment (marker and length included), or null. */
export function extractExif(bytes) {
    const range = findExif(bytes);
    return range ? bytes.slice(range.start, range.start + range.length) : null;
}

/**
 * Patch a segment in place: orientation 1 (the browser decoded the picture upright), the Exif pixel
 * dimensions, and the IFD1 link cut so the stale embedded thumbnail is no longer reachable.
 */
export function normalize(segment, width, height) {
    if (segment.length < TIFF_START + 8) return;
    const view = new DataView(segment.buffer, segment.byteOffset + TIFF_START, segment.length - TIFF_START);
    const le = view.getUint8(0) === 0x49 && view.getUint8(1) === 0x49;
    if (!le && !(view.getUint8(0) === 0x4D && view.getUint8(1) === 0x4D)) return;

    const count = at => (at >= 8 && at + 2 <= view.byteLength && at + 2 + view.getUint16(at, le) * 12 <= view.byteLength)
        ? view.getUint16(at, le) : -1;

    const ifd0 = view.getUint32(4, le);
    const n = count(ifd0);
    if (n < 0) return;

    let exif = 0;
    for (let i = 0; i < n; i++) {
        const entry = ifd0 + 2 + i * 12;
        const tag = view.getUint16(entry, le);
        if (tag === ORIENTATION) view.setUint16(entry + 8, 1, le);
        else if (tag === EXIF_IFD) exif = view.getUint32(entry + 8, le);
    }

    const next = ifd0 + 2 + n * 12;
    if (next + 4 <= view.byteLength) view.setUint32(next, 0, le);

    const m = exif ? count(exif) : -1;
    for (let i = 0; i < m; i++) {
        const entry = exif + 2 + i * 12;
        const tag = view.getUint16(entry, le);
        if (tag !== PIXEL_X && tag !== PIXEL_Y) continue;
        const value = tag === PIXEL_X ? width : height;
        const type = view.getUint16(entry + 2, le);
        if (type === 3) view.setUint16(entry + 8, Math.min(value, 0xFFFF), le);
        else if (type === 4) view.setUint32(entry + 8, value, le);
    }
}

/** Insert a segment after SOI (or after a JFIF APP0), replacing any EXIF already there. */
export function insert(jpeg, segment) {
    if (!isJpeg(jpeg)) return jpeg;

    const existing = findExif(jpeg);
    if (existing) {
        const stripped = new Uint8Array(jpeg.length - existing.length);
        stripped.set(jpeg.subarray(0, existing.start), 0);
        stripped.set(jpeg.subarray(existing.start + existing.length), existing.start);
        jpeg = stripped;
    }

    let at = 2;
    if (jpeg.length > 6 && jpeg[2] === 0xFF && jpeg[3] === 0xE0)
        at = 4 + ((jpeg[4] << 8) | jpeg[5]);

    const out = new Uint8Array(jpeg.length + segment.length);
    out.set(jpeg.subarray(0, at), 0);
    out.set(segment, at);
    out.set(jpeg.subarray(at), at + segment.length);
    return out;
}

/**
 * Copy the source file's EXIF onto an encoded JPEG blob. Resolves with the original blob when there is
 * nothing to carry (not a JPEG on either side, or no EXIF in the source).
 */
export async function carryExif(sourceFile, encodedBlob, width, height) {
    if (encodedBlob.type !== 'image/jpeg') return encodedBlob;
    try {
        // EXIF lives in the first segments, each under 64KB; 256KB clears a typical APP0 + ICC profile ahead of it
        const head = new Uint8Array(await sourceFile.slice(0, 256 * 1024).arrayBuffer());
        const segment = extractExif(head);
        if (!segment) return encodedBlob;

        normalize(segment, width, height);
        const encoded = new Uint8Array(await encodedBlob.arrayBuffer());
        return new Blob([insert(encoded, segment)], { type: 'image/jpeg' });
    }
    catch (err) {
        console.warn('[shiny-media] could not carry EXIF across the re-encode', err);
        return encodedBlob;
    }
}
