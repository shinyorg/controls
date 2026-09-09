// Image viewer: pinch to zoom, pan when zoomed, double-tap zoom toggle.
//
// The gesture machinery itself lives in zoom-pan-core.js, shared with ZoomPanView — this module is
// only the viewer's half: which element is transformed, and the backdrop that closes it.

import * as core from './zoom-pan-core.js';

const states = new WeakMap();

export function init(root, backdrop, img, dotnetRef, maxZoom) {
    const state = core.createState(img, {
        maxZoom,
        // A lightbox is nothing but the picture, so it claims the pointer stream outright and there
        // is no content inside it with a claim of its own.
        wheelMode: 'disabled',
        respectInteractive: false,
        claimTouchAlways: true
    });
    state.dotnet = dotnetRef;
    state.root = root;
    state.backdrop = backdrop;
    states.set(root, state);

    img.draggable = false;

    core.attach(state);

    const onBackdropClick = () => state.dotnet.invokeMethodAsync('OnRequestClose');
    backdrop.addEventListener('click', onBackdropClick);
    state.onBackdropClick = onBackdropClick;
}

export function open(root) {
    const state = states.get(root);
    if (state)
        core.reset(state, false);
}

export function close(root) {
    const state = states.get(root);
    if (state)
        core.reset(state, false);
}

export function dispose(root) {
    const state = states.get(root);
    if (!state) return;

    core.detach(state);
    state.backdrop.removeEventListener('click', state.onBackdropClick);
    states.delete(root);
}
