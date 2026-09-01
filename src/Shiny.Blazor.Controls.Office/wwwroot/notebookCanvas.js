// Pointer capture for the notebook canvas.
//
// A drag on the canvas — moving an item, dragging a resize handle, laying down a stroke — is a
// gesture that starts on the canvas and routinely travels off it. Without capture the browser
// re-targets pointermove at whatever is under the pointer, so the moment the cursor crosses onto the
// page list, the ribbon or the window chrome, the moves stop arriving and the pointerup never comes:
// the item freezes mid-drag and the editor is left believing a drag is still in progress. From the
// outside that reads as "dragging does not work", which is the worst possible symptom for something
// that half-works.
//
// Blazor has no binding for setPointerCapture — PointerEventArgs carries the pointerId but no element
// — so the listeners live here, alongside the ones Blazor does bind. They are passive: they never
// preventDefault and never stop propagation, so Blazor's own @onpointerdown still sees everything.

export function attach(element) {
    if (!element)
        return null;

    const onDown = e => {
        try {
            element.setPointerCapture(e.pointerId);
        } catch {
            // Capture is refused for a pointer that has already been released, which happens when a
            // click is synthesised rather than made. The drag simply behaves as it did before.
        }
    };

    const release = e => {
        try {
            if (element.hasPointerCapture(e.pointerId))
                element.releasePointerCapture(e.pointerId);
        } catch {
            // Already gone; nothing to release.
        }
    };

    element.addEventListener('pointerdown', onDown);
    element.addEventListener('pointerup', release);
    element.addEventListener('pointercancel', release);

    return DotNet.createJSObjectReference({
        dispose: () => {
            element.removeEventListener('pointerdown', onDown);
            element.removeEventListener('pointerup', release);
            element.removeEventListener('pointercancel', release);
        }
    });
}

export function detach(handle) {
    handle?.dispose?.();
    DotNet.disposeJSObjectReference?.(handle);
}
