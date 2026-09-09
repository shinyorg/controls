// The pinch/pan/double-tap zoom state machine, over any element.
//
// Nothing in here knows what it is transforming: the pan limits come from the target's own box and
// the result is written as a CSS transform. That is what lets one implementation drive both the
// image viewer's <img> and ZoomPanView's arbitrary content.

export function clamp(v, mn, mx) { return Math.max(mn, Math.min(mx, v)); }

/** Elements that own their own pointer story. A gesture starting on one of these is theirs. */
const INTERACTIVE = 'a[href],button,input,select,textarea,label,[contenteditable]:not([contenteditable="false"]),[data-shiny-nozoom]';

export function createState(target, options) {
    return {
        target,
        minZoom: options.minZoom ?? 1,
        maxZoom: options.maxZoom ?? 5,
        doubleTapZoom: options.doubleTapZoom ?? 2.5,
        doubleTapToZoom: options.doubleTapToZoom !== false,
        animationLength: options.animationLength ?? 250,
        // 'disabled' | 'modifier' | 'always'. Plain-wheel zoom steals the page's scroll, so the
        // default asks for Ctrl/Cmd - which is also the gesture a trackpad pinch already sends.
        wheelMode: options.wheelMode ?? 'modifier',
        // Leave gestures that start on a control to the control. Off for a plain image.
        respectInteractive: options.respectInteractive === true,
        // A surface that is nothing but the target claims the pointer stream outright, zoomed or
        // not. One that wraps page content only claims it once there is somewhere to pan.
        claimTouchAlways: options.claimTouchAlways === true,
        onChange: options.onChange ?? null,

        scale: 1, tx: 0, ty: 0,
        pointers: new Map(),
        pinchStartDistance: 0,
        pinchStartScale: 1,
        panStartX: 0, panStartY: 0,
        panOriginTx: 0, panOriginTy: 0,
        panning: false,
        lastTapTime: 0, lastTapX: 0, lastTapY: 0,
        handlers: null
    };
}

export function apply(state, animate) {
    state.target.style.transition = animate
        ? `transform ${state.animationLength}ms cubic-bezier(.2,.8,.2,1)`
        : 'none';
    state.target.style.transform = `translate(${state.tx}px, ${state.ty}px) scale(${state.scale})`;
}

/**
 * How far the target may be moved: half the overhang the scale creates. At or below natural size
 * there is no overhang and nothing to pan.
 */
export function clampPan(state) {
    if (state.scale <= 1) { state.tx = 0; state.ty = 0; return; }

    const maxX = state.target.clientWidth * (state.scale - 1) / 2;
    const maxY = state.target.clientHeight * (state.scale - 1) / 2;
    state.tx = clamp(state.tx, -maxX, maxX);
    state.ty = clamp(state.ty, -maxY, maxY);
}

/** Scaled past natural size, which is the same thing as "there is something to pan". */
export function isZoomed(state) { return state.scale > 1.001; }

function notify(state) {
    if (state.onChange)
        state.onChange(state.scale, isZoomed(state));
}

function settle(state, animate) {
    clampPan(state);
    apply(state, animate);
    syncTouchAction(state);
    notify(state);
}

/**
 * touch-action is what decides whether the browser scrolls the page or hands us the pointer stream.
 * Claiming it outright would kill scrolling past an unzoomed control, so it is only claimed once
 * there is somewhere to pan - the same trade as attaching the pan recognizer on MAUI.
 */
function syncTouchAction(state) {
    state.target.style.touchAction = state.claimTouchAlways || isZoomed(state) ? 'none' : '';
}

export function zoomTo(state, zoom, focus) {
    const next = clamp(zoom, state.minZoom, state.maxZoom);

    if (focus && !isZoomed(state)) {
        const rect = state.target.getBoundingClientRect();
        state.tx = -(focus.x - (rect.left + rect.width / 2)) * (next - 1);
        state.ty = -(focus.y - (rect.top + rect.height / 2)) * (next - 1);
    }

    state.scale = next;
    settle(state, true);
}

export function reset(state, animate) {
    state.scale = 1;
    state.tx = 0;
    state.ty = 0;
    settle(state, animate);
}

export function attach(state) {
    const el = state.target;

    // Without this the browser starts a native drag on pointermove and fires pointercancel, which
    // aborts the pan a few pixels in.
    el.addEventListener('dragstart', onDragStart);

    function onDragStart(ev) { ev.preventDefault(); }

    function ownedByContent(ev) {
        return state.respectInteractive && ev.target.closest(INTERACTIVE) != null;
    }

    function onDown(ev) {
        if (ownedByContent(ev) && !isZoomed(state))
            return;

        state.pointers.set(ev.pointerId, { x: ev.clientX, y: ev.clientY });

        if (state.pointers.size === 2) {
            const pts = [...state.pointers.values()];
            state.pinchStartDistance = Math.hypot(pts[0].x - pts[1].x, pts[0].y - pts[1].y);
            state.pinchStartScale = state.scale;
            // A second finger is unambiguously a pinch, so the capture can be taken now.
            capture(ev);
            return;
        }

        if (state.pointers.size === 1) {
            state.panStartX = ev.clientX;
            state.panStartY = ev.clientY;
            state.panOriginTx = state.tx;
            state.panOriginTy = state.ty;
            state.panning = false;

            if (state.doubleTapToZoom)
                detectDoubleTap(ev);
        }
    }

    function detectDoubleTap(ev) {
        const now = performance.now();
        const moved = Math.hypot(ev.clientX - state.lastTapX, ev.clientY - state.lastTapY);

        if (now - state.lastTapTime < 300 && moved < 30) {
            if (isZoomed(state))
                reset(state, true);
            else
                zoomTo(state, Math.min(state.doubleTapZoom, state.maxZoom), { x: ev.clientX, y: ev.clientY });

            state.lastTapTime = 0;
            // The zoom just moved the target, so the pan origin captured above is stale.
            state.panOriginTx = state.tx;
            state.panOriginTy = state.ty;
        }
        else {
            state.lastTapTime = now;
            state.lastTapX = ev.clientX;
            state.lastTapY = ev.clientY;
        }
    }

    function capture(ev) {
        try { el.setPointerCapture(ev.pointerId); } catch { }
    }

    function onMove(ev) {
        if (!state.pointers.has(ev.pointerId))
            return;

        state.pointers.set(ev.pointerId, { x: ev.clientX, y: ev.clientY });

        if (state.pointers.size === 2) {
            const pts = [...state.pointers.values()];
            const d = Math.hypot(pts[0].x - pts[1].x, pts[0].y - pts[1].y);

            if (state.pinchStartDistance > 0) {
                state.scale = clamp(state.pinchStartScale * (d / state.pinchStartDistance), state.minZoom, state.maxZoom);
                settle(state, false);
            }
            return;
        }

        if (state.pointers.size === 1 && isZoomed(state)) {
            const dx = ev.clientX - state.panStartX;
            const dy = ev.clientY - state.panStartY;

            // Capturing on pointerdown would swallow the click on anything inside; the capture is
            // taken only once the pointer has actually travelled far enough to be a drag.
            if (!state.panning && Math.hypot(dx, dy) < 4)
                return;

            if (!state.panning) {
                state.panning = true;
                capture(ev);
            }

            state.tx = state.panOriginTx + dx;
            state.ty = state.panOriginTy + dy;
            settle(state, false);
        }
    }

    function onUp(ev) {
        state.pointers.delete(ev.pointerId);
        try { el.releasePointerCapture(ev.pointerId); } catch { }
        state.panning = false;

        if (state.scale <= 1.001 && state.scale >= 1)
            reset(state, true);
    }

    function onWheel(ev) {
        if (state.wheelMode === 'disabled')
            return;
        if (state.wheelMode === 'modifier' && !ev.ctrlKey && !ev.metaKey)
            return;

        ev.preventDefault();

        // A wheel notch is a proportional step, not an absolute one, or zooming in and back out
        // would not land where it started.
        const next = clamp(state.scale * Math.exp(-ev.deltaY / 300), state.minZoom, state.maxZoom);
        const rect = el.getBoundingClientRect();
        const factor = next / state.scale;

        // Hold the point under the cursor still: its offset from the centre scales with everything
        // else, so the translation has to take up the difference.
        state.tx = ev.clientX - (rect.left + rect.width / 2) - ((ev.clientX - (rect.left + rect.width / 2)) - state.tx) * factor;
        state.ty = ev.clientY - (rect.top + rect.height / 2) - ((ev.clientY - (rect.top + rect.height / 2)) - state.ty) * factor;
        state.scale = next;
        settle(state, false);
    }

    el.addEventListener('pointerdown', onDown);
    el.addEventListener('pointermove', onMove);
    el.addEventListener('pointerup', onUp);
    el.addEventListener('pointercancel', onUp);
    el.addEventListener('wheel', onWheel, { passive: false });

    state.handlers = { onDragStart, onDown, onMove, onUp, onWheel };
    syncTouchAction(state);
}

export function detach(state) {
    if (!state.handlers)
        return;

    const el = state.target;
    const { onDragStart, onDown, onMove, onUp, onWheel } = state.handlers;
    el.removeEventListener('dragstart', onDragStart);
    el.removeEventListener('pointerdown', onDown);
    el.removeEventListener('pointermove', onMove);
    el.removeEventListener('pointerup', onUp);
    el.removeEventListener('pointercancel', onUp);
    el.removeEventListener('wheel', onWheel);
    state.handlers = null;
}
