// Drawing surface behind SignatureView (and so SignaturePad, which hosts one in its sheet).
// Committed strokes are stored normalised to 0-1 of the canvas, so they follow the surface when it
// resizes; the stroke in flight is in CSS pixels until it is committed.
const states = new WeakMap();

export function init(root, canvas, dotnetRef, options) {
    if (!root || states.has(root)) return;
    const ctx = canvas.getContext('2d');
    const state = {
        root, canvas, ctx,
        dotnet: dotnetRef,
        strokes: [],
        activeStroke: null,
        strokeColor: options?.strokeColor || '#000000',
        strokeWidth: options?.strokeWidth || 3,
        backgroundColor: options?.backgroundColor || '#FFFFFF',
        readOnly: !!options?.readOnly,
        pointerId: null
    };
    states.set(root, state);

    state.onDown = e => onPointerDown(state, e);
    state.onMove = e => onPointerMove(state, e);
    state.onUp = e => onPointerUp(state, e, true);
    state.onCancel = e => onPointerUp(state, e, false);
    canvas.addEventListener('pointerdown', state.onDown);
    canvas.addEventListener('pointermove', state.onMove);
    canvas.addEventListener('pointerup', state.onUp);
    canvas.addEventListener('pointercancel', state.onCancel);

    resizeCanvas(state);
    redraw(state);

    const observer = new ResizeObserver(() => { resizeCanvas(state); redraw(state); });
    observer.observe(root);
    state._observer = observer;
}

export function setOptions(root, options) {
    const state = states.get(root);
    if (!state) return;
    state.strokeColor = options?.strokeColor || '#000000';
    state.strokeWidth = options?.strokeWidth || 3;
    state.backgroundColor = options?.backgroundColor || '#FFFFFF';
    state.readOnly = !!options?.readOnly;
    if (state.readOnly) state.activeStroke = null;
    redraw(state);
}

export function clear(root) {
    const state = states.get(root);
    if (!state) return;
    state.strokes = [];
    state.activeStroke = null;
    redraw(state);
}

/** Removes the last stroke and returns how many are left. */
export function undo(root) {
    const state = states.get(root);
    if (!state) return 0;
    state.strokes.pop();
    redraw(state);
    return state.strokes.length;
}

export async function exportPng(root, width, height) {
    const state = states.get(root);
    if (!state || state.strokes.length === 0) return new Uint8Array(0);

    const offscreen = document.createElement('canvas');
    offscreen.width = width;
    offscreen.height = height;
    const ctx = offscreen.getContext('2d');

    ctx.fillStyle = state.backgroundColor;
    ctx.fillRect(0, 0, width, height);

    // Fit the surface's aspect into the export and centre it, so a wide box exported to a squarer
    // bitmap is not stretched.
    const { w, h } = cssSize(state);
    const srcW = w > 0 ? w : width;
    const srcH = h > 0 ? h : height;
    const scale = Math.min(width / srcW, height / srcH);
    const drawW = srcW * scale;
    const drawH = srcH * scale;
    ctx.translate((width - drawW) / 2, (height - drawH) / 2);

    for (const stroke of state.strokes) {
        drawStroke(ctx, stroke.points, stroke.color, Math.max(stroke.width * scale, 1), drawW, drawH);
    }

    const blob = await new Promise(resolve => offscreen.toBlob(resolve, 'image/png'));
    const buf = await blob.arrayBuffer();
    return new Uint8Array(buf);
}

export function dispose(root) {
    const state = states.get(root);
    if (!state) return;
    state._observer?.disconnect();
    state.canvas.removeEventListener('pointerdown', state.onDown);
    state.canvas.removeEventListener('pointermove', state.onMove);
    state.canvas.removeEventListener('pointerup', state.onUp);
    state.canvas.removeEventListener('pointercancel', state.onCancel);
    states.delete(root);
}

// --- Internal ---

function cssSize(state) {
    const dpr = window.devicePixelRatio || 1;
    return { w: state.canvas.width / dpr, h: state.canvas.height / dpr };
}

function resizeCanvas(state) {
    const rect = state.root.getBoundingClientRect();
    const dpr = window.devicePixelRatio || 1;
    state.canvas.width = rect.width * dpr;
    state.canvas.height = rect.height * dpr;
    state.canvas.style.width = rect.width + 'px';
    state.canvas.style.height = rect.height + 'px';
    state.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
}

function redraw(state) {
    const { ctx } = state;
    const dpr = window.devicePixelRatio || 1;
    const { w, h } = cssSize(state);

    ctx.save();
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

    ctx.fillStyle = state.backgroundColor;
    ctx.fillRect(0, 0, w, h);

    for (const stroke of state.strokes) {
        drawStroke(ctx, stroke.points, stroke.color, stroke.width, w, h);
    }

    // The stroke in flight is still in CSS pixels.
    if (state.activeStroke && state.activeStroke.length >= 2) {
        drawStroke(ctx, state.activeStroke, state.strokeColor, state.strokeWidth, 1, 1);
    }

    ctx.restore();
}

function drawStroke(ctx, points, color, width, scaleX, scaleY) {
    if (points.length < 2) return;
    ctx.strokeStyle = color;
    ctx.lineWidth = width;
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    ctx.beginPath();
    ctx.moveTo(points[0].x * scaleX, points[0].y * scaleY);
    for (let i = 1; i < points.length; i++) {
        ctx.lineTo(points[i].x * scaleX, points[i].y * scaleY);
    }
    ctx.stroke();
}

function onPointerDown(state, e) {
    if (state.readOnly || state.pointerId !== null) return;
    if (e.pointerType === 'mouse' && e.button !== 0) return;
    e.preventDefault();
    state.pointerId = e.pointerId;
    state.canvas.setPointerCapture(e.pointerId);
    state.activeStroke = [{ x: e.offsetX, y: e.offsetY }];
    state.root.classList.add('is-drawing');
}

function onPointerMove(state, e) {
    if (e.pointerId !== state.pointerId || !state.activeStroke) return;
    e.preventDefault();
    // Coalesced samples keep a fast stroke smooth instead of a polyline of frame-rate corners.
    const samples = e.getCoalescedEvents ? e.getCoalescedEvents() : [];
    if (samples.length > 0) {
        const r = state.canvas.getBoundingClientRect();
        for (const s of samples) state.activeStroke.push({ x: s.clientX - r.left, y: s.clientY - r.top });
    } else {
        state.activeStroke.push({ x: e.offsetX, y: e.offsetY });
    }
    redraw(state);
}

function onPointerUp(state, e, commit) {
    if (e.pointerId !== state.pointerId) return;
    e.preventDefault();
    try { state.canvas.releasePointerCapture(e.pointerId); } catch {}
    state.pointerId = null;
    state.root.classList.remove('is-drawing');

    if (commit && state.activeStroke && state.activeStroke.length >= 2) {
        const { w, h } = cssSize(state);
        state.strokes.push({
            points: state.activeStroke.map(p => ({ x: p.x / w, y: p.y / h })),
            color: state.strokeColor,
            width: state.strokeWidth
        });
        // the component may be mid-dispose; a late callback must not surface as an unhandled rejection
        state.dotnet.invokeMethodAsync('OnStrokeCommitted', state.strokes.length).catch(() => { });
    }

    state.activeStroke = null;
    redraw(state);
}
