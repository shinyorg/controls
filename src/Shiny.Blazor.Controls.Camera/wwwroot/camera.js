// Shiny Blazor CameraView interop.
// All frame analysis runs here in JS; only flat overlay-box + barcode DTOs cross back to .NET.

import { keep, encode } from './media-store.js';
import { detectDocument, boxCenterInWindow, toOverlayBox, inScanWindow, toBarcode, drawOverlay } from '../Shiny.Blazor.Controls.Analysis/frame-analysis.js';

const states = new WeakMap();

// A start() still waiting on getUserMedia (the permission prompt can sit there indefinitely) has no state
// yet, so a stop() in that window would find nothing and the stream would open afterwards with nobody left
// to close it. stop() flags the pending start here instead, and start() releases the stream on arrival.
const pendingStarts = new WeakMap();

export async function listCameras() {
    if (!navigator.mediaDevices?.enumerateDevices)
        return [];
    // labels are only populated after camera permission has been granted (i.e. after start())
    const devices = await navigator.mediaDevices.enumerateDevices();
    return devices
        .filter(d => d.kind === 'videoinput')
        .map((d, i) => ({ id: d.deviceId, name: d.label || `Camera ${i + 1}` }));
}

export async function start(video, overlay, dotnetRef, facingMode, analyzerKind, showOverlay, deviceId, showBoundingBox, scanWindow) {
    if (!navigator.mediaDevices?.getUserMedia)
        throw new Error('getUserMedia is unavailable (requires a secure context / HTTPS).');

    // an exact deviceId pins a specific camera; otherwise fall back to the front/back facing hint. The ideal
    // size is a preference, not a requirement — without one Chrome opens the camera at 640×480, which makes
    // every capture a VGA photo however good the lens is.
    const video_constraints = deviceId
        ? { deviceId: { exact: deviceId }, width: { ideal: 1920 }, height: { ideal: 1080 } }
        : { facingMode, width: { ideal: 1920 }, height: { ideal: 1080 } };
    const pending = { cancelled: false };
    pendingStarts.set(video, pending);

    let stream;
    try {
        stream = await navigator.mediaDevices.getUserMedia({
            video: video_constraints,
            audio: false
        });
        if (pending.cancelled) {
            stream.getTracks().forEach(t => t.stop());
            return;
        }
        video.srcObject = stream;
        await video.play();
    }
    catch (err) {
        // play() rejecting (element removed, autoplay policy) must not leave the camera light on
        stream?.getTracks().forEach(t => t.stop());
        if (video.srcObject === stream) video.srcObject = null;
        throw err;
    }
    finally {
        if (pendingStarts.get(video) === pending) pendingStarts.delete(video);
    }

    if (pending.cancelled) {
        stream.getTracks().forEach(t => t.stop());
        video.srcObject = null;
        return;
    }

    const state = {
        video, overlay, dotnet: dotnetRef, stream,
        analyzerKind, showOverlay,
        showBoundingBox: showBoundingBox !== false,
        scanWindow: scanWindow || null,   // [x, y, w, h] normalized, or null for the whole frame
        // the element keeps its filter across a stop/start (an analyzer or lens change restarts the stream), so
        // seed from it rather than resetting to none and shipping unfiltered document crops afterwards
        filterCss: video.style.filter || 'none',
        running: true,
        rafId: null,
        detector: null,
        busy: false,
        armed: false,  // gated: a result is only delivered to .NET while armed (see arm())
        docStable: 0,  // consecutive frames a document has been present (document mode)
        docCleared: true,   // no document has been in view since the last delivery
        requireNew: false   // this arm wants a different page: wait for docCleared before delivering
    };
    states.set(video, state);

    if (analyzerKind === 'barcode' && 'BarcodeDetector' in globalThis) {
        try { state.detector = new globalThis.BarcodeDetector(); }
        catch { state.detector = null; }
    }
    else if (analyzerKind === 'barcode') {
        // No native detector (Firefox/Safari). Report once; a ZXing-js module can be slotted in here.
        try { await dotnetRef.invokeMethodAsync('OnJsError', 'BarcodeDetector not supported in this browser'); }
        catch { /* ignore */ }
    }
    else if (analyzerKind && analyzerKind !== 'document') {
        // barcode + document have in-browser engines today; other kinds are placeholders
        try { await dotnetRef.invokeMethodAsync('OnJsError', `Analyzer '${analyzerKind}' is not supported in the browser`); }
        catch { /* ignore */ }
    }

    const ctx = overlay.getContext('2d');
    const loop = async () => {
        if (!state.running) return;
        syncOverlaySize(state);

        if (state.detector && !state.busy) {
            state.busy = true;
            try {
                let codes = await state.detector.detect(video);
                // restrict to the scan window (codes whose center falls inside it)
                if (state.scanWindow)
                    codes = codes.filter(c => inScanWindow(c, video, state.scanWindow));

                const boxes = state.showBoundingBox ? codes.map(c => toOverlayBox(c, video)) : [];
                // boxes always draw + flow to .NET (presentation); the decoded value is gated behind arm()
                if (state.showOverlay) drawOverlay(ctx, overlay, boxes, state.scanWindow);
                await state.dotnet.invokeMethodAsync('OnOverlays', boxes);
                if (state.armed && codes.length > 0) {
                    state.armed = false; // one delivery per arm; .NET re-arms to keep scanning
                    await state.dotnet.invokeMethodAsync('OnBarcodes', codes.map(c => toBarcode(c, video)));
                }
            }
            catch { /* transient detect error; keep looping */ }
            finally { state.busy = false; }
        }
        else if (state.analyzerKind === 'document' && !state.busy) {
            state.busy = true;
            try {
                // cheap presence gate (no OCR): find the document's bounding box from a downscaled frame
                const box = detectDocument(video);
                let drawBox = null;
                if (box) {
                    if (state.scanWindow && !boxCenterInWindow(box, state.scanWindow)) {
                        // outside the aim window — treat as absent
                        state.docStable = 0;
                        state.docCleared = true;
                    }
                    else {
                        state.docStable++;
                        drawBox = state.showBoundingBox
                            ? { x: box.x, y: box.y, w: box.w, h: box.h, strokeColor: '#14B8A6', text: null, textColor: '#14B8A6' }
                            : null;
                    }
                }
                else {
                    state.docStable = 0;
                    state.docCleared = true;   // the page left the frame; the next one is a new document
                }

                const boxes = drawBox ? [drawBox] : [];
                if (state.showOverlay) drawOverlay(ctx, overlay, boxes, state.scanWindow);
                await state.dotnet.invokeMethodAsync('OnOverlays', boxes);

                // ship the image to .NET only when armed and the document has been steadily in view
                if (state.armed && box && state.docStable >= DOC_STABILITY && (!state.requireNew || state.docCleared)) {
                    state.armed = false;
                    state.docStable = 0;
                    state.docCleared = false;
                    const jpeg = captureRegion(video, state.filterCss, box, DOC_PADDING);
                    await state.dotnet.invokeMethodAsync('OnDocumentImage', [box.x, box.y, box.w, box.h], jpeg);
                }
            }
            catch { /* transient frame error; keep looping */ }
            finally { state.busy = false; }
        }
        state.rafId = requestAnimationFrame(loop);
    };
    state.rafId = requestAnimationFrame(loop);
}

// document presence tuning
const DOC_STABILITY = 6;   // frames a document must persist before it's shipped (debounce blur/motion)
const DOC_PADDING = 0.04;  // fraction of the document size added as crop margin on each side


export function stop(video) {
    const pending = pendingStarts.get(video);
    if (pending) {
        pending.cancelled = true;
        pendingStarts.delete(video);
    }

    const state = states.get(video);
    if (!state) return;
    state.running = false;
    if (state.rafId) cancelAnimationFrame(state.rafId);
    // an in-flight recording holds its own microphone stream; drop it rather than leave the mic open
    // (handlers are left in place so an outstanding stopRecording() still resolves with what was captured)
    if (state.recorder) {
        try { if (state.recorder.state !== 'inactive') state.recorder.stop(); } catch { /* already stopped */ }
    }
    state.recExtraAudio?.getTracks().forEach(t => t.stop());
    state.stream?.getTracks().forEach(t => t.stop());
    video.srcObject = null;
    const ctx = state.overlay.getContext('2d');
    ctx.clearRect(0, 0, state.overlay.width, state.overlay.height);
    states.delete(video);
}


// Arm the detector to deliver the next frame's decoded barcodes to .NET (then it self-disarms). Boxes keep
// drawing every frame regardless; this only gates the OnBarcodes callback.
export function arm(video, requireNew) {
    const state = states.get(video);
    if (!state) return;
    state.armed = true;
    state.requireNew = !!requireNew;
}


export function disarm(video) {
    const state = states.get(video);
    if (state) state.armed = false;
}


// A single hidden <svg> holds every generated filter definition. Effects that CSS shorthands can't express —
// an arbitrary colour matrix, a convolution — live here and are referenced from the CSS `filter` value as
// url(#id), which the spec composes in order alongside the built-in filter functions.
let svgDefsHost = null;

function ensureSvgHost() {
    if (svgDefsHost) return svgDefsHost;

    svgDefsHost = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svgDefsHost.setAttribute('aria-hidden', 'true');
    // must stay in the layout tree for the filters to resolve, so hide it without display:none
    svgDefsHost.style.cssText = 'position:absolute;width:0;height:0;overflow:hidden;pointer-events:none';
    document.body.appendChild(svgDefsHost);
    return svgDefsHost;
}

// ids/markups are parallel arrays (not an array of objects) — see ApplyFilterAsync for why.
function applySvgFilters(prefix, ids, markups) {
    const host = ensureSvgHost();

    // drop this camera's previous definitions before adding the new ones
    for (const existing of [...host.children])
        if (existing.id && existing.id.startsWith(prefix)) existing.remove();

    if (!ids || ids.length === 0) return;

    for (let i = 0; i < ids.length; i++) {
        const filter = document.createElementNS('http://www.w3.org/2000/svg', 'filter');
        filter.setAttribute('id', ids[i]);
        // the frame is already in sRGB; the SVG default (linearRGB) would shift every colour
        filter.setAttribute('color-interpolation-filters', 'sRGB');
        // Pin the filter region to the frame. The default (-10%, -10%, 120%, 120%) pads the source with
        // transparent black, which both softens the edges of a convolution and — because feTile tiles from the
        // region origin — knocks the pixelate mosaic out of alignment with the picture.
        filter.setAttribute('x', '0');
        filter.setAttribute('y', '0');
        filter.setAttribute('width', '100%');
        filter.setAttribute('height', '100%');
        filter.innerHTML = markups[i];
        host.appendChild(filter);
    }
}

// Final teardown for a disposed CameraView: release the camera (including a start still waiting on
// permission) and remove this instance's SVG filter definitions from the shared host in <body>.
export function dispose(video, prefix) {
    stop(video);
    if (prefix && svgDefsHost) applySvgFilters(prefix, null, null);
}

export function setFilter(video, css, prefix, ids, markups) {
    if (prefix) applySvgFilters(prefix, ids, markups);

    video.style.filter = css || 'none';
    const state = states.get(video);
    if (state) state.filterCss = css || 'none';
}


export async function startRecording(video, includeAudio) {
    const state = states.get(video);
    if (!state) throw new Error('Camera is not started');

    let stream = state.stream;
    let extraAudio = null;
    if (includeAudio) {
        try {
            extraAudio = await navigator.mediaDevices.getUserMedia({ audio: true });
            stream = new MediaStream([...state.stream.getVideoTracks(), ...extraAudio.getAudioTracks()]);
        }
        catch { extraAudio = null; /* fall back to video-only */ }
    }

    // The microphone prompt can sit open indefinitely. If the camera was stopped meanwhile (the view closed),
    // starting now would record a dead stream and, worse, leave the freshly granted microphone on.
    if (states.get(video) !== state || !state.running) {
        extraAudio?.getTracks().forEach(t => t.stop());
        throw new Error('Camera stopped before recording could start');
    }

    const chunks = [];
    const rec = new MediaRecorder(stream);
    rec.ondataavailable = e => { if (e.data.size) chunks.push(e.data); };
    state.recorder = rec;
    state.recChunks = chunks;
    state.recExtraAudio = extraAudio;
    state.recStartedAt = performance.now();
    rec.start();
}


// Stop the recorder and resolve with the finished Blob plus how long it ran.
function finishRecording(video) {
    return new Promise((resolve, reject) => {
        const state = states.get(video);
        if (!state?.recorder) { reject(new Error('Not recording')); return; }
        const rec = state.recorder;
        rec.onstop = () => {
            const blob = new Blob(state.recChunks, { type: rec.mimeType || 'video/webm' });
            const durationMs = state.recStartedAt ? performance.now() - state.recStartedAt : -1;
            state.recExtraAudio?.getTracks().forEach(t => t.stop());
            state.recorder = null; state.recChunks = null; state.recExtraAudio = null; state.recStartedAt = null;
            resolve({ blob, durationMs });
        };
        rec.stop();
    });
}


export async function stopRecording(video) {
    const { blob } = await finishRecording(video);
    return new Uint8Array(await blob.arrayBuffer());
}


// IMediaService: stop and keep the recording in media-store.js, handing back only its descriptor.
export async function stopRecordingStored(video) {
    const { blob, durationMs } = await finishRecording(video);
    return keep(blob, video.videoWidth, video.videoHeight, durationMs, null);
}


// IMediaService: capture a still downscaled to maxDim and encoded as mime/quality, kept in media-store.js.
export async function captureStored(video, filterCss, maxWidth, maxHeight, mime, quality) {
    const { blob, width, height } = await encode(video, video.videoWidth, video.videoHeight, maxWidth, maxHeight, mime, quality, filterCss);
    return keep(blob, width, height, -1, null);
}


export function capture(video, filterCss) {
    const canvas = document.createElement('canvas');
    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;
    const ctx = canvas.getContext('2d');
    if (filterCss && filterCss !== 'none')
        ctx.filter = filterCss;   // bake the preview filter into the still
    ctx.drawImage(video, 0, 0);
    const dataUrl = canvas.toDataURL('image/jpeg', 0.92);
    const base64 = dataUrl.split(',')[1];
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    return bytes;
}


// Crop the (padded) document region out of the current frame and JPEG-encode it for the AI call.
function captureRegion(video, filterCss, box, padding) {
    const vw = video.videoWidth, vh = video.videoHeight;
    const pad = padding || 0;
    let x = Math.max(0, box.x - box.w * pad) * vw;
    let y = Math.max(0, box.y - box.h * pad) * vh;
    let right = Math.min(1, box.x + box.w * (1 + pad)) * vw;
    let bottom = Math.min(1, box.y + box.h * (1 + pad)) * vh;
    const cw = Math.max(1, Math.round(right - x));
    const ch = Math.max(1, Math.round(bottom - y));

    const canvas = document.createElement('canvas');
    canvas.width = cw; canvas.height = ch;
    const ctx = canvas.getContext('2d');
    if (filterCss && filterCss !== 'none') ctx.filter = filterCss;
    ctx.drawImage(video, Math.round(x), Math.round(y), cw, ch, 0, 0, cw, ch);

    const dataUrl = canvas.toDataURL('image/jpeg', 0.9);
    const base64 = dataUrl.split(',')[1];
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    return bytes;
}


function syncOverlaySize(state) {
    const { overlay, video } = state;
    if (overlay.width !== video.clientWidth || overlay.height !== video.clientHeight) {
        overlay.width = video.clientWidth;
        overlay.height = video.clientHeight;
    }
}


