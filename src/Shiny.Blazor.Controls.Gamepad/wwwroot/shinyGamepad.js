// Pointer plumbing for the Shiny on-screen gamepad. Everything a player feels - which button a thumb
// is on, where a stick is - is decided in .NET by the shared engine; this file only reports raw
// pointers, and does so cheaply:
//
//  - downs and ups are sent the moment they happen, so a tap shorter than a frame is never lost
//  - moves are coalesced to the latest position per finger and sent once per animation frame
//  - everything goes as one flat number array: arrays of DTOs lose their element type to the trimmer
//    in a published WASM app, and primitives never do
//
// State lives in a WeakMap keyed by the root element rather than in an object handed to .NET - a
// plain object cannot be marshalled back as an IJSObjectReference.

const DOWN = 0, MOVE = 1, UP = 2, CANCEL = 3;
const states = new WeakMap();

export function attach(root, dotnet, options) {
    detach(root);

    const state = {
        dotnet,
        options: options || {},
        active: new Set(),
        pendingMoves: new Map(),
        frame: 0,
        fallback: 0,
        idleTimer: 0,
        observer: null,
        listeners: []
    };
    states.set(root, state);

    const point = e => {
        const r = root.getBoundingClientRect();
        return [e.clientX - r.left, e.clientY - r.top];
    };

    const send = batch => {
        state.dotnet.invokeMethodAsync('OnPointers', batch).catch(() => { /* circuit gone */ });
    };

    const flushMoves = () => {
        if (state.frame)
            cancelAnimationFrame(state.frame);

        clearTimeout(state.fallback);
        state.frame = 0;
        state.fallback = 0;
        if (state.pendingMoves.size === 0)
            return;

        const batch = [];
        for (const [id, [x, y]] of state.pendingMoves)
            batch.push(MOVE, id, x, y);

        state.pendingMoves.clear();
        send(batch);
    };

    const on = (target, type, handler, opts) => {
        target.addEventListener(type, handler, opts);
        state.listeners.push(() => target.removeEventListener(type, handler, opts));
    };

    on(root, 'pointerdown', e => {
        // in pass-through mode only the hit shapes take pointer events, so anything that reaches the
        // root is meant for the gamepad
        e.preventDefault();
        try { e.target.setPointerCapture(e.pointerId); } catch { /* already released */ }

        state.active.add(e.pointerId);
        wake(root, state);
        const [x, y] = point(e);
        send([DOWN, e.pointerId, x, y]);
    });

    on(root, 'pointermove', e => {
        if (!state.active.has(e.pointerId))
            return;

        e.preventDefault();
        state.pendingMoves.set(e.pointerId, point(e));
        if (!state.frame && !state.fallback) {
            // one flush per frame; the timer is a floor for when frames stop (a throttled iframe,
            // a page the browser considers hidden) so a held stick never freezes mid-drag
            state.frame = requestAnimationFrame(flushMoves);
            state.fallback = setTimeout(flushMoves, 50);
        }
    });

    const end = kind => e => {
        if (!state.active.delete(e.pointerId))
            return;

        // the last position must land before the release, or a flick ends a frame short
        if (state.pendingMoves.has(e.pointerId)) {
            const [x, y] = state.pendingMoves.get(e.pointerId);
            state.pendingMoves.delete(e.pointerId);
            send([MOVE, e.pointerId, x, y, kind, e.pointerId, 0, 0]);
        }
        else {
            send([kind, e.pointerId, 0, 0]);
        }

        if (state.active.size === 0)
            scheduleIdle(root, state);
    };
    on(root, 'pointerup', end(UP));
    on(root, 'pointercancel', end(CANCEL));
    on(root, 'lostpointercapture', end(CANCEL));

    // a long press would otherwise open the context menu, and a double tap would zoom the page
    on(root, 'contextmenu', e => e.preventDefault());
    on(root, 'touchstart', e => { if (e.target !== root) e.preventDefault(); }, { passive: false });

    state.observer = new ResizeObserver(entries => {
        const box = entries[0].contentRect;
        state.dotnet.invokeMethodAsync('OnResize', box.width, box.height).catch(() => { });
    });
    state.observer.observe(root);

    scheduleIdle(root, state);
}


export function update(root, options) {
    const state = states.get(root);
    if (!state)
        return;

    state.options = options || {};
    wake(root, state);
    scheduleIdle(root, state);
}


export function detach(root) {
    const state = states.get(root);
    if (!state)
        return;

    state.listeners.forEach(off => off());
    state.observer?.disconnect();
    if (state.frame)
        cancelAnimationFrame(state.frame);

    clearTimeout(state.fallback);
    clearTimeout(state.idleTimer);
    states.delete(root);
}


export function canVibrate() {
    return typeof navigator !== 'undefined' && typeof navigator.vibrate === 'function';
}


export function vibrate(milliseconds) {
    // Android browsers only - iOS Safari exposes no vibration API at all
    if (canVibrate())
        navigator.vibrate(milliseconds);
}


function wake(root, state) {
    clearTimeout(state.idleTimer);
    delete root.dataset.idle;
}


function scheduleIdle(root, state) {
    clearTimeout(state.idleTimer);
    const { idleOpacity, idleDelayMs } = state.options;
    if (idleOpacity == null || idleOpacity >= 1)
        return;

    // a data attribute Blazor never renders, so a re-render cannot strip it
    state.idleTimer = setTimeout(() => {
        if (state.active.size === 0)
            root.dataset.idle = '';
    }, idleDelayMs ?? 4000);
}
