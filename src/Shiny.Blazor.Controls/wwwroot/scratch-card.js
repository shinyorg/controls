// Scratch card for Shiny.Blazor.Controls.
//
// The cover is live markup, so it cannot be erased pixel by pixel. Instead the strokes are drawn into an
// offscreen canvas - opaque where the cover remains, cleared where it has been scratched - and that canvas
// is applied to the cover as a CSS mask. The canvas is also what gets measured: the share of transparent
// pixels is the scratched share, reported to .NET a few times a second and at the end of every stroke.
// The cover's inline style belongs to this script; the component never writes a style attribute on it.

const states = new WeakMap();
const REPORT_INTERVAL = 120;

export function init(root, dotnet, json) {
    if (!root) return;
    const state = {
        root, dotnet,
        opts: JSON.parse(json),
        cover: null,
        canvas: document.createElement('canvas'),
        ctx: null,
        scratched: false,
        pointerId: null,
        last: null,
        raf: 0,
        reportTimer: 0,
        lastReport: 0,
        observer: null,
        handlers: null
    };
    state.ctx = state.canvas.getContext('2d', { willReadFrequently: true });

    if (typeof ResizeObserver === 'function')
        state.observer = new ResizeObserver(() => resize(state));

    states.set(root, state);
    attach(state);
}

export function update(root, json) {
    const state = states.get(root);
    if (!state) return;
    state.opts = JSON.parse(json);
    attach(state);
}

/** A fresh, unscratched cover. */
export function reset(root) {
    const state = states.get(root);
    if (!state) return;
    state.scratched = false;
    state.pointerId = null;
    state.last = null;
    fill(state);
    attach(state);
    if (state.cover) {
        state.cover.style.removeProperty('mask-image');
        state.cover.style.removeProperty('-webkit-mask-image');
    }
}

export function dispose(root) {
    const state = states.get(root);
    if (!state) return;
    detach(state);
    state.observer?.disconnect();
    cancelAnimationFrame(state.raf);
    clearTimeout(state.reportTimer);
    states.delete(root);
}

// The cover element is removed after a reveal and comes back as a new element after a reset, so every
// update re-finds it and moves the listeners across when it has changed.
function attach(state) {
    const cover = state.root.querySelector(':scope > .shiny-scratch-card__cover');
    if (cover === state.cover) return;

    detach(state);
    state.cover = cover;
    if (!cover) return;

    state.handlers = {
        down: e => onDown(state, e),
        move: e => onMove(state, e),
        up: e => onUp(state, e)
    };
    cover.addEventListener('pointerdown', state.handlers.down);
    cover.addEventListener('pointermove', state.handlers.move);
    cover.addEventListener('pointerup', state.handlers.up);
    cover.addEventListener('pointercancel', state.handlers.up);
    cover.addEventListener('lostpointercapture', state.handlers.up);
    state.observer?.observe(cover);
    resize(state);
}

function detach(state) {
    const { cover, handlers } = state;
    if (cover && handlers) {
        cover.removeEventListener('pointerdown', handlers.down);
        cover.removeEventListener('pointermove', handlers.move);
        cover.removeEventListener('pointerup', handlers.up);
        cover.removeEventListener('pointercancel', handlers.up);
        cover.removeEventListener('lostpointercapture', handlers.up);
        state.observer?.unobserve(cover);
    }
    state.cover = null;
    state.handlers = null;
}

// One canvas pixel per CSS pixel: the mask is stretched over the cover anyway, and a smaller canvas keeps
// both the PNG encode and the measurement cheap. Anything already scratched is carried across a resize.
function resize(state) {
    const cover = state.cover;
    if (!cover) return;
    // Not laid out yet; the observer calls back once it is.
    if (cover.clientWidth === 0 || cover.clientHeight === 0) return;
    const w = Math.round(cover.clientWidth);
    const h = Math.round(cover.clientHeight);
    const { canvas } = state;
    if (canvas.width === w && canvas.height === h) return;

    let previous = null;
    if (state.scratched && canvas.width > 0 && canvas.height > 0) {
        previous = document.createElement('canvas');
        previous.width = canvas.width;
        previous.height = canvas.height;
        previous.getContext('2d').drawImage(canvas, 0, 0);
    }

    canvas.width = w;
    canvas.height = h;
    if (previous) {
        state.ctx.globalCompositeOperation = 'source-over';
        state.ctx.clearRect(0, 0, w, h);
        state.ctx.drawImage(previous, 0, 0, w, h);
        scheduleMask(state);
    } else {
        fill(state);
    }
}

function fill(state) {
    const { ctx, canvas } = state;
    ctx.globalCompositeOperation = 'source-over';
    ctx.fillStyle = '#000';
    ctx.fillRect(0, 0, canvas.width, canvas.height);
}

function point(state, e) {
    const rect = state.cover.getBoundingClientRect();
    const sx = rect.width > 0 ? state.canvas.width / rect.width : 1;
    const sy = rect.height > 0 ? state.canvas.height / rect.height : 1;
    return { x: (e.clientX - rect.left) * sx, y: (e.clientY - rect.top) * sy, scale: (sx + sy) / 2 };
}

function onDown(state, e) {
    if (!state.opts.enabled || state.pointerId !== null) return;
    if (e.pointerType === 'mouse' && e.button !== 0) return;

    e.preventDefault();
    state.pointerId = e.pointerId;
    try { state.cover.setPointerCapture(e.pointerId); } catch { }

    const p = point(state, e);
    state.last = p;
    stroke(state, p, p);
    state.dotnet.invokeMethodAsync('ScratchStarted').catch(() => { });
}

function onMove(state, e) {
    if (e.pointerId !== state.pointerId || !state.last) return;
    if (!state.opts.enabled) { onUp(state, e); return; }

    // Coalesced events keep a fast flick from scratching a dotted line.
    const events = typeof e.getCoalescedEvents === 'function' ? e.getCoalescedEvents() : null;
    for (const ev of (events && events.length ? events : [e])) {
        const p = point(state, ev);
        stroke(state, state.last, p);
        state.last = p;
    }
}

function onUp(state, e) {
    if (e.pointerId !== state.pointerId) return;
    state.pointerId = null;
    state.last = null;
    try { state.cover?.releasePointerCapture(e.pointerId); } catch { }
    report(state, true);
}

function stroke(state, from, to) {
    const { ctx } = state;
    const r = Math.max(2, state.opts.width) / 2 * to.scale;
    ctx.globalCompositeOperation = 'destination-out';
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    ctx.lineWidth = r * 2;
    ctx.beginPath();
    if (from.x === to.x && from.y === to.y) {
        ctx.arc(to.x, to.y, r, 0, Math.PI * 2);
        ctx.fill();
    } else {
        ctx.moveTo(from.x, from.y);
        ctx.lineTo(to.x, to.y);
        ctx.stroke();
    }

    state.scratched = true;
    scheduleMask(state);
    report(state, false);
}

function scheduleMask(state) {
    if (state.raf) return;
    state.raf = requestAnimationFrame(() => {
        state.raf = 0;
        if (!state.cover) return;
        const url = `url(${state.canvas.toDataURL()})`;
        state.cover.style.setProperty('-webkit-mask-image', url);
        state.cover.style.setProperty('mask-image', url);
    });
}

// Throttled while a stroke is in flight; immediate when it ends.
function report(state, now) {
    const elapsed = performance.now() - state.lastReport;
    if (!now && elapsed < REPORT_INTERVAL) {
        if (!state.reportTimer)
            state.reportTimer = setTimeout(() => { state.reportTimer = 0; report(state, true); }, REPORT_INTERVAL - elapsed);
        return;
    }

    clearTimeout(state.reportTimer);
    state.reportTimer = 0;
    state.lastReport = performance.now();
    state.dotnet.invokeMethodAsync('ReportProgress', measure(state)).catch(() => { });
}

// Samples every few pixels: exact enough for a threshold, and it keeps a large card cheap.
function measure(state) {
    const { canvas, ctx } = state;
    if (canvas.width === 0 || canvas.height === 0) return 0;
    const data = ctx.getImageData(0, 0, canvas.width, canvas.height).data;
    const step = 4;
    let total = 0, cleared = 0;
    for (let y = 0; y < canvas.height; y += step) {
        for (let x = 0; x < canvas.width; x += step) {
            total++;
            if (data[((y * canvas.width) + x) * 4 + 3] < 128) cleared++;
        }
    }
    return total ? cleared / total : 0;
}
