const instances = new Map();

function percentFrom(trackEl, clientX, clientY, vertical) {
    const rect = trackEl.getBoundingClientRect();

    // Vertical runs bottom-to-top: the minimum is at the largest client Y.
    const raw = vertical
        ? (rect.bottom - clientY) / rect.height
        : (clientX - rect.left) / rect.width;

    return Math.max(0, Math.min(1, raw));
}

export function init(trackEl, dotNetRef, vertical) {
    const state = { trackEl, dotNetRef, vertical: !!vertical, dragging: false };

    const onPointerMove = (e) => {
        if (!state.dragging) return;
        e.preventDefault();
        dotNetRef.invokeMethodAsync('OnDragUpdate', percentFrom(trackEl, e.clientX, e.clientY, state.vertical));
    };

    const onPointerUp = () => {
        state.dragging = false;
        document.removeEventListener('pointermove', onPointerMove);
        document.removeEventListener('pointerup', onPointerUp);
    };

    trackEl.addEventListener('pointerdown', (e) => {
        // closest(), not the target's own class: a thumb with template content in it hands the
        // pointerdown to that content, and testing the target alone would never start the drag.
        if (e.target.closest && e.target.closest('.shiny-gs-thumb')) {
            state.dragging = true;
            e.preventDefault();
            document.addEventListener('pointermove', onPointerMove);
            document.addEventListener('pointerup', onPointerUp);
        }
    });

    state.onPointerMove = onPointerMove;
    state.onPointerUp = onPointerUp;
    instances.set(trackEl, state);
}

/** The drag maths depends on the axis, so a slider that flips orientation has to say so. */
export function setOrientation(trackEl, vertical) {
    const state = instances.get(trackEl);
    if (state) {
        state.vertical = !!vertical;
    }
}

export function getClickPercent(trackEl, clientX, clientY, vertical) {
    return percentFrom(trackEl, clientX, clientY, vertical);
}

export function dispose(trackEl) {
    const state = instances.get(trackEl);
    if (state) {
        document.removeEventListener('pointermove', state.onPointerMove);
        document.removeEventListener('pointerup', state.onPointerUp);
        instances.delete(trackEl);
    }
}
