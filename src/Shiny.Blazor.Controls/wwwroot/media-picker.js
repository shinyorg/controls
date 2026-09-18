// Shiny.Blazor.Controls MediaPickerButton
//
// Picks files through hidden <input> elements, resizes and re-encodes each on an offscreen canvas
// (the same toBlob path the image editor uses), and KEEPS THE BLOB HERE.
//
// What crosses into .NET is only a descriptor: an id, an object URL for <img src>, the dimensions
// and the byte count. The bytes themselves go one of two ways, and never as base64:
//
//   * upload() posts the blob as multipart form data, straight from the browser, with progress;
//   * read() hands the blob to .NET as an IJSStreamReference for chunked reading.
//
// Base64 was the old path and cost more than it looked: a 4MB photo became a 5.4MB string built in
// full before a byte could move, and on Blazor Server that string is one SignalR message — bigger
// than the default 32KB cap, so the server closed the circuit and the upload never happened.

const states = new Map();

// The chooser and the editor cover the screen, and a picker inside a sheet or a panel has a
// transformed ancestor - so they are raised to the top layer rather than trusting position:fixed.
export { show as raise } from './top-layer.js';

// WebKit on iPhone and iPad answers any <input type="file" accept="image/*"> with its own sheet -
// Photo Library, Take Photo, Choose File - and nothing on the input skips it. A chooser of our own in
// front of that asks the same question twice, so on those devices the button goes straight to it.
// iPadOS reports itself as a Mac; touch support is what gives it away.
function hasNativeImageChooser() {
    const ua = navigator.userAgent;
    return /iPhone|iPad|iPod/.test(ua) || (/Macintosh/.test(ua) && navigator.maxTouchPoints > 1);
}

// Answers whether the platform shows its own gallery/camera sheet (see hasNativeImageChooser).
export function init(root, galleryInput, cameraInput, dotnetRef, options) {
    const state = { dotnetRef, options, galleryInput, cameraInput, blobs: new Map(), urls: new Map() };

    const handler = async e => {
        const file = e.target.files && e.target.files[0];
        e.target.value = ''; // allow re-picking the same file
        if (!file) return;
        try {
            const bitmap = await createImageBitmap(file);
            const descriptor = await store(state, bitmap, file.name);
            bitmap.close?.();
            await state.dotnetRef.invokeMethodAsync('OnFilePicked', descriptor);
        } catch (err) {
            console.error('[shiny-mediapicker] failed to process file', err);
        }
    };

    galleryInput.addEventListener('change', handler);
    cameraInput.addEventListener('change', handler);
    state.handler = handler;
    states.set(root, state);

    return hasNativeImageChooser();
}

export function updateOptions(root, options) {
    const state = states.get(root);
    if (state) state.options = options;
}

export function openGallery(root) {
    states.get(root)?.galleryInput.click();
}

export function openCamera(root) {
    states.get(root)?.cameraInput.click();
}

/**
 * Hands one photo's bytes to .NET as a stream.
 *
 * Returned raw, NOT wrapped in DotNet.createJSStreamReference: Blazor wraps the return value itself
 * when the .NET side asks for an IJSStreamReference, and wrapping it here first hands that code a
 * plain object instead of a Blob — which fails with "Supplied value is not a typed array or blob",
 * pointing at Blazor's internals rather than at this line.
 */
export function read(root, id) {
    const blob = states.get(root)?.blobs.get(id);
    if (!blob)
        throw new Error(`Picked photo '${id}' is no longer available.`);

    return blob;
}

/**
 * Uploads one photo as multipart form data and reports progress as it goes.
 *
 * XMLHttpRequest rather than fetch: it is still the only way to observe upload progress. The answer
 * comes back as text so the caller can read whatever its own server returned.
 */
export function upload(root, id, request, dotnetRef) {
    const state = states.get(root);
    const blob = state && state.blobs.get(id);
    if (!blob)
        return Promise.resolve({ ok: false, status: 0, body: '' });

    return new Promise(resolve => {
        const form = new FormData();
        const name = request.fileName || `photo.${blob.type.includes('png') ? 'png' : 'jpg'}`;
        form.append(request.fieldName || 'file', blob, name);

        const xhr = new XMLHttpRequest();
        xhr.open(request.method || 'POST', request.url, true);

        if (request.headers) {
            for (const [key, value] of Object.entries(request.headers))
                xhr.setRequestHeader(key, value);
        }
        if (request.withCredentials)
            xhr.withCredentials = true;

        if (dotnetRef) {
            xhr.upload.onprogress = e => {
                if (e.lengthComputable)
                    dotnetRef.invokeMethodAsync('OnUploadProgress', id, e.loaded, e.total);
            };
        }
        xhr.onload = () => resolve({ ok: xhr.status >= 200 && xhr.status < 300, status: xhr.status, body: xhr.responseText });
        xhr.onerror = () => resolve({ ok: false, status: 0, body: '' });
        xhr.send(form);
    });
}

/** Puts edited bytes back in the browser, in place of what was picked. */
export async function replace(root, id, stream, contentType) {
    const state = states.get(root);
    if (!state) return null;

    const buffer = await stream.arrayBuffer();
    const blob = new Blob([buffer], { type: contentType });
    const bitmap = await createImageBitmap(blob);

    release(root, id);
    const descriptor = keep(state, id, blob, bitmap.width, bitmap.height);
    bitmap.close?.();

    return descriptor;
}

/** Forgets one photo, so a large one does not sit in memory for the life of the page. */
export function release(root, id) {
    const state = states.get(root);
    if (!state) return;

    const url = state.urls.get(id);
    if (url) URL.revokeObjectURL(url);

    state.urls.delete(id);
    state.blobs.delete(id);
}

export function dispose(root) {
    const state = states.get(root);
    if (!state) return;

    state.galleryInput?.removeEventListener('change', state.handler);
    state.cameraInput?.removeEventListener('change', state.handler);

    for (const url of state.urls.values())
        URL.revokeObjectURL(url);

    state.urls.clear();
    state.blobs.clear();
    states.delete(root);
}


async function store(state, bitmap, fileName) {
    const options = state.options || {};
    let w = bitmap.width;
    let h = bitmap.height;
    const max = options.maxDimension || 0;

    if (max > 0 && Math.max(w, h) > max) {
        const scale = max / Math.max(w, h);
        w = Math.round(w * scale);
        h = Math.round(h * scale);
    }

    const canvas = document.createElement('canvas');
    canvas.width = w;
    canvas.height = h;
    canvas.getContext('2d').drawImage(bitmap, 0, 0, w, h);

    const mime = options.format === 'png' ? 'image/png' : 'image/jpeg';
    const quality = typeof options.quality === 'number' ? options.quality : 0.92;
    const blob = await new Promise(resolve => canvas.toBlob(resolve, mime, quality));

    const descriptor = keep(state, newId(), blob, w, h);
    descriptor.fileName = fileName || '';

    return descriptor;
}


function keep(state, id, blob, width, height) {
    const url = URL.createObjectURL(blob);
    state.blobs.set(id, blob);
    state.urls.set(id, url);

    return { id, previewUrl: url, width, height, contentType: blob.type, size: blob.size, fileName: '' };
}


function newId() {
    return crypto.randomUUID ? crypto.randomUUID() : `photo-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}
