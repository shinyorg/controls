// The Office shell's browser half: width reporting for the responsive layout, pointer capture for
// the ruler's drags, and focusing the search box. Everything else is plain Blazor markup.

const observers = new WeakMap();

/*
    Reports the shell's width to .NET whenever it crosses a size that matters. The breakpoints are
    decided in C# (OfficeShellLayout); this only has to say how wide the shell is, and only when it
    changed by a whole pixel - a ResizeObserver fires on every sub-pixel reflow otherwise.
*/
export function observe(el, dotnet) {
    if (!el || observers.has(el)) return;

    let last = -1;
    const report = () => {
        const width = Math.round(el.getBoundingClientRect().width);
        if (width === last) return;
        last = width;
        dotnet.invokeMethodAsync('OnShellResized', width).catch(() => { });
    };

    const observer = new ResizeObserver(report);
    observer.observe(el);
    observers.set(el, observer);
    report();
}

export function unobserve(el) {
    const observer = el && observers.get(el);
    if (!observer) return;
    observer.disconnect();
    observers.delete(el);
}

/* A drag on the ruler keeps reporting after the pointer leaves the strip, which is what a marker
   dragged into the margin needs. */
export function capture(el, pointerId) {
    try { el?.setPointerCapture(pointerId); } catch { }
}

export function release(el, pointerId) {
    try { el?.releasePointerCapture(pointerId); } catch { }
}

export function focus(el, select) {
    if (!el) return;
    el.focus();
    if (select && typeof el.select === 'function') el.select();
}

/* The element's box as [left, top, width, height] - numbers rather than an object, so nothing
   anonymous crosses the interop boundary (trimmed WASM cannot rebuild one). */
export function bounds(el) {
    if (!el) return [0, 0, 0, 0];
    const r = el.getBoundingClientRect();
    return [r.left, r.top, r.width, r.height];
}
