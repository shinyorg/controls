// Shared blob store for IMediaService (Shiny.Blazor.Controls.Camera).
//
// Captured stills, recordings and picked files STAY HERE. What crosses into .NET is only a descriptor —
// an id, an object URL for <img>/<video src>, the dimensions and the byte count — and the bytes follow on
// request as a raw Blob, which Blazor streams in chunks (IJSStreamReference). A recording is easily tens of
// megabytes; as a byte[] return it would be one SignalR message on Blazor Server, far past the default
// 32KB cap, and the circuit would close.
//
// camera.js (captures, recordings) and media.js (pickers) both import this module by the same relative
// path, so they share one instance and one id space.

const blobs = new Map();
let nextId = 0;

/** Keep a blob and describe it. durationMs is -1 when unknown. */
export function keep(blob, width, height, durationMs, name) {
    const id = `m${++nextId}`;
    const url = URL.createObjectURL(blob);
    blobs.set(id, { blob, url });
    return {
        id,
        url,
        width: width || 0,
        height: height || 0,
        size: blob.size,
        contentType: blob.type || '',
        durationMs: typeof durationMs === 'number' && isFinite(durationMs) ? Math.round(durationMs) : -1,
        name: name || null
    };
}

/**
 * The raw Blob for an id. Returned as-is, NOT wrapped in DotNet.createJSStreamReference: Blazor wraps the
 * return value itself when .NET asks for an IJSStreamReference, and a pre-wrapped one fails inside Blazor's
 * internals with "Supplied value is not a typed array or blob".
 */
export function take(id) {
    const entry = blobs.get(id);
    if (!entry)
        throw new Error(`Media '${id}' is no longer available.`);
    return entry.blob;
}

/** Forget one blob and revoke its object URL. Safe to call twice. */
export function release(id) {
    const entry = blobs.get(id);
    if (!entry) return;
    URL.revokeObjectURL(entry.url);
    blobs.delete(id);
}

/**
 * Draw a source (video element, ImageBitmap) onto a canvas no larger than maxDim on its long edge and
 * encode it. JPEG gets a white ground first — a transparent PNG picked from the gallery would otherwise
 * come out black, because JPEG has no alpha and the canvas default is transparent black.
 */
export async function encode(source, sourceWidth, sourceHeight, maxDim, mime, quality, filterCss) {
    let width = sourceWidth || 1;
    let height = sourceHeight || 1;
    if (maxDim > 0 && Math.max(width, height) > maxDim) {
        const scale = maxDim / Math.max(width, height);
        width = Math.max(1, Math.round(width * scale));
        height = Math.max(1, Math.round(height * scale));
    }

    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext('2d');
    if (mime === 'image/jpeg') {
        ctx.fillStyle = '#FFFFFF';
        ctx.fillRect(0, 0, width, height);
    }
    if (filterCss && filterCss !== 'none')
        ctx.filter = filterCss;   // bake the preview look into the still
    ctx.drawImage(source, 0, 0, width, height);

    const blob = await new Promise((resolve, reject) =>
        canvas.toBlob(b => b ? resolve(b) : reject(new Error('Image encoding failed')), mime, Math.min(1, Math.max(0.01, quality / 100))));

    return { blob, width, height };
}
