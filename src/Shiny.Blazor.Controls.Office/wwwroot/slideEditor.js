// Keyboard shortcuts for the slide editor that the browser would otherwise act on first.
//
// Blazor decides preventDefault when the component renders — one keystroke behind the handler that
// wants it — so a bound F5 would sometimes reload the page (and lose an unsaved deck), and Ctrl+R,
// Ctrl+L, Ctrl+E, Ctrl+J, Ctrl+D, Ctrl+K and Ctrl+G would open the browser's reload, address bar,
// search, downloads, bookmark and find-next. This listener cancels exactly those, synchronously, and
// lets the event carry on to Blazor's own handler, which does the work.
const Blocked = new Set(['b', 'i', 'u', 'e', 'l', 'r', 'j', 'g', 'm', 'd', 'k', 'f', 'h', '.', ',', '>', '<', '=', '+', ' ', ']', '[', '}', '{']);

export function attach(element) {
    if (!element)
        return null;

    const onKeyDown = e => {
        const command = e.ctrlKey || e.metaKey;
        if (e.key === 'F5' || (command && Blocked.has(e.key.toLowerCase())))
            e.preventDefault();
    };

    element.addEventListener('keydown', onKeyDown);

    // Wrapped for .NET: a plain object cannot come back as an IJSObjectReference.
    return DotNet.createJSObjectReference({ dispose: () => element.removeEventListener('keydown', onKeyDown) });
}

export function detach(handle) {
    handle?.dispose?.();
    DotNet.disposeJSObjectReference?.(handle);
}
