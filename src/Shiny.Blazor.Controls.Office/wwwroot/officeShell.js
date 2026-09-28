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

/* Focuses a dialog's first text field (or, failing that, its first button). */
export function focusFirstField(el) {
    if (!el) return;
    const field = el.querySelector('input:not([type=checkbox]), textarea') || el.querySelector('button');
    if (field) {
        field.focus();
        if (typeof field.select === 'function' && field.value) field.select();
    }
}

/*
    Save and print for the Word shell. The bytes arrive as a DotNetStreamReference, which is the one
    way to move a file out of .NET without base64-inflating it through a string.
*/
async function blobFrom(streamRef, mime) {
    const buffer = await streamRef.arrayBuffer();
    return new Blob([buffer], { type: mime });
}

export async function saveFile(name, mime, streamRef) {
    const url = URL.createObjectURL(await blobFrom(streamRef, mime));
    const a = document.createElement('a');
    a.href = url;
    a.download = name;
    a.style.display = 'none';
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60000);
}

/*
    Prints a PDF through the browser's own viewer: loaded into a hidden frame and printed from there,
    so the page itself is not what gets printed. Where a browser will not print a PDF from a frame
    (Safari, Firefox with pdf.js disabled) the PDF opens in a tab instead, which has its own Print.
*/
export async function printPdf(streamRef) {
    const url = URL.createObjectURL(await blobFrom(streamRef, 'application/pdf'));
    const frame = document.createElement('iframe');
    frame.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0;visibility:hidden';
    frame.src = url;

    const cleanup = () => setTimeout(() => { frame.remove(); URL.revokeObjectURL(url); }, 60000);

    frame.onload = () => {
        try {
            frame.contentWindow.focus();
            frame.contentWindow.print();
        } catch {
            window.open(url, '_blank');
        }
        cleanup();
    };

    document.body.appendChild(frame);
}
