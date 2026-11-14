// Shiny Blazor MediaElement frame analysis: runs the shared frame-analysis detectors over a playing <video>.
//
// Frames are taken with requestVideoFrameCallback where the browser has it, which fires once per *presented*
// frame — so a paused video is analyzed once rather than sixty times a second — and requestAnimationFrame with a
// currentTime check where it does not. A seek clears the boxes and tells .NET (OnAnalysisDiscontinuity).

import { detectDocument, boxCenterInWindow, toOverlayBox, inScanWindow, toBarcode, drawOverlay } from './frame-analysis.js';

const states = new WeakMap();

export function start(video, overlay, dotnet, kind, showOverlay, showBoundingBox, scanWindow, fit) {
    stop(video);

    const state = {
        video, overlay, dotnet, kind,
        showOverlay: showOverlay !== false,
        showBoundingBox: showBoundingBox !== false,
        scanWindow: scanWindow || null,
        fit: fit || 'contain',
        running: true,
        busy: false,
        armed: false,
        lastTime: -1,
        detector: null,
        reportedError: false,
        onSeeking: null
    };

    if (kind === 'barcode' && 'BarcodeDetector' in globalThis) {
        try { state.detector = new globalThis.BarcodeDetector(); }
        catch { state.detector = null; }
    }
    if (kind === 'barcode' && !state.detector)
        reportError(state, 'BarcodeDetector not supported in this browser');
    else if (kind !== 'barcode' && kind !== 'document')
        reportError(state, `Analyzer '${kind}' is not supported in the browser`);

    state.onSeeking = () => {
        state.lastTime = -1;
        clear(state);
        state.dotnet.invokeMethodAsync('OnAnalysisDiscontinuity').catch(() => { });
    };
    video.addEventListener('seeking', state.onSeeking);
    states.set(video, state);
    schedule(state);
}

export function stop(video) {
    const state = states.get(video);
    if (!state) return;
    state.running = false;
    video.removeEventListener('seeking', state.onSeeking);
    clear(state);
    states.delete(video);
}

export function arm(video) {
    const state = states.get(video);
    if (state) state.armed = true;
}

export function disarm(video) {
    const state = states.get(video);
    if (state) state.armed = false;
}

export function setFit(video, fit) {
    const state = states.get(video);
    if (state) state.fit = fit || 'contain';
}

function schedule(state) {
    if (!state.running) return;
    const video = state.video;
    if ('requestVideoFrameCallback' in video)
        video.requestVideoFrameCallback(() => tick(state));
    else
        requestAnimationFrame(() => tick(state));
}

async function tick(state) {
    if (!state.running) return;
    const video = state.video;

    // without rVFC, only a frame the clock has moved past is a new one
    if (!('requestVideoFrameCallback' in video) && video.currentTime === state.lastTime) {
        schedule(state);
        return;
    }
    state.lastTime = video.currentTime;

    if (!state.busy && video.videoWidth > 0) {
        state.busy = true;
        try {
            if (state.kind === 'barcode' && state.detector)
                await detectBarcodes(state);
            else if (state.kind === 'document')
                await detectDocumentPresence(state);
        }
        catch (err) {
            // a tainted (cross-origin, no CORS) frame throws SecurityError on every read — say so once
            if (err && err.name === 'SecurityError')
                reportError(state, 'The video is cross-origin without CORS, so its frames cannot be read. Serve it with Access-Control-Allow-Origin and set CrossOrigin="anonymous".');
        }
        finally {
            state.busy = false;
        }
    }
    schedule(state);
}

async function detectBarcodes(state) {
    const video = state.video;
    let codes = await state.detector.detect(video);
    if (state.scanWindow)
        codes = codes.filter(c => inScanWindow(c, video, state.scanWindow));

    const boxes = state.showBoundingBox ? codes.map(c => toOverlayBox(c, video)) : [];
    draw(state, boxes);
    await state.dotnet.invokeMethodAsync('OnAnalysisOverlays', boxes, video.currentTime);
    if (state.armed && codes.length > 0) {
        state.armed = false; // one delivery per arm; .NET re-arms to keep scanning
        await state.dotnet.invokeMethodAsync('OnAnalysisBarcodes', codes.map(c => toBarcode(c, video)), video.currentTime);
    }
}

async function detectDocumentPresence(state) {
    const box = detectDocument(state.video);
    const inside = box && (!state.scanWindow || boxCenterInWindow(box, state.scanWindow));
    const boxes = inside && state.showBoundingBox
        ? [{ x: box.x, y: box.y, w: box.w, h: box.h, strokeColor: '#14B8A6', text: null, textColor: '#14B8A6' }]
        : [];
    draw(state, boxes);
    await state.dotnet.invokeMethodAsync('OnAnalysisOverlays', boxes, state.video.currentTime);
    if (inside && state.armed) {
        state.armed = false;
        await state.dotnet.invokeMethodAsync('OnAnalysisDocument', [box.x, box.y, box.w, box.h], state.video.currentTime);
    }
}

function draw(state, boxes) {
    if (!state.showOverlay || !state.overlay) return;
    const { overlay, video } = state;
    if (overlay.width !== video.clientWidth || overlay.height !== video.clientHeight) {
        overlay.width = video.clientWidth;
        overlay.height = video.clientHeight;
    }
    drawOverlay(overlay.getContext('2d'), overlay, boxes, state.scanWindow, pictureRect(state));
}

function clear(state) {
    const ctx = state.overlay?.getContext('2d');
    if (ctx) ctx.clearRect(0, 0, state.overlay.width, state.overlay.height);
}

// Where the picture sits inside the element for its object-fit, in element pixels.
function pictureRect(state) {
    const { video, fit } = state;
    const ew = video.clientWidth, eh = video.clientHeight;
    const vw = video.videoWidth || ew, vh = video.videoHeight || eh;
    if (fit === 'fill' || !vw || !vh) return { x: 0, y: 0, w: ew, h: eh };

    const scale = fit === 'cover' ? Math.max(ew / vw, eh / vh) : Math.min(ew / vw, eh / vh);
    const w = vw * scale, h = vh * scale;
    return { x: (ew - w) / 2, y: (eh - h) / 2, w, h };
}

function reportError(state, message) {
    if (state.reportedError) return;
    state.reportedError = true;
    state.dotnet.invokeMethodAsync('OnAnalysisErrorJs', message).catch(() => { });
}
