// ZoomPanView: pinch, pan, double-tap and wheel zoom over arbitrary content.
//
// The gesture machinery is zoom-pan-core.js, shared with the image viewer. What is different here is
// that the thing being transformed is a live control tree, so the core is asked to leave gestures
// that start on a control alone until there is actually something to pan.

import * as core from './zoom-pan-core.js';

const states = new WeakMap();

export function init(host, surface, dotnetRef, options) {
    const state = core.createState(surface, {
        minZoom: options.minZoom,
        maxZoom: options.maxZoom,
        doubleTapZoom: options.doubleTapZoom,
        doubleTapToZoom: options.doubleTapToZoom,
        animationLength: options.animationLength,
        wheelMode: options.wheelMode,
        respectInteractive: true,
        onChange: (scale, zoomed) => dotnetRef.invokeMethodAsync('OnZoomChanged', scale, zoomed)
    });
    states.set(host, state);

    if (options.enabled !== false)
        core.attach(state);
}

/** Re-reads the options without tearing the element's listeners down. */
export function update(host, options) {
    const state = states.get(host);
    if (!state) return;

    state.minZoom = options.minZoom;
    state.maxZoom = options.maxZoom;
    state.doubleTapZoom = options.doubleTapZoom;
    state.doubleTapToZoom = options.doubleTapToZoom;
    state.animationLength = options.animationLength;
    state.wheelMode = options.wheelMode;

    if (options.enabled === false) {
        core.reset(state, false);
        core.detach(state);
    }
    else if (!state.handlers) {
        core.attach(state);
    }
    else if (state.scale > state.maxZoom || state.scale < state.minZoom) {
        // A tightened limit has to pull an already-zoomed surface back under it. Only then: zoomTo
        // notifies .NET, and notifying on an update that changed nothing is a render loop.
        core.zoomTo(state, state.scale);
    }
}

export function zoomTo(host, zoom) {
    const state = states.get(host);
    if (state)
        core.zoomTo(state, zoom, null);
}

export function reset(host) {
    const state = states.get(host);
    if (state)
        core.reset(state, true);
}

export function dispose(host) {
    const state = states.get(host);
    if (!state) return;

    core.detach(state);
    states.delete(host);
}
