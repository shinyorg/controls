// Shiny Blazor frame analysis — the in-browser detection shared by CameraView (a live camera) and MediaElement
// (a playing video). Every function takes a <video> element, so the source does not matter.
//
// A frame read from a cross-origin video without CORS taints the canvas, and getImageData / BarcodeDetector then
// throw a SecurityError: a MediaElement analyzing remote media needs the server's CORS headers plus
// crossorigin="anonymous" on the element.

// --- Document presence detection (mirrors the managed/native MAUI edge detector) ----------------
// Downscale the frame to a small grayscale grid, Otsu-threshold it, take the largest bright connected
// region and read its bounding box. Cheap enough to run every frame; only answers "is a document-shaped
// bright region filling much of the frame, and where?" — never reads text. Returns a normalized box or null.
const docCanvas = document.createElement('canvas');
const docCtx = docCanvas.getContext('2d', { willReadFrequently: true });

export function detectDocument(video) {
    const vw = video.videoWidth, vh = video.videoHeight;
    if (!vw || !vh) return null;

    const target = 120;
    const scale = Math.max(vw, vh) / target;
    const sw = Math.max(16, Math.round(vw / scale));
    const sh = Math.max(16, Math.round(vh / scale));
    docCanvas.width = sw; docCanvas.height = sh;
    docCtx.drawImage(video, 0, 0, sw, sh);

    const data = docCtx.getImageData(0, 0, sw, sh).data;
    const n = sw * sh;
    const lum = new Uint8Array(n);
    const hist = new Int32Array(256);
    for (let i = 0; i < n; i++) {
        const r = data[i * 4], g = data[i * 4 + 1], b = data[i * 4 + 2];
        const y = (r * 77 + g * 150 + b * 29) >> 8;
        lum[i] = y; hist[y]++;
    }

    // Otsu threshold
    let sum = 0;
    for (let t = 0; t < 256; t++) sum += t * hist[t];
    let sumB = 0, wB = 0, maxVar = 0, thresh = 127;
    for (let t = 0; t < 256; t++) {
        wB += hist[t];
        if (wB === 0) continue;
        const wF = n - wB;
        if (wF === 0) break;
        sumB += t * hist[t];
        const mB = sumB / wB, mF = (sum - sumB) / wF;
        const between = wB * wF * (mB - mF) * (mB - mF);
        if (between > maxVar) { maxVar = between; thresh = t; }
    }

    const fg = new Uint8Array(n);
    for (let i = 0; i < n; i++) fg[i] = lum[i] > thresh ? 1 : 0;

    // largest bright connected component (iterative flood fill, 4-connectivity) + its bbox
    const visited = new Uint8Array(n);
    const stack = [];
    let bestArea = 0, bbox = null;
    for (let seed = 0; seed < n; seed++) {
        if (!fg[seed] || visited[seed]) continue;
        let area = 0, minX = sw, minY = sh, maxX = 0, maxY = 0;
        visited[seed] = 1; stack.length = 0; stack.push(seed);
        while (stack.length) {
            const p = stack.pop();
            const px = p % sw, py = (p / sw) | 0;
            area++;
            if (px < minX) minX = px; if (px > maxX) maxX = px;
            if (py < minY) minY = py; if (py > maxY) maxY = py;
            if (px > 0 && fg[p - 1] && !visited[p - 1]) { visited[p - 1] = 1; stack.push(p - 1); }
            if (px < sw - 1 && fg[p + 1] && !visited[p + 1]) { visited[p + 1] = 1; stack.push(p + 1); }
            if (py > 0 && fg[p - sw] && !visited[p - sw]) { visited[p - sw] = 1; stack.push(p - sw); }
            if (py < sh - 1 && fg[p + sw] && !visited[p + sw]) { visited[p + sw] = 1; stack.push(p + sw); }
        }
        if (area > bestArea) { bestArea = area; bbox = { minX, minY, maxX, maxY }; }
    }

    const frac = bestArea / n;
    if (!bbox || frac < 0.15 || frac > 0.99) return null;

    const bw = (bbox.maxX - bbox.minX) / sw;
    const bh = (bbox.maxY - bbox.minY) / sh;
    if (bw < 0.25 || bh < 0.25) return null; // don't ship a sliver

    return { x: bbox.minX / sw, y: bbox.minY / sh, w: bw, h: bh };
}


export function boxCenterInWindow(box, win) {
    const cx = box.x + box.w / 2;
    const cy = box.y + box.h / 2;
    return cx >= win[0] && cx <= win[0] + win[2] && cy >= win[1] && cy <= win[1] + win[3];
}


export function toOverlayBox(code, video) {
    // BarcodeDetector boundingBox is in video pixel space; normalize to 0..1.
    const w = video.videoWidth || 1;
    const h = video.videoHeight || 1;
    const b = code.boundingBox;
    return {
        x: b.x / w,
        y: b.y / h,
        w: b.width / w,
        h: b.height / h,
        strokeColor: '#22C55E',
        text: code.rawValue,
        textColor: '#22C55E'
    };
}


// True when the code's center falls inside the normalized scan window [x, y, w, h].
export function inScanWindow(code, video, win) {
    const vw = video.videoWidth || 1;
    const vh = video.videoHeight || 1;
    const b = code.boundingBox;
    const cx = (b.x + b.width / 2) / vw;
    const cy = (b.y + b.height / 2) / vh;
    return cx >= win[0] && cx <= win[0] + win[2] && cy >= win[1] && cy <= win[1] + win[3];
}


export function toBarcode(code, video) {
    const w = video.videoWidth || 1;
    const h = video.videoHeight || 1;
    const b = code.boundingBox;
    return {
        format: code.format,
        value: code.rawValue,
        x: b.x / w,
        y: b.y / h,
        w: b.width / w,
        h: b.height / h
    };
}


// `rect` (optional) is where the picture actually sits in the overlay, in overlay pixels — a letterboxed or
// cropped video (object-fit: contain / cover) does not fill it. Omitted, the picture is taken to fill the overlay.
export function drawOverlay(ctx, overlay, boxes, scanWindow, rect) {
    ctx.clearRect(0, 0, overlay.width, overlay.height);
    const ox = rect ? rect.x : 0, oy = rect ? rect.y : 0;
    const pw = rect ? rect.w : overlay.width, ph = rect ? rect.h : overlay.height;

    // scan-window viewfinder: dim outside it and frame it with a reticle
    if (scanWindow) {
        const wx = ox + scanWindow[0] * pw;
        const wy = oy + scanWindow[1] * ph;
        const ww = scanWindow[2] * pw;
        const wh = scanWindow[3] * ph;
        ctx.fillStyle = 'rgba(0, 0, 0, 0.43)';
        ctx.fillRect(0, 0, overlay.width, wy);
        ctx.fillRect(0, wy + wh, overlay.width, overlay.height - (wy + wh));
        ctx.fillRect(0, wy, wx, wh);
        ctx.fillRect(wx + ww, wy, overlay.width - (wx + ww), wh);
        ctx.lineWidth = 2;
        ctx.strokeStyle = '#FFFFFF';
        ctx.strokeRect(wx, wy, ww, wh);
    }

    ctx.lineWidth = 3;
    ctx.font = '16px sans-serif';
    for (const b of boxes) {
        const x = ox + b.x * pw;
        const y = oy + b.y * ph;
        const w = b.w * pw;
        const h = b.h * ph;
        ctx.strokeStyle = b.strokeColor || '#22D3EE';
        ctx.fillStyle = b.textColor || b.strokeColor || '#22D3EE';
        ctx.strokeRect(x, y, w, h);
        if (b.text) ctx.fillText(b.text, x, Math.max(14, y - 6));
    }
}


