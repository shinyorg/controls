// Shiny Blazor IMediaService interop — permissions and gallery picking. The modal camera itself is a
// CameraView (camera.js); both share media-store.js, so every result is a descriptor plus a Blob kept here.

import { keep, take, release as releaseBlob, encode } from './media-store.js';
export { listCameras } from './camera.js';

export function isCameraSupported() {
    return !!navigator.mediaDevices?.getUserMedia && window.isSecureContext !== false;
}

/**
 * The camera permission WITHOUT prompting: 'granted', 'denied', 'prompt' or 'unsupported'. Browsers that
 * cannot answer (Firefox and older Safari reject the 'camera' permission name) report 'prompt', which the
 * service treats as "open the camera and let getUserMedia ask".
 */
export async function cameraPermissionState() {
    if (!isCameraSupported()) return 'unsupported';
    try {
        const status = await navigator.permissions.query({ name: 'camera' });
        return status.state;
    }
    catch {
        return 'prompt';
    }
}

/** Ask for the camera (and microphone) by opening and immediately closing a stream. */
export async function requestCameraPermission(includeMicrophone) {
    if (!isCameraSupported()) return 'unsupported';
    try {
        const stream = await navigator.mediaDevices.getUserMedia({ video: true, audio: !!includeMicrophone });
        stream.getTracks().forEach(t => t.stop());
        return 'granted';
    }
    catch (err) {
        switch (err?.name) {
            case 'SecurityError':
                return 'restricted';     // permissions policy / insecure context — asking again will not help
            case 'NotFoundError':
            case 'OverconstrainedError':
                return 'unsupported';    // no camera (or no microphone) on this device
            default:
                return 'denied';
        }
    }
}

/**
 * Show the browser's file chooser and resolve with descriptors for what was picked ([] on cancel).
 * Images are decoded (EXIF orientation applied), downscaled and re-encoded; videos are kept as picked.
 */
export function pick(accept, multiple, maxCount, isImage, maxDim, mime, quality) {
    return new Promise(resolve => {
        const input = document.createElement('input');
        input.type = 'file';
        input.accept = accept;
        input.multiple = !!multiple;
        // off-screen rather than display:none — some WebKit builds refuse to open a chooser for a hidden input
        input.style.cssText = 'position:fixed;left:-10000px;top:0;width:1px;height:1px;opacity:0';
        document.body.appendChild(input);

        let done = false;
        const finish = async files => {
            if (done) return;
            done = true;
            input.remove();

            const picked = [];
            for (const file of files.slice(0, Math.max(1, maxCount || 1))) {
                try {
                    picked.push(isImage
                        ? await storeImage(file, maxDim, mime, quality)
                        : await storeVideo(file));
                }
                catch (err) {
                    console.warn('[shiny-media] skipped a file that could not be read', file.name, err);
                }
            }
            resolve(picked);
        };

        input.addEventListener('change', () => finish([...(input.files || [])]));
        input.addEventListener('cancel', () => finish([]));

        // Browsers that predate the input 'cancel' event (Safari < 16.4) never say the chooser was dismissed;
        // the window regaining focus is the only signal, and it can arrive before 'change', hence the delay.
        if (!('oncancel' in input)) {
            window.addEventListener('focus', () => setTimeout(() => {
                if (!input.files || input.files.length === 0) finish([]);
            }, 1000), { once: true });
        }

        input.click();
    });
}

async function storeImage(file, maxDim, mime, quality) {
    const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
    try {
        const { blob, width, height } = await encode(bitmap, bitmap.width, bitmap.height, maxDim, mime, quality, null);
        return keep(blob, width, height, -1, file.name);
    }
    finally {
        bitmap.close?.();
    }
}

async function storeVideo(file) {
    const meta = await readVideoMetadata(file);
    return keep(file, meta.width, meta.height, meta.durationMs, file.name);
}

// Dimensions and duration from a throwaway <video>. Best effort: a codec the browser cannot decode never
// fires loadedmetadata, so give up after a few seconds rather than hold the pick hostage.
function readVideoMetadata(file) {
    return new Promise(resolve => {
        const url = URL.createObjectURL(file);
        const video = document.createElement('video');
        video.preload = 'metadata';
        video.muted = true;

        const done = meta => {
            clearTimeout(timer);
            URL.revokeObjectURL(url);
            video.removeAttribute('src');
            resolve(meta);
        };
        const timer = setTimeout(() => done({ width: 0, height: 0, durationMs: -1 }), 4000);

        video.onloadedmetadata = () => done({
            width: video.videoWidth,
            height: video.videoHeight,
            durationMs: isFinite(video.duration) ? video.duration * 1000 : -1
        });
        video.onerror = () => done({ width: 0, height: 0, durationMs: -1 });
        video.src = url;
    });
}

export function read(id) {
    return take(id);
}

export function release(id) {
    releaseBlob(id);
}

export function isBarcodeDetectorAvailable() {
    return 'BarcodeDetector' in globalThis;
}

export function vibrate(ms) {
    try { navigator.vibrate?.(ms); } catch { /* not allowed without a user gesture on some browsers */ }
}
