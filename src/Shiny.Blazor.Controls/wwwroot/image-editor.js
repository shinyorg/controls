const states = new WeakMap();

export function init(root, canvas, dotnetRef, options) {
    const ctx = canvas.getContext('2d');
    const state = {
        root, canvas, ctx, dotnet: dotnetRef,
        image: null,
        actions: [],
        redoStack: [],
        mode: 'none',
        viewTransform: { scale: 1, tx: 0, ty: 0 },
        // Crop
        cropRect: null, // { x, y, w, h } normalized 0-1
        activeCropHandle: null,
        cropDragStart: null,
        cropStartRect: null,
        // Draw
        currentStroke: null,
        drawColor: options?.drawColor || '#ffffff',
        drawWidth: options?.drawWidth || 3,
        // Line / arrow
        activeLine: null, // { start: {x,y}, end: {x,y}, isArrow: bool }
        // Shapes. The border is the ink colour/width above; the fill is its own colour and is
        // null when the shape is an outline only.
        activeShape: null, // { kind: 'rect'|'ellipse'|'circle', start: {x,y}, end: {x,y}, square: bool }
        shapeFill: options?.shapeFill || null,
        // Text
        textColor: options?.textColor || '#ffffff',
        textSize: options?.textSize || 16,
        textFont: options?.textFont || 'Arial',
        // Zoom / pan. The transform is applied to the canvas rather than to the element so
        // that it works with every tool: pointer coordinates are mapped back through it before
        // any tool math runs, which is what lets you zoom to 400% and still draw accurately.
        pointers: new Map(),
        pinch: null,
        pan: null,
        allowZoom: options?.allowZoom !== false,
        minZoom: options?.minZoom || 1,
        maxZoom: options?.maxZoom || 8,
        // Image rect cache
        imageRect: { x: 0, y: 0, w: 0, h: 0 }
    };

    states.set(root, state);

    state._handlers = {
        pointerdown: e => onPointerDown(state, e),
        pointermove: e => onPointerMove(state, e),
        pointerup: e => onPointerUp(state, e),
        pointercancel: e => onPointerUp(state, e),
        wheel: e => onWheel(state, e),
        dblclick: e => onDoubleClick(state, e),
        contextmenu: e => e.preventDefault()
    };
    for (const [name, handler] of Object.entries(state._handlers)) {
        canvas.addEventListener(name, handler, name === 'wheel' ? { passive: false } : undefined);
    }

    resizeCanvas(state);
    const container = canvas.parentElement;
    const observer = new ResizeObserver(() => { resizeCanvas(state); clampOffsets(state); redraw(state); });
    observer.observe(container);
    state._observer = observer;
    updateCursor(state);
}

export function loadImage(root, src) {
    const state = states.get(root);
    if (!state) return;

    const img = new Image();
    img.crossOrigin = 'anonymous';
    img.onload = () => { state.image = img; redraw(state); };
    img.src = src;
}

export function loadImageData(root, bytes) {
    const state = states.get(root);
    if (!state) return;

    const blob = new Blob([bytes]);
    const url = URL.createObjectURL(blob);
    const img = new Image();
    img.onload = () => {
        URL.revokeObjectURL(url);
        state.image = img;
        redraw(state);
    };
    img.src = url;
}

export function setMode(root, mode) {
    const state = states.get(root);
    if (!state) return;

    finalizeOperation(state);
    state.mode = mode;

    if (mode === 'crop') {
        state.cropRect = { x: 0.1, y: 0.1, w: 0.8, h: 0.8 };
        state.viewTransform = { scale: 1, tx: 0, ty: 0 };
        notifyZoom(state);
    } else {
        state.cropRect = null;
    }
    state.activeLine = null;
    state.activeShape = null;
    redraw(state);
    updateCursor(state);
}

export function undo(root) {
    const state = states.get(root);
    if (!state || state.actions.length === 0) return;

    const action = state.actions.pop();
    state.redoStack.push(action);
    redraw(state);
    notifyUndoState(state);
}

export function redo(root) {
    const state = states.get(root);
    if (!state || state.redoStack.length === 0) return;

    const action = state.redoStack.pop();
    state.actions.push(action);
    redraw(state);
    notifyUndoState(state);
}

export function rotate(root, degrees) {
    const state = states.get(root);
    if (!state) return;

    state.actions.push({ type: 'rotate', angle: degrees });
    state.redoStack = [];
    redraw(state);
    notifyUndoState(state);
}

export function reset(root) {
    const state = states.get(root);
    if (!state) return;

    state.actions = [];
    state.redoStack = [];
    state.cropRect = null;
    state.currentStroke = null;
    state.activeLine = null;
    state.activeShape = null;
    state.mode = 'none';
    state.viewTransform = { scale: 1, tx: 0, ty: 0 };
    state.pinch = null;
    state.pan = null;
    redraw(state);
    notifyUndoState(state);
    notifyZoom(state);
    updateCursor(state);
}

export function applyCrop(root) {
    const state = states.get(root);
    if (!state || !state.cropRect) return;

    const c = state.cropRect;
    if (c.x < 0.01 && c.y < 0.01 && c.w > 0.98 && c.h > 0.98) {
        state.cropRect = null;
        state.mode = 'none';
        redraw(state);
        return;
    }

    state.actions.push({ type: 'crop', rect: { ...c } });
    state.redoStack = [];
    state.cropRect = null;
    state.mode = 'none';
    redraw(state);
    notifyUndoState(state);
}

export function updateDrawSettings(root, color, width) {
    const state = states.get(root);
    if (!state) return;
    state.drawColor = color;
    state.drawWidth = width;
}

/** `fill` is any CSS colour, or null for an outline-only shape. */
export function updateShapeSettings(root, fill) {
    const state = states.get(root);
    if (!state) return;
    state.shapeFill = fill || null;
}

export function updateTextSettings(root, color, size, font) {
    const state = states.get(root);
    if (!state) return;
    state.textColor = color;
    state.textSize = size;
    state.textFont = font;
}

export function updateAllowZoom(root, allow) {
    const state = states.get(root);
    if (!state) return;

    state.allowZoom = allow;
    if (!allow && state.viewTransform.scale !== 1) {
        state.viewTransform = { scale: 1, tx: 0, ty: 0 };
        redraw(state);
        notifyZoom(state);
    }
    updateCursor(state);
}

export async function exportImage(root, format, quality, targetWidth, targetHeight) {
    const state = states.get(root);
    if (!state || !state.image) return new Uint8Array(0);

    const eff = computeEffective(state.image, state.actions);
    const w = targetWidth || Math.max(1, Math.round(eff.needsSwap ? eff.sh : eff.sw));
    const h = targetHeight || Math.max(1, Math.round(eff.needsSwap ? eff.sw : eff.sh));

    const offscreen = document.createElement('canvas');
    offscreen.width = w;
    offscreen.height = h;
    const ctx = offscreen.getContext('2d');

    // Replay all actions at export resolution (no view transform)
    replayActions(ctx, state.image, state.actions, w, h);

    const mimeType = format === 'jpeg' ? 'image/jpeg'
        : format === 'webp' ? 'image/webp'
        : 'image/png';

    const blob = await new Promise(resolve => offscreen.toBlob(resolve, mimeType, quality));
    const buf = await blob.arrayBuffer();
    return new Uint8Array(buf);
}

export function dispose(root) {
    const state = states.get(root);
    if (!state) return;

    for (const [name, handler] of Object.entries(state._handlers || {})) {
        state.canvas.removeEventListener(name, handler);
    }
    state._observer?.disconnect();
    states.delete(root);
}

// --- Internal ---

function resizeCanvas(state) {
    const container = state.canvas.parentElement;
    const rect = container.getBoundingClientRect();
    const dpr = window.devicePixelRatio || 1;
    state.canvas.width = rect.width * dpr;
    state.canvas.height = rect.height * dpr;
    state.canvas.style.width = rect.width + 'px';
    state.canvas.style.height = rect.height + 'px';
    state.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
}

function redraw(state) {
    const { ctx, canvas } = state;
    const dpr = window.devicePixelRatio || 1;
    const w = canvas.width / dpr;
    const h = canvas.height / dpr;

    ctx.save();
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, w, h);
    ctx.fillStyle = '#000';
    ctx.fillRect(0, 0, w, h);

    if (!state.image) {
        ctx.restore();
        return;
    }

    // Apply view transform
    const cx = w / 2, cy = h / 2;
    const vt = state.viewTransform;
    ctx.translate(cx + vt.tx, cy + vt.ty);
    ctx.scale(vt.scale, vt.scale);
    ctx.translate(-cx, -cy);

    // Draw image with all committed actions; the returned rect is where the
    // (cropped/rotated) image actually landed, which pointer handlers need
    const ir = replayActions(ctx, state.image, state.actions, w, h);
    state.imageRect = ir;

    // Draw in-progress crop overlay
    if (state.mode === 'crop' && state.cropRect) {
        drawCropOverlay(ctx, state.cropRect, ir, vt.scale);
    }

    // Draw in-progress stroke, clipped to the image for the reason the committed ones are - the
    // stroke being drawn right now has to look like the stroke that is about to be kept.
    if (state.mode === 'draw' && state.currentStroke && state.currentStroke.points.length >= 2) {
        ctx.save();
        ctx.beginPath();
        ctx.rect(ir.x, ir.y, ir.w, ir.h);
        ctx.clip();
        drawStroke(ctx, state.currentStroke.points, state.drawColor, state.drawWidth);
        ctx.restore();
    }

    // Draw in-progress line / arrow
    if ((state.mode === 'line' || state.mode === 'arrow') && state.activeLine) {
        drawLine(ctx, state.activeLine.start, state.activeLine.end, state.drawColor, state.drawWidth, state.activeLine.isArrow);
    }

    // Draw in-progress shape
    if (state.activeShape) {
        const s = state.activeShape;
        drawShape(ctx, s.kind, buildShapeRect(s.start, s.end, s.kind, s.square), state.shapeFill, state.drawColor, state.drawWidth);
    }

    ctx.restore();
}

function computeEffective(image, actions) {
    // Cumulative crop as a normalized source-image rect, plus total rotation.
    // Crop rects are captured in displayed (possibly rotated) space, so each
    // one is mapped back through the rotation in effect when it was applied.
    let src = { x: 0, y: 0, w: 1, h: 1 };
    let rotation = 0;

    for (const action of actions) {
        if (action.type === 'rotate') {
            rotation = ((rotation + action.angle) % 360 + 360) % 360;
        } else if (action.type === 'crop') {
            const m = mapCropToSource(action.rect, rotation);
            src = {
                x: src.x + m.x * src.w,
                y: src.y + m.y * src.h,
                w: m.w * src.w,
                h: m.h * src.h
            };
        }
    }

    return {
        sx: src.x * image.naturalWidth,
        sy: src.y * image.naturalHeight,
        sw: src.w * image.naturalWidth,
        sh: src.h * image.naturalHeight,
        rotation,
        needsSwap: Math.abs(rotation % 180 - 90) < 0.1
    };
}

function mapCropToSource(c, rotation) {
    const r = ((Math.round(rotation / 90) * 90) % 360 + 360) % 360;
    switch (r) {
        case 90: return { x: c.y, y: 1 - c.x - c.w, w: c.h, h: c.w };
        case 180: return { x: 1 - c.x - c.w, y: 1 - c.y - c.h, w: c.w, h: c.h };
        case 270: return { x: 1 - c.y - c.h, y: c.x, w: c.h, h: c.w };
        default: return { ...c };
    }
}

function replayActions(ctx, image, actions, canvasW, canvasH) {
    const eff = computeEffective(image, actions);

    // Fit the cropped (and possibly 90°-swapped) region into the canvas
    const fitW = eff.needsSwap ? eff.sh : eff.sw;
    const fitH = eff.needsSwap ? eff.sw : eff.sh;
    const drawRect = calculateFitRect(fitW, fitH, canvasW, canvasH);

    const cx = drawRect.x + drawRect.w / 2;
    const cy = drawRect.y + drawRect.h / 2;

    if (Math.abs(eff.rotation) > 0.1) {
        ctx.save();
        ctx.translate(cx, cy);
        ctx.rotate(eff.rotation * Math.PI / 180);
        const uw = eff.needsSwap ? drawRect.h : drawRect.w;
        const uh = eff.needsSwap ? drawRect.w : drawRect.h;
        ctx.drawImage(image, eff.sx, eff.sy, eff.sw, eff.sh, -uw / 2, -uh / 2, uw, uh);
        ctx.restore();
    } else {
        ctx.drawImage(image, eff.sx, eff.sy, eff.sw, eff.sh, drawRect.x, drawRect.y, drawRect.w, drawRect.h);
    }

    // Second pass: draw overlays (strokes, text).
    //
    // Clipped to the image. Overlay geometry is stored normalized against the image rect, so a
    // point that was put down past the edge is simply <0 or >1 and paints outside it - over the
    // letterbox on screen, and past the edge of the bitmap on export, since this same function is
    // what renders the exported copy. Clipping rather than clamping the points: clamping would drag
    // a stroke that wandered off and came back along the border and draw a line that was never made.
    ctx.save();
    ctx.beginPath();
    ctx.rect(drawRect.x, drawRect.y, drawRect.w, drawRect.h);
    ctx.clip();

    for (const action of actions) {
        if (action.type === 'draw') {
            const pts = action.points.map(p => ({
                x: drawRect.x + p.x * drawRect.w,
                y: drawRect.y + p.y * drawRect.h
            }));
            drawStroke(ctx, pts, action.color, rescale(action.width, action.refWidth, drawRect.w));
        } else if (action.type === 'text') {
            const tx = drawRect.x + action.position.x * drawRect.w;
            const ty = drawRect.y + action.position.y * drawRect.h;
            ctx.font = `${rescale(action.size, action.refWidth, drawRect.w)}px ${action.font}`;
            ctx.fillStyle = action.color;
            ctx.textBaseline = 'top';
            ctx.fillText(action.text, tx, ty);
        } else if (action.type === 'shape') {
            const bounds = {
                x: drawRect.x + action.bounds.x * drawRect.w,
                y: drawRect.y + action.bounds.y * drawRect.h,
                w: action.bounds.w * drawRect.w,
                h: action.bounds.h * drawRect.h
            };
            drawShape(ctx, action.shape, bounds, action.fill, action.stroke,
                rescale(action.width, action.refWidth, drawRect.w));
        } else if (action.type === 'line') {
            const start = {
                x: drawRect.x + action.start.x * drawRect.w,
                y: drawRect.y + action.start.y * drawRect.h
            };
            const end = {
                x: drawRect.x + action.end.x * drawRect.w,
                y: drawRect.y + action.end.y * drawRect.h
            };
            drawLine(ctx, start, end, action.color, rescale(action.width, action.refWidth, drawRect.w), action.isArrow);
        }
    }

    ctx.restore();

    return drawRect;
}

/**
 * Scales a stroke width / font size captured against `refWidth` to the rect being drawn into,
 * so a 3px pen on a 400px preview is not a hairline on a 4000px export.
 */
function rescale(value, refWidth, currentWidth) {
    return refWidth > 0.01 ? value * (currentWidth / refWidth) : value;
}

function drawCropOverlay(ctx, crop, ir, scale) {
    const cx = ir.x + crop.x * ir.w;
    const cy = ir.y + crop.y * ir.h;
    const cw = crop.w * ir.w;
    const ch = crop.h * ir.h;

    // Dim overlay (4 rects around crop)
    ctx.fillStyle = 'rgba(0,0,0,0.5)';
    ctx.fillRect(ir.x, ir.y, ir.w, cy - ir.y); // top
    ctx.fillRect(ir.x, cy + ch, ir.w, ir.y + ir.h - cy - ch); // bottom
    ctx.fillRect(ir.x, cy, cx - ir.x, ch); // left
    ctx.fillRect(cx + cw, cy, ir.x + ir.w - cx - cw, ch); // right

    // Crop border. Chrome divides by the view scale so handles and hairlines stay the same
    // on-screen size however far the user has zoomed in.
    ctx.strokeStyle = '#fff';
    ctx.lineWidth = 2 / scale;
    ctx.strokeRect(cx, cy, cw, ch);

    // Rule of thirds
    ctx.strokeStyle = 'rgba(255,255,255,0.3)';
    ctx.lineWidth = 1 / scale;
    const tw = cw / 3, th = ch / 3;
    for (let i = 1; i <= 2; i++) {
        ctx.beginPath();
        ctx.moveTo(cx + tw * i, cy); ctx.lineTo(cx + tw * i, cy + ch);
        ctx.stroke();
        ctx.beginPath();
        ctx.moveTo(cx, cy + th * i); ctx.lineTo(cx + cw, cy + th * i);
        ctx.stroke();
    }

    // Handles
    ctx.fillStyle = '#fff';
    const hs = 10 / scale;
    const handles = [
        [cx, cy], [cx + cw / 2, cy], [cx + cw, cy],
        [cx, cy + ch / 2], [cx + cw, cy + ch / 2],
        [cx, cy + ch], [cx + cw / 2, cy + ch], [cx + cw, cy + ch]
    ];
    for (const [hx, hy] of handles) {
        ctx.fillRect(hx - hs / 2, hy - hs / 2, hs, hs);
    }
}

function drawLine(ctx, start, end, color, width, arrow) {
    ctx.strokeStyle = color;
    ctx.lineWidth = width;
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    ctx.beginPath();
    ctx.moveTo(start.x, start.y);
    ctx.lineTo(end.x, end.y);
    ctx.stroke();

    if (!arrow) return;

    const dx = end.x - start.x;
    const dy = end.y - start.y;
    const len = Math.sqrt(dx * dx + dy * dy);
    if (len < 0.5) return;

    const headLen = Math.max(width * 4, 12);
    const ux = dx / len, uy = dy / len;
    const angle = Math.PI / 6; // 30°
    const cos = Math.cos(angle), sin = Math.sin(angle);

    const leftX = end.x - headLen * (ux * cos + uy * sin);
    const leftY = end.y - headLen * (uy * cos - ux * sin);
    const rightX = end.x - headLen * (ux * cos - uy * sin);
    const rightY = end.y - headLen * (uy * cos + ux * sin);

    ctx.fillStyle = color;
    ctx.beginPath();
    ctx.moveTo(end.x, end.y);
    ctx.lineTo(leftX, leftY);
    ctx.lineTo(rightX, rightY);
    ctx.closePath();
    ctx.fill();
}

/**
 * Turns the two corners of a shape drag into its bounds. A circle (or a shift-drag) takes the
 * smaller of the two extents rather than the larger, so it can never escape the bounds the drag
 * was clamped to.
 */
function buildShapeRect(start, end, kind, square) {
    let x = Math.min(start.x, end.x);
    let y = Math.min(start.y, end.y);
    let w = Math.abs(end.x - start.x);
    let h = Math.abs(end.y - start.y);

    if (kind === 'circle' || square) {
        const side = Math.min(w, h);
        // Grow from whichever corner the drag started at, so the shape tracks the pointer
        if (end.x < start.x) x = start.x - side;
        if (end.y < start.y) y = start.y - side;
        w = h = side;
    }

    return { x, y, w, h };
}

function drawShape(ctx, kind, rect, fill, stroke, width) {
    if (rect.w <= 0 || rect.h <= 0) return;

    ctx.beginPath();
    if (kind === 'rect') {
        ctx.rect(rect.x, rect.y, rect.w, rect.h);
    } else {
        ctx.ellipse(rect.x + rect.w / 2, rect.y + rect.h / 2, rect.w / 2, rect.h / 2, 0, 0, Math.PI * 2);
    }

    if (fill) {
        ctx.fillStyle = fill;
        ctx.fill();
    }

    if (stroke && width > 0) {
        ctx.strokeStyle = stroke;
        ctx.lineWidth = width;
        ctx.lineJoin = 'round';
        ctx.stroke();
    }
}

function drawStroke(ctx, points, color, width) {
    if (points.length < 2) return;
    ctx.strokeStyle = color;
    ctx.lineWidth = width;
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    ctx.beginPath();
    ctx.moveTo(points[0].x, points[0].y);
    for (let i = 1; i < points.length; i++) {
        ctx.lineTo(points[i].x, points[i].y);
    }
    ctx.stroke();
}

function calculateFitRect(imgW, imgH, canvasW, canvasH) {
    if (imgW <= 0 || imgH <= 0) return { x: 0, y: 0, w: 0, h: 0 };
    const scale = Math.min(canvasW / imgW, canvasH / imgH);
    const w = imgW * scale, h = imgH * scale;
    return { x: (canvasW - w) / 2, y: (canvasH - h) / 2, w, h };
}

function finalizeOperation(state) {
    // Commit in-progress line / arrow
    if (state.activeLine) {
        commitLine(state);
    }

    // Commit in-progress shape
    if (state.activeShape) {
        commitShape(state);
    }

    // Commit in-progress stroke
    if (state.currentStroke && state.currentStroke.points.length >= 2) {
        const ir = state.imageRect;
        if (ir.w > 0 && ir.h > 0) {
            const normalized = state.currentStroke.points.map(p => ({
                x: (p.x - ir.x) / ir.w,
                y: (p.y - ir.y) / ir.h
            }));
            state.actions.push({
                type: 'draw',
                points: normalized,
                color: state.drawColor,
                width: state.drawWidth,
                refWidth: ir.w
            });
            state.redoStack = [];
            notifyUndoState(state);
        }
        state.currentStroke = null;
    }
}

function notifyUndoState(state) {
    state.dotnet.invokeMethodAsync('OnCanUndoChanged', state.actions.length > 0);
    state.dotnet.invokeMethodAsync('OnCanRedoChanged', state.redoStack.length > 0);
}

// --- View transform (zoom / pan) ---

function canvasSize(state) {
    const dpr = window.devicePixelRatio || 1;
    return { w: state.canvas.width / dpr, h: state.canvas.height / dpr };
}

/** Maps a point on the canvas element into the un-zoomed space imageRect lives in. */
function screenToWorld(state, p) {
    const vt = state.viewTransform;
    if (vt.scale === 1 && vt.tx === 0 && vt.ty === 0) return { ...p };

    const { w, h } = canvasSize(state);
    const cx = w / 2, cy = h / 2;
    return {
        x: (p.x - cx - vt.tx) / vt.scale + cx,
        y: (p.y - cy - vt.ty) / vt.scale + cy
    };
}

/** Inverse of screenToWorld — used to position the inline text input over the image. */
function worldToScreen(state, p) {
    const vt = state.viewTransform;
    if (vt.scale === 1 && vt.tx === 0 && vt.ty === 0) return { ...p };

    const { w, h } = canvasSize(state);
    const cx = w / 2, cy = h / 2;
    return {
        x: (p.x - cx) * vt.scale + cx + vt.tx,
        y: (p.y - cy) * vt.scale + cy + vt.ty
    };
}

/** Zooms while keeping the image content under `anchor` (element coordinates) pinned. */
function setZoomAt(state, scale, anchor) {
    if (!state.allowZoom) scale = 1;
    const world = screenToWorld(state, anchor);
    applyTransform(state, clamp(scale, state.minZoom, state.maxZoom), anchor, world);
}

/** The single place the view transform is written. */
function applyTransform(state, scale, screen, world) {
    const { w, h } = canvasSize(state);
    const cx = w / 2, cy = h / 2;

    state.viewTransform.scale = scale;
    state.viewTransform.tx = screen.x - cx - (world.x - cx) * scale;
    state.viewTransform.ty = screen.y - cy - (world.y - cy) * scale;

    clampOffsets(state);
    redraw(state);
    notifyZoom(state);
    updateCursor(state);
}

/** Centres the image while it is smaller than the view, edge-locks it once it is larger. */
function clampOffsets(state) {
    const ir = state.imageRect;
    const { w, h } = canvasSize(state);

    if (!ir || ir.w <= 0 || ir.h <= 0 || w <= 0 || h <= 0) {
        state.viewTransform.tx = 0;
        state.viewTransform.ty = 0;
        return;
    }

    const s = state.viewTransform.scale;
    state.viewTransform.tx = clampAxis(state.viewTransform.tx, ir.x + ir.w / 2, ir.w, w / 2, 0, w, s);
    state.viewTransform.ty = clampAxis(state.viewTransform.ty, ir.y + ir.h / 2, ir.h, h / 2, 0, h, s);
}

function clampAxis(offset, imageCenter, imageSize, viewCenter, viewMin, viewMax, scale) {
    const scaled = imageSize * scale;
    const center = (imageCenter - viewCenter) * scale + viewCenter;
    const half = scaled / 2;
    const viewSize = viewMax - viewMin;

    return scaled >= viewSize
        ? clamp(offset, viewMax - center - half, viewMin - center + half)
        : clamp(offset, viewMin - center + half, viewMax - center - half);
}

function notifyZoom(state) {
    if (state.lastNotifiedZoom === state.viewTransform.scale) return;
    state.lastNotifiedZoom = state.viewTransform.scale;
    state.dotnet.invokeMethodAsync('OnZoomChanged', state.viewTransform.scale);
}

function updateCursor(state) {
    const pannable = state.viewTransform.scale > 1.001;
    state.canvas.style.cursor = state.pan ? 'grabbing'
        : state.mode === 'none' ? (pannable ? 'grab' : 'default')
        : 'crosshair';
}

export function setZoom(root, scale) {
    const state = states.get(root);
    if (!state) return;
    const { w, h } = canvasSize(state);
    setZoomAt(state, scale, { x: w / 2, y: h / 2 });
}

export function zoomIn(root) { stepZoom(root, 1.5); }
export function zoomOut(root) { stepZoom(root, 1 / 1.5); }
export function zoomToFit(root) { setZoom(root, 1); }

function stepZoom(root, factor) {
    const state = states.get(root);
    if (!state) return;
    const { w, h } = canvasSize(state);
    setZoomAt(state, state.viewTransform.scale * factor, { x: w / 2, y: h / 2 });
}

export function updateZoomLimits(root, min, max) {
    const state = states.get(root);
    if (!state) return;
    state.minZoom = min > 0 ? min : 1;
    state.maxZoom = max > state.minZoom ? max : state.minZoom;
}

// --- Pointer events ---

function localPoint(state, e) {
    const rect = state.canvas.getBoundingClientRect();
    return { x: e.clientX - rect.left, y: e.clientY - rect.top };
}

function onPointerDown(state, e) {
    e.preventDefault();
    state.canvas.setPointerCapture(e.pointerId);

    const screen = localPoint(state, e);
    state.pointers.set(e.pointerId, screen);

    if (state.pointers.size >= 2) {
        beginPinch(state);
        return;
    }

    // Middle-drag pans in every mode, so a mouse user can reposition mid-edit
    if (e.button === 1) {
        beginPan(state, screen);
        return;
    }

    const pt = screenToWorld(state, screen);

    switch (state.mode) {
        case 'none':
            beginPan(state, screen);
            break;
        case 'crop': {
            startCropDrag(state, pt);
            // Dragging outside the crop box pans instead of fighting the user
            if (!state.activeCropHandle) beginPan(state, screen);
            break;
        }
        case 'draw':
        {
            // The same guard line, arrow and the shapes already make. Clipping stops an out-of-bounds
            // stroke from being painted, but a gesture that starts in the letterbox was never aimed
            // at the picture at all, and letting it begin leaves an invisible stroke on the undo
            // stack that clears nothing when undone.
            const ir = state.imageRect;
            if (ir.w <= 0 || ir.h <= 0) break;
            if (pt.x < ir.x || pt.x > ir.x + ir.w || pt.y < ir.y || pt.y > ir.y + ir.h) break;
            state.currentStroke = { points: [pt] };
            redraw(state);
            break;
        }
        case 'text':
            handleTextPlacement(state, pt, screen);
            break;
        case 'line':
        case 'arrow':
        {
            const ir = state.imageRect;
            if (ir.w <= 0 || ir.h <= 0) break;
            if (pt.x < ir.x || pt.x > ir.x + ir.w || pt.y < ir.y || pt.y > ir.y + ir.h) break;
            state.activeLine = { start: pt, end: pt, isArrow: state.mode === 'arrow' };
            redraw(state);
            break;
        }
        case 'rect':
        case 'ellipse':
        case 'circle':
        {
            const ir = state.imageRect;
            if (ir.w <= 0 || ir.h <= 0) break;
            if (pt.x < ir.x || pt.x > ir.x + ir.w || pt.y < ir.y || pt.y > ir.y + ir.h) break;
            state.activeShape = { kind: state.mode, start: pt, end: pt, square: e.shiftKey };
            redraw(state);
            break;
        }
    }
}

function onPointerMove(state, e) {
    // Ignore hover moves — only pointers that went down here drive the tools
    if (!state.pointers.has(e.pointerId)) return;
    e.preventDefault();

    const screen = localPoint(state, e);
    state.pointers.set(e.pointerId, screen);

    if (state.pinch) {
        updatePinch(state);
        return;
    }

    // A second finger arriving mid-stroke turns the gesture into a zoom
    if (state.pointers.size >= 2 && state.allowZoom) {
        abandonToolGesture(state);
        beginPinch(state);
        return;
    }

    if (state.pan) {
        updatePan(state, screen);
        return;
    }

    const pt = screenToWorld(state, screen);

    switch (state.mode) {
        case 'crop':
            moveCropDrag(state, pt);
            break;
        case 'draw':
            if (state.currentStroke) {
                state.currentStroke.points.push(pt);
                redraw(state);
            }
            break;
        case 'line':
        case 'arrow':
            if (state.activeLine) {
                const ir = state.imageRect;
                state.activeLine.end = {
                    x: clamp(pt.x, ir.x, ir.x + ir.w),
                    y: clamp(pt.y, ir.y, ir.y + ir.h)
                };
                redraw(state);
            }
            break;
        case 'rect':
        case 'ellipse':
        case 'circle':
            if (state.activeShape) {
                const ir = state.imageRect;
                state.activeShape.end = {
                    x: clamp(pt.x, ir.x, ir.x + ir.w),
                    y: clamp(pt.y, ir.y, ir.y + ir.h)
                };
                // Held live rather than read at pointerdown, so shift can be taken or released mid-drag
                state.activeShape.square = e.shiftKey;
                redraw(state);
            }
            break;
    }
}

function onPointerUp(state, e) {
    e.preventDefault();
    try { state.canvas.releasePointerCapture(e.pointerId); } catch { }
    state.pointers.delete(e.pointerId);

    if (state.pinch) {
        if (state.pointers.size < 2) state.pinch = null;
        return;
    }

    if (state.pan) {
        state.pan = null;
        updateCursor(state);
        return;
    }

    switch (state.mode) {
        case 'crop':
            state.activeCropHandle = null;
            break;
        case 'draw':
            if (state.currentStroke && state.currentStroke.points.length >= 2) {
                finalizeOperation(state);
                redraw(state);
            } else {
                state.currentStroke = null;
            }
            break;
        case 'line':
        case 'arrow':
            commitLine(state);
            redraw(state);
            break;
        case 'rect':
        case 'ellipse':
        case 'circle':
            commitShape(state);
            redraw(state);
            break;
    }
}

function onWheel(state, e) {
    if (!state.allowZoom) return;
    e.preventDefault();

    // Trackpad pinch arrives as ctrl+wheel; both gestures zoom about the cursor
    const factor = Math.exp(-e.deltaY * (e.ctrlKey ? 0.01 : 0.0022));
    setZoomAt(state, state.viewTransform.scale * factor, localPoint(state, e));
}

function onDoubleClick(state, e) {
    if (!state.allowZoom || state.mode !== 'none') return;
    e.preventDefault();

    const target = state.viewTransform.scale > 1.05 ? 1 : Math.min(2.5, state.maxZoom);
    setZoomAt(state, target, localPoint(state, e));
}

function beginPinch(state) {
    if (!state.allowZoom) return;
    abandonToolGesture(state);

    const pts = [...state.pointers.values()];
    const distance = dist(pts[0], pts[1]);
    if (distance <= 1) return;

    const mid = midpoint(pts);
    state.pinch = {
        startDist: distance,
        startScale: state.viewTransform.scale,
        startWorld: screenToWorld(state, mid)
    };
}

function updatePinch(state) {
    const pts = [...state.pointers.values()];
    if (pts.length < 2) return;

    const scale = clamp(
        state.pinch.startScale * (dist(pts[0], pts[1]) / state.pinch.startDist),
        state.minZoom,
        state.maxZoom);

    // Re-anchoring the pinch-start world point to the *current* midpoint gives
    // pinch and two-finger pan in one expression
    applyTransform(state, scale, midpoint(pts), state.pinch.startWorld);
}

function beginPan(state, screen) {
    if (state.viewTransform.scale <= 1.001) return;
    state.pan = {
        screen,
        tx: state.viewTransform.tx,
        ty: state.viewTransform.ty
    };
    updateCursor(state);
}

function updatePan(state, screen) {
    state.viewTransform.tx = state.pan.tx + (screen.x - state.pan.screen.x);
    state.viewTransform.ty = state.pan.ty + (screen.y - state.pan.screen.y);
    clampOffsets(state);
    redraw(state);
}

/** Drops an in-progress stroke/line without committing it (a pinch took over). */
function abandonToolGesture(state) {
    state.currentStroke = null;
    state.activeLine = null;
    state.activeShape = null;
    state.activeCropHandle = null;
    state.pan = null;
    redraw(state);
}

function midpoint(pts) {
    return { x: (pts[0].x + pts[1].x) / 2, y: (pts[0].y + pts[1].y) / 2 };
}

function commitLine(state) {
    if (!state.activeLine) return;
    const { start, end, isArrow } = state.activeLine;
    state.activeLine = null;

    const ir = state.imageRect;
    if (ir.w <= 0 || ir.h <= 0) return;

    const dx = end.x - start.x, dy = end.y - start.y;
    if (dx * dx + dy * dy < 4) return; // ignore taps without drag

    state.actions.push({
        type: 'line',
        start: { x: (start.x - ir.x) / ir.w, y: (start.y - ir.y) / ir.h },
        end: { x: (end.x - ir.x) / ir.w, y: (end.y - ir.y) / ir.h },
        color: state.drawColor,
        width: state.drawWidth,
        refWidth: ir.w,
        isArrow: isArrow
    });
    state.redoStack = [];
    notifyUndoState(state);
}

function commitShape(state) {
    if (!state.activeShape) return;
    const { kind, start, end, square } = state.activeShape;
    state.activeShape = null;

    const ir = state.imageRect;
    if (ir.w <= 0 || ir.h <= 0) return;

    const rect = buildShapeRect(start, end, kind, square);
    if (rect.w < 2 || rect.h < 2) return; // ignore taps without a drag

    state.actions.push({
        type: 'shape',
        shape: kind,
        bounds: {
            x: (rect.x - ir.x) / ir.w,
            y: (rect.y - ir.y) / ir.h,
            w: rect.w / ir.w,
            h: rect.h / ir.h
        },
        fill: state.shapeFill,
        stroke: state.drawColor,
        width: state.drawWidth,
        refWidth: ir.w
    });
    state.redoStack = [];
    notifyUndoState(state);
}

// --- Crop drag ---

function startCropDrag(state, pt) {
    if (!state.cropRect) return;
    const ir = state.imageRect;
    const cr = {
        x: ir.x + state.cropRect.x * ir.w,
        y: ir.y + state.cropRect.y * ir.h,
        w: state.cropRect.w * ir.w,
        h: state.cropRect.h * ir.h
    };

    state.activeCropHandle = hitTestCropHandle(pt, cr, 20 / state.viewTransform.scale);
    state.cropDragStart = pt;
    state.cropStartRect = { ...state.cropRect };
}

function moveCropDrag(state, pt) {
    if (!state.activeCropHandle || !state.cropDragStart || !state.cropStartRect) return;
    const ir = state.imageRect;
    if (ir.w <= 0 || ir.h <= 0) return;

    const dx = (pt.x - state.cropDragStart.x) / ir.w;
    const dy = (pt.y - state.cropDragStart.y) / ir.h;
    const c = state.cropStartRect;
    const minSize = 0.05;

    let nc = { ...c };

    switch (state.activeCropHandle) {
        case 'move':
            nc.x = clamp(c.x + dx, 0, 1 - c.w);
            nc.y = clamp(c.y + dy, 0, 1 - c.h);
            break;
        case 'tl': nc = resizeCrop(c, dx, dy, 0, 0, minSize); break;
        case 'tc': nc = resizeCrop(c, 0, dy, 0, 0, minSize); break;
        case 'tr': nc = resizeCrop(c, 0, dy, dx, 0, minSize); break;
        case 'ml': nc = resizeCrop(c, dx, 0, 0, 0, minSize); break;
        case 'mr': nc = resizeCrop(c, 0, 0, dx, 0, minSize); break;
        case 'bl': nc = resizeCrop(c, dx, 0, 0, dy, minSize); break;
        case 'bc': nc = resizeCrop(c, 0, 0, 0, dy, minSize); break;
        case 'br': nc = resizeCrop(c, 0, 0, dx, dy, minSize); break;
    }

    state.cropRect = nc;
    redraw(state);
}

function resizeCrop(c, dLeft, dTop, dRight, dBottom, minSize) {
    let x = c.x + dLeft, y = c.y + dTop;
    let w = c.w - dLeft + dRight, h = c.h - dTop + dBottom;
    if (w < minSize) { w = minSize; x = c.x + c.w - minSize; }
    if (h < minSize) { h = minSize; y = c.y + c.h - minSize; }
    x = clamp(x, 0, 1 - minSize);
    y = clamp(y, 0, 1 - minSize);
    w = Math.min(w, 1 - x);
    h = Math.min(h, 1 - y);
    return { x, y, w, h };
}

function hitTestCropHandle(pt, cr, r) {
    const cx = cr.x + cr.w / 2, cy = cr.y + cr.h / 2;

    if (dist(pt, { x: cr.x, y: cr.y }) < r) return 'tl';
    if (dist(pt, { x: cr.x + cr.w, y: cr.y }) < r) return 'tr';
    if (dist(pt, { x: cr.x, y: cr.y + cr.h }) < r) return 'bl';
    if (dist(pt, { x: cr.x + cr.w, y: cr.y + cr.h }) < r) return 'br';
    if (dist(pt, { x: cx, y: cr.y }) < r) return 'tc';
    if (dist(pt, { x: cx, y: cr.y + cr.h }) < r) return 'bc';
    if (dist(pt, { x: cr.x, y: cy }) < r) return 'ml';
    if (dist(pt, { x: cr.x + cr.w, y: cy }) < r) return 'mr';

    if (pt.x >= cr.x && pt.x <= cr.x + cr.w && pt.y >= cr.y && pt.y <= cr.y + cr.h)
        return 'move';

    return null;
}

async function handleTextPlacement(state, pt, screen) {
    const ir = state.imageRect;
    if (ir.w <= 0 || ir.h <= 0) return;
    if (pt.x < ir.x || pt.x > ir.x + ir.w || pt.y < ir.y || pt.y > ir.y + ir.h) return;

    const normalized = {
        x: (pt.x - ir.x) / ir.w,
        y: (pt.y - ir.y) / ir.h
    };

    // The input is an ordinary DOM element on top of the canvas, so it is placed in screen
    // coordinates and sized by the zoom factor to match what will be rendered
    await state.dotnet.invokeMethodAsync(
        'OnRequestTextInput', screen.x, screen.y, normalized.x, normalized.y, state.viewTransform.scale);
}

export function addTextAnnotation(root, text, normX, normY) {
    const state = states.get(root);
    if (!state || !text) return;

    state.actions.push({
        type: 'text',
        text,
        position: { x: normX, y: normY },
        size: state.textSize,
        color: state.textColor,
        font: state.textFont,
        refWidth: state.imageRect.w
    });
    state.redoStack = [];
    redraw(state);
    notifyUndoState(state);
}

function dist(a, b) {
    return Math.sqrt((a.x - b.x) ** 2 + (a.y - b.y) ** 2);
}

function clamp(v, mn, mx) {
    return Math.max(mn, Math.min(mx, v));
}
