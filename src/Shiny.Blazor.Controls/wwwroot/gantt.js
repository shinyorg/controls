// Scroll synchronisation, pointer capture and wheel zoom for GanttView.
//
// The scroll sync deliberately never crosses into .NET. A Gantt scrolls in both axes and has two
// dependent surfaces - the timeline header and the task pane - that must move on the same frame the
// bars do. Routing that through an interop callback puts a render pass between the scroll event and
// the transform, which shows up as the header visibly lagging the bars on every flick.

const handles = new WeakMap();

export function attach(scroller, headerInner, paneInner, dotnet) {
    if (!scroller) {
        return;
    }

    const sync = () => {
        if (headerInner) {
            headerInner.style.transform = `translateX(${-scroller.scrollLeft}px)`;
        }
        if (paneInner) {
            paneInner.style.transform = `translateY(${-scroller.scrollTop}px)`;
        }
    };

    // Ctrl/Cmd + wheel is the near-universal zoom gesture, and it is also a browser page zoom, so it
    // has to be claimed explicitly. passive:false is required for preventDefault to be honoured.
    const onWheel = (e) => {
        if (!e.ctrlKey && !e.metaKey) {
            return;
        }
        e.preventDefault();

        const rect = scroller.getBoundingClientRect();
        const anchorX = e.clientX - rect.left + scroller.scrollLeft;
        dotnet?.invokeMethodAsync('OnWheelZoom', e.deltaY, anchorX);
    };

    scroller.addEventListener('scroll', sync, { passive: true });
    scroller.addEventListener('wheel', onWheel, { passive: false });

    handles.set(scroller, { sync, onWheel });
    sync();
}


export function detach(scroller) {
    const handle = scroller && handles.get(scroller);
    if (!handle) {
        return;
    }
    scroller.removeEventListener('scroll', handle.sync);
    scroller.removeEventListener('wheel', handle.onWheel);
    handles.delete(scroller);
}


export function scrollTo(scroller, x, y, smooth) {
    scroller?.scrollTo({
        left: x,
        top: y === null || y === undefined ? scroller.scrollTop : y,
        behavior: smooth ? 'smooth' : 'auto'
    });
}


export function viewport(scroller) {
    if (!scroller) {
        return { width: 0, height: 0, scrollLeft: 0, scrollTop: 0 };
    }
    return {
        width: scroller.clientWidth,
        height: scroller.clientHeight,
        scrollLeft: scroller.scrollLeft,
        scrollTop: scroller.scrollTop
    };
}


// Without capture, dragging a bar past the edge of the canvas silently drops the gesture and the bar
// freezes mid-drag with no pointerup ever arriving.
export function capture(element, pointerId) {
    try {
        element?.setPointerCapture(pointerId);
    } catch {
        // A pointer that has already been released throws here; nothing to recover.
    }
}


export function release(element, pointerId) {
    try {
        element?.releasePointerCapture(pointerId);
    } catch {
        // Same.
    }
}


// The canvas's position in client space, so .NET can turn a pointer event into timeline coordinates.
//
// This exists because MouseEvent.offsetX is relative to the *event target*, and the target of a press
// on a bar is the bar, not the canvas the handler is bound to. Using offsetX directly makes every
// drag hit-test against a coordinate space that shifts with whichever element happens to be under the
// pointer. Blazor does not surface event.target, so the fix is to work from clientX/clientY and
// subtract this - fetched once when the drag starts, not on every move.
export function origin(element) {
    const rect = element?.getBoundingClientRect();
    return rect ? { left: rect.left, top: rect.top } : { left: 0, top: 0 };
}
