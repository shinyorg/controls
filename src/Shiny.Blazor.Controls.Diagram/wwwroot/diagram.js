// Viewport measurement, wheel zoom and pointer capture for DiagramView.
//
// Three jobs, and only three - everything else about the diagram is drawn from .NET state, because a
// second source of truth for where a node is would be a second thing to keep in sync with the layout
// engine.
//
//  1. Report the surface's size and screen origin. Blazor's PointerEventArgs.OffsetX is relative to
//     whichever element the browser considers the event target, which inside an SVG is whatever path
//     is under the finger - so it silently reports coordinates in a child's space. Every hit test
//     therefore works from ClientX minus this origin instead.
//  2. Ctrl/Cmd + wheel zoom. It is also the browser's page zoom, so it has to be claimed explicitly,
//     and preventDefault is only honoured on a non-passive listener.
//  3. Pointer capture, so a drag that leaves the surface keeps arriving.

const handles = new WeakMap();

export function attach(surface, dotnet) {
    if (!surface || handles.has(surface)) {
        return;
    }

    const report = () => {
        const rect = surface.getBoundingClientRect();
        dotnet?.invokeMethodAsync('OnSurfaceMeasured', rect.left, rect.top, rect.width, rect.height);
    };

    const onWheel = (e) => {
        // Plain wheel scrolls the page, which is what a reader of a long article containing a diagram
        // wants. Zoom is the deliberate gesture.
        if (!e.ctrlKey && !e.metaKey) {
            return;
        }

        e.preventDefault();

        const rect = surface.getBoundingClientRect();
        dotnet?.invokeMethodAsync(
            'OnWheelZoom',
            e.deltaY,
            e.clientX - rect.left,
            e.clientY - rect.top
        );
    };

    // Fires once immediately, which is what gives the first render a real size to lay out against.
    const observer = new ResizeObserver(report);
    observer.observe(surface);

    // A scroll anywhere up the tree moves the surface without resizing it, and the origin has to
    // follow or every hit test lands at an offset.
    window.addEventListener('scroll', report, { passive: true, capture: true });
    window.addEventListener('resize', report, { passive: true });
    surface.addEventListener('wheel', onWheel, { passive: false });

    handles.set(surface, { observer, report, onWheel });
    report();
}

export function detach(surface) {
    const handle = handles.get(surface);
    if (!handle) {
        return;
    }

    handle.observer.disconnect();
    window.removeEventListener('scroll', handle.report, { capture: true });
    window.removeEventListener('resize', handle.report);
    surface.removeEventListener('wheel', handle.onWheel);
    handles.delete(surface);
}

export function capture(surface, pointerId) {
    try {
        surface?.setPointerCapture(pointerId);
    } catch {
        // Capturing a pointer that has already been released throws; that is a race with the pointer
        // being lifted, not an error worth surfacing.
    }
}

export function release(surface, pointerId) {
    try {
        surface?.releasePointerCapture(pointerId);
    } catch {
        // Same race, in the other direction.
    }
}

export function measure(surface) {
    if (!surface) {
        return null;
    }

    const rect = surface.getBoundingClientRect();
    return { left: rect.left, top: rect.top, width: rect.width, height: rect.height };
}
