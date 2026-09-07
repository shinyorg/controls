// Pointer capture, and nothing else.
//
// A drag on a plan routinely leaves the canvas - you grab a room near the edge and pull it towards
// the middle of the screen, or you rubber-band from inside the plan out past its border. Without
// capture the browser stops delivering pointermove the moment the pointer crosses out of the
// element, and the drag freezes with the element half-moved and the button still down.
export function capture(element, pointerId) {
    try {
        element.setPointerCapture(pointerId);
    }
    catch {
        // Already released, or a pointer id the browser has forgotten. Nothing to do - the drag just
        // behaves the way it would have without capture.
    }
}

export function release(element, pointerId) {
    try {
        if (element.hasPointerCapture(pointerId))
            element.releasePointerCapture(pointerId);
    }
    catch {
        // As above.
    }
}
